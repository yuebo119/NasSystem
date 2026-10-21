<script setup lang="ts">
import { ref, watch, computed, nextTick, onMounted, onUnmounted, h } from 'vue'
import { subscribeDirectory, watchHubConnectionState } from '@/utils/signalr'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useFileStore } from '@/stores/file'
import { fileApi } from '@/api/file'
import { fileVersionApi } from '@/api/fileVersion'
import { ocrApi } from '@/api/ocr'
import { thumbnailApi } from '@/api/thumbnail'
import { fileShareApi, ShareAccessType, PermissionLevel, type FileShareDto, type CreateFileShareRequest } from '@/api/fileShare'
import { storageApi } from '@/api/storage'
import { ElMessage, ElMessageBox, ElCheckbox } from 'element-plus'
import { ElTableV2, ElAutoResizer, TableV2SortOrder, type Column, type ColumnSortParams, type RowEventHandlers, type TableV2Instance } from 'element-plus'
import type { UploadFile } from 'element-plus'
import {
  Refresh, Upload, Delete, Download,
  CopyDocument, Rank, FolderAdd, Search,
  Grid, List, View, Hide, DeleteFilled, Back,
  Document, Folder, HomeFilled, Share, Link, Loading, Reading, ArrowDown, FolderOpened
} from '@element-plus/icons-vue'
import type { FileItem, FileVersion, OcrText, VersionComparison } from '@/types'
import { formatSize, formatDate, getFileIcon, getFileIconColor } from '@/utils/format'
import { createPreviewUrlManager } from '@/utils/previewUrl'
import OcrImageViewer from '@/components/OcrImageViewer.vue'
import DirectoryBrowserDialog from '@/components/DirectoryBrowserDialog.vue'
import { useDrawerSize, useIsMobile } from '@/utils/useIsMobile'

// ITM-M3 收尾：Files 页 i18n 全量接入
const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const fileStore = useFileStore()
// 模态统一右侧抽屉：窄屏全宽，宽屏 clamp 随视口连续缩放
const drawerSize = useDrawerSize()
// 窄屏（≤768px）表格收敛为 选择/名称/操作 三列：六列总宽 870px 远超手机
// 视口，且 el-table-v2 超宽列是裁剪而非滚动，行操作按钮被挤到视口外
// 全部不可达（2026-10-21 移动视口实测 x=405 vs 视口 390）。元信息降级为
// 名称单元格内的次级行，与 iOS 文件 App 的同款模式
const isMobile = useIsMobile()

// 对话框状态
const createFolderDialogVisible = ref(false)
const renameDialogVisible = ref(false)
const searchDialogVisible = ref(false)
const uploadDialogVisible = ref(false)
const previewDialogVisible = ref(false)

// 历史版本（ROADMAP Phase 1）
const versionDialogVisible = ref(false)
const versionTarget = ref<FileItem | null>(null)
const versionList = ref<FileVersion[]>([])
const versionLoading = ref(false)

// 表单数据
const newFolderName = ref('')
const renameTarget = ref<FileItem | null>(null)
const newName = ref('')
const searchQuery = ref('')
const searchResults = ref<FileItem[]>([])
// 搜索命中总数（服务端口径）。与 items.length 分开：pageSize 封顶 200，
// 两者相等才是"结果已全量呈现"
const searchTotal = ref(0)
const searchLoading = ref(false)
const searchItemType = ref('')
const searchExtension = ref('')
const searchInCurrentDir = ref(true)
// 只搜识别文字（OCR）：定向检索图片内文字（后端 ocrOnly），
// 文件名通道关闭。用于"文件名搜不到、图里有"的扫描件/截图场景
const searchOcrOnly = ref(false)
const previewItem = ref<FileItem | null>(null)

// 搜索防抖定时器
let searchDebounceTimer: ReturnType<typeof setTimeout> | null = null

// 上传相关
const uploadFiles = ref<UploadFile[]>([])
const uploading = ref(false)
const uploadProgress = ref(0)

// 文本预览内容
const textContent = ref('')

// 扫描同步
const scanning = ref(false)

// 分享相关
const shareDialogVisible = ref(false)
const shareTarget = ref<FileItem | null>(null)
const shareForm = ref<CreateFileShareRequest>({
  fileItemId: '',
  accessType: ShareAccessType.Public,
  password: '',
  permission: PermissionLevel.Read,
  expiresAt: undefined,
  maxDownloads: undefined
})
const shareResult = ref<FileShareDto | null>(null)
const shareLoading = ref(false)
const expiresOption = ref('never') // never, 1d, 7d, 30d, custom
const maxDownloadsOption = ref('unlimited') // unlimited, 1, 10, 100, custom

// 表格引用。类型取 TableV2Instance：scrollToRow 等方法只在这个接口上，
// InstanceType<typeof ElTableV2> 是组件公开实例类型，不含它们
const fileTableRef = ref<TableV2Instance | null>(null)

// 监听路由变化加载目录
watch(
  () => route.params.path,
  (path) => {
    const targetPath = Array.isArray(path) ? '/' + path.join('/') : '/'
    fileStore.loadDirectory(targetPath)
  },
  { immediate: true }
)

// Dashboard 快捷操作的跳转意图：?upload=1 / ?newFolder=1
// 分别打开上传、新建文件夹。用完即清 query——不清的话刷新会
// 重复弹出，浏览器返回也会再次触发。
// 回收站 2026-10-19 提升为独立菜单页，Dashboard 的直接跳 /recycle-bin
watch(
  () => route.query,
  (query) => {
    if (query.upload) {
      uploadDialogVisible.value = true
      router.replace({ path: route.path })
    } else if (query.newFolder) {
      createFolderDialogVisible.value = true
      router.replace({ path: route.path })
    }
  },
  { immediate: true }
)

// 监听搜索关键词变化，自动触发搜索（防抖）
watch(
  [searchQuery, searchItemType, searchExtension, searchInCurrentDir, searchOcrOnly],
  () => {
    if (searchDebounceTimer) {
      clearTimeout(searchDebounceTimer)
    }
    if (searchQuery.value.trim()) {
      searchDebounceTimer = setTimeout(() => {
        handleSearch()
      }, 300)
    } else {
      searchResults.value = []
      searchTotal.value = 0
    }
  }
)

// 聚焦特定文件的方法
async function focusFileItem(itemId: string) {
  await nextTick()

  const index = fileStore.sortedFiles.findIndex(f => f.id === itemId)
  if (index < 0) {
    fileStore.setFocusItem(null)
    return
  }

  const item = fileStore.sortedFiles[index]
  fileStore.clearSelection()
  fileStore.selectItem(item)

  // el-table-v2 提供按行号滚动，不用再手工算 scrollTop（原实现里那套
  // querySelectorAll + 手动计算只对 el-table 有效，换表后整段失效）
  fileTableRef.value?.scrollToRow(index, 'center')

  fileStore.setFocusItem(null)
}

// 监听目录加载完成后聚焦特定文件
watch(
  () => fileStore.isLoading,
  async (loading, prevLoading) => {
    // 当加载完成时（从 true 变为 false）
    if (prevLoading && !loading && fileStore.focusItemId) {
      await focusFileItem(fileStore.focusItemId)
    }
  }
)

// 导航到文件夹
function navigateToFolder(item: FileItem) {
  if (item.isFolder) {
    router.push(`/files${item.path}`)
  } else {
    // 预览文件
    openPreview(item)
  }
}

// 面包屑导航
function navigateToBreadcrumb(path: string) {
  router.push(`/files${path}`)
}

// 返回上级目录
function goBack() {
  const parentPath = fileStore.directoryContent?.parentPath || '/'
  router.push(`/files${parentPath}`)
}

// 创建文件夹
async function handleCreateFolder() {
  if (!newFolderName.value.trim()) {
    ElMessage.warning(t('filesView.enterFolderName'))
    return
  }
  const result = await fileStore.createFolder(newFolderName.value.trim())
  if (result.success) {
    ElMessage.success(t('filesView.createSuccess'))
    createFolderDialogVisible.value = false
    newFolderName.value = ''
  } else {
    ElMessage.error(result.error?.message || t('filesView.createFailed'))
  }
}

// 打开重命名对话框
function openRenameDialog(item: FileItem) {
  renameTarget.value = item
  newName.value = item.name
  renameDialogVisible.value = true
}

// 重命名
async function handleRename() {
  if (!renameTarget.value || !newName.value.trim()) {
    ElMessage.warning(t('filesView.enterNewName'))
    return
  }
  if (newName.value === renameTarget.value.name) {
    renameDialogVisible.value = false
    return
  }
  const result = await fileStore.rename(renameTarget.value, newName.value.trim())
  if (result.success) {
    ElMessage.success(t('filesView.renameSuccess'))
    renameDialogVisible.value = false
  } else {
    ElMessage.error(result.error?.message || t('filesView.renameFailed'))
  }
}

// 删除选中项
async function handleDelete(permanent = false) {
  const items = [...fileStore.selectedItems]
  const count = items.length
  if (count === 0) {
    ElMessage.warning(t('filesView.selectToDelete'))
    return
  }
  try {
    await ElMessageBox.confirm(
      permanent 
        ? t('filesView.confirmPermDeleteSelected', { count })
        : t('filesView.confirmDeleteSelected', { count }),
      permanent ? t('filesView.permanentDelete') : t('filesView.deleteConfirm'),
      { confirmButtonText: permanent ? t('filesView.permanentDelete') : t('filesView.delete'), cancelButtonText: t('filesView.cancel'), type: 'warning' }
    )
    
    // ITM-N19：一次数组请求（后端返回 per-item 结果），替代逐项循环
    try {
      const result = await fileApi.delete(items.map(i => i.path), permanent)
      const failed: string[] = (result.data as { failedItems?: string[] } | null)?.failedItems ?? []
      if (failed.length === 0) {
        ElMessage.success(t('filesView.deletedCount', { count: items.length }))
      } else {
        ElMessage.warning(t('filesView.deletePartialWithReason', { ok: items.length - failed.length, fail: failed.length, first: failed[0] }))
      }
    } catch {
      // 拦截器已弹错
    }

    fileStore.clearSelection()
    await fileStore.loadDirectory(fileStore.currentPath)
  } catch {
    // 取消操作
  }
}

// 删除单个项目
async function handleDeleteItem(item: FileItem, permanent = false) {
  try {
    await ElMessageBox.confirm(
      permanent 
        ? t('filesView.confirmPermDeleteItem', { name: item.name })
        : t('filesView.confirmDeleteItem', { name: item.name }),
      permanent ? t('filesView.permanentDelete') : t('filesView.deleteConfirm'),
      { confirmButtonText: permanent ? t('filesView.permanentDelete') : t('filesView.delete'), cancelButtonText: t('filesView.cancel'), type: 'warning' }
    )
    const result = await fileApi.delete([item.path], permanent)
    if (result.success) {
      ElMessage.success(permanent ? t('filesView.permanentlyDeleted') : t('filesView.movedToRecycleBin'))
      await fileStore.loadDirectory(fileStore.currentPath)
    } else {
      ElMessage.error(result.error?.message || t('filesView.deleteFailedMsg'))
    }
  } catch {
    // 取消操作
  }
}

// 复制到剪贴板（状态与结果计算在 file store，guard 提示与通知留视图层）
function copyToClipboard() {
  if (!fileStore.setClipboard('copy')) {
    ElMessage.warning(t('filesView.selectFilesFirst'))
    return
  }
  ElMessage.success(t('filesView.copiedCount', { count: fileStore.selectedItems.length }))
}

// 剪切到剪贴板
function cutToClipboard() {
  if (!fileStore.setClipboard('cut')) {
    ElMessage.warning(t('filesView.selectFilesFirst'))
    return
  }
  ElMessage.success(t('filesView.cutCount', { count: fileStore.selectedItems.length }))
}

// 粘贴
async function handlePaste() {
  const result = await fileStore.pasteClipboard()
  if (!result) {
    ElMessage.warning(t('filesView.clipboardEmpty'))
    return
  }
  const actionText = t(result.action === 'copy' ? 'filesView.copyAction' : 'filesView.moveAction')
  if (result.failed === 0) {
    ElMessage.success(t('filesView.pasteDone', { action: actionText, count: result.ok }))
  } else {
    ElMessage.warning(t('filesView.pastePartial', { action: actionText, ok: result.ok, fail: result.failed }))
  }
}

// ─── 移动到… / 复制到…（2026-10-21 补齐：目标目录点选，不必先导航过去再粘贴）───
// 剪贴板模型保留为快捷方式；深层路径用目录浏览器一步到位
const transferDialogVisible = ref(false)
const transferMode = ref<'move' | 'copy'>('move')
const transferRunning = ref(false)

function openTransferDialog(mode: 'move' | 'copy') {
  if (!fileStore.hasSelection) {
    ElMessage.warning(t('filesView.selectFilesFirst'))
    return
  }
  transferMode.value = mode
  transferDialogVisible.value = true
}

async function runTransferTo(destination: string) {
  const items = [...fileStore.selectedItems]
  if (items.length === 0) return
  // 目标不能是所选条目自己或其后代：把 /a 移进 /a/b 会递归出错
  const invalid = items.find(i => destination === i.path || destination.startsWith(i.path + '/'))
  if (invalid) {
    ElMessage.warning(t('filesView.transferInvalidTarget', { name: invalid.name }))
    return
  }
  transferRunning.value = true
  try {
    const paths = items.map(i => i.path)
    const result = transferMode.value === 'move'
      ? await fileApi.move(paths, destination)
      : await fileApi.copy(paths, destination)
    const failed: string[] = (result.data as { failedItems?: string[] } | null)?.failedItems ?? []
    if (failed.length === 0) {
      ElMessage.success(t('filesView.transferDone', {
        action: t(transferMode.value === 'move' ? 'filesView.moveAction' : 'filesView.copyAction'),
        count: items.length,
        dest: destination
      }))
      transferDialogVisible.value = false
      fileStore.clearSelection()
    } else {
      ElMessage.warning(t('filesView.transferPartial', { ok: items.length - failed.length, fail: failed.length, first: failed[0] }))
    }
    await fileStore.loadDirectory(fileStore.currentPath)
  } catch {
    // 拦截器已弹错
  } finally {
    transferRunning.value = false
  }
}

// ─── 文件详情面板（2026-10-21 补齐：/files/info 后端一直可用，此前无 UI 入口）───
const infoDialogVisible = ref(false)
const infoLoading = ref(false)
const infoData = ref<FileItem | null>(null)

async function openInfoDialog(item: FileItem) {
  infoDialogVisible.value = true
  infoLoading.value = true
  infoData.value = null
  try {
    const result = await fileApi.getFileInfo(item.path)
    if (result.success && result.data) infoData.value = result.data
  } catch {
    // 拦截器已弹错
  } finally {
    infoLoading.value = false
  }
}

// 搜索扩展名规范化：allow-create 允许任意输入，统一补前导点（.flac）
function normalizeSearchExtension(value: string) {
  if (!value) {
    searchExtension.value = ''
    return
  }
  searchExtension.value = value.startsWith('.') ? value.toLowerCase() : '.' + value.toLowerCase()
}

// 实时连接状态：SignalR 断连时状态栏明示（否则"实时同步"静默失效，
// 用户误以为多标签页/多用户变更会自动出现）
const realtimeConnected = ref(false)

// 整页拖放上传：拖文件进页面即弹出上传抽屉并灌入文件列表。
// dragenter/leave 用计数器：子元素的 leave 会把母体也算一次，直接置 false 会闪断
const pageDragging = ref(false)
let pageDragCounter = 0

function onPageDragEnter() {
  pageDragCounter++
  pageDragging.value = true
}

function onPageDragOver(event: DragEvent) {
  // 必须 preventDefault 否则浏览器把 drop 当"打开文件"处理
  event.preventDefault()
  pageDragging.value = true
}

function onPageDragLeave() {
  pageDragCounter--
  if (pageDragCounter <= 0) {
    pageDragCounter = 0
    pageDragging.value = false
  }
}

function onPageDrop(event: DragEvent) {
  event.preventDefault()
  pageDragCounter = 0
  pageDragging.value = false
  const dropped = Array.from(event.dataTransfer?.files ?? [])
  if (dropped.length === 0) return
  uploadFiles.value = dropped.map(f => ({ name: f.name, raw: f, size: f.size, uid: `${f.name}-${f.lastModified}` }) as unknown as UploadFile)
  uploadDialogVisible.value = true
}

// 打开上传对话框
function openUploadDialog() {
  uploadFiles.value = []
  uploadProgress.value = 0
  uploadDialogVisible.value = true
}

// 下载文件
async function downloadFile(item: FileItem) {
  if (item.isFolder) {
    ElMessage.warning(t('filesView.folderDownloadUnsupported'))
    return
  }
  // ITM-N19/G19：token 走 Authorization 头，禁止拼入 URL（防泄漏到历史/日志）
  const url = fileApi.getDownloadUrl(item.path)
  const token = localStorage.getItem('token')
  try {
    const response = await fetch(url, { headers: { Authorization: `Bearer ${token}` } })
    if (!response.ok) throw new Error(String(response.status))
    const blob = await response.blob()
    const objectUrl = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = objectUrl
    link.download = item.name
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    URL.revokeObjectURL(objectUrl)
  } catch {
    ElMessage.error(t('filesView.downloadFailed'))
  }
}

// 下载选中文件
function downloadSelected() {
  const files = fileStore.selectedItems.filter(item => !item.isFolder)
  if (files.length === 0) {
    ElMessage.warning(t('filesView.selectToDownload'))
    return
  }
  files.forEach(file => downloadFile(file))
}

// 分享选中文件
function shareSelected() {
  if (fileStore.selectedItems.length === 0) {
    ElMessage.warning(t('filesView.selectToShare'))
    return
  }
  if (fileStore.selectedItems.length > 1) {
    ElMessage.warning(t('filesView.shareSingleOnly'))
    return
  }
  const item = fileStore.selectedItems[0]
  openShareDialog(item)
}

// 搜索文件
async function handleSearch() {
  if (!searchQuery.value.trim()) {
    ElMessage.warning(t('filesView.enterSearchKeyword'))
    return
  }
  searchLoading.value = true
  try {
    const result = await fileApi.search(searchQuery.value.trim(), {
      path: searchInCurrentDir.value ? fileStore.currentPath : undefined,
      itemType: searchItemType.value || undefined,
      extension: searchExtension.value || undefined,
      ocrOnly: searchOcrOnly.value || undefined,
      pageSize: 200
    })
    if (result.success && result.data) {
      searchResults.value = result.data.items
      // 总数取 totalCount 而非 items.length：pageSize 封顶 200，
      // 用已取回数冒充总量会让用户以为结果已全量呈现
      // （2026-10-21 审计 P2）
      searchTotal.value = result.data.totalCount
    }
  } catch (e) {
    ElMessage.error(t('filesView.searchFailed'))
  } finally {
    searchLoading.value = false
  }
}

// 重置搜索
function resetSearch() {
  searchQuery.value = ''
  searchResults.value = []
  searchTotal.value = 0
  searchItemType.value = ''
  searchExtension.value = ''
  searchInCurrentDir.value = true
  searchOcrOnly.value = false
}

// 跳转到搜索结果
async function goToSearchResult(item: FileItem) {
  searchDialogVisible.value = false
  if (item.isFolder) {
    // 如果是文件夹，直接打开
    router.push(`/files${item.path}`)
  } else {
    // 如果是文件，跳转到文件所在目录并聚焦该文件
    const parentPath = item.path.substring(0, item.path.lastIndexOf('/')) || '/'
    const currentRoutePath = fileStore.currentPath
    
    if (parentPath === currentRoutePath) {
      // 如果已经在目标目录，直接聚焦文件
      await focusFileItem(item.id)
    } else {
      // 跳转到目标目录，设置 focusItemId 让 watch 处理聚焦
      fileStore.setFocusItem(item.id)
      router.push(`/files${parentPath}`)
    }
  }
}

// "保留两者"的改名：name.ext → name (2).ext → name (3).ext…
// 与 macOS Finder 的同款递进规则，在 taken 集合里找到第一个空位
function renameToNonConflicting(file: File, taken: Set<string>): File {
  const dot = file.name.lastIndexOf('.')
  const base = dot > 0 ? file.name.slice(0, dot) : file.name
  const ext = dot > 0 ? file.name.slice(dot) : ''
  let n = 2
  let candidate = `${base} (${n})${ext}`
  while (taken.has(candidate)) {
    n++
    candidate = `${base} (${n})${ext}`
  }
  return new File([file], candidate, { type: file.type, lastModified: file.lastModified })
}

// 上传文件
async function handleUpload() {
  if (uploadFiles.value.length === 0) {
    ElMessage.warning(t('filesView.selectToUpload'))
    return
  }

  // 计算总文件大小
  const totalSize = uploadFiles.value.reduce((sum, file) => sum + (file.raw?.size || 0), 0)

  // 检查存储空间
  try {
    const checkResult = await storageApi.checkSpace({ fileSize: totalSize }) as any
    if (checkResult.success && checkResult.data) {
      if (!checkResult.data.hasEnoughSpace) {
        ElMessage.error(checkResult.data.message || t('filesView.storageInsufficient', { required: formatSize(totalSize), available: formatSize(checkResult.data.availableBytes) }))
        return
      }
    }
  } catch (e) {
    console.warn('检查存储空间失败，继续上传', e)
  }

  // 冲突检测：目标目录已存在的同名文件。后端对已存在文件默认 409，
  // 覆盖上传（overwrite=true）是自动版本功能的唯一触发点，必须给用户
  // 明确选择而不是让上传静默失败（2026-10-21 E2E 旅程实证的流程断裂）
  // 检测前先拉一次最新目录：本地列表可能停在别的标签页上传新文件之前，
  //  stale 名单会让冲突漏检、最终被后端 409 计成失败（失败还不知道是哪几个）
  await fileStore.loadDirectory(fileStore.currentPath)
  const existingNames = new Set(fileStore.files.map((f: { name: string }) => f.name))
  const conflicts = uploadFiles.value.filter(f => f.raw && existingNames.has(f.raw.name))
  // 冲突文件的处理方式：'replace' 覆盖（产版本）/ 'keep-both' 自动改名 / null 尚未决定
  let conflictMode: 'replace' | 'keep-both' | null = null
  if (conflicts.length > 0) {
    try {
      await ElMessageBox({
        type: 'warning',
        title: t('filesView.uploadConflictTitle'),
        message: t('filesView.uploadConflictMessage', { count: conflicts.length }),
        confirmButtonText: t('filesView.uploadConflictReplace'),
        cancelButtonText: t('filesView.uploadConflictKeepBoth'),
        distinguishCancelAndClose: true
      })
      conflictMode = 'replace'
    } catch (action) {
      if (action === 'cancel') {
        conflictMode = 'keep-both'
      } else {
        return // 关闭（X）或 ESC：取消整个上传
      }
    }
  }

  uploading.value = true
  uploadProgress.value = 0

  const total = uploadFiles.value.length
  let completed = 0
  let failed = 0
  // 失败文件名单：部分失败时用户必须知道是哪几个（只报数字只能全部重传）
  const failedNames: string[] = []

  for (const uploadFile of uploadFiles.value) {
    if (uploadFile.raw) {
      try {
        const isConflict = existingNames.has(uploadFile.raw.name)
        // 保留两者：前端改到不冲突的名字（macOS Finder 同款 " (2)" 递进）。
        // 目录列表可能不含全部条目时后端 409 会兜住，计入失败并如实提示
        let fileToUpload: File = uploadFile.raw
        if (isConflict && conflictMode === 'keep-both') {
          fileToUpload = renameToNonConflicting(uploadFile.raw, existingNames)
          existingNames.add(fileToUpload.name)
        }
        await fileApi.upload(
          fileToUpload,
          fileStore.currentPath,
          (progress) => {
            uploadProgress.value = Math.round((completed * 100 + progress) / total)
          },
          isConflict && conflictMode === 'replace'
        )
        completed++
      } catch {
        failed++
        failedNames.push(uploadFile.name)
      }
    }
  }

  uploading.value = false
  uploadFiles.value = []

  if (failed === 0) {
    ElMessage.success(t('filesView.uploadCountSuccess', { count: completed }))
  } else {
    ElMessage.warning(t('filesView.uploadPartial', {
      ok: completed,
      fail: failed,
      first: failedNames[0] ?? ''
    }))
  }

  uploadDialogVisible.value = false
  await fileStore.loadDirectory(fileStore.currentPath)
}

// 处理文件选择
function handleFileChange(file: UploadFile) {
  uploadFiles.value.push(file)
}

// 移除上传文件
function removeUploadFile(file: UploadFile) {
  const index = uploadFiles.value.indexOf(file)
  if (index > -1) {
    uploadFiles.value.splice(index, 1)
  }
}

// 打开预览
async function openPreview(item: FileItem) {
  previewItem.value = item
  // OCR 状态随预览重置：换文件预览时上一条的识别结果不残留
  ocrText.value = null
  // 图片打开时顺带取已有识别结果（首次打开就有，不用用户先点识别）
  if (isImage(item)) {
    void loadExistingOcrText(item)
  }
  // 如果是文本文件，先加载内容
  if (isTextFile(item)) {
    textContent.value = t('filesView.loadingText')
    previewDialogVisible.value = true
    try {
      const token = localStorage.getItem('token')
      const response = await fetch(fileApi.getDownloadUrl(item.path), {
        headers: { Authorization: `Bearer ${token}` }
      })
      if (response.ok) {
        textContent.value = await response.text()
      } else {
        textContent.value = t('filesView.loadFailedText')
      }
    } catch (e) {
      textContent.value = t('filesView.loadFailedText')
    }
  } else {
    previewDialogVisible.value = true
    // 媒体类预览经 Header 拉取 blob，避免 token 进 URL
    void ensurePreviewUrl(item)
  }
}

// ─── 历史版本（ROADMAP Phase 1）───

// el-table 插槽的 row 是 DefaultRow 未定型，与 Shares.vue 的 asShare 同款收窄
const asVersion = (row: any): FileVersion => row as FileVersion

// 版本对比：勾选两个版本后可用（el-table selection-change 回传选中行）
const versionSelected = ref<FileVersion[]>([])
const versionComparing = ref(false)
const versionCompareVisible = ref(false)
const versionCompareResult = ref<VersionComparison | null>(null)

function onVersionSelectionChange(rows: FileVersion[]) {
  versionSelected.value = rows
}

async function runVersionCompare() {
  if (versionSelected.value.length !== 2 || !versionTarget.value) return
  // 按版本号排序传参：v1 旧 v2 新，SizeDifference 方向才对得上
  const [a, b] = [...versionSelected.value].sort((x, y) => x.versionNumber - y.versionNumber)
  versionComparing.value = true
  try {
    const result = await fileVersionApi.compare(versionTarget.value.id, a.versionNumber, b.versionNumber)
    if (result.success && result.data) {
      versionCompareResult.value = result.data
      versionCompareVisible.value = true
    } else {
      ElMessage.error(result.error?.message || t('filesView.versionCompareFailed'))
    }
  } catch {
    ElMessage.error(t('filesView.versionCompareFailed'))
  } finally {
    versionComparing.value = false
  }
}

// ─── 图片 OCR（ROADMAP Phase 2）───

const ocrText = ref<OcrText | null>(null)
const ocrLoading = ref(false)
// 目录批量 OCR：选中一个或多个文件夹时可用（多选逐个跑后汇总）
const ocrBatchRunning = ref(false)
const canBatchOcr = computed(() =>
  fileStore.selectedItems.length >= 1 && fileStore.selectedItems.every(i => i.isFolder))
// 预览抽屉的缩略图缓存清理（换源文件/显示异常时用）
const thumbClearing = ref(false)

// 清除当前预览图片的缩略图缓存：下次访问重新生成。
// 失败只提示不阻断预览（缓存是增强，清了大不了再生成）
async function clearThumbnailCache() {
  if (!previewItem.value) return
  thumbClearing.value = true
  try {
    const result = await thumbnailApi.clearCache(previewItem.value.path)
    if (result.success) {
      ElMessage.success(t('filesView.thumbCleared'))
      // 缓存清了要重取预览：旧的 objectURL 还挂着旧内容
      revokePreviewUrl()
      await ensurePreviewUrl(previewItem.value)
    } else {
      ElMessage.error(result.error?.message || t('filesView.thumbClearFailed'))
    }
  } catch {
    ElMessage.error(t('filesView.thumbClearFailed'))
  } finally {
    thumbClearing.value = false
  }
}

// 打开图片预览时取已有识别结果（失败静默：没识别过就是没有，不是错误）
async function loadExistingOcrText(item: FileItem) {
  try {
    const result = await ocrApi.getText(item.path)
    if (result.success) {
      ocrText.value = result.data ?? null
    }
  } catch {
    // 查询失败不影响预览主流程
  }
}

// 执行识别。首次含模型加载（秒级），loading 态必须显式，
// 否则用户以为按钮没响应会重复点
async function runOcr(item: FileItem) {
  if (item.isFolder) return
  ocrLoading.value = true
  try {
    const result = await ocrApi.recognize(item.path)
    if (result.success && result.data) {
      ocrText.value = result.data
      if (result.data.charCount === 0) {
        ElMessage.info(t('filesView.ocrNoText'))
      } else {
        ElMessage.success(t('filesView.ocrDone', { count: result.data.charCount }))
      }
    } else {
      ElMessage.error(result.error?.message || t('filesView.ocrFailed'))
    }
  } catch {
    ElMessage.error(t('filesView.ocrFailed'))
  } finally {
    ocrLoading.value = false
  }
}

// 目录批量 OCR：对选中的文件夹跑全量识别（已有文字的默认跳过，
// force=true 时全部重识别）。批量可能持续数十秒（每张百毫秒级），
// 按钮 loading 常驻，结果用汇总消息报告（成功/跳过/失败各自计数）。
// 多选文件夹时后端单端点只接一个目录，逐个跑后汇总——
// 单个目录失败不中断其余目录，计入 failures 一并报告
async function runBatchOcr(force = false) {
  const folders = fileStore.selectedItems.filter(i => i.isFolder)
  if (folders.length === 0) return
  const mode = force ? t('filesView.ocrBatchForceHint') : t('filesView.ocrBatchSkipHint')
  try {
    await ElMessageBox.confirm(
      t('filesView.ocrBatchConfirmMulti', { count: folders.length, mode }),
      t('filesView.ocrBatchTitle'),
      { type: 'info' }
    )
  } catch {
    return // 用户取消
  }
  ocrBatchRunning.value = true
  let total = 0
  let recognized = 0
  let skipped = 0
  let failed = 0
  let failures = 0
  try {
    for (const folder of folders) {
      try {
        const result = await ocrApi.recognizeDirectory(folder.path, force)
        if (result.success && result.data) {
          total += result.data.total
          recognized += result.data.recognized
          skipped += result.data.skipped
          failed += result.data.failed
        } else {
          failures++
        }
      } catch {
        failures++
      }
    }
    if (total === 0 && failures === 0) {
      ElMessage.info(t('filesView.ocrBatchNoImages'))
    } else if (failures > 0) {
      ElMessage.warning(t('filesView.ocrBatchPartial', {
        recognized, skipped, failed: failed + failures
      }))
    } else {
      ElMessage.success(t('filesView.ocrBatchDone', {
        recognized,
        skipped,
        failed
      }))
    }
    // 识别结果进了搜索索引，刷新当前目录让状态可见
    await fileStore.loadDirectory(fileStore.currentPath)
  } finally {
    ocrBatchRunning.value = false
  }
}

// 打开版本历史抽屉：仅文件有版本（文件夹的软删除走回收站，不产版本）
async function openVersionHistory(item: FileItem) {
  if (item.isFolder) return
  versionTarget.value = item
  versionDialogVisible.value = true
  versionSelected.value = []
  versionLoading.value = true
  try {
    const result = await fileVersionApi.getVersions(item.id)
    if (result.success) {
      versionList.value = result.data ?? []
    } else {
      versionList.value = []
      ElMessage.error(t('filesView.versionLoadFailed'))
    }
  } catch {
    versionList.value = []
    ElMessage.error(t('filesView.versionLoadFailed'))
  } finally {
    versionLoading.value = false
  }
}

// 恢复到指定版本：当前内容会先被服务端自动存为版本，无数据丢失窗口
async function restoreVersion(version: FileVersion) {
  if (!versionTarget.value) return
  try {
    await ElMessageBox.confirm(
      t('filesView.versionRestoreConfirm', { number: version.versionNumber }),
      t('filesView.versionHistoryTitle'),
      { type: 'warning' }
    )
  } catch {
    return // 用户取消
  }
  try {
    const result = await fileVersionApi.restore(versionTarget.value.id, version.versionNumber)
    if (result.success) {
      ElMessage.success(t('filesView.versionRestoreSuccess', { number: version.versionNumber }))
      await openVersionHistory(versionTarget.value)
      await fileStore.loadDirectory(fileStore.currentPath)
    } else {
      ElMessage.error(t('filesView.versionRestoreFailed'))
    }
  } catch {
    ElMessage.error(t('filesView.versionRestoreFailed'))
  }
}

// 删除单个历史版本（服务端拒绝删当前版本，这里同步禁用入口）
async function deleteVersion(version: FileVersion) {
  if (!versionTarget.value || version.isCurrent) {
    ElMessage.warning(t('filesView.versionCannotDeleteCurrent'))
    return
  }
  try {
    await ElMessageBox.confirm(
      t('filesView.versionDeleteConfirm', { number: version.versionNumber }),
      t('filesView.versionHistoryTitle'),
      { type: 'warning' }
    )
  } catch {
    return
  }
  try {
    const result = await fileVersionApi.delete(versionTarget.value.id, version.versionNumber)
    if (result.success) {
      ElMessage.success(t('filesView.versionDeleteSuccess'))
      await openVersionHistory(versionTarget.value)
    } else {
      ElMessage.error(t('filesView.versionDeleteFailed'))
    }
  } catch {
    ElMessage.error(t('filesView.versionDeleteFailed'))
  }
}

// 下载历史版本：token 走 Authorization 头（ITM-N19/G19，禁拼 URL）
async function downloadVersion(version: FileVersion) {
  if (!versionTarget.value) return
  const url = fileVersionApi.getVersionDownloadUrl(versionTarget.value.id, version.versionNumber)
  const token = localStorage.getItem('token')
  try {
    const response = await fetch(url, { headers: { Authorization: `Bearer ${token}` } })
    if (!response.ok) throw new Error(String(response.status))
    const blob = await response.blob()
    const objectUrl = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = objectUrl
    link.download = `${versionTarget.value.name}.v${version.versionNumber}`
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    URL.revokeObjectURL(objectUrl)
  } catch {
    ElMessage.error(t('filesView.downloadFailed'))
  }
}

// 是否可预览
function canPreview(item: FileItem): boolean {
  if (item.isFolder) return false
  const ext = item.extension?.toLowerCase()
  const previewable = ['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.svg',
    '.mp4', '.webm', '.ogg', '.mp3', '.wav', 
    '.txt', '.md', '.json', '.xml', '.js', '.ts', '.css', '.scss', '.html', '.vue',
    '.py', '.java', '.c', '.cpp', '.h', '.cs', '.go', '.rs', '.sh', '.bat', '.yml', '.yaml',
    '.ini', '.conf', '.cfg', '.log', '.sql', '.csv']
  return previewable.includes(ext || '')
}

// 是否是文本文件
function isTextFile(item: FileItem): boolean {
  const ext = item.extension?.toLowerCase()
  return ['.txt', '.md', '.json', '.xml', '.js', '.ts', '.css', '.scss', '.html', '.vue',
    '.py', '.java', '.c', '.cpp', '.h', '.cs', '.go', '.rs', '.sh', '.bat', '.yml', '.yaml',
    '.ini', '.conf', '.cfg', '.log', '.sql', '.csv'].includes(ext || '')
}

// 是否是图片
function isImage(item: FileItem): boolean {
  const ext = item.extension?.toLowerCase()
  return ['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.svg'].includes(ext || '')
}

// 是否是视频
function isVideo(item: FileItem): boolean {
  const ext = item.extension?.toLowerCase()
  return ['.mp4', '.webm', '.ogg'].includes(ext || '')
}

// 是否是音频
function isAudio(item: FileItem): boolean {
  const ext = item.extension?.toLowerCase()
  return ['.mp3', '.wav', '.ogg'].includes(ext || '')
}

// 刷新目录
function handleRefresh() {
  fileStore.loadDirectory(fileStore.currentPath)
}

// 扫描同步
async function handleScanSync() {
  try {
    await ElMessageBox.confirm(
      t('filesView.scanConfirm'),
      t('filesView.scanTitle'),
      { confirmButtonText: t('filesView.scanStart'), cancelButtonText: t('filesView.cancel'), type: 'warning' }
    )
    
    scanning.value = true
    const result = await fileApi.scanAndSync()
    
    if (result.success && result.data) {
      const data = result.data
      ElMessage.success({
        message: t('filesView.scanResult', { added: data.addedFiles, addedFolders: data.addedFolders, removed: data.removedFiles, removedFolders: data.removedFolders, updated: data.updatedFiles }),
        duration: 5000
      })
      // 刷新当前目录
      await fileStore.loadDirectory(fileStore.currentPath)
    } else {
      ElMessage.error(result.error?.message || t('filesView.scanFailed'))
    }
  } catch {
    // 用户取消
  } finally {
    scanning.value = false
  }
}

// 切换显示隐藏文件
function toggleShowHidden() {
  fileStore.showHidden = !fileStore.showHidden
  fileStore.loadDirectory(fileStore.currentPath)
}

// 选中项 id 集合：表格/网格每行选中判定从 O(行数×选中数) 降为 O(1)
const selectedIds = computed(() => new Set(fileStore.selectedItems.map(i => i.id)))

// ─── el-table-v2 列配置 ───
// 虚拟滚动只渲染视口内的行，3000 项的目录也只建 20 行左右 DOM。
// 原实现用 slice + renderLimit 做渐进渲染，但 slice 每次返回新数组引用，
// el-table 的 data watch 是引用比较，于是每次追加都触发全量表体重建：
// 实测追加 200 行耗时 1888ms → 2765ms → 4119ms，超线性，主线程被 long task 占满。
const ROW_HEIGHT = 44

// 表格容器尺寸。el-table-v2 的 width/height 是必填数字，用 el-auto-resizer 观测。
// height 初值 0 时先不挂表，避免首帧拿到 0 高度算出 0 行
const tableSize = ref({ width: 0, height: 0 })

// 行高亮类名：选中行加背景。el-table-v2 的 row-class 收行数据返回类名
function rowClass({ rowData }: { rowData: FileItem }): string {
  return selectedIds.value.has(rowData.id) ? 'selected-row' : ''
}

// KeyType 是 string|number|symbol 联合，作索引前先归一成字符串
const sortFieldByColumn: Record<string, 'name' | 'size' | 'createdAt' | 'type'> = {
  name: 'name',
  sizeBytes: 'size',
  extension: 'type',
  updatedAt: 'createdAt'
}

// CheckboxValueType 是 string|number|boolean 的联合，官方 modelValue 就是这个类型，
// 判真不能直接 === true（number 1 也是真值）
type CheckboxValue = string | number | boolean
const isChecked = (val: CheckboxValue) => val === true || val === 'true' || val === 1

const columns = computed<Column<FileItem>[]>(() => {
  const selectionColumn: Column<FileItem> = {
    key: 'selection',
    width: 50,
    // 表头是全选/全不选，单元格是行选中
    headerCellRenderer: () => h(ElCheckbox, {
      modelValue: isAllSelected.value,
      indeterminate: isPartiallySelected.value,
      'onUpdate:modelValue': (val: CheckboxValue) => toggleSelectAll(isChecked(val)),
      size: 'small'
    }),
    cellRenderer: ({ rowData }) => h(ElCheckbox, {
      modelValue: selectedIds.value.has(rowData.id),
      'onUpdate:modelValue': (val: CheckboxValue) => fileStore.selectItem(rowData, isChecked(val)),
      size: 'small'
    })
  }

  const nameColumn: Column<FileItem> = {
    key: 'name',
    title: t('filesView.colName'),
    dataKey: 'name',
    width: 300,
    minWidth: 300,
    flexGrow: 1,
    sortable: true,
    cellRenderer: ({ rowData }) => h('div', { class: 'file-name' }, [
      h('span', { style: { color: getFileIconColor(rowData), display: 'inline-flex', flexShrink: 0 } },
        h(getFileIcon(rowData))),
      h('div', { class: 'file-name-main' }, [
        h('span', { class: 'file-name-text' }, rowData.name),
        rowData.isHidden ? h('span', { class: 'hidden-tag' }, t('filesView.hiddenTag')) : null,
        // 窄屏次级行：大小·类型·时间（三列在窄屏被收起，信息不能丢）
        isMobile.value
          ? h('span', { class: 'file-name-meta' }, [
              rowData.isFolder ? '-' : formatSize(rowData.sizeBytes),
              ' · ',
              rowData.isFolder ? t('filesView.typeFolder') : (rowData.extension || '-'),
              ' · ',
              formatDate(rowData.updatedAt || rowData.createdAt)
            ].join(''))
          : null
      ])
    ])
  }

  // cellRenderer 的返回类型是 VNode 而非 string，纯文本单元格要包一层 h()
  const sizeColumn: Column<FileItem> = {
    key: 'sizeBytes',
    title: t('filesView.colSize'),
    dataKey: 'sizeBytes',
    width: 120,
    sortable: true,
    cellRenderer: ({ rowData }) => h('span', rowData.isFolder ? '-' : formatSize(rowData.sizeBytes))
  }

  const typeColumn: Column<FileItem> = {
    key: 'extension',
    title: t('filesView.colType'),
    dataKey: 'extension',
    width: 100,
    sortable: true,
    cellRenderer: ({ rowData }) => h('span', rowData.isFolder ? t('filesView.typeFolder') : (rowData.extension || '-'))
  }

  const modifiedColumn: Column<FileItem> = {
    key: 'updatedAt',
    title: t('filesView.colModified'),
    dataKey: 'updatedAt',
    width: 180,
    sortable: true,
    cellRenderer: ({ rowData }) => h('span', formatDate(rowData.updatedAt || rowData.createdAt))
  }

  const actionsColumn: Column<FileItem> = {
    key: 'actions',
    title: t('filesView.colActions'),
    width: isMobile.value ? 72 : 120,
    // 不用 fixed: RIGHT。el-table-v2 的固定列会渲染成独立的行容器
    // （实测固定列所在行只剩一个单元格，与数据行分离），
    // 导致操作按钮与数据行错位，点第 2 行的按钮拿到的是别的行。
    // 本项目列数不多、总宽未超容器，不需要固定列
    // 只渲染一个按钮，不在这里建 ElDropdown：cellRenderer 为每个可见行各建一个
    // dropdown，而 el-popper 会挂到 body 且 unmount 时不清理，实测点一次操作按钮
    // 就在 DOM 里留下 13 个残留菜单，越滚越多。改为受控单实例，见模板里的 rowActionsMenu
    cellRenderer: ({ rowData }) => h('button', {
      class: 'row-action-btn',
      onClick: (e: MouseEvent) => openRowActions(e, rowData)
    }, '···')
  }

  // 窄屏三列：选择/名称/操作。桌面六列全量
  return isMobile.value
    ? [selectionColumn, nameColumn, actionsColumn]
    : [selectionColumn, nameColumn, sizeColumn, typeColumn, modifiedColumn, actionsColumn]
})

// ─── 网格视图虚拟滚动 ───
// 网格此前与表格一样全量渲染：实测 3000 个文件的目录切到网格会一次性建
// 3000 个 .grid-item、21226 个 DOM 节点（表格改虚拟滚动后是 24 行 786 节点，
// 网格是它的 27 倍）。表格换成 el-table-v2 解决，这里给网格补上同一件事。
// 网格是等宽格子，自己按"可见行"算区间即可，不必为此引第三方虚拟滚动库。
const GRID_MIN_COL_WIDTH = 120   // 对应样式的 minmax(120px, 1fr)
const GRID_GAP = 16
const GRID_ROW_HEIGHT = 144      // 单格高度：padding 32 + 图标 48 + 名称两行约 36 + 大小 16 + 间距
const GRID_OVERSCAN_ROWS = 2     // 上下各多渲染两行，快速滚动时不出现空白

const gridContainerRef = ref<HTMLElement | null>(null)
const gridScrollTop = ref(0)
const gridWidth = ref(0)
const gridViewportHeight = ref(0)

// 列数由容器宽度决定：与 CSS 的 repeat(auto-fill, minmax(120px, 1fr)) 同口径
const gridColumns = computed(() => {
  if (gridWidth.value <= 0) return 1
  const usable = gridWidth.value - GRID_GAP * 2   // 左右 padding
  return Math.max(1, Math.floor((usable + GRID_GAP) / (GRID_MIN_COL_WIDTH + GRID_GAP)))
})

// 单格实际宽度：把扣除 gap 后的宽度均分
const gridColWidth = computed(() => {
  const usable = gridWidth.value - GRID_GAP * 2
  return Math.max(GRID_MIN_COL_WIDTH,
    (usable - GRID_GAP * (gridColumns.value - 1)) / gridColumns.value)
})

const gridRowStride = computed(() => GRID_ROW_HEIGHT + GRID_GAP)
const gridRowCount = computed(() =>
  Math.ceil(fileStore.sortedFiles.length / gridColumns.value))
// spacer 撑起总高度，让滚动条反映真实内容量
const gridTotalHeight = computed(() =>
  gridRowCount.value * gridRowStride.value + GRID_GAP)

// 只渲染可见行（加上下各两行缓冲）。绝对定位到各自的行列位置
const visibleGridCells = computed(() => {
  const cols = gridColumns.value
  const total = fileStore.sortedFiles.length
  if (total === 0 || gridViewportHeight.value <= 0) return []

  const firstRow = Math.max(0,
    Math.floor(gridScrollTop.value / gridRowStride.value) - GRID_OVERSCAN_ROWS)
  const rowsOnScreen = Math.ceil(gridViewportHeight.value / gridRowStride.value)
    + GRID_OVERSCAN_ROWS * 2
  const lastRow = Math.min(gridRowCount.value, firstRow + rowsOnScreen)

  const start = firstRow * cols
  const end = Math.min(total, lastRow * cols)

  const cells = []
  for (let i = start; i < end; i++) {
    cells.push({
      item: fileStore.sortedFiles[i],
      top: Math.floor(i / cols) * gridRowStride.value + GRID_GAP,
      left: GRID_GAP + (i % cols) * (gridColWidth.value + GRID_GAP),
      width: gridColWidth.value
    })
  }
  return cells
})

// 容器尺寸变化时重算列数与视口高度
let gridResizeObserver: ResizeObserver | null = null
watch(gridContainerRef, (el) => {
  gridResizeObserver?.disconnect()
  gridResizeObserver = null
  if (!el) return
  gridResizeObserver = new ResizeObserver(entries => {
    const box = entries[0]?.contentRect
    gridWidth.value = box?.width ?? 0
    gridViewportHeight.value = el.clientHeight
  })
  gridResizeObserver.observe(el)
  gridWidth.value = el.clientWidth
  gridViewportHeight.value = el.clientHeight
}, { immediate: true })

onUnmounted(() => {
  gridResizeObserver?.disconnect()
  gridResizeObserver = null
})

// ─── 行操作菜单（受控单实例）───
// el-table-v2 没有"行内下拉"的插槽，而每行各建一个 ElDropdown 会泄漏 popper 到 body
// （实测点一次操作按钮就留下 13 个残留菜单，关闭后不销毁，越滚越多）。
// el-dropdown 的 virtual-triggering 模式在本项目里 ref 绑不上、handleOpen 调不动，
// 故改用最直接的原生定位菜单：一个 div，位置取自行按钮的 getBoundingClientRect，
// 全局只有一个实例，不存在泄漏面。
const rowActionsTarget = ref<FileItem | null>(null)
const rowActionsPos = ref<{ top: number; left: number } | null>(null)

function openRowActions(event: MouseEvent, item: FileItem) {
  const el = event.currentTarget as HTMLElement
  const rect = el.getBoundingClientRect()
  rowActionsTarget.value = item
  // 菜单出现在按钮下方，右对齐按钮
  rowActionsPos.value = { top: rect.bottom + 6, left: Math.max(8, rect.right - 140) }
}

function closeRowActions() {
  rowActionsPos.value = null
  rowActionsTarget.value = null
}

function runRowAction(command: string) {
  const target = rowActionsTarget.value
  closeRowActions()
  if (target) handleCommand(command, target)
}

// 点击菜单外侧或按 Esc 关闭。监听挂在 document 上，关闭时移除
watch(rowActionsPos, (pos) => {
  if (!pos) return
  const onDocClick = (e: MouseEvent) => {
    const target = e.target as HTMLElement | null
    if (target?.closest('.row-actions-menu') || target?.closest('.row-action-btn')) return
    closeRowActions()
  }
  const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') closeRowActions() }
  // 延后挂载：否则本次点击本身会被当成"外侧点击"立刻关掉
  const timer = setTimeout(() => {
    document.addEventListener('click', onDocClick)
    document.addEventListener('keydown', onKey)
  }, 0)
  return () => {
    clearTimeout(timer)
    document.removeEventListener('click', onDocClick)
    document.removeEventListener('keydown', onKey)
  }
})

// 全选/部分选：作用于当前已加载的全部条目（虚拟滚动下"全部"就是 store 里的全部）
const isAllSelected = computed(() =>
  fileStore.sortedFiles.length > 0 && selectedIds.value.size === fileStore.sortedFiles.length)
const isPartiallySelected = computed(() =>
  selectedIds.value.size > 0 && selectedIds.value.size < fileStore.sortedFiles.length)

function toggleSelectAll(checked: boolean) {
  if (checked) {
    fileStore.selectedItems = [...fileStore.sortedFiles]
  } else {
    fileStore.clearSelection()
  }
}

// 表格排序：受控，交给 store 的 setSort（内部会切换同字段的方向）
const tableSortBy = computed(() => ({
  key: fileStore.sortBy === 'size' ? 'sizeBytes'
    : fileStore.sortBy === 'type' ? 'extension'
    : fileStore.sortBy === 'createdAt' ? 'updatedAt'
    : 'name',
  order: fileStore.sortDesc ? TableV2SortOrder.DESC : TableV2SortOrder.ASC
}))

function handleColumnSort({ key, order }: ColumnSortParams<FileItem>) {
  const field = sortFieldByColumn[String(key)] ?? 'name'
  if (fileStore.sortBy === field) {
    // 同字段：store 的 setSort 会切换方向，仅在方向确实不同时调用
    const currentDesc = fileStore.sortDesc
    const wantDesc = order === TableV2SortOrder.DESC
    if (currentDesc !== wantDesc) fileStore.setSort(field)
  } else {
    fileStore.sortBy = field
    fileStore.sortDesc = order === TableV2SortOrder.DESC
  }
}

// 行事件：table-v2 的行事件是统一入口，没有"操作列不响应点击"的列概念，
// 因此改为在单元格渲染侧不挂事件、只在行级判断点击来源
const rowEventHandlers = computed<RowEventHandlers>(() => ({
  onClick: ({ rowData, event }) => {
    const target = (event as MouseEvent).target as HTMLElement | null
    // 操作列与复选框内的点击不触发行选中
    if (target?.closest('.row-action-btn') || target?.closest('.el-checkbox')) return
    const isCtrl = (event as MouseEvent).ctrlKey || (event as MouseEvent).metaKey
    fileStore.selectItem(rowData as FileItem, isCtrl)
  },
  onDblclick: ({ rowData }) => navigateToFolder(rowData as FileItem),
  onContextmenu: ({ rowData }) => fileStore.selectItem(rowData as FileItem)
}))

// 预览资源 objectURL 管理。单槽语义与 create/revoke 的配对关系抽到
// utils/previewUrl.ts，那里有纯单元测试锁住"每次 create 最终有且仅有一次 revoke"
// （本项目没有 @vue/test-utils，视图层没有组件测试兜底，逻辑必须可单测）。
// 原实现把 URL 存在只增不减的 Record 里：关闭对话框不清、卸载不 revoke、
// 同一文件重复预览还重新 fetch，objectURL 指向的 blob 会一直钉在内存里。
const previewUrlManager = createPreviewUrlManager(
  (url) => URL.revokeObjectURL(url)
)

function revokePreviewUrl() {
  previewUrlManager.release()
}

function getPreviewUrl(item: FileItem): string | undefined {
  return previewUrlManager.urlFor(item.path)
}

async function ensurePreviewUrl(item: FileItem): Promise<void> {
  // 已缓存同一个文件则直接复用，不重复 fetch
  if (previewUrlManager.has(item.path)) return

  try {
    const token = localStorage.getItem('token')
    const response = await fetch(fileApi.getDownloadUrl(item.path), {
      headers: { Authorization: `Bearer ${token}` }
    })
    if (!response.ok) throw new Error(String(response.status))
    const blob = await response.blob()
    // put 内部会先释放上一个 URL，避免同时钉住多个 blob
    previewUrlManager.put(item.path, URL.createObjectURL(blob))
  } catch {
    ElMessage.error(t('filesView.loadFailedText'))
  }
}

// 剪贴板状态文本（数据源已迁到 file store）
const clipboardText = computed(() => {
  if (!fileStore.clipboard) return ''
  const action = fileStore.clipboard.action === 'copy' ? t('filesView.copyWord') : t('filesView.cutAction')
  return t('filesView.clipboardStatus', { action, count: fileStore.clipboard.items.length })
})

// 处理右键菜单命令
function handleCommand(command: string, item: FileItem) {
  switch (command) {
    case 'download':
      downloadFile(item)
      break
    case 'preview':
      openPreview(item)
      break
    case 'info':
      // 详情：/files/info 的元数据面板（MIME/精确时间/隐藏只读标记）
      openInfoDialog(item)
      break
    case 'ocr':
      // 行菜单直认：打开预览抽屉（顺带取已有结果）并立即重新识别，
      // 识别结果落在抽屉里的 OCR 区块
      if (isImage(item)) {
        openPreview(item)
        void runOcr(item)
      }
      break
    case 'permanent-delete':
      // 永久删除：不进回收站（入口在回收站页之外唯一一处）
      handleDeleteItem(item, true)
      break
    case 'rename':
      openRenameDialog(item)
      break
    case 'copy':
      fileStore.selectItem(item)
      copyToClipboard()
      break
    case 'cut':
      fileStore.selectItem(item)
      cutToClipboard()
      break
    case 'delete':
      handleDeleteItem(item)
      break
    case 'share':
      openShareDialog(item)
      break
    case 'version':
      // 历史版本：文件才有（文件夹无版本语义）。入口从预览抽屉补到行菜单，
      // 否则不可预览的文件类型（压缩包等）到不了版本历史
      if (!item.isFolder) openVersionHistory(item)
      break
  }
}

// 打开分享对话框
function openShareDialog(item: FileItem) {
  shareTarget.value = item
  shareForm.value = {
    fileItemId: item.id,
    accessType: ShareAccessType.Public,
    password: '',
    permission: PermissionLevel.Read,
    expiresAt: undefined,
    maxDownloads: undefined
  }
  shareResult.value = null
  expiresOption.value = 'never'
  maxDownloadsOption.value = 'unlimited'
  shareDialogVisible.value = true
}

// 生成随机密码
function generatePassword() {
  const chars = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789'
  let password = ''
  for (let i = 0; i < 8; i++) {
    // ITM-N1：分享密码必须用加密随机数，Math.random 可预测
    const randomValues = new Uint32Array(1)
    crypto.getRandomValues(randomValues)
    password += chars.charAt(randomValues[0] % chars.length)
  }
  shareForm.value.password = password
}

// 计算过期时间
function computeExpiresAt(): string | undefined {
  if (expiresOption.value === 'never') return undefined
  if (expiresOption.value === 'custom') return shareForm.value.expiresAt
  
  const now = new Date()
  const days = parseInt(expiresOption.value.replace('d', ''))
  now.setDate(now.getDate() + days)
  return now.toISOString()
}

// 计算下载限制
function computeMaxDownloads(): number | undefined {
  if (maxDownloadsOption.value === 'unlimited') return undefined
  if (maxDownloadsOption.value === 'custom') return shareForm.value.maxDownloads
  return parseInt(maxDownloadsOption.value)
}

// 创建分享
async function handleCreateShare() {
  if (!shareTarget.value) return
  
  shareLoading.value = true
  try {
    const request: CreateFileShareRequest = {
      fileItemId: shareTarget.value.id,
      accessType: shareForm.value.accessType,
      permission: shareForm.value.permission,
      expiresAt: computeExpiresAt(),
      maxDownloads: computeMaxDownloads()
    }
    
    if (shareForm.value.accessType === ShareAccessType.Password && shareForm.value.password) {
      request.password = shareForm.value.password
    }
    
    const result = await fileShareApi.create(request)
    if (result.data) {
      shareResult.value = result.data
      ElMessage.success(t('filesView.shareCreateSuccess'))
    }
  } catch (error: any) {
    ElMessage.error(error.message || t('filesView.shareCreateFailed'))
  } finally {
    shareLoading.value = false
  }
}

// 复制分享链接
async function copyShareLink() {
  if (!shareResult.value) return
  try {
    await navigator.clipboard.writeText(shareResult.value.shareUrl)
    ElMessage.success(t('filesView.linkCopied'))
  } catch {
    ElMessage.error(t('filesView.linkCopyFailed'))
  }
}

// 右键菜单
function openContextMenu(_event: MouseEvent, item: FileItem) {
  fileStore.selectItem(item)
}

// ═══ 大目录渲染 ═══
// 原实现的渐进渲染（slice + renderLimit + 滚动追加）已整体移除：
// slice 每次返回新数组引用，el-table 的 data watch 是引用比较，
// 于是每次追加都触发全量表体重建，实测追加 200 行耗时
// 1888ms → 2765ms → 4119ms（超线性），主线程被 long task 占满。
// 改用 el-table-v2 虚拟滚动后，无论目录多大都只渲染视口内的行，
// 不再需要分批逻辑。

// ═══ 实时刷新（SignalR 目录订阅，多标签页/多用户变更自动同步）═══
let unsubscribe: (() => void) | null = null
let stopRealtimeWatch: (() => void) | null = null
// 快切目录时多个 resubscribe 会交叉进行（退订与订阅都是 async）：
// 旧调用后完成就会用旧订阅覆盖新订阅、并用旧 stopRealtimeWatch 拆掉
// 新调用的连接状态监听。序号闸保证只有最后一次调用允许落地
let resubscribeSeq = 0

async function resubscribe(path: string) {
  const seq = ++resubscribeSeq
  try {
    await unsubscribe?.()
    // 退订等待期间又来了更新的调用：旧订阅已退，新的那次会自己订，
    // 这里直接让位。unsubscribe 变量不碰——它可能已指向新订阅
    if (seq !== resubscribeSeq) return
    const cleanup = await subscribeDirectory(path, (changedPath) => {
      // 仅当变更位于当前目录的直接层级时静默刷新（子目录深处的批量变更不打断浏览）
      const parent = changedPath.slice(0, changedPath.lastIndexOf('/')) || '/'
      if (parent === fileStore.currentPath && !fileStore.isLoading) {
        void fileStore.loadDirectory(fileStore.currentPath)
      }
    })
    // 订阅等待期间被更新的调用取代：立刻退掉刚拿到的这个订阅。
    // 不能留给 onUnmounted——那时 unsubscribe 指向的已是新订阅，
    // 这个旧订阅会无声泄漏（服务端订阅列表只增不减）
    if (seq !== resubscribeSeq) {
      void cleanup()
      return
    }
    unsubscribe = cleanup
    realtimeConnected.value = true
    // 跟随底层连接状态：自动重连失败/掉线时把状态栏的"实时"打成断开，
    // 而不是让用户误以为外部变更还会自动出现
    stopRealtimeWatch?.()
    stopRealtimeWatch = watchHubConnectionState((connected) => {
      realtimeConnected.value = connected
    })
  } catch {
    // 订阅失败（SignalR 不可达）：降级为纯 HTTP 语义，状态栏明示断开
    realtimeConnected.value = false
  }
}

onMounted(() => void resubscribe(fileStore.currentPath))
watch(() => fileStore.currentPath, (path) => void resubscribe(path))
onUnmounted(() => {
  void unsubscribe?.()
  stopRealtimeWatch?.()
  // 离开页面前释放预览用的 objectURL，否则指向的 blob 会一直钉在内存里
  revokePreviewUrl()
  // 防抖定时器不属于组件生命周期：卸载后 300ms 到点仍会打 handleSearch，
  // 往已离页的 store 里写搜索结果、白跑一次搜索请求
  if (searchDebounceTimer) {
    clearTimeout(searchDebounceTimer)
    searchDebounceTimer = null
  }
})
</script>

<template>
  <div
    class="files-page"
    @dragenter.prevent="onPageDragEnter"
    @dragover.prevent="onPageDragOver"
    @dragleave.prevent="onPageDragLeave"
    @drop.prevent="onPageDrop"
  >
    <!-- 整页拖放高亮遮罩：拖文件进页面即提示松开上传 -->
    <div v-if="pageDragging" class="page-drop-overlay">
      <div class="page-drop-hint">
        <el-icon :size="40"><Upload /></el-icon>
        <span>{{ $t('filesView.dropToUpload') }}</span>
      </div>
    </div>

    <!-- 工具栏 -->
    <div class="toolbar">
      <div class="toolbar-left">
        <el-button-group>
          <el-tooltip :content="$t('filesView.goBackTip')" placement="bottom">
            <el-button @click="goBack" :disabled="fileStore.currentPath === '/'">
              <el-icon><Back /></el-icon>
            </el-button>
          </el-tooltip>
          <el-tooltip :content="$t('filesView.refreshTip')" placement="bottom">
            <el-button @click="handleRefresh" :loading="fileStore.isLoading">
              <el-icon><Refresh /></el-icon>
            </el-button>
          </el-tooltip>
          <el-tooltip :content="$t('filesView.scanTip')" placement="bottom">
            <el-button @click="handleScanSync" :loading="scanning" type="warning">
              <el-icon><Refresh /></el-icon>
              {{ $t('filesView.scanShort') }}
            </el-button>
          </el-tooltip>
        </el-button-group>
        
        <el-divider direction="vertical" />
        
        <el-button type="primary" @click="createFolderDialogVisible = true">
          <el-icon><FolderAdd /></el-icon>
          {{ $t('filesView.newFolder') }}
        </el-button>
        <el-button @click="openUploadDialog">
          <el-icon><Upload /></el-icon>
          {{ $t('filesView.upload') }}
        </el-button>
        
        <el-divider direction="vertical" />
        
        <el-button-group>
          <el-tooltip :content="$t('filesView.copyTip')" placement="bottom">
            <el-button @click="copyToClipboard" :disabled="!fileStore.hasSelection">
              <el-icon><CopyDocument /></el-icon>
            </el-button>
          </el-tooltip>
          <el-tooltip :content="$t('filesView.cutTip')" placement="bottom">
            <el-button @click="cutToClipboard" :disabled="!fileStore.hasSelection">
              <el-icon><Rank /></el-icon>
            </el-button>
          </el-tooltip>
          <el-tooltip :content="$t('filesView.pasteTip')" placement="bottom">
            <el-button @click="handlePaste" :disabled="!fileStore.clipboard">
              <el-icon><Document /></el-icon>
            </el-button>
          </el-tooltip>
        </el-button-group>

        <!-- 移动到… / 复制到…：目标目录点选（深层路径一步到位）。
             剪贴板的复制/剪切/粘贴保留为快捷方式 -->
        <el-button size="default" :disabled="!fileStore.hasSelection" @click="openTransferDialog('move')">
          <el-icon><Folder /></el-icon>
          {{ $t('filesView.moveTo') }}
        </el-button>
        <el-button size="default" :disabled="!fileStore.hasSelection" @click="openTransferDialog('copy')">
          <el-icon><CopyDocument /></el-icon>
          {{ $t('filesView.copyTo') }}
        </el-button>

        <el-tooltip :content="$t('filesView.downloadTip')" placement="bottom">
          <el-button @click="downloadSelected" :disabled="!fileStore.hasSelection">
            <el-icon><Download /></el-icon>
          </el-button>
        </el-tooltip>
        <el-tooltip :content="$t('filesView.shareTip')" placement="bottom">
          <el-button @click="shareSelected" :disabled="!fileStore.hasSelection" type="primary">
            <el-icon><Share /></el-icon>
          </el-button>
        </el-tooltip>
        <!-- 目录批量 OCR（ROADMAP Phase 2）：选中一个或多个文件夹时可用。
             下拉给两档：默认跳过已识别，force 全部重识别 -->
        <el-dropdown trigger="click" @command="cmd => runBatchOcr(cmd === 'force')">
          <el-button :disabled="!canBatchOcr" :loading="ocrBatchRunning" :title="$t('filesView.ocrBatchTip')">
            <el-icon><Reading /></el-icon>
            <el-icon><ArrowDown /></el-icon>
          </el-button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item command="normal">{{ $t('filesView.ocrBatchSkipExisting') }}</el-dropdown-item>
              <el-dropdown-item command="force">{{ $t('filesView.ocrBatchForce') }}</el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
        <!-- 删除：默认软删进回收站（可在回收站页恢复）；永久删除不进回收站 -->
        <el-tooltip :content="$t('filesView.delete')" placement="bottom">
          <el-button type="danger" @click="handleDelete(false)" :disabled="!fileStore.hasSelection">
            <el-icon><Delete /></el-icon>
          </el-button>
        </el-tooltip>
        <el-tooltip :content="$t('filesView.permanentDeleteTip')" placement="bottom">
          <el-button type="danger" plain @click="handleDelete(true)" :disabled="!fileStore.hasSelection">
            <el-icon><DeleteFilled /></el-icon>
          </el-button>
        </el-tooltip>
      </div>
      
      <div class="toolbar-right">
        <el-tooltip :content="clipboardText" v-if="fileStore.clipboard">
          <el-tag closable @close="fileStore.clearClipboard()" size="small">
            {{ fileStore.clipboard.action === 'copy' ? $t('filesView.copiedBadge') : $t('filesView.cutTip') }} {{ fileStore.clipboard.items.length }}
          </el-tag>
        </el-tooltip>
        
        <el-tooltip :content="$t('filesView.searchTip')" placement="bottom">
          <el-button @click="searchDialogVisible = true">
            <el-icon><Search /></el-icon>
          </el-button>
        </el-tooltip>
        <el-tooltip :content="$t('filesView.recycleBinTip')" placement="bottom">
          <router-link to="/recycle-bin">
            <el-button>
              <el-icon><DeleteFilled /></el-icon>
            </el-button>
          </router-link>
        </el-tooltip>
        <el-tooltip :content="fileStore.showHidden ? $t('filesView.hideHiddenTip') : $t('filesView.showHiddenTip')" placement="bottom">
          <el-button @click="toggleShowHidden" :type="fileStore.showHidden ? 'primary' : 'default'">
            <el-icon><component :is="fileStore.showHidden ? View : Hide" /></el-icon>
          </el-button>
        </el-tooltip>
        <!-- 文件夹优先开关：关掉后文件与文件夹混排（按大小看全目录时文件夹不再霸顶） -->
        <el-tooltip :content="fileStore.foldersFirst ? $t('filesView.foldersFirstOn') : $t('filesView.foldersFirstOff')" placement="bottom">
          <el-button @click="fileStore.foldersFirst = !fileStore.foldersFirst" :type="fileStore.foldersFirst ? 'primary' : 'default'">
            <el-icon><FolderOpened /></el-icon>
          </el-button>
        </el-tooltip>
        <el-tooltip :content="fileStore.viewMode === 'list' ? $t('filesView.toGridTip') : $t('filesView.toListTip')" placement="bottom">
          <el-button @click="fileStore.toggleViewMode">
            <el-icon><component :is="fileStore.viewMode === 'list' ? Grid : List" /></el-icon>
          </el-button>
        </el-tooltip>
      </div>
    </div>

    <!-- 面包屑 -->
    <el-breadcrumb separator="/" class="breadcrumb">
      <el-breadcrumb-item>
        <span class="breadcrumb-item" @click="navigateToBreadcrumb('/')">
          <el-icon><HomeFilled /></el-icon>
          {{ $t('filesView.rootCrumb') }}
        </span>
      </el-breadcrumb-item>
      <el-breadcrumb-item v-for="crumb in fileStore.breadcrumbs" :key="crumb.path">
        <span class="breadcrumb-item" @click="navigateToBreadcrumb(crumb.path)">
          {{ crumb.name }}
        </span>
      </el-breadcrumb-item>
    </el-breadcrumb>

    <!-- 列表视图：el-table-v2 虚拟滚动，只渲染视口内的行 -->
    <div v-if="fileStore.viewMode === 'list'" v-loading="fileStore.isLoading" class="table-v2-wrap">
      <el-auto-resizer @resize="({ height, width }) => { tableSize = { height, width } }">
        <template #default="{ height, width }">
          <el-table-v2
            :columns="columns"
            :data="fileStore.sortedFiles"
            :width="width"
            :height="height"
            :row-height="ROW_HEIGHT"
            :sort-by="tableSortBy"
            :row-class="rowClass"
            :row-event-handlers="rowEventHandlers"
            row-key="id"
            ref="fileTableRef"
            @column-sort="handleColumnSort"
          />
        </template>
      </el-auto-resizer>
    </div>

    <!-- 网格视图：虚拟滚动，只渲染可见行（加两行缓冲），绝对定位到各自行列 -->
    <div
      v-else
      ref="gridContainerRef"
      class="grid-view"
      v-loading="fileStore.isLoading"
      @scroll.passive="gridScrollTop = ($event.target as HTMLElement).scrollTop"
    >
      <div class="grid-spacer" :style="{ height: gridTotalHeight + 'px' }">
        <div
          v-for="cell in visibleGridCells"
          :key="cell.item.id"
          class="grid-item"
          :class="{ selected: selectedIds.has(cell.item.id) }"
          :style="{ top: cell.top + 'px', left: cell.left + 'px', width: cell.width + 'px' }"
          @click="fileStore.selectItem(cell.item, $event.ctrlKey)"
          @dblclick="navigateToFolder(cell.item)"
          @contextmenu.prevent="openContextMenu($event, cell.item)"
        >
          <!-- 行操作按钮：与列表视图同一套全局单实例菜单（openRowActions）。
               悬停显示，触屏（无 hover）常显 -->
          <button
            class="grid-item-actions"
            :title="$t('filesView.colActions')"
            @click.stop="openRowActions($event, cell.item)"
          >···</button>
          <div class="grid-item-icon">
            <el-icon :size="48" :color="getFileIconColor(cell.item)">
              <component :is="getFileIcon(cell.item)" />
            </el-icon>
          </div>
          <div class="grid-item-name" :title="cell.item.name">{{ cell.item.name }}</div>
          <div class="grid-item-size">{{ cell.item.isFolder ? '' : formatSize(cell.item.sizeBytes) }}</div>
        </div>
      </div>
      <el-empty v-if="fileStore.sortedFiles.length === 0 && !fileStore.isLoading" :description="$t('filesView.emptyFolderList')" />
    </div>

    <!-- 状态栏 -->
    <div class="status-bar">
      <span v-if="fileStore.directoryContent">
        {{ fileStore.directoryContent.folderCount }} {{ $t('filesView.folderUnit') }}
        {{ fileStore.directoryContent.fileCount }} {{ $t('filesView.fileUnit') }}
        <span v-if="fileStore.directoryContent.totalSizeBytes > 0">
          ({{ formatSize(fileStore.directoryContent.totalSizeBytes) }})
        </span>
      </span>
      <span v-if="fileStore.hasSelection" class="selection-info">
        {{ $t('filesView.selectedCount', { count: fileStore.selectionCount }) }}
      </span>
      <!-- 数据新鲜度与实时通道状态：更新时间让用户知道看到的是多久前的，
           实时断开时明示（否则"外部变更自动同步"静默失效） -->
      <span v-if="fileStore.lastCheckedAt" class="status-meta">
        {{ $t('filesView.updatedAt', { time: fileStore.lastCheckedAt.toLocaleTimeString('zh-CN', { hour12: false }) }) }}
      </span>
      <span class="status-meta" :class="{ 'realtime-off': !realtimeConnected }">
        <span class="realtime-dot" :class="{ on: realtimeConnected }" />
        {{ realtimeConnected ? $t('filesView.realtimeOn') : $t('filesView.realtimeOff') }}
      </span>
    </div>

    <!-- 新建文件夹抽屉 -->
    <el-drawer v-model="createFolderDialogVisible" :title="$t('filesView.newFolderTitle')" :size="drawerSize('360px', '32vw', '440px')" :mask-closable="false">
      <el-input
        v-model="newFolderName"
        :placeholder="$t('filesView.folderNamePlaceholder')"
        @keyup.enter="handleCreateFolder"
        autofocus
      />
      <template #footer>
        <el-button @click="createFolderDialogVisible = false">{{ $t('filesView.cancel') }}</el-button>
        <el-button type="primary" @click="handleCreateFolder">{{ $t('filesView.create') }}</el-button>
      </template>
    </el-drawer>

    <!-- 重命名抽屉 -->
    <el-drawer v-model="renameDialogVisible" :title="$t('filesView.renameTitle')" :size="drawerSize('360px', '32vw', '440px')" :mask-closable="false">
      <el-input
        v-model="newName"
        :placeholder="$t('filesView.newNamePlaceholder')"
        @keyup.enter="handleRename"
        autofocus
      />
      <template #footer>
        <el-button @click="renameDialogVisible = false">{{ $t('filesView.cancel') }}</el-button>
        <el-button type="primary" @click="handleRename">{{ $t('filesView.ok') }}</el-button>
      </template>
    </el-drawer>

    <!-- 搜索抽屉 -->
    <el-drawer v-model="searchDialogVisible" :title="$t('filesView.searchDialogTitle')" :size="drawerSize('520px', '46vw', '760px')" @close="resetSearch">
      <div class="search-form">
        <el-input
          v-model="searchQuery"
          :placeholder="$t('filesView.searchInputPlaceholder')"
          @keyup.enter="handleSearch"
          clearable
          class="search-input"
        >
          <template #append>
            <el-button @click="handleSearch" :loading="searchLoading">{{ $t('filesView.searchTip') }}</el-button>
          </template>
        </el-input>
        
        <div class="search-filters">
          <el-select v-model="searchItemType" :placeholder="$t('filesView.typeAll')" clearable style="width: 120px">
            <el-option :label="$t('filesView.allOption')" value="" />
            <el-option :label="$t('filesView.typeFile')" value="file" />
            <el-option :label="$t('filesView.typeFolder')" value="folder" />
          </el-select>
          
          <!-- 扩展名筛选：预设组之外可输入任意扩展名（allow-create），
               后端 extension 参数本就收逗号分隔列表 -->
          <el-select
            v-model="searchExtension"
            :placeholder="$t('filesView.extLabel')"
            filterable
            allow-create
            default-first-option
            clearable
            style="width: 150px"
            @change="normalizeSearchExtension"
          >
            <el-option :label="$t('filesView.allOption')" value="" />
            <el-option :label="$t('filesView.extImages')" value=".jpg,.jpeg,.png,.gif,.webp" />
            <el-option :label="$t('filesView.extVideos')" value=".mp4,.avi,.mkv,.mov" />
            <el-option :label="$t('filesView.extAudios')" value=".mp3,.wav,.flac" />
            <el-option :label="$t('filesView.extDocs')" value=".pdf,.doc,.docx,.txt,.md" />
            <el-option :label="$t('filesView.extArchives')" value=".zip,.rar,.7z,.tar" />
          </el-select>
          
          <el-checkbox v-model="searchInCurrentDir">{{ $t('filesView.currentDirOnly') }}</el-checkbox>
          <el-checkbox v-model="searchOcrOnly">{{ $t('filesView.ocrTextOnly') }}</el-checkbox>
        </div>
      </div>
      
      <div class="search-results" v-loading="searchLoading">
        <div v-if="searchResults.length > 0" class="results-header">
          {{ $t('filesView.foundResults', { count: searchTotal }) }}
          <span v-if="searchTotal > searchResults.length" class="results-truncated">
            {{ $t('filesView.searchTruncated', { shown: searchResults.length }) }}
          </span>
        </div>
        <div v-if="searchResults.length > 0" class="results-list">
          <div
            v-for="item in searchResults"
            :key="item.id"
            class="result-item"
            @click="goToSearchResult(item)"
          >
            <el-icon :color="getFileIconColor(item)" :size="20">
              <component :is="getFileIcon(item)" />
            </el-icon>
            <div class="result-info">
              <div class="result-name">{{ item.name }}</div>
              <div class="result-meta">
                <span class="result-path">{{ item.path }}</span>
                <span class="result-size" v-if="!item.isFolder">{{ formatSize(item.sizeBytes) }}</span>
              </div>
            </div>
          </div>
        </div>
        <el-empty v-else-if="searchQuery && !searchLoading" :description="$t('filesView.noMatchFound')" />
      </div>
    </el-drawer>

    <!-- 上传抽屉 -->
    <el-drawer v-model="uploadDialogVisible" :title="$t('filesView.uploadTitle')" :size="drawerSize('420px', '38vw', '560px')" :mask-closable="false">
      <div class="upload-path-info">
        <el-icon><Folder /></el-icon>
        <span>{{ $t('filesView.uploadTo') }} <strong>{{ fileStore.currentPath }}</strong></span>
      </div>
      <el-upload
        drag
        multiple
        :auto-upload="false"
        :on-change="handleFileChange"
        :on-remove="removeUploadFile"
        :file-list="uploadFiles"
      >
        <el-icon class="el-icon--upload"><Upload /></el-icon>
        <div class="el-upload__text">
          {{ $t('filesView.dropHereBefore') }}<em>{{ $t('filesView.dropHereAfter') }}</em>
        </div>
      </el-upload>
      <div class="upload-info" v-if="uploadFiles.length > 0">
        {{ $t('filesView.selectedUploadInfo', { count: uploadFiles.length, size: formatSize(uploadFiles.reduce((sum, f) => sum + (f.raw?.size || 0), 0)) }) }}
      </div>
      <el-progress v-if="uploading" :percentage="uploadProgress" />
      <template #footer>
        <el-button @click="uploadDialogVisible = false">{{ $t('filesView.cancel') }}</el-button>
        <el-button type="primary" @click="handleUpload" :loading="uploading" :disabled="uploadFiles.length === 0">
          {{ $t('filesView.startUpload') }}
        </el-button>
      </template>
    </el-drawer>

    <!-- 预览抽屉：媒体内容要宽屏视野，宽屏 80% 视口（clamp 夹住上下限）、窄屏全宽 -->
    <el-drawer
      v-model="previewDialogVisible"
      :title="previewItem?.name || $t('filesView.previewFallbackTitle')"
      :size="drawerSize('640px', '80vw', '1200px')"
      class="preview-drawer"
      @closed="revokePreviewUrl"
    >
      <div class="preview-content" v-if="previewItem">
        <!-- 图片预览 -->
        <img v-if="isImage(previewItem)" :src="getPreviewUrl(previewItem)" class="preview-image" />
        <!-- 视频预览 -->
        <video v-else-if="isVideo(previewItem)" :src="getPreviewUrl(previewItem)" controls class="preview-video" />
        <!-- 音频预览 -->
        <audio v-else-if="isAudio(previewItem)" :src="getPreviewUrl(previewItem)" controls class="preview-audio" />
        <!-- 文本预览 -->
        <div v-else-if="isTextFile(previewItem)" class="preview-text">
          <pre><code>{{ textContent }}</code></pre>
        </div>
        <!-- 其他文件 -->
        <div v-else class="preview-unsupported">
          <el-icon :size="64" color="var(--el-text-color-placeholder)"><Document /></el-icon>
          <p>{{ $t('filesView.unsupportedPreview') }}</p>
          <el-button type="primary" @click="downloadFile(previewItem)">{{ $t('filesView.downloadFileText') }}</el-button>
        </div>

        <!-- 图片 OCR（ROADMAP Phase 2）：识别图内文字，结果可被全站搜索命中 -->
        <div class="ocr-section" v-if="isImage(previewItem)">
          <div class="ocr-header">
            <span class="ocr-title">{{ $t('filesView.ocrTitle') }}</span>
            <el-button
              size="small"
              :loading="ocrLoading"
              @click="runOcr(previewItem!)"
            >
              {{ ocrText ? $t('filesView.ocrReRecognize') : $t('filesView.ocrRecognize') }}
            </el-button>
          </div>
          <div v-if="ocrLoading" class="ocr-loading">
            <el-icon class="is-loading"><Loading /></el-icon>
            {{ $t('filesView.ocrRunning') }}
          </div>
          <!-- 有位置区域：叠加查看（文字按坐标叠回图片原位，可选中复制）。
               预览大图在抽屉上方（getPreviewUrl），组件出清单与复制入口 -->
          <div v-else-if="ocrText && ocrText.regions.length > 0" class="ocr-text">
            <OcrImageViewer
              :image-url="getPreviewUrl(previewItem!) ?? ''"
              :regions="ocrText.regions"
              :alt="previewItem!.name"
            />
            <div class="ocr-meta">
              {{ $t('filesView.ocrMeta', { count: ocrText.charCount, ms: ocrText.elapsedMs }) }}
            </div>
          </div>
          <!-- 存量识别结果（无位置区域）：纯文本降级展示 -->
          <div v-else-if="ocrText && ocrText.charCount > 0" class="ocr-text">
            <pre>{{ ocrText.text }}</pre>
            <div class="ocr-meta">
              {{ $t('filesView.ocrMeta', { count: ocrText.charCount, ms: ocrText.elapsedMs }) }}
            </div>
          </div>
          <div v-else-if="ocrText" class="ocr-empty">{{ $t('filesView.ocrNoText') }}</div>
          <div v-else class="ocr-empty">{{ $t('filesView.ocrHint') }}</div>
        </div>
      </div>
      <template #footer>
        <el-button @click="previewDialogVisible = false">{{ $t('filesView.close') }}</el-button>
        <el-button
          v-if="previewItem && isImage(previewItem)"
          :loading="thumbClearing"
          @click="clearThumbnailCache"
        >
          {{ $t('filesView.thumbClear') }}
        </el-button>
        <el-button @click="openVersionHistory(previewItem!)" v-if="previewItem && !previewItem.isFolder">
          {{ $t('filesView.versionHistoryBtn') }}
        </el-button>
        <el-button type="primary" @click="downloadFile(previewItem!)" v-if="previewItem">{{ $t('filesView.downloadTip') }}</el-button>
      </template>
    </el-drawer>

    <!-- 历史版本抽屉（ROADMAP Phase 1） -->
    <el-drawer
      v-model="versionDialogVisible"
      :title="$t('filesView.versionHistoryTitle')"
      :size="drawerSize('420px', '38vw', '560px')"
    >
      <div class="version-toolbar">
        <span class="version-hint">{{ $t('filesView.versionCompareHint') }}</span>
        <el-button
          size="small"
          :disabled="versionSelected.length !== 2"
          :loading="versionComparing"
          @click="runVersionCompare"
        >
          {{ $t('filesView.versionCompare') }}
        </el-button>
      </div>
      <div class="version-target" v-if="versionTarget">
        <el-icon :size="20" :color="getFileIconColor(versionTarget)">
          <component :is="getFileIcon(versionTarget)" />
        </el-icon>
        <div class="version-target-info">
          <div class="version-target-name">{{ versionTarget.name }}</div>
          <div class="version-target-path">{{ versionTarget.path }}</div>
        </div>
      </div>

      <el-table
        v-loading="versionLoading"
        :data="versionList"
        :empty-text="$t('filesView.versionEmpty')"
        style="width: 100%"
        @selection-change="onVersionSelectionChange"
      >
        <el-table-column type="selection" width="42" :selectable="() => true" />
        <el-table-column :label="$t('filesView.versionNumberCol')" width="90">
          <template #default="{ row }">
            <span>v{{ row.versionNumber }}</span>
            <el-tag v-if="row.isCurrent" type="success" size="small" style="margin-left: 6px">
              {{ $t('filesView.versionCurrentBadge') }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="$t('filesView.versionSizeCol')" width="110">
          <template #default="{ row }">{{ formatSize(row.fileSize) }}</template>
        </el-table-column>
        <el-table-column :label="$t('filesView.versionTimeCol')" min-width="160">
          <template #default="{ row }">{{ formatDate(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column :label="$t('filesView.delete')" width="170">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="downloadVersion(asVersion(row))">
              {{ $t('filesView.versionDownload') }}
            </el-button>
            <el-button link type="primary" size="small" @click="restoreVersion(asVersion(row))">
              {{ $t('filesView.versionRestore') }}
            </el-button>
            <el-button
              link
              type="danger"
              size="small"
              :disabled="asVersion(row).isCurrent"
              @click="deleteVersion(asVersion(row))"
            >
              {{ $t('filesView.versionDelete') }}
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-drawer>

    <!-- 版本对比对话框（ROADMAP Phase 1） -->
    <el-dialog
      v-model="versionCompareVisible"
      :title="$t('filesView.versionCompareTitle')"
      width="420px"
    >
      <div v-if="versionCompareResult" class="compare-body">
        <div class="compare-row">
          <span class="compare-label">{{ $t('filesView.versionComparePair') }}</span>
          <span class="compare-value">
            v{{ versionCompareResult.version1.versionNumber }} ↔ v{{ versionCompareResult.version2.versionNumber }}
          </span>
        </div>
        <div class="compare-row">
          <span class="compare-label">{{ $t('filesView.versionCompareSize') }}</span>
          <span class="compare-value">
            {{ formatSize(versionCompareResult.version1.fileSize) }} → {{ formatSize(versionCompareResult.version2.fileSize) }}
            （{{ versionCompareResult.sizeDifference >= 0 ? '+' : '' }}{{ formatSize(Math.abs(versionCompareResult.sizeDifference)) }}）
          </span>
        </div>
        <div class="compare-row">
          <span class="compare-label">{{ $t('filesView.versionCompareHash') }}</span>
          <span class="compare-value">
            {{ versionCompareResult.hashDiffers ? $t('filesView.versionCompareHashDiffers') : $t('filesView.versionCompareHashSame') }}
          </span>
        </div>
        <div class="compare-row">
          <span class="compare-label">{{ $t('filesView.versionCompareTime') }}</span>
          <span class="compare-value">
            {{ formatDate(versionCompareResult.version1.createdAt) }} → {{ formatDate(versionCompareResult.version2.createdAt) }}
          </span>
        </div>
      </div>
      <template #footer>
        <el-button @click="versionCompareVisible = false">{{ $t('filesView.close') }}</el-button>
      </template>
    </el-dialog>
    <el-drawer v-model="shareDialogVisible" :title="$t('filesView.shareDialogTitle')" :size="drawerSize('420px', '38vw', '560px')" :mask-closable="false">
      <div class="share-target" v-if="shareTarget">
        <el-icon :size="24" :color="getFileIconColor(shareTarget)">
          <component :is="getFileIcon(shareTarget)" />
        </el-icon>
        <div class="share-target-info">
          <div class="share-target-name">{{ shareTarget.name }}</div>
          <div class="share-target-path">{{ shareTarget.path }}</div>
        </div>
      </div>

      <!-- 分享结果 -->
      <div v-if="shareResult" class="share-result">
        <el-alert type="success" :closable="false" show-icon>
          <template #title>{{ $t('filesView.linkGeneratedTitle') }}</template>
        </el-alert>
        <div class="share-link-box">
          <el-input v-model="shareResult.shareUrl" readonly>
            <template #append>
              <el-button @click="copyShareLink">
                <el-icon><CopyDocument /></el-icon>
                {{ $t('filesView.copyTip') }}
              </el-button>
            </template>
          </el-input>
        </div>
        <div class="share-info-list">
          <div class="share-info-item">
            <span class="label">{{ $t('filesView.accessTypeLabel') }}</span>
            <span>{{ shareResult.accessType === 0 ? $t('filesView.accessPublicCol') : shareResult.accessType === 1 ? $t('filesView.accessPasswordCol') : $t('filesView.accessInternalCol') }}</span>
          </div>
          <div class="share-info-item" v-if="shareResult.accessType === 1 && shareForm.password">
            <span class="label">{{ $t('filesView.passwordLabel') }}</span>
            <span class="password">{{ shareForm.password }}</span>
          </div>
          <div class="share-info-item">
            <span class="label">{{ $t('filesView.permissionLabel') }}</span>
            <span>{{ shareResult.permission === 1 ? $t('filesView.permReadOnlyCol') : shareResult.permission === 2 ? $t('filesView.permReadWriteCol') : $t('filesView.permFullCol') }}</span>
          </div>
          <div class="share-info-item" v-if="shareResult.expiresAt">
            <span class="label">{{ $t('filesView.expiresUntilLabel') }}</span>
            <span>{{ formatDate(shareResult.expiresAt) }}</span>
          </div>
          <div class="share-info-item" v-if="shareResult.maxDownloads">
            <span class="label">{{ $t('filesView.downloadLimitInfoLabel') }}</span>
            <span>{{ $t('filesView.timesSuffix', { count: shareResult.maxDownloads }) }}</span>
          </div>
        </div>
      </div>

      <!-- 分享表单 -->
      <el-form v-else label-width="80px" class="share-form">
        <el-form-item :label="$t('filesView.accessTypeLabel')">
          <el-radio-group v-model="shareForm.accessType">
            <el-radio :value="ShareAccessType.Public">{{ $t('filesView.publicRadio') }}</el-radio>
            <el-radio :value="ShareAccessType.Password">{{ $t('filesView.passwordRadio') }}</el-radio>
            <el-radio :value="ShareAccessType.Internal">{{ $t('filesView.internalRadio') }}</el-radio>
          </el-radio-group>
        </el-form-item>

        <el-form-item :label="$t('filesView.passwordLabel')" v-if="shareForm.accessType === ShareAccessType.Password">
          <el-input v-model="shareForm.password" :placeholder="$t('filesView.passwordSetPlaceholder')" show-password>
            <template #append>
              <el-button @click="generatePassword">{{ $t('filesView.generateRandom') }}</el-button>
            </template>
          </el-input>
        </el-form-item>

        <el-form-item :label="$t('filesView.permissionLabel')">
          <el-radio-group v-model="shareForm.permission">
            <el-radio :value="PermissionLevel.Read">{{ $t('filesView.readOnly') }}</el-radio>
            <el-radio :value="PermissionLevel.ReadWrite">{{ $t('filesView.readWrite') }}</el-radio>
            <el-radio :value="PermissionLevel.FullControl">{{ $t('filesView.fullControl') }}</el-radio>
          </el-radio-group>
        </el-form-item>

        <el-form-item :label="$t('filesView.expiryLabel')">
          <el-radio-group v-model="expiresOption">
            <el-radio value="never">{{ $t('filesView.neverExpire') }}</el-radio>
            <el-radio value="1d">{{ $t('filesView.oneDay') }}</el-radio>
            <el-radio value="7d">{{ $t('filesView.sevenDays') }}</el-radio>
            <el-radio value="30d">{{ $t('filesView.thirtyDays') }}</el-radio>
            <el-radio value="custom">{{ $t('filesView.custom') }}</el-radio>
          </el-radio-group>
          <el-date-picker
            v-if="expiresOption === 'custom'"
            v-model="shareForm.expiresAt"
            type="datetime"
            :placeholder="$t('filesView.expiryLabel')"
            style="margin-top: 8px; width: 100%"
          />
        </el-form-item>

        <el-form-item :label="$t('filesView.downloadLimitLabel')">
          <el-radio-group v-model="maxDownloadsOption">
            <el-radio value="unlimited">{{ $t('filesView.unlimited') }}</el-radio>
            <el-radio value="1">{{ $t('filesView.once') }}</el-radio>
            <el-radio value="10">{{ $t('filesView.times10') }}</el-radio>
            <el-radio value="100">{{ $t('filesView.times100') }}</el-radio>
            <el-radio value="custom">{{ $t('filesView.custom') }}</el-radio>
          </el-radio-group>
          <el-input-number
            v-if="maxDownloadsOption === 'custom'"
            v-model="shareForm.maxDownloads"
            :min="1"
            :max="10000"
            :placeholder="$t('filesView.downloadLimitLabel')"
            style="margin-top: 8px; width: 100%"
          />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="shareDialogVisible = false">{{ shareResult ? $t('filesView.closeText') : $t('filesView.cancel') }}</el-button>
        <el-button v-if="!shareResult" type="primary" @click="handleCreateShare" :loading="shareLoading">
          {{ $t('filesView.createShareBtn') }}
        </el-button>
        <el-button v-else type="primary" @click="copyShareLink">
          <el-icon><Link /></el-icon>
          {{ $t('filesView.copyLinkBtn') }}
        </el-button>
      </template>
    </el-drawer>

    <!-- 行操作菜单：全局单实例，虚拟滚动下不会为每行各建一个 ElDropdown
         （那样会把 el-popper 泄漏到 body 且 unmount 不清理）。
         位置由行按钮的 getBoundingClientRect 算出，点击外侧或 Esc 关闭 -->
    <div
      v-if="rowActionsPos && rowActionsTarget"
      class="row-actions-menu"
      :style="{ top: rowActionsPos.top + 'px', left: rowActionsPos.left + 'px' }"
    >
      <button v-if="!rowActionsTarget.isFolder" class="row-actions-item" @click="runRowAction('download')">
        {{ $t('filesView.downloadTip') }}
      </button>
      <button v-if="canPreview(rowActionsTarget)" class="row-actions-item" @click="runRowAction('preview')">
        {{ $t('filesView.actPreview') }}
      </button>
      <button class="row-actions-item" @click="runRowAction('info')">{{ $t('filesView.actInfo') }}</button>
      <button class="row-actions-item" @click="runRowAction('share')">{{ $t('filesView.shareTip') }}</button>
      <button v-if="isImage(rowActionsTarget)" class="row-actions-item" @click="runRowAction('ocr')">
        {{ $t('filesView.ocrTitle') }}
      </button>
      <button v-if="!rowActionsTarget.isFolder" class="row-actions-item" @click="runRowAction('version')">
        {{ $t('filesView.versionHistoryBtn') }}
      </button>
      <button class="row-actions-item" @click="runRowAction('rename')">{{ $t('filesView.actRename') }}</button>
      <button class="row-actions-item" @click="runRowAction('copy')">{{ $t('filesView.copyTip') }}</button>
      <button class="row-actions-item" @click="runRowAction('cut')">{{ $t('filesView.cutTip') }}</button>
      <button class="row-actions-item danger" @click="runRowAction('delete')">{{ $t('filesView.delete') }}</button>
      <button class="row-actions-item danger" @click="runRowAction('permanent-delete')">{{ $t('filesView.permanentDelete') }}</button>
    </div>

    <!-- 移动到… / 复制到…：目标目录点选（复用全站目录浏览器组件） -->
    <DirectoryBrowserDialog
      v-model="transferDialogVisible"
      :title="transferMode === 'move' ? $t('filesView.moveToTitle') : $t('filesView.copyToTitle')"
      @select="runTransferTo"
    />

    <!-- 文件详情面板：/files/info 元数据（MIME/精确时间/隐藏只读标记） -->
    <el-dialog v-model="infoDialogVisible" :title="$t('filesView.infoTitle')" width="480px" align-center>
      <div v-loading="infoLoading" class="info-panel">
        <template v-if="infoData">
          <div class="info-row">
            <span class="info-key">{{ $t('filesView.colName') }}</span>
            <span class="info-value">{{ infoData.name }}</span>
          </div>
          <div class="info-row">
            <span class="info-key">{{ $t('filesView.infoPath') }}</span>
            <span class="info-value mono">{{ infoData.path }}</span>
          </div>
          <div class="info-row">
            <span class="info-key">{{ $t('filesView.colType') }}</span>
            <span class="info-value">{{ infoData.isFolder ? $t('filesView.typeFolder') : $t('filesView.typeFile') }}</span>
          </div>
          <div class="info-row" v-if="!infoData.isFolder">
            <span class="info-key">{{ $t('filesView.infoSize') }}</span>
            <span class="info-value">{{ formatSize(infoData.sizeBytes) }}</span>
          </div>
          <div class="info-row" v-if="infoData.mimeType">
            <span class="info-key">{{ $t('filesView.mimeType') }}</span>
            <span class="info-value mono">{{ infoData.mimeType }}</span>
          </div>
          <div class="info-row" v-if="infoData.extension">
            <span class="info-key">{{ $t('filesView.extLabel') }}</span>
            <span class="info-value mono">{{ infoData.extension }}</span>
          </div>
          <div class="info-row">
            <span class="info-key">{{ $t('filesView.infoCreatedAt') }}</span>
            <span class="info-value">{{ formatDate(infoData.createdAt) }}</span>
          </div>
          <div class="info-row" v-if="infoData.updatedAt">
            <span class="info-key">{{ $t('filesView.colModified') }}</span>
            <span class="info-value">{{ formatDate(infoData.updatedAt) }}</span>
          </div>
          <div class="info-row">
            <span class="info-key">{{ $t('filesView.flags') }}</span>
            <span class="info-value">
              <el-tag v-if="infoData.isHidden" size="small" type="info" style="margin-right: 4px">{{ $t('filesView.hiddenFlag') }}</el-tag>
              <el-tag v-if="infoData.isReadOnly" size="small" type="warning">{{ $t('filesView.readOnlyFlag') }}</el-tag>
              <span v-if="!infoData.isHidden && !infoData.isReadOnly">{{ $t('common.none') }}</span>
            </span>
          </div>
        </template>
      </div>
      <template #footer>
        <el-button @click="infoDialogVisible = false">{{ $t('common.close') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style lang="scss" scoped>
.files-page {
  height: 100%;
  display: flex;
  flex-direction: column;
  background: var(--el-bg-color);
  border-radius: var(--mac-radius-card);
  padding: 16px;
  box-shadow: var(--card-shadow, 0 2px 12px rgba(0, 0, 0, 0.06));
  border: 1px solid var(--el-border-color-lighter);
  // 拖放遮罩的定位锚（absolute inset-0）
  position: relative;
}

.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  flex-wrap: wrap;
  gap: 8px;
  
  .toolbar-left, .toolbar-right {
    display: flex;
    align-items: center;
    gap: 8px;
    flex-wrap: wrap;
  }
}

.breadcrumb {
  margin-bottom: 16px;
  padding: 12px 16px;
  background: var(--el-fill-color-light);
  border-radius: var(--mac-radius-control);
  border: 1px solid var(--el-border-color-lighter);

  .breadcrumb-item {
    cursor: pointer;
    display: inline-flex;
    align-items: center;
    gap: 4px;
    transition: color var(--mac-duration-fast);
    
    &:hover {
      color: var(--el-color-primary);
    }
  }
}

// h() 创建的单元格元素（列 cellRenderer、行操作按钮）运行在 el-table-v2 子组件的
// 渲染上下文里，拿不到本组件的 scoped data-v 属性，普通 scoped 规则匹配不到——
// 自 el-table-v2 迁移（4ca712c）起这批样式一直没生效（行按钮只有 17px、文件名换行）。
// :deep() 编译为 [data-v-x] .cls，根元素带 data-v，模板与 h() 创建的后代都能命中，
// 且选择器仍以本组件根为锚，不会泄漏到其他视图
:deep(.file-name) {
  display: flex;
  align-items: center;
  gap: 8px;
  // 长文件名在 44px 固定行高的单元格里必须单行省略号：flex 子项默认
  // min-width:auto 不可收缩，中文名无空格会整体顶出单元格被 mid-glyph 裁切
  min-width: 0;
}

:deep(.file-name-text) {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

// 窄屏名称列的两级结构：主行文件名 + 次级行元信息（大小·类型·时间）。
// 三列在窄屏收进名称单元格，信息不丢（iOS 文件 App 同款模式）
:deep(.file-name-main) {
  display: flex;
  flex-direction: column;
  min-width: 0;
  gap: 2px;
}

:deep(.file-name-meta) {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

:deep(.hidden-tag) {
  font-size: 12px;
  line-height: 1;
  padding: 2px 6px;
  border-radius: var(--mac-radius-pill);
  background: var(--el-fill-color);
  color: var(--el-text-color-secondary);
  flex-shrink: 0;
}

// el-table-v2 的容器必须有确定高度，auto-resizer 才能测出可用空间
.table-v2-wrap {
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

// el-table-v2 渲染在组件内部，scoped 样式要用 :deep 才能穿透
:deep(.el-table-v2__row.selected-row) {
  background-color: var(--el-color-primary-light-9) !important;
}

:deep(.el-table-v2__header-cell) {
  background: var(--el-fill-color-lighter);
}

// 行操作按钮：h() 创建于 cellRenderer，同 :deep() 理由见 .file-name 处注释。
// el-table-v2 的 cellRenderer 里渲染的按钮，需要明确的尺寸与可点区域
:deep(.row-action-btn) {
  width: 28px;
  height: 28px;
  border: none;
  border-radius: var(--mac-radius-pill);
  background: transparent;
  color: var(--el-text-color-regular);
  cursor: pointer;
  font-size: 16px;
  line-height: 1;

  &:hover {
    background: var(--el-fill-color);
    color: var(--el-color-primary);
  }
}

// 行操作菜单：position fixed 跟随视口，避开 el-table-v2 内部的 overflow 裁剪
.row-actions-menu {
  position: fixed;
  z-index: 3000;
  min-width: 140px;
  padding: 4px 0;
  background: var(--mac-vibrancy-bg);
  backdrop-filter: var(--mac-vibrancy-blur);
  -webkit-backdrop-filter: var(--mac-vibrancy-blur);
  border: 1px solid var(--mac-hairline);
  border-radius: var(--mac-radius-popover);
  box-shadow: var(--card-shadow-hover);

  .row-actions-item {
    display: block;
    width: 100%;
    padding: 8px 16px;
    border: none;
    background: transparent;
    color: var(--el-text-color-regular);
    font-size: 13px;
    text-align: left;
    cursor: pointer;
    white-space: nowrap;

    &:hover {
      background: var(--el-color-primary-light-9);
      color: var(--el-color-primary);
    }

    &.danger {
      color: var(--el-color-danger);

      &:hover {
        background: var(--el-color-danger-light-9);
        color: var(--el-color-danger);
      }
    }
  }
}

:deep(.el-table) {
  --el-table-bg-color: transparent;
  --el-table-tr-bg-color: transparent;
  
  .el-table__header-wrapper th {
    background: var(--el-fill-color-lighter) !important;
  }
  
  .el-table__row:hover > td {
    background: var(--el-fill-color-light) !important;
  }
}

// 网格虚拟滚动：容器负责滚动，spacer 撑起总高度让滚动条反映真实内容量，
// 单个格子用绝对定位放到各自的行列位置。列宽与列数由 JS 按容器宽度算出
// （GRID_MIN_COL_WIDTH / GRID_GAP），与原先 CSS 的 auto-fill 同口径
.grid-view {
  flex: 1;
  min-height: 0;
  position: relative;
  overflow: auto;
  padding: 16px;

  .grid-spacer {
    position: relative;
    width: 100%;
  }

  .grid-item {
    position: absolute;
    display: flex;
    flex-direction: column;
    align-items: center;
    height: 144px;   // 与 JS 的 GRID_ROW_HEIGHT 一致，两边必须同源
    padding: 16px 8px;
    border-radius: var(--mac-radius-card);
    cursor: pointer;
    transition: all var(--mac-duration-fast) ease;
    border: 1px solid transparent;
    box-sizing: border-box;

    &:hover {
      background: var(--el-fill-color-light);
      border-color: var(--el-border-color-lighter);
    }

    &.selected {
      background: var(--el-color-primary-light-9);
      border-color: var(--el-color-primary);
    }

    // 行操作按钮：右上角，悬停显示（触屏无 hover 时常显）
    .grid-item-actions {
      position: absolute;
      top: 6px;
      right: 6px;
      width: 26px;
      height: 26px;
      border: none;
      border-radius: 6px;
      background: var(--el-fill-color);
      color: var(--el-text-color-secondary);
      font-size: 16px;
      line-height: 1;
      cursor: pointer;
      opacity: 0;
      transition: opacity var(--mac-duration-fast) ease;

      &:hover {
        background: var(--el-fill-color-dark);
        color: var(--el-text-color-primary);
      }
    }

    &:hover .grid-item-actions { opacity: 1; }

    // 触屏无 hover：操作按钮常显
    @media (hover: none) {
      .grid-item-actions { opacity: 1; }
    }

    .grid-item-icon {
      margin-bottom: 8px;
      flex-shrink: 0;
    }

    .grid-item-name {
      font-size: 13px;
      text-align: center;
      word-break: break-all;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
      color: var(--el-text-color-primary);
    }

    .grid-item-size {
      font-size: 12px;
      color: var(--el-text-color-secondary);
      margin-top: 4px;
      flex-shrink: 0;
    }
  }
}

.status-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  padding: 12px 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  border-top: 1px solid var(--el-border-color-lighter);
  margin-top: auto;

  .selection-info {
    color: var(--el-color-primary);
  }

  // 数据新鲜度与实时通道状态（右端一组）
  .status-meta {
    display: inline-flex;
    align-items: center;
    gap: 5px;

    &.realtime-off {
      color: var(--el-color-warning);
    }
  }

  .realtime-dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--el-color-success);

    &:not(.on) {
      background: var(--el-color-warning);
    }
  }
}

// 整页拖放上传遮罩：虚线框 + 居中提示，拖入即现、离开即消
.page-drop-overlay {
  position: absolute;
  inset: 0;
  z-index: 10;
  display: flex;
  align-items: center;
  justify-content: center;
  background: color-mix(in srgb, var(--el-color-primary) 8%, transparent);
  border: 2px dashed var(--el-color-primary);
  border-radius: var(--mac-radius-card);
  pointer-events: none; // 不拦 drop 事件，事件照常落到根元素

  .page-drop-hint {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8px;
    color: var(--el-color-primary);
    font-size: 14px;
    font-weight: 500;
  }
}

// 文件详情面板：键值行
.info-panel {
  .info-row {
    display: flex;
    gap: 16px;
    padding: 8px 0;
    border-bottom: 1px solid var(--el-border-color-lighter);
    font-size: 13px;

    &:last-child { border-bottom: none; }

    .info-key {
      flex-shrink: 0;
      width: 72px;
      color: var(--el-text-color-secondary);
    }

    .info-value {
      flex: 1;
      min-width: 0;
      word-break: break-all;

      &.mono { font-family: monospace; }
    }
  }
}

.search-results {
  margin-top: 16px;
  max-height: 400px;
  overflow: auto;
  
  .results-header {
    font-size: 13px;
    color: var(--el-text-color-secondary);
    margin-bottom: 12px;
    padding-bottom: 8px;
    border-bottom: 1px solid var(--el-border-color-lighter);
  }
  
  .results-list {
    .result-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px;
      border-radius: var(--mac-radius-control);
      cursor: pointer;
      transition: background var(--mac-duration-fast);
      
      &:hover {
        background: var(--el-fill-color-light);
      }
      
      .result-info {
        flex: 1;
        min-width: 0;
        
        .result-name {
          font-weight: 500;
          color: var(--el-text-color-primary);
        }
        
        .result-meta {
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-top: 4px;
        }
        
        .result-path {
          font-size: 12px;
          color: var(--el-text-color-secondary);
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
          flex: 1;
        }
        
        .result-size {
          font-size: 12px;
          color: var(--el-text-color-secondary);
          margin-left: 12px;
          flex-shrink: 0;
        }
      }
    }
  }
}

.search-form {
  .search-input {
    margin-bottom: 12px;
  }
  
  .search-filters {
    display: flex;
    align-items: center;
    gap: 12px;
    flex-wrap: wrap;
  }
}

.upload-path-info {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px;
  margin-bottom: 16px;
  background: var(--el-fill-color-light);
  border-radius: var(--mac-radius-control);
  font-size: 14px;
  color: var(--el-text-color-secondary);
  
  strong {
    color: var(--el-text-color-primary);
  }
}

.upload-info {
  margin-top: 12px;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.preview-content {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 300px;
  
  .preview-image {
    max-width: 100%;
    max-height: 70vh;
    object-fit: contain;
    border-radius: var(--mac-radius-control);
  }
  
  .preview-video {
    max-width: 100%;
    max-height: 70vh;
    border-radius: var(--mac-radius-control);
  }
  
  .preview-audio {
    width: 100%;
  }
  
  .preview-text {
    width: 100%;
    max-height: 70vh;
    overflow: auto;
    background: var(--el-fill-color-light);
    border-radius: var(--mac-radius-control);
    
    pre {
      margin: 0;
      padding: 16px;
      
      code {
        font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
        font-size: 13px;
        line-height: 1.6;
        color: var(--el-text-color-primary);
        white-space: pre-wrap;
        word-break: break-all;
      }
    }
  }
  
  .preview-unsupported {
    text-align: center;
    color: var(--el-text-color-secondary);
    
    p {
      margin: 16px 0;
    }
  }

  .ocr-section {
    width: 100%;
    margin-top: 16px;
    border: 1px solid var(--el-border-color-lighter);
    border-radius: var(--mac-radius-control);
    padding: 12px;

    .ocr-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 8px;

      .ocr-title {
        font-size: 13px;
        font-weight: 500;
        color: var(--el-text-color-primary);
      }
    }

    .ocr-loading {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      color: var(--el-text-color-secondary);
    }

    .ocr-text {
      pre {
        margin: 0;
        max-height: 200px;
        overflow: auto;
        font-size: 13px;
        line-height: 1.6;
        white-space: pre-wrap;
        word-break: break-all;
        color: var(--el-text-color-regular);
      }

      .ocr-meta {
        margin-top: 8px;
        font-size: 12px;
        color: var(--el-text-color-secondary);
      }
    }

    .ocr-empty {
      font-size: 13px;
      color: var(--el-text-color-secondary);
    }
  }
}

.share-target {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px;
  background: var(--el-fill-color-light);
  border-radius: var(--mac-radius-card);
  margin-bottom: 16px;
  border: 1px solid var(--el-border-color-lighter);
  
  .share-target-info {
    flex: 1;
    min-width: 0;
    
    .share-target-name {
      font-weight: 500;
      font-size: 14px;
      color: var(--el-text-color-primary);
    }
    
    .share-target-path {
      font-size: 12px;
      color: var(--el-text-color-secondary);
      margin-top: 4px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  }
}

.version-target {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px;
  background: var(--el-fill-color-light);
  border-radius: var(--mac-radius-card);
  margin-bottom: 12px;
  border: 1px solid var(--el-border-color-lighter);

  .version-target-info {
    flex: 1;
    min-width: 0;

    .version-target-name {
      font-weight: 500;
      font-size: 14px;
      color: var(--el-text-color-primary);
    }

    .version-target-path {
      font-size: 12px;
      color: var(--el-text-color-secondary);
      margin-top: 4px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  }
}

// 版本抽屉工具栏：对比提示 + 按钮（version-target 的兄弟节点）
.version-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;

  .version-hint {
    font-size: 12px;
    color: var(--el-text-color-secondary);
  }
}

// 版本对比对话框
.compare-body {
  .compare-row {
    display: flex;
    justify-content: space-between;
    gap: 16px;
    padding: 8px 0;
    border-bottom: 1px solid var(--el-border-color-lighter);
    font-size: 13px;

    &:last-child {
      border-bottom: none;
    }

    .compare-label {
      color: var(--el-text-color-secondary);
      white-space: nowrap;
    }

    .compare-value {
      color: var(--el-text-color-primary);
      text-align: right;
      word-break: break-all;
    }
  }
}

.share-result {
  .share-link-box {
    margin: 16px 0;
  }
  
  .share-info-list {
    background: var(--el-fill-color-light);
    border-radius: var(--mac-radius-card);
    padding: 16px;
    border: 1px solid var(--el-border-color-lighter);
    
    .share-info-item {
      display: flex;
      align-items: center;
      padding: 8px 0;
      font-size: 14px;
      
      &:not(:last-child) {
        border-bottom: 1px solid var(--el-border-color-lighter);
      }
      
      .label {
        color: var(--el-text-color-secondary);
        width: 80px;
        flex-shrink: 0;
      }
      
      .password {
        font-family: monospace;
        background: var(--el-color-warning-light-9);
        padding: 2px 8px;
        border-radius: var(--mac-radius-pill);
        color: var(--el-color-warning-dark-2);
      }
    }
  }
}

.share-form {
  :deep(.el-form-item) {
    margin-bottom: 16px;
  }
  
  :deep(.el-radio-group) {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
  }
}
</style>

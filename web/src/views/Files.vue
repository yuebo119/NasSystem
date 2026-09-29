<script setup lang="ts">
import { ref, watch, computed, nextTick, onMounted, onUnmounted } from 'vue'
import { subscribeDirectory } from '@/utils/signalr'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useFileStore } from '@/stores/file'
import { fileApi } from '@/api/file'
import { fileShareApi, ShareAccessType, PermissionLevel, type FileShareDto, type CreateFileShareRequest } from '@/api/fileShare'
import { storageApi } from '@/api/storage'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { UploadFile } from 'element-plus'
import { 
  Refresh, Upload, Delete, Download, 
  CopyDocument, Rank, MoreFilled, FolderAdd, Search,
  Grid, List, View, Hide, DeleteFilled, Back,
  Document, Folder, HomeFilled, Share, Link
} from '@element-plus/icons-vue'
import type { FileItem } from '@/types'
import { formatSize, formatDate, getFileIcon, getFileIconColor } from '@/utils/format'

// ITM-M3 收尾：Files 页 i18n 全量接入
const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const fileStore = useFileStore()

// 对话框状态
const createFolderDialogVisible = ref(false)
const renameDialogVisible = ref(false)
const searchDialogVisible = ref(false)
const recycleBinDialogVisible = ref(false)
const uploadDialogVisible = ref(false)
const previewDialogVisible = ref(false)

// 表单数据
const newFolderName = ref('')
const renameTarget = ref<FileItem | null>(null)
const newName = ref('')
const searchQuery = ref('')
const searchResults = ref<FileItem[]>([])
const searchLoading = ref(false)
const searchItemType = ref('')
const searchExtension = ref('')
const searchInCurrentDir = ref(true)
const recycleBinItems = ref<FileItem[]>([])
const recycleBinLoading = ref(false)
const previewItem = ref<FileItem | null>(null)

// 搜索防抖定时器
let searchDebounceTimer: ReturnType<typeof setTimeout> | null = null

// 上传相关
const uploadFiles = ref<UploadFile[]>([])
const uploading = ref(false)
const uploadProgress = ref(0)

// 文本预览内容
const textContent = ref('')

// 剪贴板
const clipboard = ref<{ items: FileItem[], action: 'copy' | 'cut' } | null>(null)

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

// 表格引用
const fileTableRef = ref<any>(null)

// 监听路由变化加载目录
watch(
  () => route.params.path,
  (path) => {
    const targetPath = Array.isArray(path) ? '/' + path.join('/') : '/'
    fileStore.loadDirectory(targetPath)
  },
  { immediate: true }
)

// 监听搜索关键词变化，自动触发搜索（防抖）
watch(
  [searchQuery, searchItemType, searchExtension, searchInCurrentDir],
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
    }
  }
)

// 聚焦特定文件的方法
async function focusFileItem(itemId: string) {
  await nextTick()
  await nextTick() // 等待 DOM 更新
  
  // 等待一小段时间确保表格渲染完成
  await new Promise(resolve => setTimeout(resolve, 150))
  
  const item = fileStore.sortedFiles.find(f => f.id === itemId)
  if (item) {
    // 清除之前的选择
    fileStore.clearSelection()
    fileStore.selectItem(item)
    
    // 使用 el-table 的方法选中行
    if (fileTableRef.value) {
      // 先清除所有选择
      fileTableRef.value.clearSelection()
      // 选中目标行
      fileTableRef.value.toggleRowSelection(item, true)
      fileTableRef.value.setCurrentRow(item)
      
      // 滚动到该行
      const index = fileStore.sortedFiles.findIndex(f => f.id === item.id)
      if (index >= 0) {
        await nextTick()
        // 尝试多种方式滚动到目标行
        const tableEl = fileTableRef.value.$el
        if (tableEl) {
          // 方法1: 使用 scrollIntoView
          const rows = tableEl.querySelectorAll('.el-table__body-wrapper tbody tr')
          if (rows[index]) {
            rows[index].scrollIntoView({ behavior: 'smooth', block: 'center' })
          } else {
            // 方法2: 手动计算滚动位置
            const tableBody = tableEl.querySelector('.el-table__body-wrapper')
            if (tableBody) {
              const rowHeight = 48
              const targetScroll = index * rowHeight - tableBody.clientHeight / 2 + rowHeight / 2
              tableBody.scrollTo({ top: Math.max(0, targetScroll), behavior: 'smooth' })
            }
          }
        }
      }
    }
  }
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

// 复制到剪贴板
function copyToClipboard() {
  if (fileStore.selectedItems.length === 0) {
    ElMessage.warning(t('filesView.selectFilesFirst'))
    return
  }
  clipboard.value = { items: [...fileStore.selectedItems], action: 'copy' }
  ElMessage.success(t('filesView.copiedCount', { count: fileStore.selectedItems.length }))
}

// 剪切到剪贴板
function cutToClipboard() {
  if (fileStore.selectedItems.length === 0) {
    ElMessage.warning(t('filesView.selectFilesFirst'))
    return
  }
  clipboard.value = { items: [...fileStore.selectedItems], action: 'cut' }
  ElMessage.success(t('filesView.cutCount', { count: fileStore.selectedItems.length }))
}

// 粘贴
async function handlePaste() {
  if (!clipboard.value) {
    ElMessage.warning(t('filesView.clipboardEmpty'))
    return
  }
  const items = [...clipboard.value.items]
  const action = clipboard.value.action
  const destination = fileStore.currentPath
  
  // ITM-N19：一次数组请求，替代逐项循环
  let pasteFailed = 0
  try {
    const paths = items.map(i => i.path)
    if (action === 'copy') {
      const result = await fileApi.copy(paths, destination)
      pasteFailed = ((result.data as { failedItems?: string[] } | null)?.failedItems ?? []).length
    } else {
      const result = await fileApi.move(paths, destination)
      pasteFailed = ((result.data as { failedItems?: string[] } | null)?.failedItems ?? []).length
    }
  } catch {
    pasteFailed = items.length
  }

  const okCount = items.length - pasteFailed
  if (pasteFailed === 0) {
    ElMessage.success(t('filesView.pasteDone', { action: t(action === 'copy' ? 'filesView.copyAction' : 'filesView.moveAction'), count: okCount }))
  } else {
    ElMessage.warning(t('filesView.pastePartial', { action: t(action === 'copy' ? 'filesView.copyAction' : 'filesView.moveAction'), ok: okCount, fail: pasteFailed }))
  }

  if (action === 'cut') {
    clipboard.value = null  // 剪切后清空剪贴板
  }

  await fileStore.loadDirectory(fileStore.currentPath)
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
      pageSize: 200
    })
    if (result.success && result.data) {
      searchResults.value = result.data.items
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
  searchItemType.value = ''
  searchExtension.value = ''
  searchInCurrentDir.value = true
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

// 打开回收站
async function openRecycleBin() {
  recycleBinDialogVisible.value = true
  recycleBinLoading.value = true
  try {
    const result = await fileApi.getRecycleBin()
    if (result.success && result.data) {
      recycleBinItems.value = result.data
    }
  } catch (e) {
    ElMessage.error(t('filesView.loadRecycleBinFailed'))
  } finally {
    recycleBinLoading.value = false
  }
}

// 恢复文件
async function restoreFile(item: FileItem) {
  const result = await fileApi.restore(item.id)
  if (result.success) {
    ElMessage.success(t('filesView.restoreSuccess'))
    recycleBinItems.value = recycleBinItems.value.filter(i => i.id !== item.id)
    await fileStore.loadDirectory(fileStore.currentPath)
  } else {
    ElMessage.error(result.error?.message || t('filesView.restoreFailed'))
  }
}

// 清空回收站
async function emptyRecycleBin() {
  try {
    await ElMessageBox.confirm(t('filesView.confirmEmptyRecycleBin'), t('filesView.emptyRecycleBinTitle'), {
      confirmButtonText: t('filesView.emptyAction'), cancelButtonText: t('filesView.cancel'), type: 'warning'
    })
    const result = await fileApi.emptyRecycleBin()
    if (result.success) {
      ElMessage.success(t('filesView.recycleBinEmptied'))
      recycleBinItems.value = []
    } else {
      ElMessage.error(result.error?.message || t('filesView.emptyFailed'))
    }
  } catch {
    // 取消操作
  }
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
  
  uploading.value = true
  uploadProgress.value = 0
  
  const total = uploadFiles.value.length
  let completed = 0
  let failed = 0
  
  for (const uploadFile of uploadFiles.value) {
    if (uploadFile.raw) {
      try {
        await fileApi.upload(uploadFile.raw, fileStore.currentPath, (progress) => {
          uploadProgress.value = Math.round((completed * 100 + progress) / total)
        })
        completed++
      } catch (e) {
        failed++
      }
    }
  }
  
  uploading.value = false
  uploadFiles.value = []
  
  if (failed === 0) {
    ElMessage.success(t('filesView.uploadCountSuccess', { count: completed }))
  } else {
    ElMessage.warning(t('filesView.uploadPartial', { ok: completed, fail: failed }))
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

// 获取行样式类名
function getRowClassName({ row }: { row: FileItem }): string {
  return selectedIds.value.has(row.id) ? 'selected-row' : ''
}

// 处理行点击
function handleRowClick(row: FileItem, column: any, event: Event) {
  // 如果点击的是操作列，不处理
  if (column?.property === 'actions') return
  // 按住 Ctrl 多选
  const isCtrl = (event as MouseEvent).ctrlKey || (event as MouseEvent).metaKey
  fileStore.selectItem(row, isCtrl)
}

// 处理行双击
function handleRowDblClick(row: FileItem) {
  navigateToFolder(row)
}

// 处理表格选择变化
function handleSelectionChange(selection: FileItem[]) {
  fileStore.selectedItems = selection
}

// 处理表格排序变化
function handleSortChange({ prop, order }: { prop: string, order: string | null }) {
  if (!order) {
    // 取消排序，恢复默认按名称排序
    fileStore.setSort('name')
    return
  }
  
  // 映射列属性到排序字段
  const sortFieldMap: Record<string, 'name' | 'size' | 'createdAt' | 'type'> = {
    name: 'name',
    sizeBytes: 'size',
    extension: 'type',
    createdAt: 'createdAt'
  }
  
  const sortField = sortFieldMap[prop] || 'name'
  const isDesc = order === 'descending'
  
  // 如果当前排序字段相同，只切换方向
  if (fileStore.sortBy === sortField) {
    if (fileStore.sortDesc !== isDesc) {
      fileStore.setSort(sortField) // 这会切换方向
    }
  } else {
    // 设置新的排序字段
    fileStore.sortBy = sortField
    fileStore.sortDesc = isDesc
  }
}

// 预览资源 objectURL 缓存（ITM-N19/G19：token 走 Header，blob 经 createObjectURL 提供给媒体标签）
const previewUrls = ref<Record<string, string>>({})

async function ensurePreviewUrl(item: FileItem): Promise<void> {
  if (previewUrls.value[item.path]) return
  try {
    const token = localStorage.getItem('token')
    const response = await fetch(fileApi.getDownloadUrl(item.path), {
      headers: { Authorization: `Bearer ${token}` }
    })
    if (!response.ok) throw new Error(String(response.status))
    const blob = await response.blob()
    previewUrls.value[item.path] = URL.createObjectURL(blob)
  } catch {
    ElMessage.error(t('filesView.loadFailedText'))
  }
}

// 剪贴板状态文本
const clipboardText = computed(() => {
  if (!clipboard.value) return ''
  const action = clipboard.value.action === 'copy' ? t('filesView.copyWord') : t('filesView.cutAction')
  return t('filesView.clipboardStatus', { action, count: clipboard.value.items.length })
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

// ═══ 大目录渐进渲染（ITM-N5 前端专项：万级目录首屏只渲染 RENDER_BATCH 条，
// 滚动到底部自动追加——保持 el-table/网格现有交互不变的低侵入方案）═══
const RENDER_BATCH = 200
const renderLimit = ref(RENDER_BATCH)
const visibleFiles = computed(() => fileStore.sortedFiles.slice(0, renderLimit.value))
const hasMoreToRender = computed(() => fileStore.sortedFiles.length > renderLimit.value)

function loadMoreOnScroll(event: Event) {
  const el = event.target as HTMLElement
  if (!hasMoreToRender.value) return
  if (el.scrollTop + el.clientHeight >= el.scrollHeight - 120) {
    renderLimit.value += RENDER_BATCH
  }
}

watch(() => fileStore.currentPath, () => {
  renderLimit.value = RENDER_BATCH
})

// ═══ 实时刷新（ITM-N5 收尾// ═══ 实时刷新（ITM-N5 收尾：SignalR 目录订阅，多标签页/多用户变更自动同步）═══
let unsubscribe: (() => void) | null = null

async function resubscribe(path: string) {
  try {
    await unsubscribe?.()
    unsubscribe = await subscribeDirectory(path, (changedPath) => {
      // 仅当变更位于当前目录的直接层级时静默刷新（子目录深处的批量变更不打断浏览）
      const parent = changedPath.slice(0, changedPath.lastIndexOf('/')) || '/'
      if (parent === fileStore.currentPath && !fileStore.isLoading) {
        void fileStore.loadDirectory(fileStore.currentPath)
      }
    })
  } catch {
    // SignalR 不可达时静默降级为纯 HTTP 轮询语义（进入目录仍会拉取）
  }
}

onMounted(() => void resubscribe(fileStore.currentPath))
watch(() => fileStore.currentPath, (path) => void resubscribe(path))
onUnmounted(() => void unsubscribe?.())
</script>

<template>
  <div class="files-page">
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
            <el-button @click="handlePaste" :disabled="!clipboard">
              <el-icon><Document /></el-icon>
            </el-button>
          </el-tooltip>
        </el-button-group>
        
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
        <el-tooltip :content="$t('filesView.delete')" placement="bottom">
          <el-button type="danger" @click="handleDelete(false)" :disabled="!fileStore.hasSelection">
            <el-icon><Delete /></el-icon>
          </el-button>
        </el-tooltip>
      </div>
      
      <div class="toolbar-right">
        <el-tooltip :content="clipboardText" v-if="clipboard">
          <el-tag closable @close="clipboard = null" size="small">
            {{ clipboard.action === 'copy' ? $t('filesView.copiedBadge') : $t('filesView.cutTip') }} {{ clipboard.items.length }}
          </el-tag>
        </el-tooltip>
        
        <el-tooltip :content="$t('filesView.searchTip')" placement="bottom">
          <el-button @click="searchDialogVisible = true">
            <el-icon><Search /></el-icon>
          </el-button>
        </el-tooltip>
        <el-tooltip :content="$t('filesView.recycleBinTip')" placement="bottom">
          <el-button @click="openRecycleBin">
            <el-icon><DeleteFilled /></el-icon>
          </el-button>
        </el-tooltip>
        <el-tooltip :content="fileStore.showHidden ? $t('filesView.hideHiddenTip') : $t('filesView.showHiddenTip')" placement="bottom">
          <el-button @click="toggleShowHidden" :type="fileStore.showHidden ? 'primary' : 'default'">
            <el-icon><component :is="fileStore.showHidden ? View : Hide" /></el-icon>
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

    <!-- 列表视图 -->
    <el-table
      v-if="fileStore.viewMode === 'list'"
      v-loading="fileStore.isLoading"
      :data="visibleFiles"
      height="calc(100vh - 260px)"
      @scroll="loadMoreOnScroll"
      style="width: 100%"
      row-key="id"
      @row-click="handleRowClick"
      @row-dblclick="handleRowDblClick"
      @selection-change="handleSelectionChange"
      @sort-change="handleSortChange"
      :row-class-name="getRowClassName"
      :default-sort="{ prop: fileStore.sortBy === 'type' ? 'extension' : (fileStore.sortBy === 'size' ? 'sizeBytes' : fileStore.sortBy), order: fileStore.sortDesc ? 'descending' : 'ascending' }"
      ref="fileTableRef"
    >
      <el-table-column type="selection" width="50" />
      
      <el-table-column prop="name" :label="$t('filesView.colName')" min-width="300" sortable="custom">
        <template #default="{ row }">
          <div class="file-name">
            <el-icon :size="20" :color="getFileIconColor(row)">
              <component :is="getFileIcon(row)" />
            </el-icon>
            <span>{{ row.name }}</span>
            <el-tag v-if="row.isHidden" size="small" type="info">{{ $t('filesView.hiddenTag') }}</el-tag>
          </div>
        </template>
      </el-table-column>
      
      <el-table-column prop="sizeBytes" :label="$t('filesView.colSize')" width="120" sortable="custom">
        <template #default="{ row }">
          {{ row.isFolder ? '-' : formatSize(row.sizeBytes) }}
        </template>
      </el-table-column>
      
      <el-table-column prop="extension" :label="$t('filesView.colType')" width="100" sortable="custom">
        <template #default="{ row }">
          {{ row.isFolder ? $t('filesView.typeFolder') : (row.extension || '-') }}
        </template>
      </el-table-column>
      
      <el-table-column prop="createdAt" :label="$t('filesView.colModified')" width="180" sortable="custom">
        <template #default="{ row }">
          {{ formatDate(row.updatedAt || row.createdAt) }}
        </template>
      </el-table-column>
      
      <el-table-column :label="$t('filesView.colActions')" width="120" fixed="right" property="actions">
        <template #default="{ row }">
          <el-dropdown trigger="click" @command="(cmd: string) => handleCommand(cmd, row)">
            <el-button :icon="MoreFilled" circle size="small" />
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item v-if="!row.isFolder" command="download">
                  {{ $t('filesView.downloadTip') }}
                </el-dropdown-item>
                <el-dropdown-item v-if="canPreview(row)" command="preview">
                  {{ $t('filesView.actPreview') }}
                </el-dropdown-item>
                <el-dropdown-item command="share">
                  {{ $t('filesView.shareTip') }}
                </el-dropdown-item>
                <el-dropdown-item command="rename">
                  {{ $t('filesView.actRename') }}
                </el-dropdown-item>
                <el-dropdown-item command="copy">
                  {{ $t('filesView.copyTip') }}
                </el-dropdown-item>
                <el-dropdown-item command="cut">
                  {{ $t('filesView.cutTip') }}
                </el-dropdown-item>
                <el-dropdown-item divided command="delete">
                  <el-icon color="var(--el-color-danger)"><Delete /></el-icon>
                  <span style="color: var(--el-color-danger)">{{ $t('filesView.delete') }}</span>
                </el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </template>
      </el-table-column>
    </el-table>

    <!-- 网格视图 -->
    <div v-else class="grid-view" v-loading="fileStore.isLoading" @scroll="loadMoreOnScroll">
      <div
        v-for="item in visibleFiles"
        :key="item.id"
        class="grid-item"
        :class="{ selected: selectedIds.has(item.id) }"
        @click="fileStore.selectItem(item, $event.ctrlKey)"
        @dblclick="navigateToFolder(item)"
        @contextmenu.prevent="openContextMenu($event, item)"
      >
        <div class="grid-item-icon">
          <el-icon :size="48" :color="getFileIconColor(item)">
            <component :is="getFileIcon(item)" />
          </el-icon>
        </div>
        <div class="grid-item-name" :title="item.name">{{ item.name }}</div>
        <div class="grid-item-size">{{ item.isFolder ? '' : formatSize(item.sizeBytes) }}</div>
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
    </div>

    <!-- 新建文件夹对话框 -->
    <el-dialog v-model="createFolderDialogVisible" :title="$t('filesView.newFolderTitle')" width="400">
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
    </el-dialog>

    <!-- 重命名对话框 -->
    <el-dialog v-model="renameDialogVisible" :title="$t('filesView.renameTitle')" width="400">
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
    </el-dialog>

    <!-- 搜索对话框 -->
    <el-dialog v-model="searchDialogVisible" :title="$t('filesView.searchDialogTitle')" width="650" @close="resetSearch">
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
          
          <el-select v-model="searchExtension" :placeholder="$t('filesView.extLabel')" clearable style="width: 120px">
            <el-option :label="$t('filesView.allOption')" value="" />
            <el-option :label="$t('filesView.extImages')" value=".jpg,.jpeg,.png,.gif,.webp" />
            <el-option :label="$t('filesView.extVideos')" value=".mp4,.avi,.mkv,.mov" />
            <el-option :label="$t('filesView.extAudios')" value=".mp3,.wav,.flac" />
            <el-option :label="$t('filesView.extDocs')" value=".pdf,.doc,.docx,.txt,.md" />
            <el-option :label="$t('filesView.extArchives')" value=".zip,.rar,.7z,.tar" />
          </el-select>
          
          <el-checkbox v-model="searchInCurrentDir">{{ $t('filesView.currentDirOnly') }}</el-checkbox>
        </div>
      </div>
      
      <div class="search-results" v-loading="searchLoading">
        <div v-if="searchResults.length > 0" class="results-header">
          {{ $t('filesView.foundResults', { count: searchResults.length }) }}
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
    </el-dialog>

    <!-- 回收站对话框 -->
    <el-dialog v-model="recycleBinDialogVisible" :title="$t('filesView.recycleBinTitle')" width="700">
      <template #header>
        <div class="recycle-header">
          <span>{{ $t('filesView.recycleBinTitle') }}</span>
          <el-button type="danger" size="small" @click="emptyRecycleBin" :disabled="recycleBinItems.length === 0">
            {{ $t('filesView.clearRecycleBin') }}
          </el-button>
        </div>
      </template>
      <el-table :data="recycleBinItems" v-loading="recycleBinLoading" max-height="400">
        <el-table-column prop="name" :label="$t('filesView.colName')" min-width="200">
          <template #default="{ row }">
            <div class="file-name">
              <el-icon :color="getFileIconColor(row)">
                <component :is="getFileIcon(row)" />
              </el-icon>
              <span>{{ row.name }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="path" :label="$t('filesView.colOriginalPath')" min-width="200" />
        <el-table-column :label="$t('filesView.colActions')" width="120">
          <template #default="{ row }">
            <el-button size="small" type="primary" @click="restoreFile(row)">{{ $t('filesView.actRestore') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="recycleBinItems.length === 0 && !recycleBinLoading" :description="$t('filesView.recycleBinEmpty')" />
    </el-dialog>

    <!-- 上传对话框 -->
    <el-dialog v-model="uploadDialogVisible" :title="$t('filesView.uploadTitle')" width="500">
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
    </el-dialog>

    <!-- 预览对话框 -->
    <el-dialog 
      v-model="previewDialogVisible" 
      :title="previewItem?.name || $t('filesView.previewFallbackTitle')" 
      width="80%"
      class="preview-dialog"
    >
      <div class="preview-content" v-if="previewItem">
        <!-- 图片预览 -->
        <img v-if="isImage(previewItem)" :src="previewUrls[previewItem.path]" class="preview-image" />
        <!-- 视频预览 -->
        <video v-else-if="isVideo(previewItem)" :src="previewUrls[previewItem.path]" controls class="preview-video" />
        <!-- 音频预览 -->
        <audio v-else-if="isAudio(previewItem)" :src="previewUrls[previewItem.path]" controls class="preview-audio" />
        <!-- 文本预览 -->
        <div v-else-if="isTextFile(previewItem)" class="preview-text">
          <pre><code>{{ textContent }}</code></pre>
        </div>
        <!-- 其他文件 -->
        <div v-else class="preview-unsupported">
          <el-icon :size="64" color="#909399"><Document /></el-icon>
          <p>{{ $t('filesView.unsupportedPreview') }}</p>
          <el-button type="primary" @click="downloadFile(previewItem)">{{ $t('filesView.downloadFileText') }}</el-button>
        </div>
      </div>
      <template #footer>
        <el-button @click="previewDialogVisible = false">{{ $t('filesView.close') }}</el-button>
        <el-button type="primary" @click="downloadFile(previewItem!)" v-if="previewItem">{{ $t('filesView.downloadTip') }}</el-button>
      </template>
    </el-dialog>

    <!-- 分享对话框 -->
    <el-dialog v-model="shareDialogVisible" :title="$t('filesView.shareDialogTitle')" width="500">
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
    </el-dialog>
  </div>
</template>

<style lang="scss" scoped>
.files-page {
  height: 100%;
  display: flex;
  flex-direction: column;
  background: var(--el-bg-color);
  border-radius: 12px;
  padding: 20px;
  box-shadow: var(--card-shadow, 0 2px 12px rgba(0, 0, 0, 0.06));
  border: 1px solid var(--el-border-color-lighter);
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
  border-radius: 8px;
  border: 1px solid var(--el-border-color-lighter);

  .breadcrumb-item {
    cursor: pointer;
    display: inline-flex;
    align-items: center;
    gap: 4px;
    transition: color 0.2s;
    
    &:hover {
      color: var(--el-color-primary);
    }
  }
}

.file-name {
  display: flex;
  align-items: center;
  gap: 8px;
}

:deep(.selected-row) {
  background-color: var(--el-color-primary-light-9) !important;
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

.grid-view {
  flex: 1;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  gap: 16px;
  padding: 16px;
  overflow: auto;
  align-content: start;
  
  .grid-item {
    display: flex;
    flex-direction: column;
    align-items: center;
    padding: 16px 8px;
    border-radius: 12px;
    cursor: pointer;
    transition: all 0.2s ease;
    border: 1px solid transparent;
    
    &:hover {
      background: var(--el-fill-color-light);
      border-color: var(--el-border-color-lighter);
    }
    
    &.selected {
      background: var(--el-color-primary-light-9);
      border-color: var(--el-color-primary);
    }
    
    .grid-item-icon {
      margin-bottom: 8px;
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
      font-size: 11px;
      color: var(--el-text-color-secondary);
      margin-top: 4px;
    }
  }
}

.status-bar {
  display: flex;
  justify-content: space-between;
  padding: 12px 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  border-top: 1px solid var(--el-border-color-lighter);
  margin-top: auto;
  
  .selection-info {
    color: var(--el-color-primary);
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
      border-radius: 8px;
      cursor: pointer;
      transition: background 0.2s;
      
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

.recycle-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
  padding-right: 40px;
}

.upload-path-info {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px;
  margin-bottom: 16px;
  background: var(--el-fill-color-light);
  border-radius: 8px;
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
    border-radius: 8px;
  }
  
  .preview-video {
    max-width: 100%;
    max-height: 70vh;
    border-radius: 8px;
  }
  
  .preview-audio {
    width: 100%;
  }
  
  .preview-text {
    width: 100%;
    max-height: 70vh;
    overflow: auto;
    background: #1e1e1e;
    border-radius: 8px;
    
    pre {
      margin: 0;
      padding: 16px;
      
      code {
        font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
        font-size: 13px;
        line-height: 1.6;
        color: #d4d4d4;
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
}

.share-target {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px;
  background: var(--el-fill-color-light);
  border-radius: 12px;
  margin-bottom: 20px;
  border: 1px solid var(--el-border-color-lighter);
  
  .share-target-info {
    flex: 1;
    min-width: 0;
    
    .share-target-name {
      font-weight: 500;
      font-size: 15px;
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

.share-result {
  .share-link-box {
    margin: 16px 0;
  }
  
  .share-info-list {
    background: var(--el-fill-color-light);
    border-radius: 12px;
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
        border-radius: 4px;
        color: var(--el-color-warning-dark-2);
      }
    }
  }
}

.share-form {
  :deep(.el-form-item) {
    margin-bottom: 20px;
  }
  
  :deep(.el-radio-group) {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
  }
}
</style>

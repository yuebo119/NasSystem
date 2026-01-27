<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useFileStore } from '@/stores/file'
import { fileApi } from '@/api/file'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { UploadFile } from 'element-plus'
import { 
  Refresh, Upload, Delete, Download, EditPen, 
  CopyDocument, Rank, MoreFilled, FolderAdd, Search,
  Grid, List, View, Hide, DeleteFilled, Back,
  Document, Folder, Picture, VideoPlay, Headset,
  FolderOpened, HomeFilled
} from '@element-plus/icons-vue'
import type { FileItem } from '@/types'

const route = useRoute()
const router = useRouter()
const fileStore = useFileStore()

// 对话框状态
const createFolderDialogVisible = ref(false)
const renameDialogVisible = ref(false)
const moveDialogVisible = ref(false)
const copyDialogVisible = ref(false)
const searchDialogVisible = ref(false)
const recycleBinDialogVisible = ref(false)
const uploadDialogVisible = ref(false)
const previewDialogVisible = ref(false)

// 表单数据
const newFolderName = ref('')
const renameTarget = ref<FileItem | null>(null)
const newName = ref('')
const moveDestination = ref('/')
const copyDestination = ref('/')
const searchQuery = ref('')
const searchResults = ref<FileItem[]>([])
const searchLoading = ref(false)
const recycleBinItems = ref<FileItem[]>([])
const recycleBinLoading = ref(false)
const previewItem = ref<FileItem | null>(null)

// 上传相关
const uploadFiles = ref<UploadFile[]>([])
const uploading = ref(false)
const uploadProgress = ref(0)

// 文本预览内容
const textContent = ref('')

// 剪贴板
const clipboard = ref<{ items: FileItem[], action: 'copy' | 'cut' } | null>(null)

// 监听路由变化加载目录
watch(
  () => route.params.path,
  (path) => {
    const targetPath = Array.isArray(path) ? '/' + path.join('/') : '/'
    fileStore.loadDirectory(targetPath)
  },
  { immediate: true }
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
    ElMessage.warning('请输入文件夹名称')
    return
  }
  const result = await fileStore.createFolder(newFolderName.value.trim())
  if (result.success) {
    ElMessage.success('创建成功')
    createFolderDialogVisible.value = false
    newFolderName.value = ''
  } else {
    ElMessage.error(result.error?.message || '创建失败')
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
    ElMessage.warning('请输入新名称')
    return
  }
  if (newName.value === renameTarget.value.name) {
    renameDialogVisible.value = false
    return
  }
  const result = await fileStore.rename(renameTarget.value, newName.value.trim())
  if (result.success) {
    ElMessage.success('重命名成功')
    renameDialogVisible.value = false
  } else {
    ElMessage.error(result.error?.message || '重命名失败')
  }
}

// 删除选中项
async function handleDelete(permanent = false) {
  const count = fileStore.selectionCount
  if (count === 0) {
    ElMessage.warning('请先选择要删除的文件')
    return
  }
  try {
    await ElMessageBox.confirm(
      permanent 
        ? `确定要永久删除选中的 ${count} 个项目吗？此操作不可恢复！`
        : `确定要删除选中的 ${count} 个项目吗？`,
      permanent ? '永久删除' : '删除确认',
      { confirmButtonText: permanent ? '永久删除' : '删除', cancelButtonText: '取消', type: 'warning' }
    )
    const result = await fileStore.deleteSelected(permanent)
    if (result.success) {
      ElMessage.success(permanent ? '已永久删除' : '已移至回收站')
    } else {
      ElMessage.error(result.error?.message || '删除失败')
    }
  } catch {
    // 取消操作
  }
}

// 删除单个项目
async function handleDeleteItem(item: FileItem, permanent = false) {
  try {
    await ElMessageBox.confirm(
      permanent 
        ? `确定要永久删除 "${item.name}" 吗？此操作不可恢复！`
        : `确定要删除 "${item.name}" 吗？`,
      permanent ? '永久删除' : '删除确认',
      { confirmButtonText: permanent ? '永久删除' : '删除', cancelButtonText: '取消', type: 'warning' }
    )
    const result = await fileApi.delete([item.path], permanent)
    if (result.success) {
      ElMessage.success(permanent ? '已永久删除' : '已移至回收站')
      await fileStore.loadDirectory(fileStore.currentPath)
    } else {
      ElMessage.error(result.error?.message || '删除失败')
    }
  } catch {
    // 取消操作
  }
}

// 复制到剪贴板
function copyToClipboard() {
  if (fileStore.selectedItems.length === 0) {
    ElMessage.warning('请先选择文件')
    return
  }
  clipboard.value = { items: [...fileStore.selectedItems], action: 'copy' }
  ElMessage.success(`已复制 ${fileStore.selectedItems.length} 个项目`)
}

// 剪切到剪贴板
function cutToClipboard() {
  if (fileStore.selectedItems.length === 0) {
    ElMessage.warning('请先选择文件')
    return
  }
  clipboard.value = { items: [...fileStore.selectedItems], action: 'cut' }
  ElMessage.success(`已剪切 ${fileStore.selectedItems.length} 个项目`)
}

// 粘贴
async function handlePaste() {
  if (!clipboard.value) {
    ElMessage.warning('剪贴板为空')
    return
  }
  const paths = clipboard.value.items.map(item => item.path)
  const destination = fileStore.currentPath
  
  try {
    if (clipboard.value.action === 'copy') {
      const result = await fileApi.copy(paths, destination)
      if (result.success) {
        ElMessage.success('粘贴成功')
        await fileStore.loadDirectory(fileStore.currentPath)
      } else {
        ElMessage.error(result.error?.message || '粘贴失败')
      }
    } else {
      const result = await fileApi.move(paths, destination)
      if (result.success) {
        ElMessage.success('粘贴成功')
        clipboard.value = null  // 剪切后清空剪贴板
        await fileStore.loadDirectory(fileStore.currentPath)
      } else {
        ElMessage.error(result.error?.message || '粘贴失败')
      }
    }
  } catch (e) {
    ElMessage.error('粘贴操作失败')
  }
}

// 打开上传对话框
function openUploadDialog() {
  uploadFiles.value = []
  uploadProgress.value = 0
  uploadDialogVisible.value = true
}

// 移动文件
async function handleMove() {
  const paths = fileStore.selectedItems.map(item => item.path)
  const result = await fileApi.move(paths, moveDestination.value)
  if (result.success) {
    ElMessage.success('移动成功')
    moveDialogVisible.value = false
    await fileStore.loadDirectory(fileStore.currentPath)
  } else {
    ElMessage.error(result.error?.message || '移动失败')
  }
}

// 复制文件
async function handleCopy() {
  const paths = fileStore.selectedItems.map(item => item.path)
  const result = await fileApi.copy(paths, copyDestination.value)
  if (result.success) {
    ElMessage.success('复制成功')
    copyDialogVisible.value = false
    await fileStore.loadDirectory(fileStore.currentPath)
  } else {
    ElMessage.error(result.error?.message || '复制失败')
  }
}

// 下载文件
function downloadFile(item: FileItem) {
  if (item.isFolder) {
    ElMessage.warning('暂不支持下载文件夹')
    return
  }
  const url = fileApi.getDownloadUrl(item.path)
  const token = localStorage.getItem('token')
  // 创建隐藏的 a 标签下载
  const link = document.createElement('a')
  link.href = `${url}?token=${token}`
  link.download = item.name
  link.target = '_blank'
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
}

// 下载选中文件
function downloadSelected() {
  const files = fileStore.selectedItems.filter(item => !item.isFolder)
  if (files.length === 0) {
    ElMessage.warning('请选择要下载的文件')
    return
  }
  files.forEach(file => downloadFile(file))
}

// 搜索文件
async function handleSearch() {
  if (!searchQuery.value.trim()) {
    ElMessage.warning('请输入搜索关键词')
    return
  }
  searchLoading.value = true
  try {
    const result = await fileApi.search(searchQuery.value.trim(), {
      path: fileStore.currentPath,
      pageSize: 100
    })
    if (result.success && result.data) {
      searchResults.value = result.data.items
    }
  } catch (e) {
    ElMessage.error('搜索失败')
  } finally {
    searchLoading.value = false
  }
}

// 跳转到搜索结果
function goToSearchResult(item: FileItem) {
  searchDialogVisible.value = false
  if (item.isFolder) {
    router.push(`/files${item.path}`)
  } else {
    // 跳转到文件所在目录
    const parentPath = item.path.substring(0, item.path.lastIndexOf('/')) || '/'
    router.push(`/files${parentPath}`)
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
    ElMessage.error('加载回收站失败')
  } finally {
    recycleBinLoading.value = false
  }
}

// 恢复文件
async function restoreFile(item: FileItem) {
  const result = await fileApi.restore(item.id)
  if (result.success) {
    ElMessage.success('恢复成功')
    recycleBinItems.value = recycleBinItems.value.filter(i => i.id !== item.id)
    await fileStore.loadDirectory(fileStore.currentPath)
  } else {
    ElMessage.error(result.error?.message || '恢复失败')
  }
}

// 清空回收站
async function emptyRecycleBin() {
  try {
    await ElMessageBox.confirm('确定要清空回收站吗？此操作不可恢复！', '清空回收站', {
      confirmButtonText: '清空', cancelButtonText: '取消', type: 'warning'
    })
    const result = await fileApi.emptyRecycleBin()
    if (result.success) {
      ElMessage.success('回收站已清空')
      recycleBinItems.value = []
    } else {
      ElMessage.error(result.error?.message || '清空失败')
    }
  } catch {
    // 取消操作
  }
}

// 上传文件
async function handleUpload() {
  if (uploadFiles.value.length === 0) {
    ElMessage.warning('请选择要上传的文件')
    return
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
    ElMessage.success(`成功上传 ${completed} 个文件`)
  } else {
    ElMessage.warning(`上传完成：成功 ${completed} 个，失败 ${failed} 个`)
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
    textContent.value = '加载中...'
    previewDialogVisible.value = true
    try {
      const token = localStorage.getItem('token')
      const response = await fetch(`${fileApi.getDownloadUrl(item.path)}?token=${token}`)
      if (response.ok) {
        textContent.value = await response.text()
      } else {
        textContent.value = '加载失败'
      }
    } catch (e) {
      textContent.value = '加载失败'
    }
  } else {
    previewDialogVisible.value = true
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

// 切换显示隐藏文件
function toggleShowHidden() {
  fileStore.showHidden = !fileStore.showHidden
  fileStore.loadDirectory(fileStore.currentPath)
}

// 格式化文件大小
function formatSize(bytes: number): string {
  if (bytes === 0) return '-'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(1024))
  return (bytes / Math.pow(1024, i)).toFixed(2) + ' ' + units[i]
}

// 格式化日期
function formatDate(date: string): string {
  return new Date(date).toLocaleString('zh-CN')
}

// 获取文件图标
function getFileIcon(item: FileItem): any {
  if (item.isFolder) return Folder
  const ext = item.extension?.toLowerCase()
  const iconMap: Record<string, any> = {
    '.pdf': Document, '.doc': Document, '.docx': Document,
    '.xls': Document, '.xlsx': Document, '.ppt': Document, '.pptx': Document,
    '.txt': Document, '.md': Document, '.json': Document, '.xml': Document,
    '.jpg': Picture, '.jpeg': Picture, '.png': Picture, '.gif': Picture, '.webp': Picture, '.bmp': Picture, '.svg': Picture,
    '.mp4': VideoPlay, '.avi': VideoPlay, '.mkv': VideoPlay, '.mov': VideoPlay, '.webm': VideoPlay,
    '.mp3': Headset, '.wav': Headset, '.flac': Headset, '.ogg': Headset,
    '.zip': FolderOpened, '.rar': FolderOpened, '.7z': FolderOpened, '.tar': FolderOpened, '.gz': FolderOpened,
  }
  return iconMap[ext || ''] || Document
}

// 获取文件图标颜色
function getFileIconColor(item: FileItem): string {
  if (item.isFolder) return '#ffc107'
  const ext = item.extension?.toLowerCase()
  if (['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.svg'].includes(ext || '')) return '#67C23A'
  if (['.mp4', '.avi', '.mkv', '.mov', '.webm'].includes(ext || '')) return '#409EFF'
  if (['.mp3', '.wav', '.flac', '.ogg'].includes(ext || '')) return '#E6A23C'
  if (['.zip', '.rar', '.7z', '.tar', '.gz'].includes(ext || '')) return '#909399'
  return '#909399'
}

// 获取行样式类名
function getRowClassName({ row }: { row: FileItem }): string {
  return fileStore.selectedItems.some(i => i.id === row.id) ? 'selected-row' : ''
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

// 获取预览 URL
function getPreviewUrl(item: FileItem): string {
  const token = localStorage.getItem('token')
  return `${fileApi.getDownloadUrl(item.path)}?token=${token}`
}

// 剪贴板状态文本
const clipboardText = computed(() => {
  if (!clipboard.value) return ''
  const action = clipboard.value.action === 'copy' ? '复制' : '剪切'
  return `${action}了 ${clipboard.value.items.length} 个项目`
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
  }
}

// 右键菜单
function openContextMenu(_event: MouseEvent, item: FileItem) {
  fileStore.selectItem(item)
}
</script>

<template>
  <div class="files-page">
    <!-- 工具栏 -->
    <div class="toolbar">
      <div class="toolbar-left">
        <el-button-group>
          <el-tooltip content="返回上级" placement="bottom">
            <el-button @click="goBack" :disabled="fileStore.currentPath === '/'">
              <el-icon><Back /></el-icon>
            </el-button>
          </el-tooltip>
          <el-tooltip content="刷新" placement="bottom">
            <el-button @click="handleRefresh" :loading="fileStore.isLoading">
              <el-icon><Refresh /></el-icon>
            </el-button>
          </el-tooltip>
        </el-button-group>
        
        <el-divider direction="vertical" />
        
        <el-button type="primary" @click="createFolderDialogVisible = true">
          <el-icon><FolderAdd /></el-icon>
          新建文件夹
        </el-button>
        <el-button @click="openUploadDialog">
          <el-icon><Upload /></el-icon>
          上传
        </el-button>
        
        <el-divider direction="vertical" />
        
        <el-button-group>
          <el-tooltip content="复制" placement="bottom">
            <el-button @click="copyToClipboard" :disabled="!fileStore.hasSelection">
              <el-icon><CopyDocument /></el-icon>
            </el-button>
          </el-tooltip>
          <el-tooltip content="剪切" placement="bottom">
            <el-button @click="cutToClipboard" :disabled="!fileStore.hasSelection">
              <el-icon><Rank /></el-icon>
            </el-button>
          </el-tooltip>
          <el-tooltip content="粘贴" placement="bottom">
            <el-button @click="handlePaste" :disabled="!clipboard">
              <el-icon><Document /></el-icon>
            </el-button>
          </el-tooltip>
        </el-button-group>
        
        <el-tooltip content="下载" placement="bottom">
          <el-button @click="downloadSelected" :disabled="!fileStore.hasSelection">
            <el-icon><Download /></el-icon>
          </el-button>
        </el-tooltip>
        <el-tooltip content="删除" placement="bottom">
          <el-button type="danger" @click="handleDelete(false)" :disabled="!fileStore.hasSelection">
            <el-icon><Delete /></el-icon>
          </el-button>
        </el-tooltip>
      </div>
      
      <div class="toolbar-right">
        <el-tooltip :content="clipboardText" v-if="clipboard">
          <el-tag closable @close="clipboard = null" size="small">
            {{ clipboard.action === 'copy' ? '已复制' : '已剪切' }} {{ clipboard.items.length }}
          </el-tag>
        </el-tooltip>
        
        <el-tooltip content="搜索" placement="bottom">
          <el-button @click="searchDialogVisible = true">
            <el-icon><Search /></el-icon>
          </el-button>
        </el-tooltip>
        <el-tooltip content="回收站" placement="bottom">
          <el-button @click="openRecycleBin">
            <el-icon><DeleteFilled /></el-icon>
          </el-button>
        </el-tooltip>
        <el-tooltip :content="fileStore.showHidden ? '隐藏隐藏文件' : '显示隐藏文件'" placement="bottom">
          <el-button @click="toggleShowHidden" :type="fileStore.showHidden ? 'primary' : 'default'">
            <el-icon><component :is="fileStore.showHidden ? View : Hide" /></el-icon>
          </el-button>
        </el-tooltip>
        <el-tooltip :content="fileStore.viewMode === 'list' ? '切换到网格视图' : '切换到列表视图'" placement="bottom">
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
          根目录
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
      :data="fileStore.sortedFiles"
      style="width: 100%"
      @row-click="handleRowClick"
      @row-dblclick="handleRowDblClick"
      @selection-change="handleSelectionChange"
      :row-class-name="getRowClassName"
    >
      <el-table-column type="selection" width="50" />
      
      <el-table-column prop="name" label="名称" min-width="300" sortable>
        <template #default="{ row }">
          <div class="file-name">
            <el-icon :size="20" :color="getFileIconColor(row)">
              <component :is="getFileIcon(row)" />
            </el-icon>
            <span>{{ row.name }}</span>
            <el-tag v-if="row.isHidden" size="small" type="info">隐藏</el-tag>
          </div>
        </template>
      </el-table-column>
      
      <el-table-column prop="sizeBytes" label="大小" width="120" sortable>
        <template #default="{ row }">
          {{ row.isFolder ? '-' : formatSize(row.sizeBytes) }}
        </template>
      </el-table-column>
      
      <el-table-column prop="extension" label="类型" width="100">
        <template #default="{ row }">
          {{ row.isFolder ? '文件夹' : (row.extension || '-') }}
        </template>
      </el-table-column>
      
      <el-table-column prop="createdAt" label="修改时间" width="180" sortable>
        <template #default="{ row }">
          {{ formatDate(row.updatedAt || row.createdAt) }}
        </template>
      </el-table-column>
      
      <el-table-column label="操作" width="120" fixed="right" property="actions">
        <template #default="{ row }">
          <el-dropdown trigger="click" @command="(cmd: string) => handleCommand(cmd, row)">
            <el-button :icon="MoreFilled" circle size="small" />
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item v-if="!row.isFolder" command="download">
                  <el-icon><Download /></el-icon> 下载
                </el-dropdown-item>
                <el-dropdown-item v-if="canPreview(row)" command="preview">
                  <el-icon><View /></el-icon> 预览
                </el-dropdown-item>
                <el-dropdown-item command="rename">
                  <el-icon><EditPen /></el-icon> 重命名
                </el-dropdown-item>
                <el-dropdown-item command="copy">
                  <el-icon><CopyDocument /></el-icon> 复制
                </el-dropdown-item>
                <el-dropdown-item command="cut">
                  <el-icon><Rank /></el-icon> 剪切
                </el-dropdown-item>
                <el-dropdown-item divided command="delete">
                  <el-icon color="var(--el-color-danger)"><Delete /></el-icon>
                  <span style="color: var(--el-color-danger)">删除</span>
                </el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </template>
      </el-table-column>
    </el-table>

    <!-- 网格视图 -->
    <div v-else class="grid-view" v-loading="fileStore.isLoading">
      <div
        v-for="item in fileStore.sortedFiles"
        :key="item.id"
        class="grid-item"
        :class="{ selected: fileStore.selectedItems.some(i => i.id === item.id) }"
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
      <el-empty v-if="fileStore.sortedFiles.length === 0 && !fileStore.isLoading" description="文件夹为空" />
    </div>

    <!-- 状态栏 -->
    <div class="status-bar">
      <span v-if="fileStore.directoryContent">
        {{ fileStore.directoryContent.folderCount }} 个文件夹,
        {{ fileStore.directoryContent.fileCount }} 个文件
        <span v-if="fileStore.directoryContent.totalSizeBytes > 0">
          ({{ formatSize(fileStore.directoryContent.totalSizeBytes) }})
        </span>
      </span>
      <span v-if="fileStore.hasSelection" class="selection-info">
        已选择 {{ fileStore.selectionCount }} 项
      </span>
    </div>

    <!-- 新建文件夹对话框 -->
    <el-dialog v-model="createFolderDialogVisible" title="新建文件夹" width="400">
      <el-input
        v-model="newFolderName"
        placeholder="请输入文件夹名称"
        @keyup.enter="handleCreateFolder"
        autofocus
      />
      <template #footer>
        <el-button @click="createFolderDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleCreateFolder">创建</el-button>
      </template>
    </el-dialog>

    <!-- 重命名对话框 -->
    <el-dialog v-model="renameDialogVisible" title="重命名" width="400">
      <el-input
        v-model="newName"
        placeholder="请输入新名称"
        @keyup.enter="handleRename"
        autofocus
      />
      <template #footer>
        <el-button @click="renameDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleRename">确定</el-button>
      </template>
    </el-dialog>

    <!-- 移动对话框 -->
    <el-dialog v-model="moveDialogVisible" title="移动到" width="500">
      <el-input v-model="moveDestination" placeholder="目标路径，如 /documents">
        <template #prepend>目标路径</template>
      </el-input>
      <div class="dialog-tip">
        将移动 {{ fileStore.selectionCount }} 个项目到指定目录
      </div>
      <template #footer>
        <el-button @click="moveDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleMove">移动</el-button>
      </template>
    </el-dialog>

    <!-- 复制对话框 -->
    <el-dialog v-model="copyDialogVisible" title="复制到" width="500">
      <el-input v-model="copyDestination" placeholder="目标路径，如 /documents">
        <template #prepend>目标路径</template>
      </el-input>
      <div class="dialog-tip">
        将复制 {{ fileStore.selectionCount }} 个项目到指定目录
      </div>
      <template #footer>
        <el-button @click="copyDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleCopy">复制</el-button>
      </template>
    </el-dialog>

    <!-- 搜索对话框 -->
    <el-dialog v-model="searchDialogVisible" title="搜索文件" width="600">
      <el-input
        v-model="searchQuery"
        placeholder="输入文件名搜索"
        @keyup.enter="handleSearch"
        clearable
      >
        <template #append>
          <el-button @click="handleSearch" :loading="searchLoading">搜索</el-button>
        </template>
      </el-input>
      <div class="search-results" v-loading="searchLoading">
        <div v-if="searchResults.length > 0" class="results-list">
          <div
            v-for="item in searchResults"
            :key="item.id"
            class="result-item"
            @click="goToSearchResult(item)"
          >
            <el-icon :color="getFileIconColor(item)">
              <component :is="getFileIcon(item)" />
            </el-icon>
            <div class="result-info">
              <div class="result-name">{{ item.name }}</div>
              <div class="result-path">{{ item.path }}</div>
            </div>
          </div>
        </div>
        <el-empty v-else-if="searchQuery && !searchLoading" description="未找到匹配的文件" />
      </div>
    </el-dialog>

    <!-- 回收站对话框 -->
    <el-dialog v-model="recycleBinDialogVisible" title="回收站" width="700">
      <template #header>
        <div class="recycle-header">
          <span>回收站</span>
          <el-button type="danger" size="small" @click="emptyRecycleBin" :disabled="recycleBinItems.length === 0">
            清空回收站
          </el-button>
        </div>
      </template>
      <el-table :data="recycleBinItems" v-loading="recycleBinLoading" max-height="400">
        <el-table-column prop="name" label="名称" min-width="200">
          <template #default="{ row }">
            <div class="file-name">
              <el-icon :color="getFileIconColor(row)">
                <component :is="getFileIcon(row)" />
              </el-icon>
              <span>{{ row.name }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="path" label="原路径" min-width="200" />
        <el-table-column label="操作" width="120">
          <template #default="{ row }">
            <el-button size="small" type="primary" @click="restoreFile(row)">恢复</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="recycleBinItems.length === 0 && !recycleBinLoading" description="回收站为空" />
    </el-dialog>

    <!-- 上传对话框 -->
    <el-dialog v-model="uploadDialogVisible" title="上传文件" width="500">
      <div class="upload-path-info">
        <el-icon><Folder /></el-icon>
        <span>上传到: <strong>{{ fileStore.currentPath }}</strong></span>
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
          将文件拖到此处，或<em>点击上传</em>
        </div>
      </el-upload>
      <div class="upload-info" v-if="uploadFiles.length > 0">
        已选择 {{ uploadFiles.length }} 个文件
      </div>
      <el-progress v-if="uploading" :percentage="uploadProgress" />
      <template #footer>
        <el-button @click="uploadDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleUpload" :loading="uploading" :disabled="uploadFiles.length === 0">
          开始上传
        </el-button>
      </template>
    </el-dialog>

    <!-- 预览对话框 -->
    <el-dialog 
      v-model="previewDialogVisible" 
      :title="previewItem?.name || '预览'" 
      width="80%"
      class="preview-dialog"
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
          <el-icon :size="64" color="#909399"><Document /></el-icon>
          <p>此文件类型暂不支持预览</p>
          <el-button type="primary" @click="downloadFile(previewItem)">下载文件</el-button>
        </div>
      </div>
      <template #footer>
        <el-button @click="previewDialogVisible = false">关闭</el-button>
        <el-button type="primary" @click="downloadFile(previewItem!)" v-if="previewItem">下载</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style lang="scss" scoped>
.files-page {
  height: 100%;
  display: flex;
  flex-direction: column;
  background: #fff;
  border-radius: 8px;
  padding: 16px;
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
  border-radius: 4px;

  .breadcrumb-item {
    cursor: pointer;
    display: inline-flex;
    align-items: center;
    gap: 4px;
    
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
    border-radius: 8px;
    cursor: pointer;
    transition: all 0.2s;
    
    &:hover {
      background: var(--el-fill-color-light);
    }
    
    &.selected {
      background: var(--el-color-primary-light-9);
      border: 1px solid var(--el-color-primary);
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

.dialog-tip {
  margin-top: 12px;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.search-results {
  margin-top: 16px;
  max-height: 400px;
  overflow: auto;
  
  .results-list {
    .result-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px;
      border-radius: 4px;
      cursor: pointer;
      
      &:hover {
        background: var(--el-fill-color-light);
      }
      
      .result-info {
        flex: 1;
        min-width: 0;
        
        .result-name {
          font-weight: 500;
        }
        
        .result-path {
          font-size: 12px;
          color: var(--el-text-color-secondary);
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }
      }
    }
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
  border-radius: 4px;
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
  }
  
  .preview-video {
    max-width: 100%;
    max-height: 70vh;
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
</style>

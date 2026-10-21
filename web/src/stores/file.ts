import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { FileItem, DirectoryContent } from '@/types'
import { fileApi } from '@/api/file'

export const useFileStore = defineStore('file', () => {
  // 状态
  const currentPath = ref('/')
  const directoryContent = ref<DirectoryContent | null>(null)
  const selectedItems = ref<FileItem[]>([])
  const isLoading = ref(false)
  const viewMode = ref<'list' | 'grid'>('list')
  const sortBy = ref<'name' | 'size' | 'createdAt' | 'type'>('name')
  const sortDesc = ref(false)
  const showHidden = ref(false)
  // 文件夹优先：可关（按大小看全目录最大文件时文件夹不再霸顶）
  const foldersFirst = ref(true)
  const focusItemId = ref<string | null>(null)  // 用于聚焦特定文件
  // 最近一次成功加载（或 304 校验）的时间：状态栏"更新于"用
  const lastCheckedAt = ref<Date | null>(null)

  // 计算属性
  const files = computed(() => directoryContent.value?.items || [])
  const breadcrumbs = computed(() => directoryContent.value?.breadcrumbs || [])
  const hasSelection = computed(() => selectedItems.value.length > 0)
  const selectionCount = computed(() => selectedItems.value.length)

  // 排序后的文件列表
  const sortedFiles = computed(() => {
    const items = [...files.value]
    
    // 过滤隐藏文件
    const filtered = showHidden.value ? items : items.filter(f => !f.isHidden)
    
    // 排序
    filtered.sort((a, b) => {
      // 文件夹优先（可关）
      if (foldersFirst.value && a.isFolder !== b.isFolder) {
        return a.isFolder ? -1 : 1
      }
      
      let comparison = 0
      switch (sortBy.value) {
        case 'name':
          comparison = a.name.localeCompare(b.name, 'zh-CN')
          break
        case 'size':
          comparison = a.sizeBytes - b.sizeBytes
          break
        case 'createdAt':
          comparison = new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()
          break
        case 'type':
          // 按扩展名排序
          const extA = a.extension?.toLowerCase() || ''
          const extB = b.extension?.toLowerCase() || ''
          comparison = extA.localeCompare(extB, 'zh-CN')
          break
      }
      
      return sortDesc.value ? -comparison : comparison
    })
    
    return filtered
  })

  // 方法
  async function loadDirectory(path: string = '/') {
    isLoading.value = true
    try {
      // api 层负责 ETag/304 与内容缓存：304 时返回缓存内容 +
      // notModified 标志，这里照常渲染
      const response = await fileApi.getDirectory(path, showHidden.value)
      if (response.success && response.data) {
        const isNavigation = path !== currentPath.value
        directoryContent.value = response.data
        currentPath.value = path
        // 仅导航时清选择：同目录刷新（304/SignalR/手动）保留勾选
        if (isNavigation) selectedItems.value = []
        lastCheckedAt.value = new Date()
      }
    } finally {
      isLoading.value = false
    }
  }

  function selectItem(item: FileItem, multi = false) {
    if (multi) {
      const index = selectedItems.value.findIndex(i => i.id === item.id)
      if (index >= 0) {
        selectedItems.value.splice(index, 1)
      } else {
        selectedItems.value.push(item)
      }
    } else {
      selectedItems.value = [item]
    }
  }

  function clearSelection() {
    selectedItems.value = []
  }

  function toggleViewMode() {
    viewMode.value = viewMode.value === 'list' ? 'grid' : 'list'
  }

  function setSort(field: 'name' | 'size' | 'createdAt' | 'type') {
    if (sortBy.value === field) {
      sortDesc.value = !sortDesc.value
    } else {
      sortBy.value = field
      sortDesc.value = false
    }
  }

  function setFocusItem(itemId: string | null) {
    focusItemId.value = itemId
  }

  async function createFolder(name: string) {
    const response = await fileApi.createFolder(currentPath.value, name)
    if (response.success) {
      await loadDirectory(currentPath.value)
    }
    return response
  }

  async function rename(item: FileItem, newName: string) {
    const response = await fileApi.rename(item.path, newName)
    if (response.success) {
      await loadDirectory(currentPath.value)
    }
    return response
  }

  // 剪贴板（自 Files.vue 内迁入）：复制/剪切/粘贴的状态与结果计算。
  // guard 提示与 ElMessage 通知留在视图层，store 只提供可断言的结果。
  // 存入的是选择数组的副本：之后选择变化不会改写已复制的剪贴板内容
  const clipboard = ref<{ items: FileItem[]; action: 'copy' | 'cut' } | null>(null)

  function setClipboard(action: 'copy' | 'cut'): boolean {
    if (selectedItems.value.length === 0) return false
    clipboard.value = { items: [...selectedItems.value], action }
    return true
  }

  /** 清空剪贴板（工具栏剪贴板标签的关闭按钮） */
  function clearClipboard() {
    clipboard.value = null
  }

  /**
   * 执行粘贴：copy/move 各一次数组请求（ITM-N19：不逐项循环），
   * 统计部分失败。无剪贴板返回 null；剪切成功后清空剪贴板
   * （语义：移动即搬走）；目录重载收在这里，粘贴后刷新是剪贴板
   * 语义的一部分而非调用方的额外职责
   */
  async function pasteClipboard(): Promise<{ ok: number; failed: number; action: 'copy' | 'cut' } | null> {
    if (!clipboard.value) return null
    const { items, action } = clipboard.value
    const destination = currentPath.value

    let failed = 0
    try {
      const paths = items.map(i => i.path)
      const result = action === 'copy'
        ? await fileApi.copy(paths, destination)
        : await fileApi.move(paths, destination)
      failed = ((result.data as { failedItems?: string[] } | null)?.failedItems ?? []).length
    } catch {
      failed = items.length
    }

    if (action === 'cut') {
      clipboard.value = null
    }
    await loadDirectory(currentPath.value)
    return { ok: items.length - failed, failed, action }
  }

  return {
    currentPath,
    directoryContent,
    selectedItems,
    isLoading,
    viewMode,
    sortBy,
    sortDesc,
    showHidden,
    foldersFirst,
    focusItemId,
    lastCheckedAt,
    files,
    sortedFiles,
    breadcrumbs,
    hasSelection,
    selectionCount,
    clipboard,
    loadDirectory,
    selectItem,
    clearSelection,
    toggleViewMode,
    setSort,
    setFocusItem,
    createFolder,
    rename,
    setClipboard,
    pasteClipboard,
    clearClipboard
  }
})

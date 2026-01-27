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
  const sortBy = ref<'name' | 'size' | 'createdAt'>('name')
  const sortDesc = ref(false)
  const showHidden = ref(false)

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
      // 文件夹优先
      if (a.isFolder !== b.isFolder) {
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
      }
      
      return sortDesc.value ? -comparison : comparison
    })
    
    return filtered
  })

  // 方法
  async function loadDirectory(path: string = '/') {
    isLoading.value = true
    try {
      const response = await fileApi.getDirectory(path, showHidden.value)
      if (response.success && response.data) {
        currentPath.value = path
        directoryContent.value = response.data
        selectedItems.value = []
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

  function selectAll() {
    selectedItems.value = [...files.value]
  }

  function clearSelection() {
    selectedItems.value = []
  }

  function toggleViewMode() {
    viewMode.value = viewMode.value === 'list' ? 'grid' : 'list'
  }

  function setSort(field: 'name' | 'size' | 'createdAt') {
    if (sortBy.value === field) {
      sortDesc.value = !sortDesc.value
    } else {
      sortBy.value = field
      sortDesc.value = false
    }
  }

  async function createFolder(name: string) {
    const response = await fileApi.createFolder(currentPath.value, name)
    if (response.success) {
      await loadDirectory(currentPath.value)
    }
    return response
  }

  async function deleteSelected(permanent = false) {
    const paths = selectedItems.value.map(item => item.path)
    const response = await fileApi.delete(paths, permanent)
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

  return {
    currentPath,
    directoryContent,
    selectedItems,
    isLoading,
    viewMode,
    sortBy,
    sortDesc,
    showHidden,
    files,
    sortedFiles,
    breadcrumbs,
    hasSelection,
    selectionCount,
    loadDirectory,
    selectItem,
    selectAll,
    clearSelection,
    toggleViewMode,
    setSort,
    createFolder,
    deleteSelected,
    rename
  }
})

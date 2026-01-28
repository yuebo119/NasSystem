import http from './http'
import type {
  ApiResponse,
  FileItem,
  DirectoryContent,
  PagedResult,
  FileStatistics,
  ScanResult
} from '@/types'

export const fileApi = {
  // 获取目录内容
  async getDirectory(path: string, includeHidden = false): Promise<ApiResponse<DirectoryContent>> {
    // 后端端点是 /files/browse/{path}
    const encodedPath = path === '/' ? '' : path.replace(/^\//, '')
    return http.get(`/files/browse/${encodedPath}`, {
      params: { includeHidden }
    })
  },

  // 获取文件信息
  async getFileInfo(path: string): Promise<ApiResponse<FileItem>> {
    // 后端端点是 /files/info/{path}
    const encodedPath = path.replace(/^\//, '')
    return http.get(`/files/info/${encodedPath}`)
  },

  // 创建文件夹
  async createFolder(parentPath: string, name: string): Promise<ApiResponse<FileItem>> {
    return http.post('/files/folder', { parentPath, name })
  },

  // 重命名
  async rename(path: string, newName: string): Promise<ApiResponse<FileItem>> {
    return http.put('/files/rename', { path, newName })
  },

  // 移动
  async move(sourcePaths: string[], destinationPath: string): Promise<ApiResponse<FileItem[]>> {
    return http.post('/files/move', { sourcePaths, destinationPath })
  },

  // 复制
  async copy(sourcePaths: string[], destinationPath: string): Promise<ApiResponse<FileItem[]>> {
    return http.post('/files/copy', { sourcePaths, destinationPath })
  },

  // 删除
  async delete(paths: string[], permanent = false): Promise<ApiResponse> {
    return http.post('/files/delete', { paths, permanent })
  },

  // 获取回收站
  async getRecycleBin(): Promise<ApiResponse<FileItem[]>> {
    return http.get('/files/recycle-bin')
  },

  // 恢复文件
  async restore(fileId: string): Promise<ApiResponse<FileItem>> {
    return http.post(`/files/restore/${fileId}`)
  },

  // 清空回收站
  async emptyRecycleBin(): Promise<ApiResponse> {
    return http.delete('/files/recycle-bin')
  },

  // 搜索文件
  async search(query: string, options?: {
    path?: string
    itemType?: string
    extension?: string
    page?: number
    pageSize?: number
  }): Promise<ApiResponse<PagedResult<FileItem>>> {
    return http.get('/files/search', {
      params: { query, ...options }
    })
  },

  // 获取文件统计
  async getStatistics(): Promise<ApiResponse<FileStatistics>> {
    return http.get('/files/statistics')
  },

  // 扫描并同步文件
  async scanAndSync(): Promise<ApiResponse<ScanResult>> {
    return http.post('/files/scan')
  },

  // 获取下载 URL
  getDownloadUrl(path: string): string {
    // 后端端点是 /files/download/{path}
    const encodedPath = path.replace(/^\//, '')
    return `/api/v1/files/download/${encodedPath}`
  },

  // 获取缩略图 URL
  getThumbnailUrl(path: string, size = 150): string {
    // 后端端点是 /thumbnails/{path}
    const encodedPath = path.replace(/^\//, '')
    return `/api/v1/thumbnails/${encodedPath}?width=${size}&height=${size}`
  },

  // 上传文件
  async upload(
    file: File,
    destinationPath: string,
    onProgress?: (percentage: number) => void
  ): Promise<ApiResponse<FileItem>> {
    const formData = new FormData()
    formData.append('file', file)
    formData.append('path', destinationPath)  // 后端使用 'path' 字段

    return http.post('/files/upload', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
      onUploadProgress: (progressEvent) => {
        if (onProgress && progressEvent.total) {
          const percentage = Math.round((progressEvent.loaded * 100) / progressEvent.total)
          onProgress(percentage)
        }
      }
    })
  }
}

import http from './http'
import type {
  ApiResponse,
  FileItem,
  DirectoryContent,
  PagedResult,
  FileStatistics,
  ScanResult
} from '@/types'

// 浏览目录的条件请求（ETag/304）：后端按 path+userId+version 生成 ETag，
// 命中 If-None-Match 回 304。这里按 path|hidden 记 ETag，同时缓存上次
// 内容——304 时把缓存内容连同 notModified 标志一起返回，对调用方
// （store/目录浏览器/文件选择器）透明：它们照常拿到数据，无需各自
// 实现缓存。notModified 标志保留是让 store 区分"内容没变"（不清选择）
const dirEtags = new Map<string, string>()
const dirContents = new Map<string, DirectoryContent>()

// 缓存上限：深度浏览会不断产生新 cacheKey，无上限时两个 Map 只增不减
// （ETag 短，但 DirectoryContent 装着整个目录的 FileItem 列表，累积下来
// 是可观的内存）。Map 保序，超限从最旧的键开始淘汰——与"最近看过的
// 最可能再看"一致
const DIR_CACHE_MAX = 100

function trimDirCache<V>(map: Map<string, V>) {
  while (map.size > DIR_CACHE_MAX) {
    const oldest = map.keys().next().value
    if (oldest === undefined) break
    map.delete(oldest)
  }
}

/** getDirectory 的返回：304 时 data 为缓存内容、notModified 为 true */
export type DirectoryResult = ApiResponse<DirectoryContent> & { notModified?: boolean }

export const fileApi = {
  // 获取目录内容
  async getDirectory(path: string, includeHidden = false): Promise<DirectoryResult> {
    // 后端端点是 /files/browse/{path}
    const encodedPath = path === '/' ? '' : path.replace(/^\//, '')
    const cacheKey = `${path}|${includeHidden ? 'hidden' : 'plain'}`
    const etag = dirEtags.get(cacheKey)
    const headers: Record<string, string> = {}
    if (etag) headers['If-None-Match'] = etag

    let response = await http.get(`/files/browse/${encodedPath}`, {
      params: { includeHidden },
      headers
    }) as DirectoryResult
    // http 拦截器把 304 归一成 { success: true, data: null, notModified: true }
    if (response.notModified) {
      const cached = dirContents.get(cacheKey)
      if (cached) {
        return { success: true, data: cached, timestamp: '', notModified: true }
      }
      // 304 但本地无缓存（异常路径）：退化为一次无条件全量请求，
      // 保证调用方拿到数据而不是空
      response = await http.get(`/files/browse/${encodedPath}`, {
        params: { includeHidden }
      }) as DirectoryResult
    }
    if (response.data) {
      dirContents.set(cacheKey, response.data)
      trimDirCache(dirContents)
      // 响应头由拦截器以不可枚举 __headers 挂载（见 api/http.ts）
      const newEtag = (response as unknown as { __headers?: Record<string, string> }).__headers?.etag
      if (newEtag) {
        dirEtags.set(cacheKey, newEtag)
        trimDirCache(dirEtags)
      }
    }
    return response
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
    // true 时只命中图片内 OCR 识别文字（后端 ocrOnly），用于「识别文字搜索」
    ocrOnly?: boolean
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

  // 最近文件（Dashboard 面板）：按创建时间倒序取前 count 条
  async getRecentFiles(count = 6): Promise<ApiResponse<FileItem[]>> {
    return http.get('/files/recent', { params: { count } })
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
    onProgress?: (percentage: number) => void,
    // 覆盖已存在文件。后端 overwrite=true 时走替换路径并触发自动版本
    //（版本功能的唯一触发点）。缺省 false：撞已存在返回 409
    overwrite = false
  ): Promise<ApiResponse<FileItem>> {
    const formData = new FormData()
    // 字段顺序：path、overwrite 必须先于 file。后端用 MultipartReader 流式解析，
    // 文件 part 的流出现时就要立即消费，拿不到后面的字段
    // （S5：边收边写单趟 IO，替代 ReadFormAsync 落临时盘的三趟）
    formData.append('path', destinationPath)
    formData.append('overwrite', String(overwrite))
    formData.append('file', file)

    return http.post('/files/upload', formData, {
      // 覆盖实例默认的 30s：大文件上传必然超过，被客户端中断会让用户看到
      // "超时"而服务端还在写，结果文件落盘一半。这里不设超时，
      // 由服务端 Kestrel 的 MaxRequestBodySize 与物理写失败兜底
      timeout: 0,
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

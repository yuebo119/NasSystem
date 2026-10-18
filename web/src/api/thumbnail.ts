import http from './http'
import type { ApiResponse } from '@/types'

export const thumbnailApi = {
  // 清除某个文件的缩略图缓存（下次访问重新生成）。
  // 换源文件/缩略图显示异常时用
  async clearCache(path: string): Promise<ApiResponse> {
    const encoded = path.replace(/^\//, '')
    return http.delete(`/thumbnails/cache/${encoded}`)
  }
}

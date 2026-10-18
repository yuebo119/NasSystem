import http from './http'
import type { ApiResponse } from '@/types'

export const thumbnailApi = {
  // 清除某个文件的缩略图缓存（下次访问重新生成）。
  // 换源文件/缩略图显示异常时用
  async clearCache(path: string): Promise<ApiResponse> {
    const encoded = path.replace(/^\//, '')
    return http.delete(`/thumbnails/cache/${encoded}`)
  },

  // 清空全部缩略图缓存（管理员）：批量换源文件/缩略图整体异常时用。
  // 代价是下次访问全量重新生成，确认文案要说明
  async clearAllCache(): Promise<ApiResponse> {
    return http.delete('/thumbnails/cache')
  }
}

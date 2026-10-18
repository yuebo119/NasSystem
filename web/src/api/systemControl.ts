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

export const systemControlApi = {
  // 重启服务（202 + 后台调度；Docker restart 策略会拉回进程）
  async restart(): Promise<ApiResponse> {
    return http.post('/system/restart')
  },

  // 关机（停止进程；不期待宿主拉回）
  async shutdown(): Promise<ApiResponse> {
    return http.post('/system/shutdown')
  }
}

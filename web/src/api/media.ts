import http from './http'
import type { ApiResponse, PagedResult, MediaItem, MediaMetadata } from '@/types'

export type MediaType = 'image' | 'video' | 'audio'

// 转码能力状态（配置页展示：编码器与码率阶梯）
export interface TranscodeStatus {
  ffmpegAvailable: boolean
  encoder: string
  isHardwareEncoder: boolean
  rungs: { height: number; maxBitrateKbps: number; name: string }[]
}

export const mediaApi = {
  // 媒体列表：按类型聚合，创建时间倒序，可选月份过滤
  async getList(
    type: MediaType,
    options?: { month?: string; page?: number; pageSize?: number }
  ): Promise<ApiResponse<PagedResult<MediaItem>>> {
    return http.get('/media', {
      params: { type, ...options }
    })
  },

  // 批量元数据（一次一页）：图片尺寸、视频/音频时长
  async getMetadata(paths: string[]): Promise<ApiResponse<MediaMetadata[]>> {
    return http.post('/media/metadata', { paths })
  },

  // 转码能力状态：ffmpeg 可用性、当前编码器（硬件/软件）、码率阶梯
  async getTranscodeStatus(): Promise<ApiResponse<TranscodeStatus>> {
    return http.get('/media/stream/status')
  }
}

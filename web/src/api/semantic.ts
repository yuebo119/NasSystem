import http from './http'
import type { ApiResponse } from '@/types'

// 语义搜图（ROADMAP Phase 2）：CLIP 图文向量 + sqlite-vec KNN

export interface SemanticModelFile {
  name: string
  present: boolean
  sizeBytes: number
}

export interface SemanticStatus {
  model: {
    ready: boolean
    files: SemanticModelFile[]
  }
  index_: {
    totalImages: number
    indexedImages: number
    isIndexing: boolean
    error?: string
  }
}

export interface SemanticHit {
  id: string
  name: string
  path: string
  sizeBytes: number
  distance: number
}

export const semanticApi = {
  // 模型与索引状态（设置页卡片轮询用）
  async getStatus(): Promise<ApiResponse<SemanticStatus>> {
    return http.get('/semantic/status')
  },

  // 下载模型四件套（约 606MB，慢动作；前端轮询 status 观察就位）
  async downloadModel(): Promise<ApiResponse<null>> {
    return http.post('/semantic/model/download', {}, { timeout: 0 })
  },

  // 手动触发一轮索引：立即唤醒后台建库（auto-index 关闭时的手动入口）。
  // 202 只表示已受理，真正编码在后台——前端继续轮询 status 看进度
  async triggerIndex(): Promise<ApiResponse<null>> {
    return http.post('/semantic/index/trigger')
  },

  // 语义搜图：返回命中的图片（按距离升序）
  async search(query: string, topK = 50): Promise<ApiResponse<{ items: SemanticHit[] }>> {
    return http.post('/semantic/search', { query, topK })
  }
}

import http from './http'
import type { ApiResponse, OcrText, OcrBatchResult } from '@/types'

export const ocrApi = {
  // 识别图片文字并落库（重复识别覆盖旧结果）
  async recognize(path: string): Promise<ApiResponse<OcrText>> {
    const encoded = path.replace(/^\//, '')
    return http.post(`/files/ocr/${encoded}`)
  },

  // 查询已识别的文字（未识别过 data 为 null）
  async getText(path: string): Promise<ApiResponse<OcrText | null>> {
    const encoded = path.replace(/^\//, '')
    return http.get(`/files/ocr/${encoded}`)
  },

  // 批量识别目录下的图片（已有文字的默认跳过）
  async recognizeDirectory(path: string, force = false): Promise<ApiResponse<OcrBatchResult>> {
    return http.post('/files/ocr-batch', { path, force })
  }
}

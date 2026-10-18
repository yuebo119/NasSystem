import http from './http'
import type { ApiResponse, OcrText } from '@/types'

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
  }
}

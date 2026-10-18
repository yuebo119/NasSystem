import http from './http'
import type { ApiResponse, FileVersion } from '@/types'

export const fileVersionApi = {
  // 获取文件的所有历史版本（覆盖上传与永久删除前自动产版本，ROADMAP Phase 1）
  async getVersions(fileItemId: string): Promise<ApiResponse<FileVersion[]>> {
    return http.get(`/file-versions/${fileItemId}`)
  },

  // 下载指定历史版本
  getVersionDownloadUrl(fileItemId: string, versionNumber: number): string {
    return `/api/v1/file-versions/${fileItemId}/${versionNumber}/download`
  },

  // 恢复到指定历史版本（活动内容被版本内容替换，恢复前会自动再产一个版本）
  async restore(fileItemId: string, versionNumber: number): Promise<ApiResponse<FileVersion>> {
    return http.post(`/file-versions/${fileItemId}/${versionNumber}/restore`)
  },

  // 删除指定历史版本（当前版本不可删）
  async delete(fileItemId: string, versionNumber: number): Promise<ApiResponse> {
    return http.delete(`/file-versions/${fileItemId}/${versionNumber}`)
  }
}

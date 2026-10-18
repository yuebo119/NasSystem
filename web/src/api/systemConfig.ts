import http from './http'
import type { ApiResponse, SystemConfigSnapshot } from '@/types'

export const systemConfigApi = {
  // 配置快照：descriptor + 当前生效值（前端按 descriptor 渲染表单）
  async get(): Promise<ApiResponse<SystemConfigSnapshot>> {
    return http.get('/system/config')
  },

  // 批量保存（全量校验，任一非法整单拒绝）
  async update(values: Record<string, string>): Promise<ApiResponse> {
    return http.put('/system/config', { values })
  }
}

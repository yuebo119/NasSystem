import http from './http'
import type { ApiResponse, BackupDevice, BackupSyncPlan } from '@/types'

export const backupApi = {
  // 注册备份设备（同用户名下设备名唯一）
  async register(name: string, deviceModel?: string): Promise<ApiResponse<BackupDevice>> {
    return http.post('/backup/devices', { name, deviceModel })
  },

  // 我的设备列表（含已吊销）
  async getDevices(): Promise<ApiResponse<BackupDevice[]>> {
    return http.get('/backup/devices')
  },

  // 吊销设备（保留记录与已备份文件）
  async revoke(deviceId: string): Promise<ApiResponse> {
    return http.delete(`/backup/devices/${deviceId}`)
  },

  // 计算同步计划：客户端上报清单，服务端返回待上传条目
  async computePlan(
    deviceId: string,
    entries: { relativePath: string; sizeBytes: number }[]
  ): Promise<ApiResponse<BackupSyncPlan>> {
    return http.post(`/backup/devices/${deviceId}/plan`, { entries })
  },

  // 同步完成登记
  async recordComplete(deviceId: string, uploadedFileCount: number): Promise<ApiResponse> {
    return http.post(`/backup/devices/${deviceId}/sync-complete`, null, {
      params: { uploadedFileCount }
    })
  }
}

// =============================================================================
// NasSystem.Web - share.ts
// 共享 API
// =============================================================================

import http from './http'

// 类型定义
export interface Share {
  id: string
  name: string
  description?: string
  path: string
  physicalPath: string
  shareType: number
  shareTypeName: string
  isEnabled: boolean
  isPublic: boolean
  isReadOnly: boolean
  enableRecycleBin: boolean
  quotaBytes?: number
  smbShareName?: string
  createdByUserId: string
  createdByUserName?: string
  storagePoolId: string
  storagePoolName?: string
  createdAt: string
}

export interface SharePermission {
  id: string
  shareId: string
  userId: string
  userName?: string
  permissionLevel: number
  permissionLevelName: string
}

export interface CreateShareRequest {
  name: string
  path: string
  shareType: number
  storagePoolId: string
  description?: string
  isPublic?: boolean
  isReadOnly?: boolean
}

export interface UpdateShareRequest {
  name?: string
  description?: string
  isEnabled?: boolean
  isPublic?: boolean
  isReadOnly?: boolean
  enableRecycleBin?: boolean
  quotaBytes?: number
}

// API 函数
export const shareApi = {
  // 获取所有共享
  getShares: () => http.get<Share[]>('/shares'),

  // 获取单个共享
  getShare: (id: string) => http.get<Share>(`/shares/${id}`),

  // 创建共享
  createShare: (data: CreateShareRequest) => http.post<Share>('/shares', data),

  // 更新共享
  updateShare: (id: string, data: UpdateShareRequest) => 
    http.put<Share>(`/shares/${id}`, data),

  // 删除共享
  deleteShare: (id: string) => http.delete(`/shares/${id}`),

  // 启用/禁用共享
  toggleShare: (id: string, enabled: boolean) => 
    http.patch(`/shares/${id}/enabled`, { enabled }),

  // 获取共享权限
  getPermissions: (shareId: string) => 
    http.get<SharePermission[]>(`/shares/${shareId}/permissions`),

  // 添加权限
  addPermission: (shareId: string, userId: string, permissionLevel: number) => 
    http.post<SharePermission>(`/shares/${shareId}/permissions`, { userId, permissionLevel }),

  // 删除权限
  removePermission: (shareId: string, permissionId: string) => 
    http.delete(`/shares/${shareId}/permissions/${permissionId}`),
}

export default shareApi

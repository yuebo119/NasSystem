// =============================================================================
// NasSystem.Web - storage.ts
// 存储 API
// =============================================================================

import http from './http'

// 类型定义
export interface StoragePool {
  id: string
  name: string
  description?: string
  mountPath: string
  raidLevel: number
  raidLevelName: string
  status: number
  statusName: string
  totalBytes: number
  usedBytes: number
  availableBytes: number
  usagePercentage: number
  maxCapacityBytes?: number
  maxCapacityDisplay?: string
  isDefault: boolean
  isEnabled: boolean
  enableDeduplication: boolean
  enableCompression: boolean
  diskCount: number
  createdAt: string
}

export interface StorageOverview {
  totalBytes: number
  usedBytes: number
  availableBytes: number
  usagePercentage: number
  poolCount: number
  diskCount: number
  healthStatus: string
}

export interface CreatePoolRequest {
  name: string
  mountPath: string
  raidLevel: number
  description?: string
  diskIds?: string[]
  maxCapacityBytes?: number
}

export interface CheckStorageSpaceRequest {
  fileSize: number
  storagePoolId?: string
}

export interface CheckStorageSpaceResponse {
  hasEnoughSpace: boolean
  availableBytes: number
  requiredBytes: number
  message?: string
}

// 目录浏览相关类型
export interface DirectoryItem {
  name: string
  path: string
  isRoot: boolean
}

export interface BrowseDirectoriesResult {
  currentPath: string
  parentPath?: string
  directories: DirectoryItem[]
}

export interface DriveInfo {
  name: string
  label: string
  driveType: string
  totalSize: number
  availableSpace: number
  totalDisplay: string
  availableDisplay: string
}

// API 函数
export const storageApi = {
  // 获取存储概览
  getOverview: () => http.get<StorageOverview>('/storage/overview'),

  // 获取所有存储池
  getPools: () => http.get<StoragePool[]>('/storage/pools'),

  // 获取单个存储池
  getPool: (id: string) => http.get<StoragePool>(`/storage/pools/${id}`),

  // 创建存储池
  createPool: (data: CreatePoolRequest) => http.post<StoragePool>('/storage/pools', data),

  // 更新存储池
  updatePool: (id: string, data: Partial<CreatePoolRequest>) => 
    http.put<StoragePool>(`/storage/pools/${id}`, data),

  // 删除存储池
  deletePool: (id: string) => http.delete(`/storage/pools/${id}`),

  // 设置为默认存储池
  setDefault: (id: string) => http.post(`/storage/pools/${id}/default`),

  // 获取磁盘列表
  getDisks: () => http.get('/storage/disks'),

  // 浏览目录
  browseDirectories: (path?: string) => 
    http.get<BrowseDirectoriesResult>('/system/browse-directories', { params: { path } }),

  // 获取驱动器列表
  getDrives: () => http.get<DriveInfo[]>('/system/drives'),

  // 检查存储空间
  checkSpace: (data: CheckStorageSpaceRequest) => 
    http.post<CheckStorageSpaceResponse>('/storage/check-space', data),
}

export default storageApi

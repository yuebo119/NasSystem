// =============================================================================
// 类型定义
// =============================================================================

// API 响应
export interface ApiResponse<T = unknown> {
  success: boolean
  data?: T
  error?: ApiError
  timestamp: string
}

export interface ApiError {
  code: string
  message: string
  details?: Record<string, string[]>
}

// 分页
export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

// 用户
export interface User {
  id: string
  username: string
  displayName?: string
  email?: string
  role: UserRole
  isEnabled: boolean
  lastLoginAt?: string
  quotaBytes?: number
  usedBytes: number
  quotaDisplay?: string
  usedDisplay?: string
  usagePercentage?: number
  createdAt: string
}

export type UserRole = 'SuperAdmin' | 'Admin' | 'User' | 'Guest'

export interface LoginRequest {
  username: string
  password: string
  rememberMe?: boolean
}

export interface LoginResponse {
  token: string
  refreshToken: string
  expiresAt: string
  user: User
}

// 文件
// 2026-10-17：移除 sizeDisplay 与 hash，与后端 FileItemDto 对齐（后端不再下发，
// 大小展示一律前端 formatSize 现算；hash 全前端零使用）
export interface FileItem {
  id: string
  name: string
  path: string
  itemType: FileItemType
  sizeBytes: number
  mimeType?: string
  extension?: string
  parentId?: string
  ownerId: string
  ownerName?: string
  isHidden: boolean
  isReadOnly: boolean
  isFolder: boolean
  isFile: boolean
  createdAt: string
  updatedAt?: string
}

export type FileItemType = 'File' | 'Folder' | 'Symlink'

// 文件历史版本（ROADMAP Phase 1：覆盖上传与永久删除前自动产版本）
export interface FileVersion {
  id: string
  fileItemId: string
  versionNumber: number
  fileSize: number
  fileHash: string
  comment?: string
  isCurrent: boolean
  createdByUserId: string
  createdByUsername?: string
  createdAt: string
}

// 移动备份（ROADMAP Phase 1）
export interface BackupDevice {
  id: string
  name: string
  deviceModel?: string
  backupPath: string
  lastSyncAt?: string
  lastSyncFileCount: number
  isRevoked: boolean
  createdAt: string
}

// 图片 OCR 文本（ROADMAP Phase 2）
export interface OcrText {
  fileItemId: string
  path: string
  text: string
  charCount: number
  model: string
  elapsedMs: number
  createdAt: string
}

// 同步计划：toUpload 由客户端逐条走标准上传端点，
// orphanedOnServer 只上报（服务端有、客户端清单没有的文件）
export interface BackupSyncPlan {
  deviceId: string
  backupPath: string
  toUpload: { relativePath: string; sizeBytes: number }[]
  upToDateCount: number
  orphanedOnServer: string[]
}

export interface DirectoryContent {
  path: string
  name: string
  parentId?: string
  parentPath?: string
  items: FileItem[]
  folderCount: number
  fileCount: number
  totalSizeBytes: number
  breadcrumbs: Breadcrumb[]
}

export interface Breadcrumb {
  name: string
  path: string
}

export interface CreateFolderRequest {
  parentPath: string
  name: string
}

export interface RenameRequest {
  path: string
  newName: string
}

export interface MoveOrCopyRequest {
  sourcePaths: string[]
  destinationPath: string
}

export interface DeleteRequest {
  paths: string[]
  permanent?: boolean
}

// 权限级别
export type PermissionLevel = 'None' | 'Read' | 'ReadWrite' | 'FullControl'

// 存储池（简化版）
export interface StoragePool {
  id: string
  name: string
  description?: string
  mountPath: string
  totalBytes: number
  usedBytes: number
  availableBytes: number
  usagePercentage: number
  totalDisplay?: string
  usedDisplay?: string
  availableDisplay?: string
  maxCapacityBytes?: number
  maxCapacityDisplay?: string
  isDefault: boolean
  isEnabled: boolean
  isNearlyFull: boolean
  createdAt: string
  updatedAt?: string
}

export interface StorageOverview {
  totalBytes: number
  usedBytes: number
  availableBytes: number
  usagePercentage: number
  totalDisplay?: string
  usedDisplay?: string
  availableDisplay?: string
  poolCount: number
}

// 上传
export interface UploadProgress {
  uploadId: string
  fileName: string
  totalBytes: number
  uploadedBytes: number
  percentage: number
  status: 'pending' | 'uploading' | 'completed' | 'failed'
  errorMessage?: string
}

// 文件统计
export interface FileStatistics {
  totalFiles: number
  totalFolders: number
  totalSizeBytes: number
  totalSizeDisplay?: string
  imageCount: number
  videoCount: number
  audioCount: number
  documentCount: number
  otherCount: number
  recentFilesCount: number
}

// 扫描结果
export interface ScanResult {
  addedFiles: number
  addedFolders: number
  removedFiles: number
  removedFolders: number
  updatedFiles: number
  totalSizeBytes: number
  totalSizeDisplay?: string
  duration: string
  message?: string
}

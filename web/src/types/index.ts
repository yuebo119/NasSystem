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
export interface FileItem {
  id: string
  name: string
  path: string
  itemType: FileItemType
  sizeBytes: number
  sizeDisplay?: string
  mimeType?: string
  extension?: string
  hash?: string
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

export interface DirectoryContent {
  path: string
  name: string
  parentId?: string
  parentPath?: string
  items: FileItem[]
  folderCount: number
  fileCount: number
  totalSizeBytes: number
  totalSizeDisplay?: string
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

// 共享
export interface Share {
  id: string
  name: string
  description?: string
  path: string
  shareType: ShareType
  isEnabled: boolean
  isPublic: boolean
  isReadOnly: boolean
  createdByUserId: string
  createdByUserName?: string
  storagePoolId: string
  storagePoolName?: string
  quotaBytes?: number
  quotaDisplay?: string
  smbShareName?: string
  enableRecycleBin: boolean
  createdAt: string
  updatedAt?: string
  permissions?: SharePermission[]
}

export type ShareType = 'Smb' | 'Nfs' | 'Ftp' | 'WebDav'

export interface SharePermission {
  id: string
  shareId: string
  userId?: string
  userName?: string
  groupName?: string
  permission: PermissionLevel
  isDeny: boolean
}

export type PermissionLevel = 'None' | 'Read' | 'Write' | 'Full'

// 存储
export interface StoragePool {
  id: string
  name: string
  description?: string
  mountPath: string
  status: StoragePoolStatus
  raidLevel: RaidLevel
  totalBytes: number
  usedBytes: number
  availableBytes: number
  usagePercentage: number
  totalDisplay?: string
  usedDisplay?: string
  availableDisplay?: string
  isDefault: boolean
  isEnabled: boolean
  enableDeduplication: boolean
  enableCompression: boolean
  createdAt: string
  disks?: StoragePoolDisk[]
}

export type StoragePoolStatus = 'Unknown' | 'Healthy' | 'Degraded' | 'Critical' | 'Rebuilding' | 'Offline'
export type RaidLevel = 'None' | 'Raid0' | 'Raid1' | 'Raid5' | 'Raid6' | 'Raid10'

export interface StoragePoolDisk {
  id: string
  serialNumber: string
  model: string
  devicePath: string
  sizeBytes: number
  sizeDisplay?: string
  diskType: DiskType
  status: DiskStatus
  healthPercentage: number
  temperatureCelsius?: number
  powerOnHours?: number
  isHotSpare: boolean
  lastCheckedAt?: string
}

export type DiskType = 'Unknown' | 'Hdd' | 'Ssd' | 'Nvme'
export type DiskStatus = 'Unknown' | 'Healthy' | 'Warning' | 'Critical' | 'Failed'

export interface StorageOverview {
  totalBytes: number
  usedBytes: number
  availableBytes: number
  usagePercentage: number
  totalDisplay?: string
  usedDisplay?: string
  availableDisplay?: string
  poolCount: number
  diskCount: number
  healthyDiskCount: number
  warningDiskCount: number
  criticalDiskCount: number
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

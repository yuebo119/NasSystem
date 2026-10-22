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

/** 用户全站统计（GET /users/stats）。统计卡专用，与分页数据解耦 */
export interface UserStats {
  totalUsers: number
  adminCount: number
  enabledCount: number
  totalUsedBytes: number
}

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
  // 按行的位置区域（2026-10-21 OCR 叠加定位）。与 text 的行同序，
  // 坐标为归一化四角（8 个 0..1 float：x1,y1,x2,y2,x3,y3,x4,y4）。
  // 存量识别结果（叠加特性之前）为空数组，前端降级纯文本展示
  regions: OcrTextRegion[]
}

// OCR 行区域：一行文字 + 归一化四角坐标（原点左上，相对图片宽高）
export interface OcrTextRegion {
  text: string
  points: number[]
}

// 目录批量 OCR 结果
export interface OcrBatchResult {
  total: number
  recognized: number
  skipped: number
  failed: number
}

// 版本对比结果
export interface VersionComparison {
  version1: FileVersion
  version2: FileVersion
  sizeDifference: number
  hashDiffers: boolean
  timeDifference: string
}

// 清单快照（ROADMAP Phase 2）
export interface Snapshot {
  id: string
  name: string
  rootPath: string
  status: number // 0=Running, 1=Completed, 2=Failed
  fileCount: number
  folderCount: number
  totalSizeBytes: number
  createdByUserId: string
  error?: string
  createdAt: string
}

export interface SnapshotEntry {
  relativePath: string
  isFolder: boolean
  sizeBytes: number
  contentHash?: string
  lastModified: string
}

// diff 条目类别：0=Added, 1=Removed, 2=Modified, 3=Unchanged
export type SnapshotDiffKind = 0 | 1 | 2 | 3

export interface SnapshotDiffItem {
  relativePath: string
  isFolder: boolean
  kind: SnapshotDiffKind
  baseSizeBytes: number
  targetSizeBytes: number
  baseHash?: string
  targetHash?: string
}

export interface SnapshotDiff {
  items: SnapshotDiffItem[]
  totalCount: number
  addedCount: number
  removedCount: number
  modifiedCount: number
  unchangedCount: number
}

// 受保护目录（ROADMAP Phase 2 只读保护）
export interface ProtectedDirectory {
  fileItemId: string
  path: string
  name: string
  note?: string
  markedByUserId: string
  createdAt: string
}

// 媒体库（ROADMAP Phase 3）
export interface MediaItem {
  id: string
  name: string
  path: string
  extension: string
  sizeBytes: number
  mediaType: MediaType
  dateGroup: string
  createdAt: string
}

export type MediaType = 'image' | 'video' | 'audio'

// 系统动态配置（配置页，descriptor 驱动）
export interface SystemConfigItem {
  key: string
  labelKey: string
  type: number // 0=Int, 1=Bool, 2=String（与后端 ConfigValueType 对齐）
  value: string
  defaultValue: string
  min?: number
  max?: number
  descriptionKey?: string
}

export interface SystemConfigGroup {
  group: string
  titleKey: string
  items: SystemConfigItem[]
}

export interface SystemConfigSnapshot {
  groups: SystemConfigGroup[]
  lastUpdatedAt?: string
}

// 元数据按需提取，未取到的字段缺席（后端 WhenWritingNull）
export interface MediaMetadata {
  path: string
  width?: number
  height?: number
  durationSeconds?: number
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
  /** 权限/读取失败被跳过的目录与文件数（后端 F-084） */
  skippedUnreadable: number
  totalSizeBytes: number
  totalSizeDisplay?: string
  duration: string
  message?: string
}

// 系统进程状态（GET /system/status）
export interface SystemStatus {
  status: string
  uptime: string
  memoryUsageMB: number
  cpuTime: string
  threadCount: number
  handleCount: number
}

// 系统信息（GET /system/info）：按域分组的全量模块。Dashboard 的八组卡与
// SystemInfo 页共用同一形状，故放公共类型而非各页私有
export interface SystemInfo {
  name: string
  version: string
  machineName: string
  startTime: string
  runningInContainer: boolean
  runtime: SystemInfoRuntime
  operatingSystem: SystemInfoOperatingSystem
  cpu: SystemInfoCpu
  memory: SystemInfoMemory
  database: SystemInfoDatabase
  network: SystemInfoNetwork
  process: SystemInfoProcess
}

export interface SystemInfoRuntime {
  framework: string
  version: string
  runtimeIdentifier: string
  osArchitecture: string
  processArchitecture: string
  is64BitProcess: boolean
  gcMode: string
  gcGen0Collections: number
  gcGen1Collections: number
  gcGen2Collections: number
}

export interface SystemInfoOperatingSystem {
  description: string
  version: string
  architecture: string
  is64Bit: boolean
  timeZone: string
  culture: string
  userName: string
}

export interface SystemInfoCpu {
  logicalCores: number
  model: string
  processCpuPercent: number
}

export interface SystemInfoMemory {
  totalPhysicalBytes: number
  processWorkingSetBytes: number
  gcHeapBytes: number
}

export interface SystemInfoDatabase {
  provider: string
  version: string
  filePath: string
  fileSizeBytes: number
  journalMode: string
}

export interface SystemInfoNetwork {
  isAvailable: boolean
  hostName: string
  primaryInterface: string
  interfaceType: string
  ipv4Addresses: string[]
  receivedBytes: number
  sentBytes: number
}

export interface SystemInfoProcess {
  threadCount: number
  handleCount: number
  pagedMemoryBytes: number
  totalProcessorSeconds: number
}

import http from './http'

// 分享访问类型
export enum ShareAccessType {
  Public = 0,    // 公开访问
  Password = 1,  // 密码保护
  Internal = 2   // 仅内部用户
}

// 权限级别
export enum PermissionLevel {
  None = 0,
  Read = 1,
  ReadWrite = 2,
  FullControl = 3
}

// 文件分享 DTO
export interface FileShareDto {
  id: string
  shareCode: string
  shareUrl: string
  fileItemId: string
  fileName: string
  filePath: string
  isFolder: boolean
  sizeBytes?: number
  sizeDisplay?: string
  accessType: ShareAccessType
  permission: PermissionLevel
  expiresAt?: string
  maxDownloads?: number
  downloadCount: number
  isEnabled: boolean
  isExpired: boolean
  isDownloadLimitReached: boolean
  createdByUserId: string
  createdByUserName?: string
  createdAt: string
  updatedAt?: string
}

// 公开分享信息 DTO
export interface PublicShareInfoDto {
  shareCode: string
  fileName: string
  isFolder: boolean
  sizeBytes?: number
  sizeDisplay?: string
  accessType: ShareAccessType
  permission: PermissionLevel
  requiresPassword: boolean
  isExpired: boolean
  isDownloadLimitReached: boolean
  isAccessible: boolean
  createdByUserName?: string
}

// 创建分享请求
export interface CreateFileShareRequest {
  fileItemId: string
  accessType?: ShareAccessType
  password?: string
  permission?: PermissionLevel
  expiresAt?: string
  maxDownloads?: number
}

// 更新分享请求
export interface UpdateFileShareRequest {
  accessType?: ShareAccessType
  password?: string
  clearPassword?: boolean
  permission?: PermissionLevel
  expiresAt?: string
  clearExpiresAt?: boolean
  maxDownloads?: number
  clearMaxDownloads?: boolean
  isEnabled?: boolean
}

// 分享文件夹内容
export interface ShareFolderContentDto {
  shareCode: string
  currentPath: string
  items: ShareFileItemDto[]
}

// 分享文件项
export interface ShareFileItemDto {
  id: string
  name: string
  isFolder: boolean
  sizeBytes?: number
  sizeDisplay?: string
  mimeType?: string
  createdAt: string
  updatedAt?: string
}

// 管理 API
export const fileShareApi = {
  // 创建分享
  create: (data: CreateFileShareRequest) =>
    http.post<FileShareDto>('/file-shares', data),

  // 获取我的分享列表
  getMyShares: () =>
    http.get<FileShareDto[]>('/file-shares'),

  // 获取分享详情
  getById: (id: string) =>
    http.get<FileShareDto>(`/file-shares/${id}`),

  // 更新分享
  update: (id: string, data: UpdateFileShareRequest) =>
    http.put<FileShareDto>(`/file-shares/${id}`, data),

  // 删除分享
  delete: (id: string) =>
    http.delete(`/file-shares/${id}`),

  // 重新生成分享码
  regenerateCode: (id: string) =>
    http.post<FileShareDto>(`/file-shares/${id}/regenerate-code`)
}

// 认证用户的分享访问 API（ITM-N23：Internal 型分享的唯一合法通道）。
// 已登录用户点开内部分享链接时走这一组：Bearer 头由 http 拦截器自动带上，
// 下载因要带 Authorization 头不能用 <a href>，与 files/download 同型走 fetch + blob
export const authenticatedShareApi = {
  // 认证下载：单文件走 Range 流式响应，文件夹走 ZipDirectoryResult
  async download(shareCode: string, subPath?: string): Promise<Blob> {
    const token = localStorage.getItem('token')
    let url = `/api/v1/file-shares/${shareCode}/download`
    if (subPath) url += `/${subPath}`
    const response = await fetch(url, {
      headers: token ? { Authorization: `Bearer ${token}` } : {}
    })
    if (!response.ok) {
      throw new Error(`authenticated share download failed: ${response.status}`)
    }
    return response.blob()
  },

  // 认证浏览分享文件夹（密码型分享仍要求 accessToken，认证身份不豁免密码校验）
  getFolderContent: (shareCode: string, subPath?: string, token?: string) => {
    let url = `/file-shares/${shareCode}/files`
    if (subPath) url += `/${subPath}`
    const params: Record<string, string> = {}
    if (token) params.token = token
    return http.get<ShareFolderContentDto>(url, { params })
  }
}

// 公开访问 API
export const publicShareApi = {
  // 获取公开分享信息
  getInfo: (shareCode: string) =>
    http.get<PublicShareInfoDto>(`/public-shares/${shareCode}`),

  // 验证密码
  verifyPassword: (shareCode: string, password: string) =>
    http.post<{ accessToken: string }>(`/public-shares/${shareCode}/verify`, { password }),

  // 获取下载链接
  getDownloadUrl: (shareCode: string, token?: string, subPath?: string) => {
    let url = `/api/v1/public-shares/${shareCode}/download`
    if (subPath) url += `/${subPath}`
    if (token) url += `?token=${encodeURIComponent(token)}`
    return url
  },

  // 公开下载。token 已内嵌 URL，不需要 Authorization 头，但同样不能用
  // <a href> 导航：浏览器原生下载不可观测，token 失效或分享已删时页面内
  // 零反馈（2026-10-21 审计 P2：loading 纯靠 1 秒定时器收场）。走 fetch
  // 后失败进 catch，与 authenticatedShareApi.download 同型
  async download(shareCode: string, token?: string, subPath?: string): Promise<Blob> {
    const response = await fetch(
      publicShareApi.getDownloadUrl(shareCode, token, subPath))
    if (!response.ok) {
      throw new Error(`public share download failed: ${response.status}`)
    }
    return response.blob()
  },

  // 获取文件夹内容
  getFolderContent: (shareCode: string, token?: string, subPath?: string) => {
    let url = `/public-shares/${shareCode}/files`
    if (subPath) url += `/${subPath}`
    const params: Record<string, string> = {}
    if (token) params.token = token
    return http.get<ShareFolderContentDto>(url, { params })
  }
}

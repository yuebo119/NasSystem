import http from './http'
import type { ApiResponse, User, LoginRequest, LoginResponse, PagedResult } from '@/types'

export const userApi = {
  // 登录
  async login(data: LoginRequest): Promise<ApiResponse<LoginResponse>> {
    return http.post('/users/login', data)
  },

  // 登出
  async logout(): Promise<ApiResponse> {
    return http.post('/users/logout')
  },

  // 获取当前用户信息
  async getCurrentUser(): Promise<ApiResponse<User>> {
    return http.get('/users/me')
  },

  // 获取用户列表
  async getUsers(params?: {
    page?: number
    pageSize?: number
    searchTerm?: string
  }): Promise<ApiResponse<PagedResult<User>>> {
    return http.get('/users', { params })
  },

  // 获取用户详情
  async getUser(id: string): Promise<ApiResponse<User>> {
    return http.get(`/users/${id}`)
  },

  // 创建用户
  async createUser(data: {
    username: string
    password: string
    displayName?: string
    email?: string
    role?: string
    quotaBytes?: number
  }): Promise<ApiResponse<User>> {
    return http.post('/users', data)
  },

  // 更新用户
  async updateUser(id: string, data: {
    displayName?: string
    email?: string
    role?: string
    isEnabled?: boolean
    quotaBytes?: number
  }): Promise<ApiResponse<User>> {
    return http.put(`/users/${id}`, data)
  },

  // 删除用户
  async deleteUser(id: string): Promise<ApiResponse> {
    return http.delete(`/users/${id}`)
  },

  // 修改密码
  async changePassword(id: string, data: {
    currentPassword: string
    newPassword: string
  }): Promise<ApiResponse> {
    return http.post(`/users/${id}/password`, data)
  },

  // 重置密码 (管理员)
  async resetPassword(id: string, data: {
    newPassword: string
  }): Promise<ApiResponse> {
    return http.post(`/users/${id}/reset-password`, data)
  }
}

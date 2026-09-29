import axios from 'axios'
import type { AxiosInstance, AxiosError, InternalAxiosRequestConfig } from 'axios'
import type { ApiResponse } from '@/types'
import { ElMessage } from 'element-plus'
import router from '@/router'
import i18n from '@/locales'

// 创建 axios 实例
const http: AxiosInstance = axios.create({
  baseURL: '/api/v1',
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json'
  }
})

// 请求拦截器
http.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const token = localStorage.getItem('token')
    if (token && config.headers) {
      config.headers.Authorization = `Bearer ${token}`
    }
    return config
  },
  (error: AxiosError) => {
    return Promise.reject(error)
  }
)

// 响应拦截器
http.interceptors.response.use(
  (response) => {
    return response.data
  },
  (error: AxiosError<ApiResponse>) => {
    const { response } = error
    // 模块级无组件上下文，经 i18n 全局实例取翻译函数（locales 不反向依赖 api，无循环导入）
    const t = i18n.global.t

    if (response) {
      const { status, data } = response
      
      switch (status) {
        case 401:
          // 未授权，清除登录状态并跳转登录页
          localStorage.removeItem('token')
          router.push({ name: 'Login' })
          ElMessage.error(t('error.sessionExpired'))
          break
        case 403:
          ElMessage.error(t('error.forbidden'))
          break
        case 404:
          ElMessage.error(data?.error?.message || t('error.notFound'))
          break
        case 500:
          ElMessage.error(t('error.serverError'))
          break
        default:
          ElMessage.error(data?.error?.message || t('error.requestFailed'))
      }
    } else if (error.code === 'ECONNABORTED') {
      ElMessage.error(t('error.timeout'))
    } else {
      ElMessage.error(t('error.network'))
    }
    
    return Promise.reject(error)
  }
)

export default http

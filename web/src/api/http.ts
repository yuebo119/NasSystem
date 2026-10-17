import axios from 'axios'
import type { AxiosInstance, AxiosError, InternalAxiosRequestConfig } from 'axios'
import type { ApiResponse } from '@/types'
import { ElMessage } from 'element-plus'
import router from '@/router'
import i18n from '@/locales'

// 创建 axios 实例
// timeout 按请求类型区分：默认 30s 只适合普通 JSON API。
// 上传走 formData 且后端允许 500MB，30s 传不完就被 axios 中断，
// 而服务端可能还在继续写，结果是用户看到"超时"但文件落盘一半。
// 上传因此不设客户端超时，靠服务端 Kestrel 的 MaxRequestBodySize 与
// 磁盘写失败兜底（http.ts 的 upload 方法单独覆盖 timeout）
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

import axios from 'axios'
import type { AxiosInstance, AxiosError, InternalAxiosRequestConfig } from 'axios'
import type { ApiResponse } from '@/types'
import { ElMessage } from 'element-plus'
import router from '@/router'
import i18n from '@/locales'

// 自定义请求配置：401 时跳过拦截器的自动跳登录。供会话校验类请求
// （/users/me）使用——它们的 401 由 store 与路由守卫自行处理
declare module 'axios' {
  export interface AxiosRequestConfig {
    skipAuthRedirect?: boolean
  }
}

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
    // 条件请求支持：响应头挂到返回体的 __headers 上（ETag 机制用）。
    // 用不可枚举属性挂载：JSON 序列化与 deep-equal 都看不到它，
    // 消费方按 .success/.data 解构或整体比对均不受影响
    if (response.data && typeof response.data === 'object') {
      Object.defineProperty(response.data, '__headers', {
        value: response.headers,
        enumerable: false,
        configurable: true
      })
    }
    return response.data
  },
  (error: AxiosError<ApiResponse>) => {
    const { response } = error
    // 模块级无组件上下文，经 i18n 全局实例取翻译函数（locales 不反向依赖 api，无循环导入）
    const t = i18n.global.t

    if (response) {
      const { status, data } = response

      if (status === 304) {
        // Not Modified：条件请求命中（浏览目录的 ETag）。归一成成功空响应，
        // 让调用方走"复用缓存"分支——落进下面的错误分支会误弹"请求失败"
        return Promise.resolve({ success: true, data: null, notModified: true })
      }

      if (status === 401) {
        // skipAuthRedirect 的请求（会话校验）不跳转：token 失效由 store
        // 的 checkAuth 清状态、路由守卫带 redirect 跳登录。否则公开分享页
        // 的访客带过期令牌时，checkAuth 的 401 会经这里把人弹去登录页，
        // 分享页永远加载不出来，且丢失原目标地址（E2E 走查实证）
        if (!error.config?.skipAuthRedirect) {
          // 未授权，清除登录状态并跳转登录页
          localStorage.removeItem('token')
          router.push({ name: 'Login' })
          ElMessage.error(t('error.sessionExpired'))
        }
      } else {
        switch (status) {
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

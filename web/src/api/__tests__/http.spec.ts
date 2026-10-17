/**
 * http 拦截器契约测试。
 *
 * 【为什么必须测这组】拦截器是全站请求的静默公共层：401 跳登录、
 * token 装配、错误提示分流，任何一处回归都是全局性的，而调用方
 * （各 api 模块）的测试 mock 掉 http 后根本碰不到这条路径。
 *
 * 测法：axios adapter 注入假响应，element-plus/router/locales 三个
 * 模块级依赖用 mock 替身，不改动 http.ts 生产代码。t 的替身表与
 * zh-CN.ts 的 error 段逐字对齐，断言的是"哪把钥匙开哪把锁"。
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'

vi.mock('element-plus', () => ({
  ElMessage: { error: vi.fn() }
}))

// t 替身表与 src/locales/zh-CN.ts 的 error 段逐字一致
vi.mock('@/locales', () => ({
  default: {
    global: {
      t: (key: string) => {
        const table: Record<string, string> = {
          'error.sessionExpired': '登录已过期，请重新登录',
          'error.forbidden': '没有权限执行此操作',
          'error.notFound': '请求的资源不存在',
          'error.serverError': '服务器错误，请稍后重试',
          'error.requestFailed': '请求失败',
          'error.timeout': '请求超时，请检查网络连接',
          'error.network': '网络错误，请检查网络连接'
        }
        return table[key] ?? key
      }
    }
  }
}))

const routerPush = vi.fn()
vi.mock('@/router', () => ({
  default: { push: (...args: unknown[]) => routerPush(...args) }
}))

import http from '@/api/http'
import { ElMessage } from 'element-plus'

const mockMessage = vi.mocked(ElMessage.error)

/** 捕获最后一次实际发出的配置，用于断言请求头 */
let lastConfig: InternalAxiosRequestConfig | undefined

/** 注入成功响应（200），data 原样返回给调用方 */
function respondWith(data: unknown) {
  http.defaults.adapter = async (config) => {
    lastConfig = config
    return { data, status: 200, statusText: 'OK', headers: {}, config } as AxiosResponse
  }
}

/** 注入带 HTTP 状态码的失败响应 */
function rejectWith(status: number, data?: unknown) {
  http.defaults.adapter = async (config) => {
    const response = { data, status, statusText: '', headers: {}, config } as AxiosResponse
    throw new AxiosError(`Request failed with status code ${status}`, String(status), config, undefined, response)
  }
}

/** 注入无 response 的网络层失败，code 为 ECONNABORTED 时是超时 */
function rejectNetwork(code?: string) {
  http.defaults.adapter = async (config) => {
    throw new AxiosError(code ? 'timeout of 0ms exceeded' : 'Network Error', code, config)
  }
}

function authorizationOf(config: InternalAxiosRequestConfig | undefined): string | undefined {
  return (config?.headers as unknown as Record<string, string> | undefined)?.['Authorization']
}

describe('http 请求拦截器', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    lastConfig = undefined
  })

  afterEach(() => {
    localStorage.clear()
  })

  it('localStorage 有 token 时请求带 Authorization 头', async () => {
    localStorage.setItem('token', 'tok-abc')
    respondWith({ success: true })

    await http.get('/users/me')

    expect(authorizationOf(lastConfig)).toBe('Bearer tok-abc')
  })

  it('无 token 时不装配 Authorization 头', async () => {
    respondWith({ success: true })

    await http.get('/users/me')

    expect(authorizationOf(lastConfig)).toBeUndefined()
  })
})

describe('http 响应拦截器', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    lastConfig = undefined
  })

  afterEach(() => {
    localStorage.clear()
  })

  it('成功响应剥壳返回 data，不弹提示', async () => {
    respondWith({ success: true, data: { id: 'u1' }, timestamp: '' })

    const result = await http.get('/users/me')

    expect(result).toEqual({ success: true, data: { id: 'u1' }, timestamp: '' })
    expect(mockMessage).not.toHaveBeenCalled()
    expect(routerPush).not.toHaveBeenCalled()
  })

  it('401 清除本地 token、跳转登录页并提示会话过期', async () => {
    localStorage.setItem('token', 'tok-abc')
    rejectWith(401, { error: { code: 'Unauthorized', message: 'x' } })

    await expect(http.get('/users/me')).rejects.toThrow()

    expect(localStorage.getItem('token')).toBeNull()
    expect(routerPush).toHaveBeenCalledWith({ name: 'Login' })
    expect(mockMessage).toHaveBeenCalledWith('登录已过期，请重新登录')
  })

  it('403 提示无权限，不清 token 不跳转', async () => {
    localStorage.setItem('token', 'tok-abc')
    rejectWith(403)

    await expect(http.get('/users')).rejects.toThrow()

    expect(mockMessage).toHaveBeenCalledWith('没有权限执行此操作')
    expect(localStorage.getItem('token')).toBe('tok-abc')
    expect(routerPush).not.toHaveBeenCalled()
  })

  it('404 优先展示后端 error.message', async () => {
    rejectWith(404, { error: { code: 'NotFound', message: '文件不存在' } })

    await expect(http.get('/files/x')).rejects.toThrow()

    expect(mockMessage).toHaveBeenCalledWith('文件不存在')
  })

  it('404 后端无 message 时回落通用不存在提示', async () => {
    rejectWith(404)

    await expect(http.get('/files/x')).rejects.toThrow()

    expect(mockMessage).toHaveBeenCalledWith('请求的资源不存在')
  })

  it('500 提示服务器错误', async () => {
    rejectWith(500)

    await expect(http.get('/files')).rejects.toThrow()

    expect(mockMessage).toHaveBeenCalledWith('服务器错误，请稍后重试')
  })

  it('其他状态码优先用后端 message', async () => {
    rejectWith(502, { error: { code: 'BadGateway', message: '上游炸了' } })

    await expect(http.get('/files')).rejects.toThrow()

    expect(mockMessage).toHaveBeenCalledWith('上游炸了')
  })

  it('ECONNABORTED 判定为超时', async () => {
    rejectNetwork('ECONNABORTED')

    await expect(http.get('/files')).rejects.toThrow()

    expect(mockMessage).toHaveBeenCalledWith('请求超时，请检查网络连接')
  })

  it('无 response 且非超时判定为网络错误', async () => {
    rejectNetwork()

    await expect(http.get('/files')).rejects.toThrow()

    expect(mockMessage).toHaveBeenCalledWith('网络错误，请检查网络连接')
  })
})

/**
 * user store 鉴权状态机测试。
 *
 * 【为什么必须测这组】这个 store 是全站登录态的单一事实源：
 * 路由守卫靠 isAuthenticated 放行/拦截，http 拦截器靠 localStorage 的 token
 * 拼 Authorization 头，登出靠它清理。它有三处容易回归的隐性契约：
 * 1. checkAuth 的 in-flight 合并（main.ts 挂载后恢复会话与路由守卫并发调用，
 *    不合并会重复打 /users/me）；
 * 2. logout 的 finally 语义（登出接口失败也必须清本地状态，否则用户被卡在
 *    一个永远认证失败的半登录态）；
 * 3. isAuthenticated 的三态语义（有 token 但尚未初始化时算"可能已认证"，
 *    让首屏不误跳登录页，D1 首屏优化依赖这一点）。
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

// userApi 整组 mock：store 只依赖 login/logout/getCurrentUser 三个方法
vi.mock('@/api/user', () => ({
  userApi: {
    login: vi.fn(),
    logout: vi.fn(),
    getCurrentUser: vi.fn()
  }
}))

import { userApi } from '@/api/user'
import { useUserStore } from '@/stores/user'
import type { LoginResponse, User } from '@/types'

const mockLogin = vi.mocked(userApi.login)
const mockLogout = vi.mocked(userApi.logout)
const mockGetCurrentUser = vi.mocked(userApi.getCurrentUser)

function makeUser(overrides: Partial<User> = {}): User {
  return {
    id: 'u1',
    username: 'admin',
    role: 'Admin',
    isEnabled: true,
    usedBytes: 0,
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides
  }
}

function loginSuccess(user: User = makeUser()): LoginResponse {
  return { token: 'tok-123', refreshToken: 'ref-456', expiresAt: '2026-10-18T00:00:00Z', user }
}

/** 后端成功响应的固定形态 */
function ok<T>(data: T) {
  return { success: true, data, timestamp: '2026-10-17T00:00:00Z' }
}

function fail() {
  return { success: false, error: { code: 'BadRequest', message: 'boom' }, timestamp: '2026-10-17T00:00:00Z' }
}

describe('useUserStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    vi.clearAllMocks()
  })

  afterEach(() => {
    localStorage.clear()
  })

  describe('login', () => {
    it('成功时写入 token/user 并标记 initialized', async () => {
      mockLogin.mockResolvedValue(ok(loginSuccess()))

      const store = useUserStore()
      const result = await store.login({ username: 'admin', password: 'x' })

      expect(result).toBe(true)
      expect(store.token).toBe('tok-123')
      expect(store.user?.username).toBe('admin')
      expect(store.initialized).toBe(true)
      expect(localStorage.getItem('token')).toBe('tok-123')
      expect(store.isAuthenticated).toBe(true)
    })

    it('success=false 时不落任何状态，返回 false', async () => {
      mockLogin.mockResolvedValue(fail())

      const store = useUserStore()
      const result = await store.login({ username: 'admin', password: 'wrong' })

      expect(result).toBe(false)
      expect(store.token).toBeNull()
      expect(store.user).toBeNull()
      expect(store.initialized).toBe(false)
      expect(localStorage.getItem('token')).toBeNull()
      expect(store.isAuthenticated).toBe(false)
    })

    it('登录接口抛错时异常向上传播，不吞', async () => {
      mockLogin.mockRejectedValue(new Error('network'))

      const store = useUserStore()
      await expect(store.login({ username: 'admin', password: 'x' })).rejects.toThrow('network')
    })
  })

  describe('logout', () => {
    it('登出接口失败时仍清空本地状态，异常照常传播给调用方', async () => {
      // finally 保证清理，无 catch 保证调用方能看到失败（不会静默吞掉网络错误）
      mockLogout.mockRejectedValue(new Error('offline'))
      mockLogin.mockResolvedValue(ok(loginSuccess()))
      const store = useUserStore()
      await store.login({ username: 'admin', password: 'x' })

      await expect(store.logout()).rejects.toThrow('offline')

      expect(store.token).toBeNull()
      expect(store.user).toBeNull()
      expect(store.initialized).toBe(true)
      expect(localStorage.getItem('token')).toBeNull()
      expect(store.isAuthenticated).toBe(false)
    })

    it('登出接口成功时同样清空', async () => {
      mockLogout.mockResolvedValue({ success: true, timestamp: '' })
      mockLogin.mockResolvedValue(ok(loginSuccess()))
      const store = useUserStore()
      await store.login({ username: 'admin', password: 'x' })

      await store.logout()

      expect(store.token).toBeNull()
      expect(localStorage.getItem('token')).toBeNull()
    })
  })

  describe('checkAuth', () => {
    it('无 token 时直接判定未认证，不打 /users/me', async () => {
      const store = useUserStore()

      const result = await store.checkAuth()

      expect(result).toBe(false)
      expect(mockGetCurrentUser).not.toHaveBeenCalled()
      expect(store.initialized).toBe(true)
      expect(store.isAuthenticated).toBe(false)
    })

    it('有 token 且接口成功时恢复用户', async () => {
      localStorage.setItem('token', 'tok-123')
      mockGetCurrentUser.mockResolvedValue(ok(makeUser({ username: 'alice' })))

      const store = useUserStore()
      const result = await store.checkAuth()

      expect(result).toBe(true)
      expect(store.user?.username).toBe('alice')
      expect(store.initialized).toBe(true)
      expect(store.token).toBe('tok-123')
    })

    it('接口抛错（token 失效）时清除本地登录态', async () => {
      localStorage.setItem('token', 'bad-token')
      mockGetCurrentUser.mockRejectedValue(new Error('401'))

      const store = useUserStore()
      const result = await store.checkAuth()

      expect(result).toBe(false)
      expect(store.token).toBeNull()
      expect(store.user).toBeNull()
      expect(localStorage.getItem('token')).toBeNull()
      expect(store.initialized).toBe(true)
    })

    it('接口返回 success=false 时判定未认证但保留 token', async () => {
      // 与抛错路径的差异：服务端业务拒绝不应清 token，只标记未认证
      localStorage.setItem('token', 'tok-123')
      mockGetCurrentUser.mockResolvedValue(fail())

      const store = useUserStore()
      const result = await store.checkAuth()

      expect(result).toBe(false)
      expect(store.token).toBe('tok-123')
      expect(localStorage.getItem('token')).toBe('tok-123')
      expect(store.initialized).toBe(true)
    })

    it('并发调用只发一次 /users/me（in-flight 合并）', async () => {
      localStorage.setItem('token', 'tok-123')
      let resolveFn: (v: unknown) => void = () => {}
      mockGetCurrentUser.mockReturnValue(new Promise((resolve) => { resolveFn = resolve }) as never)

      const store = useUserStore()
      // 模拟 main.ts 后台恢复会话与路由守卫同时触发
      const first = store.checkAuth()
      const second = store.checkAuth()

      resolveFn(ok(makeUser()))
      const [r1, r2] = await Promise.all([first, second])

      expect(mockGetCurrentUser).toHaveBeenCalledTimes(1)
      expect(r1).toBe(true)
      expect(r2).toBe(true)
    })

    it('in-flight 结束后可重新发起请求', async () => {
      localStorage.setItem('token', 'tok-123')
      mockGetCurrentUser.mockResolvedValue(ok(makeUser()))

      const store = useUserStore()
      await store.checkAuth()
      await store.checkAuth()

      expect(mockGetCurrentUser).toHaveBeenCalledTimes(2)
    })
  })

  describe('isAuthenticated / isAdmin', () => {
    it('有 token 但未初始化时视为可能已认证（首屏不误跳登录）', () => {
      localStorage.setItem('token', 'tok-123')

      const store = useUserStore()

      expect(store.initialized).toBe(false)
      expect(store.isAuthenticated).toBe(true)
    })

    it('SuperAdmin 与 Admin 判为管理员，普通用户否', async () => {
      mockLogin.mockResolvedValue(ok(loginSuccess(makeUser({ role: 'SuperAdmin' }))))
      const store = useUserStore()
      await store.login({ username: 'root', password: 'x' })
      expect(store.isAdmin).toBe(true)

      setActivePinia(createPinia())
      localStorage.clear()
      mockLogin.mockResolvedValue(ok(loginSuccess(makeUser({ role: 'User' }))))
      const store2 = useUserStore()
      await store2.login({ username: 'bob', password: 'x' })
      expect(store2.isAdmin).toBe(false)
    })
  })

  describe('updateUser', () => {
    it('有用户时浅合并局部字段', async () => {
      mockLogin.mockResolvedValue(ok(loginSuccess()))
      const store = useUserStore()
      await store.login({ username: 'admin', password: 'x' })

      store.updateUser({ displayName: '管理员' })

      expect(store.user?.displayName).toBe('管理员')
      expect(store.user?.username).toBe('admin')
      expect(store.displayName).toBe('管理员')
    })

    it('用户未加载时是 no-op 不抛错', () => {
      const store = useUserStore()

      expect(() => store.updateUser({ displayName: 'x' })).not.toThrow()
      expect(store.user).toBeNull()
    })
  })
})

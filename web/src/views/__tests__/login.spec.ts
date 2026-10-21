/**
 * Login 视图层契约测试（第二个视图 spec，挂载样板用 src/__tests__/helpers）。
 *
 * 【为什么必须测这组】登录页有三条运行时才暴露的路径：el-form 的
 * required 校验拦截（空值根本不该发请求）、登录成功后的 redirect 跳转
 * （带 query 与默认值两条）、登录失败与接口异常的分支提示。这些逻辑
 * 原先没有任何自动化兜底，而登录是整站的入口。
 *
 * 测法：真 store（mock 其 api 依赖）+ 真 memory router（验真实导航
 * 结果而非 mock 调用）+ ElMessage 替身。el-form 的校验由 element-plus
 * 真实执行，点的也是真实 submit 按钮。
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const elMessage = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  warning: vi.fn()
}))

// 只替身 ElMessage（模块级无组件上下文的通知出口），其余组件透传真实实现
vi.mock('element-plus', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>()
  return { ...actual, ElMessage: elMessage }
})

vi.mock('@/api/user', () => ({
  userApi: {
    login: vi.fn(),
    logout: vi.fn(),
    getCurrentUser: vi.fn()
  }
}))

import { userApi } from '@/api/user'
import i18n from '@/locales'
import Login from '@/views/Login.vue'
import { flush, mountView } from '@/__tests__/helpers'

const mockLogin = vi.mocked(userApi.login)

// 返回 any：userApi.login 的标注签名是 ApiResponse（axios 声明层未跟上
// 拦截器剥壳），mock 只提供被测字段
function loginOk(): any {
  return {
    success: true,
    data: {
      token: 'tok-1',
      refreshToken: 'ref-1',
      expiresAt: '2026-10-18T00:00:00Z',
      user: { id: 'u1', username: 'admin', role: 'Admin', isEnabled: true, usedBytes: 0, createdAt: '2026-01-01T00:00:00Z' }
    },
    timestamp: ''
  }
}

/** 填表单：直接写 input 值并派发 input 事件驱动 v-model */
function fillForm(div: HTMLElement, username: string, password: string) {
  const inputs = div.querySelectorAll('.login-form input')
  const userInput = inputs[0] as HTMLInputElement
  const passInput = inputs[1] as HTMLInputElement
  userInput.value = username
  userInput.dispatchEvent(new Event('input'))
  passInput.value = password
  passInput.dispatchEvent(new Event('input'))
}

/**
 * 提交表单。happy-dom 下 button.click() 不触发表单 submit 事件
 * （实测 submit 计数为 0），直接向 form 派发 cancelable 的 submit，
 * el-form 的校验链与 @submit.prevent 仍按真实路径执行
 */
function submit(div: HTMLElement) {
  const form = div.querySelector('.login-form') as HTMLFormElement
  form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
}

describe('Login 视图层契约', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    // happy-dom 的 navigator.language 是 en-US，固定中文让断言对齐 zh-CN 包
    i18n.global.locale.value = 'zh-CN'
  })

  afterEach(() => {
    localStorage.clear()
  })

  it('空表单提交被提交前 guard 拦截，不发登录请求', async () => {
    // 修复前此处是产品缺口：el-form 2.14 根元素不监听原生 submit，
    // rules 只配 trigger:'blur'，空值会直达后端。修复为 handleLogin
    // 前置手写 guard（与 Settings.changePassword 同模式，不依赖 el-form
    // 校验链），提示对应字段缺失
    mockLogin.mockResolvedValue(loginOk())
    const { div, unmount } = await mountView(Login, { route: '/login' })

    submit(div)
    await flush(50)

    expect(mockLogin).not.toHaveBeenCalled()
    expect(elMessage.warning).toHaveBeenCalledWith('请输入用户名')
    unmount()
  })

  it('只填用户名不填密码时提示密码缺失', async () => {
    mockLogin.mockResolvedValue(loginOk())
    const { div, unmount } = await mountView(Login, { route: '/login' })

    fillForm(div, 'admin', '')
    submit(div)
    await flush(50)

    expect(mockLogin).not.toHaveBeenCalled()
    expect(elMessage.warning).toHaveBeenCalledWith('请输入密码')
    unmount()
  })

  it('校验通过且登录成功：提示成功、跳转 redirect 目标、store 落态', async () => {
    mockLogin.mockResolvedValue(loginOk())
    const { div, router, unmount } = await mountView(Login, { route: '/login?redirect=/files' })

    fillForm(div, 'admin', 'admin123')
    submit(div)
    await vi.waitFor(() => {
      if (router.currentRoute.value.path !== '/files') throw new Error('not navigated')
    })

    expect(elMessage.success).toHaveBeenCalledWith('登录成功')
    expect(localStorage.getItem('token')).toBe('tok-1')
    unmount()
  })

  // 2026-10-21 全量审计 P1 回归：LoginDto.RememberMe 后端字段一直都在，
  // 但 Login.vue 提交对象只含 username/password，复选框是死控件，
  // 勾与不勾行为零差异。这两条锁住勾选状态必须进请求体
  it('勾选记住我：登录请求体带 rememberMe=true', async () => {
    mockLogin.mockResolvedValue(loginOk())
    const { div, unmount } = await mountView(Login, { route: '/login' })

    fillForm(div, 'admin', 'admin123')
    const checkbox = div.querySelector('.login-form input[type="checkbox"]') as HTMLInputElement
    checkbox.checked = true
    checkbox.dispatchEvent(new Event('change'))
    submit(div)
    await vi.waitFor(() => {
      if (!mockLogin.mock.calls.length) throw new Error('not called')
    })

    expect(mockLogin).toHaveBeenCalledWith(
      expect.objectContaining({ username: 'admin', password: 'admin123', rememberMe: true }))
    unmount()
  })

  it('不勾选记住我：登录请求体带 rememberMe=false', async () => {
    mockLogin.mockResolvedValue(loginOk())
    const { div, unmount } = await mountView(Login, { route: '/login' })

    fillForm(div, 'admin', 'admin123')
    submit(div)
    await vi.waitFor(() => {
      if (!mockLogin.mock.calls.length) throw new Error('not called')
    })

    expect(mockLogin).toHaveBeenCalledWith(
      expect.objectContaining({ username: 'admin', password: 'admin123', rememberMe: false }))
    unmount()
  })

  it('无 redirect 参数时成功登录跳默认仪表盘', async () => {
    mockLogin.mockResolvedValue(loginOk())
    const { div, router, unmount } = await mountView(Login, { route: '/login' })

    fillForm(div, 'admin', 'admin123')
    submit(div)
    await vi.waitFor(() => {
      if (router.currentRoute.value.path !== '/dashboard') throw new Error('not navigated')
    })

    expect(elMessage.success).toHaveBeenCalledWith('登录成功')
    unmount()
  })

  it('登录失败（success=false）：提示失败且停在登录页', async () => {
    mockLogin.mockResolvedValue({ success: false, error: { code: 'BadRequest', message: 'bad' }, timestamp: '' })
    const { div, router, unmount } = await mountView(Login, { route: '/login' })

    fillForm(div, 'admin', 'wrong')
    submit(div)
    await flush(50)

    expect(elMessage.error).toHaveBeenCalledWith('登录失败')
    expect(router.currentRoute.value.path).toBe('/login')
    expect(localStorage.getItem('token')).toBeNull()
    unmount()
  })

  it('登录接口抛异常：提示失败且 loading 复位', async () => {
    mockLogin.mockRejectedValue(new Error('offline'))
    const { div, unmount } = await mountView(Login, { route: '/login' })

    fillForm(div, 'admin', 'x')
    submit(div)
    await flush(50)

    expect(elMessage.error).toHaveBeenCalledWith('登录失败')
    // finally 复位 loading：按钮回到可点击态（loading class 移除）
    expect(div.querySelector('.login-button.is-loading')).toBeNull()
    unmount()
  })
})

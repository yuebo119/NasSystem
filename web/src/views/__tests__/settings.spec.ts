/**
 * Settings 视图层契约测试（第四个视图 spec）。
 *
 * 【为什么必须测这组】设置页的核心是两条表单链路：个人资料保存与
 * 密码修改。密码修改有四道前置 guard（当前密码非空、新密码非空、
 * 长度至少 6 位、两次一致），任何一道漏掉或顺序错了，无效请求就会
 * 打到后端；保存路径上的 trim 语义（空串转 undefined，让后端区分
 * "清空"与"不修改"）也是只在运行时可见的契约。
 *
 * 主题与语言切换的持久化逻辑已在 theme.spec.ts 覆盖，这里只验
 * Settings 层的调用链（handleThemeChange 确实走了 setTheme）。
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

const elMessage = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  warning: vi.fn()
}))

vi.mock('element-plus', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>()
  return { ...actual, ElMessage: elMessage }
})

vi.mock('@/api/user', () => ({
  userApi: {
    updateUser: vi.fn(),
    changePassword: vi.fn()
  }
}))

vi.mock('@/api/http', () => ({
  default: { get: vi.fn().mockResolvedValue({ success: true, data: {} }) }
}))

import { userApi } from '@/api/user'
import i18n from '@/locales'
import { useUserStore } from '@/stores/user'
import Settings from '@/views/Settings.vue'
import { flush, mountView } from '@/__tests__/helpers'

const mockUpdateUser = vi.mocked(userApi.updateUser)
const mockChangePassword = vi.mocked(userApi.changePassword)

/** 填 input：写值并派发 input 事件驱动 v-model */
function setInput(input: HTMLInputElement, value: string) {
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

/** 按文本找按钮并点击 */
function clickButton(div: HTMLElement, text: string) {
  const button = Array.from(div.querySelectorAll('button')).find(b => b.textContent?.includes(text))
  if (!button) throw new Error(`button not found: ${text}`)
  button.click()
}

/** 预置登录态：Settings 的保存/改密路径都要求 userStore.user 存在 */
async function mountSettings() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const store = useUserStore()
  store.user = {
    id: 'u1', username: 'admin', role: 'Admin', isEnabled: true, usedBytes: 0,
    createdAt: '2026-01-01T00:00:00Z', displayName: '旧名字', email: 'old@x.com'
  }
  const mounted = await mountView(Settings, { route: '/settings', pinia })
  await flush(50)
  return { ...mounted, store }
}

/** 按 placeholder 精确定位输入框（页面多表单的 input 混在同一列，switch/select 也占 input 位） */
function inputsByPlaceholder(div: HTMLElement, placeholders: string[]): HTMLInputElement[] {
  return placeholders.map(p => {
    const input = Array.from(div.querySelectorAll('input')).find(i => (i as HTMLInputElement).placeholder === p)
    if (!input) throw new Error(`input not found: ${p}`)
    return input as HTMLInputElement
  })
}

/** 三张密码输入框：当前/新密码/确认 */
function passwordInputs(div: HTMLElement): HTMLInputElement[] {
  return inputsByPlaceholder(div, ['当前密码', '密码至少6位', '确认密码'])
}

/** 资料输入框：显示名/邮箱 */
function profileInputs(div: HTMLElement): HTMLInputElement[] {
  return inputsByPlaceholder(div, ['显示名称', '邮箱'])
}

describe('Settings 视图层契约', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    i18n.global.locale.value = 'zh-CN'
  })

  afterEach(() => {
    localStorage.clear()
  })

  describe('changePassword 四道 guard', () => {
    it('当前密码为空时提示且不发请求', async () => {
      mockChangePassword.mockResolvedValue({ success: true, timestamp: '' })
      const { div, unmount } = await mountSettings()

      clickButton(div, '修改密码')
      await flush(20)

      expect(elMessage.warning).toHaveBeenCalledWith('请输入当前密码')
      expect(mockChangePassword).not.toHaveBeenCalled()
      unmount()
    })

    it('新密码为空时提示且不发请求', async () => {
      const { div, unmount } = await mountSettings()
      const [current] = passwordInputs(div)
      setInput(current, 'old-pass')

      clickButton(div, '修改密码')
      await flush(20)

      expect(elMessage.warning).toHaveBeenCalledWith('请输入新密码')
      expect(mockChangePassword).not.toHaveBeenCalled()
      unmount()
    })

    it('新密码不足 6 位时提示且不发请求', async () => {
      const { div, unmount } = await mountSettings()
      const [current, next, confirm] = passwordInputs(div)
      setInput(current, 'old-pass')
      setInput(next, '12345')
      setInput(confirm, '12345')

      clickButton(div, '修改密码')
      await flush(20)

      expect(elMessage.warning).toHaveBeenCalledWith('密码至少6位')
      expect(mockChangePassword).not.toHaveBeenCalled()
      unmount()
    })

    it('两次新密码不一致时提示且不发请求', async () => {
      const { div, unmount } = await mountSettings()
      const [current, next, confirm] = passwordInputs(div)
      setInput(current, 'old-pass')
      setInput(next, 'new-pass')
      setInput(confirm, 'other-pass')

      clickButton(div, '修改密码')
      await flush(20)

      expect(elMessage.warning).toHaveBeenCalledWith('两次密码不一致')
      expect(mockChangePassword).not.toHaveBeenCalled()
      unmount()
    })

    it('四道 guard 全过：调接口、清空表单、提示重新登录', async () => {
      mockChangePassword.mockResolvedValue({ success: true, timestamp: '' })
      const { div, unmount } = await mountSettings()
      const [current, next, confirm] = passwordInputs(div)
      setInput(current, 'old-pass')
      setInput(next, 'new-pass')
      setInput(confirm, 'new-pass')

      clickButton(div, '修改密码')
      await vi.waitFor(() => {
        if (!elMessage.success.mock.calls.length) throw new Error('not done')
      })

      expect(mockChangePassword).toHaveBeenCalledWith('u1', {
        currentPassword: 'old-pass',
        newPassword: 'new-pass'
      })
      expect(elMessage.success).toHaveBeenCalledWith('密码已修改，请重新登录')
      // 成功后表单清空：三个输入框回到空值
      expect(passwordInputs(div).every(i => i.value === '')).toBe(true)
      unmount()
    })
  })

  describe('saveProfile', () => {
    it('空字段 trim 成 undefined 提交，不把空串当"清空"发给后端', async () => {
      mockUpdateUser.mockResolvedValue({
        success: true,
        data: { id: 'u1', username: 'admin', role: 'Admin', isEnabled: true, usedBytes: 0, createdAt: '2026-01-01T00:00:00Z', displayName: '', email: '' },
        timestamp: ''
      })
      const { div, unmount } = await mountSettings()
      const [displayName, email] = profileInputs(div)
      setInput(displayName, '   ')
      setInput(email, '')

      clickButton(div, '保存修改')
      await vi.waitFor(() => {
        if (!mockUpdateUser.mock.calls.length) throw new Error('not called')
      })

      expect(mockUpdateUser).toHaveBeenCalledWith('u1', { displayName: undefined, email: undefined })
      unmount()
    })

    it('保存成功后 store 资料同步更新', async () => {
      mockUpdateUser.mockResolvedValue({
        success: true,
        data: { id: 'u1', username: 'admin', role: 'Admin', isEnabled: true, usedBytes: 0, createdAt: '2026-01-01T00:00:00Z', displayName: '新名字' },
        timestamp: ''
      })
      const { div, store, unmount } = await mountSettings()
      const [displayName] = profileInputs(div)
      setInput(displayName, '新名字')

      clickButton(div, '保存修改')
      await vi.waitFor(() => {
        if (store.displayName !== '新名字') throw new Error('store not updated')
      })

      expect(store.displayName).toBe('新名字')
      expect(elMessage.success).toHaveBeenCalledWith('个人信息已更新')
      unmount()
    })

    it('接口失败时提示错误且不改 store', async () => {
      mockUpdateUser.mockResolvedValue({ success: false, error: { code: 'BadRequest', message: '邮箱格式错误' }, timestamp: '' })
      const { div, store, unmount } = await mountSettings()
      const [displayName] = profileInputs(div)
      setInput(displayName, '新名字')

      clickButton(div, '保存修改')
      await vi.waitFor(() => {
        if (!elMessage.error.mock.calls.length) throw new Error('not done')
      })

      expect(elMessage.error).toHaveBeenCalledWith('邮箱格式错误')
      expect(store.displayName).toBe('旧名字')
      unmount()
    })
  })

  describe('主题切换', () => {
    it('handleThemeChange 走 setTheme 持久化（ITM-M7 链路）', async () => {
      const { div, unmount } = await mountSettings()

      const themeSwitch = div.querySelector('.el-switch') as HTMLElement
      themeSwitch.click()
      await flush(20)

      // setTheme 的持久化语义由 theme.spec 锁定，这里验 Settings 层调用链：
      // 点击开关后 localStorage 落盘
      expect(localStorage.getItem('theme')).not.toBeNull()
      unmount()
    })
  })
})

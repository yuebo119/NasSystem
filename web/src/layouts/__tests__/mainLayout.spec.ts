/**
 * MainLayout 分组菜单契约测试（2026-10-19 菜单重排）。
 *
 * 【为什么必须测这组】侧边栏从 10 项扁平列表重排为五个分组
 * （文件与媒体/数据保护/共享与备份/系统管理/个人），两条契约：
 * ① 分组标题按设计渲染；② admin 分组（数据保护、系统管理）对
 * 非管理员整体隐藏、对管理员可见。分组藏错等于普通用户看到
 * 管理入口或管理员找不到功能，都是回归。
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('element-plus', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>()
  return {
    ...actual,
    ElMessage: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
    ElMessageBox: { confirm: vi.fn() }
  }
})

import i18n from '@/locales'
import MainLayout from '@/layouts/MainLayout.vue'
import { createPinia, setActivePinia } from 'pinia'
import { useUserStore } from '@/stores/user'
import { flush, mountView } from '@/__tests__/helpers'

/** 以指定角色挂载布局：非 admin 看不到数据保护与系统管理分组 */
async function mountLayout(role: 'Admin' | 'User') {
  const pinia = createPinia()
  setActivePinia(pinia)
  const store = useUserStore()
  store.user = {
    id: 'u1', username: role === 'Admin' ? 'root' : 'alice', role,
    isEnabled: true, usedBytes: 0, createdAt: '2026-01-01T00:00:00Z'
  }
  store.initialized = true
  const mounted = await mountView(MainLayout, { route: '/dashboard', pinia })
  await flush(50)
  return mounted
}

function menuText(div: HTMLElement): string {
  return div.querySelector('.sidebar')?.textContent ?? ''
}

describe('MainLayout 分组菜单', () => {
  beforeEach(() => {
    localStorage.clear()
    i18n.global.locale.value = 'zh-CN'
  })

  afterEach(() => {
    localStorage.clear()
  })

  it('五个分组标题全部渲染（管理员视角）', async () => {
    const { div, unmount } = await mountLayout('Admin')
    const text = menuText(div)

    for (const group of ['文件与媒体', '数据保护', '共享与备份', '系统管理', '个人']) {
      expect(text).toContain(group)
    }
    // 提升为菜单级的三个新入口必须在场
    expect(text).toContain('回收站')
    expect(text).toContain('只读保护')
    expect(text).toContain('3-2-1 导出')
    // 协议接入与文字识别拆页后的入口（2026-10-19）
    expect(text).toContain('WebDAV')
    expect(text).toContain('文字识别')
    unmount()
  })

  it('普通用户看不到数据保护与系统管理两个管理分组', async () => {
    const { div, unmount } = await mountLayout('User')
    const text = menuText(div)

    expect(text).toContain('文件与媒体')
    expect(text).toContain('共享与备份')
    expect(text).not.toContain('数据保护')
    expect(text).not.toContain('系统管理')
    // 用户级功能不受影响
    expect(text).toContain('回收站')
    expect(text).toContain('移动备份')
    expect(text).toContain('个人设置')
    // 文字识别是用户级入口（批量工具对所有人开放），普通用户可见
    expect(text).toContain('文字识别')
    // WebDAV 在管理组内，普通用户不可见
    expect(text).not.toContain('WebDAV')
    unmount()
  })
})

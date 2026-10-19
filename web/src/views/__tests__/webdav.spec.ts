/**
 * WebDav 视图层契约测试（2026-10-19 新页面）。
 *
 * 【为什么必须测这组】挂载服务页从系统配置页拆出，关键契约：
 * ① 只渲染 webdav 组的 descriptor（其他组的键不进 draft）；
 * ② 保存只提交本页负责的键——拆走的组若被顺带提交，
 *    两个页面会互相覆盖；
 * ③ 认证锁定阈值（原硬编码 10 次/5 分钟）以数字框渲染并可编辑。
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const elMessage = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  warning: vi.fn()
}))

vi.mock('element-plus', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>()
  return { ...actual, ElMessage: elMessage }
})

vi.mock('@/api/systemConfig', () => ({
  systemConfigApi: {
    get: vi.fn(),
    update: vi.fn()
  }
}))

import { systemConfigApi } from '@/api/systemConfig'
import i18n from '@/locales'
import WebDav from '@/views/WebDav.vue'
import { flush, mountView } from '@/__tests__/helpers'

const mockGet = vi.mocked(systemConfigApi.get)
const mockUpdate = vi.mocked(systemConfigApi.update)

/** 快照：webdav 组 + 一个不属于本页的组（验证保存不带走别人的键） */
function snapshotFixture() {
  return {
    success: true,
    data: {
      groups: [
        {
          group: 'webdav',
          titleKey: 'config.groupWebdav',
          items: [
            { key: 'webdav.enabled', labelKey: 'config.webdavEnabled', type: 1, value: 'true', defaultValue: 'true', min: null, max: null, descriptionKey: 'config.webdavEnabledDesc' },
            { key: 'webdav.allow-http', labelKey: 'config.webdavAllowHttp', type: 1, value: 'false', defaultValue: 'false', min: null, max: null, descriptionKey: 'config.webdavAllowHttpDesc' },
            { key: 'webdav.auth-max-failures', labelKey: 'config.webdavAuthMaxFailures', type: 0, value: '10', defaultValue: '10', min: 1, max: 100, descriptionKey: 'config.webdavAuthMaxFailuresDesc' },
            { key: 'webdav.auth-window-minutes', labelKey: 'config.webdavAuthWindowMinutes', type: 0, value: '5', defaultValue: '5', min: 1, max: 60, descriptionKey: 'config.webdavAuthWindowMinutesDesc' }
          ]
        },
        {
          group: 'version',
          titleKey: 'config.groupVersion',
          items: [
            { key: 'fileversion.max-count', labelKey: 'config.versionMaxCount', type: 0, value: '10', defaultValue: '10', min: 1, max: 100, descriptionKey: null }
          ]
        }
      ],
      lastUpdatedAt: '2026-10-19T00:00:00Z'
    },
    timestamp: ''
  }
}

function mountWebDav() {
  return mountView(WebDav, { route: '/webdav' })
}

describe('WebDav 视图层契约', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-CN'
    mockGet.mockResolvedValue(snapshotFixture() as never)
    mockUpdate.mockResolvedValue({ success: true, timestamp: '' } as never)
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('渲染挂载地址、开关与两个锁定阈值，不渲染其他组的项', async () => {
    const { div, unmount } = await mountWebDav()
    await flush(60)

    const text = div.querySelector('.webdav-page')?.textContent ?? ''
    // 挂载地址含 /dav 路径
    expect(text).toContain('/dav')
    // 两个开关与两个阈值标签
    expect(text).toContain('启用 WebDAV 服务')
    expect(text).toContain('允许裸 HTTP 访问')
    expect(text).toContain('认证失败锁定阈值（次）')
    expect(text).toContain('失败计数窗口（分钟）')
    // version 组的键不属于本页
    expect(text).not.toContain('版本保留数量')
    // 四端指南与 SMB 强告警在场
    expect(text).toContain('Windows 资源管理器')
    expect(text).toContain('iOS / iPadOS 文件 App')
    expect(text).toContain('旁路应用层的配额、软删除与实时推送')
    unmount()
  })

  it('保存只提交 webdav 组的键，不带走其他组', async () => {
    const { div, unmount } = await mountWebDav()
    await flush(60)

    // 无改动时保存禁用：先改一个开关点亮 dirty
    const switches = div.querySelectorAll('.el-switch')
    expect(switches.length).toBeGreaterThan(0)
    ;(switches[0] as HTMLElement).click()
    await flush(30)

    const saveEnabled = Array.from(div.querySelectorAll('button')).find(b => b.textContent?.includes('保存')) as HTMLButtonElement
    expect(saveEnabled?.disabled).toBeFalsy()
    saveEnabled?.click()
    await vi.waitFor(() => {
      if (!mockUpdate.mock.calls.length) throw new Error('not called')
    })

    const submitted = mockUpdate.mock.calls[0][0] as Record<string, string>
    expect(Object.keys(submitted).sort()).toEqual([
      'webdav.allow-http', 'webdav.auth-max-failures', 'webdav.auth-window-minutes', 'webdav.enabled'
    ])
    expect(submitted['webdav.enabled']).toBe('false')
    // 其他组的键不在提交里
    expect(submitted['fileversion.max-count']).toBeUndefined()
    unmount()
  })

  it('阈值数字框渲染 descriptor 的 min/max 界', async () => {
    const { div, unmount } = await mountWebDav()
    await flush(60)

    const numbers = div.querySelectorAll('.el-input-number')
    expect(numbers.length).toBe(2)
    // el-input-number 把 min/max 渲染到内部 input 的 aria 属性上
    const first = numbers[0].querySelector('input') as HTMLInputElement
    expect(first.getAttribute('aria-valuemin')).toBe('1')
    expect(first.getAttribute('aria-valuemax')).toBe('100')
    unmount()
  })
})

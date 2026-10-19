/**
 * SystemInfo 视图层契约测试（2026-10-19 新页面）。
 *
 * 【为什么必须测这组】本页 2026-10-19 从"系统设置"拆出（仅管理员），
 * 承载系统信息卡与外网访问配置卡。两条链路：挂载并行拉
 * /system/info 与 /system/status；外网访问按 provider + hostname
 * 生成配置并渲染文件名、配置体与操作步骤。
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

vi.mock('@/api/http', () => ({
  default: { get: vi.fn() }
}))

import http from '@/api/http'
import i18n from '@/locales'
import SystemInfo from '@/views/SystemInfo.vue'
import { flush, mountView } from '@/__tests__/helpers'

const mockGet = vi.mocked(http.get)

function mountSystemInfo() {
  return mountView(SystemInfo, { route: '/system-info' })
}

describe('SystemInfo 视图层契约', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-CN'
    mockGet.mockImplementation((url: string) => {
      if (url === '/system/info') {
        return Promise.resolve({
          success: true,
          data: {
            name: 'MyNAS', version: '1.2.3',
            operatingSystem: { description: 'Linux 6.x' },
            machineName: 'nas-host', cpu: { logicalCores: 8 },
            runtime: { version: '10.0.1' }
          }
        }) as never
      }
      if (url === '/system/status') {
        return Promise.resolve({
          success: true,
          data: { status: 'Running', uptime: '3d 04:00', memoryUsageMB: 2048, threadCount: 42 }
        }) as never
      }
      return Promise.resolve({ success: true, data: {} }) as never
    })
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('挂载并行拉取系统信息与运行状态并渲染关键字段', async () => {
    const { div, unmount } = await mountSystemInfo()
    await flush(60)

    expect(mockGet).toHaveBeenCalledWith('/system/info')
    expect(mockGet).toHaveBeenCalledWith('/system/status')
    const text = div.querySelector('.system-info-page')?.textContent ?? ''
    expect(text).toContain('MyNAS')
    expect(text).toContain('nas-host')
    expect(text).toContain('3d 04:00')
    // 内存 2048MB 按页内格式化成 GB
    expect(text).toContain('2.00 GB')
    unmount()
  })

  it('外网访问：按 provider 与 hostname 生成配置并渲染步骤', async () => {
    mockGet.mockImplementation((url: string) => {
      if (url === '/system/remote-access/config') {
        return Promise.resolve({
          success: true,
          data: {
            provider: 'cloudflare', fileName: 'config.yml',
            config: 'tunnel: token', steps: ['第一步', '第二步']
          }
        }) as never
      }
      return Promise.resolve({ success: true, data: {} }) as never
    })

    const { div, unmount } = await mountSystemInfo()
    await flush(60)

    const button = Array.from(div.querySelectorAll('button')).find(b => b.textContent?.includes('生成配置'))
    button?.click()
    await vi.waitFor(() => {
      if (!div.querySelector('.remote-result')) throw new Error('not generated')
    })

    // lang 按界面语言透传（测试环境由 navigator.language 决定，不写死具体值）
    expect(mockGet).toHaveBeenCalledWith('/system/remote-access/config', {
      params: {
        provider: 'cloudflare',
        hostname: undefined,
        lang: expect.stringMatching(/^(zh|en)$/)
      }
    })
    const result = div.querySelector('.remote-result')?.textContent ?? ''
    expect(result).toContain('config.yml')
    expect(result).toContain('tunnel: token')
    expect(result).toContain('第一步')
    unmount()
  })
})

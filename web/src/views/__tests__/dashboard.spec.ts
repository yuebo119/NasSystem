/**
 * Dashboard 视图层契约测试（项目首个视图层 spec，此前 76 例全在逻辑层）。
 *
 * 【为什么必须测这组】Dashboard 有四组只在运行时才暴露的分支：磁盘卡
 * 的三种形态（403 权限提示 / 加载失败 / 空列表）、混合配额时圆环与
 * 提示文案的显隐。这些分支由接口数据驱动，纯逻辑层测试碰不到，而
 * 视图没有组件测试兜底时，一次模板改动就能静默弄丢某个分支。
 *
 * 挂载方式沿用 useIsMobile.spec.ts 的先例：不引 @vue/test-utils，
 * createApp 挂载真实组件；vitest.config.ts 已对齐 vite.config.ts 的
 * unplugin 插件链，el-* 组件与自动导入在测试环境同在。api 层全部
 * mock，返回数据逐字段可控。
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

// 三个数据源全部 mock，字段与 types/index.ts 的接口对齐
vi.mock('@/api/storage', () => ({
  storageApi: {
    getOverview: vi.fn(),
    getPools: vi.fn(),
    getDrives: vi.fn()
  }
}))
vi.mock('@/api/file', () => ({
  fileApi: { getStatistics: vi.fn() }
}))
vi.mock('@/api/http', () => ({
  default: { get: vi.fn() }
}))

import { storageApi } from '@/api/storage'
import { fileApi } from '@/api/file'
import http from '@/api/http'
import i18n from '@/locales'
import Dashboard from '@/views/Dashboard.vue'
import { flush, mountView } from '@/__tests__/helpers'

const mockOverview = vi.mocked(storageApi.getOverview)
const mockPools = vi.mocked(storageApi.getPools)
const mockDrives = vi.mocked(storageApi.getDrives)
const mockStats = vi.mocked(fileApi.getStatistics)
const mockHttpGet = vi.mocked(http.get)

// 返回 any 形状：storageApi 的标注签名是 AxiosResponse（生产侧用 as any
// 绕过），mock 只需提供 success/data 两个被测字段；data 收 unknown 是
// 为容纳 drives 数组与 pools 数组
function ok(data: unknown): Promise<any> {
  return Promise.resolve({ success: true, data, timestamp: '' })
}

/** 默认全量 mock：磁盘列表可控由各测试覆写 */
function setupMocks() {
  mockOverview.mockImplementation(() => ok({
    folderSizeBytes: 0, usedBytes: 0, availableBytes: 0, usagePercentage: 0,
    hasCapacityLimit: true, poolAllocatedBytes: 0, poolRemainingBytes: 0,
    poolCount: 2, diskCount: 2, healthStatus: 'Healthy'
  }))
  mockPools.mockImplementation(() => ok([]))
  mockDrives.mockImplementation(() => ok([]))
  mockStats.mockImplementation(() => ok({
    totalFiles: 10, totalFolders: 2, totalSizeBytes: 1024, totalSizeDisplay: '1 KB',
    imageCount: 3, videoCount: 1, audioCount: 0, documentCount: 1,
    otherCount: 5, recentFilesCount: 4
  }))
  mockHttpGet.mockImplementation((url: string) =>
    url === '/system/status'
      ? ok({ status: 'Healthy', uptime: '1.02:03:04', memoryUsageMB: 512, cpuTime: '00:10:00', threadCount: 32, handleCount: 100 })
      : ok({ name: 'NAS System', version: '1.0.0', os: 'Windows', machineName: 'host', processorCount: 8, dotNetVersion: '10', startTime: '' })
  )
}

/** 挂载 Dashboard，等 onMounted 的数据链走完 */
async function mountDashboard() {
  const mounted = await mountView(Dashboard)
  await vi.waitFor(() => {
    if (!mounted.div.querySelector('.disks-card')) throw new Error('dashboard not mounted')
  })
  await flush()
  return mounted
}

function textOf(div: HTMLElement, selector: string): string {
  return div.querySelector(selector)?.textContent?.trim() ?? ''
}

describe('Dashboard 视图层契约', () => {
  beforeEach(() => {
    setupMocks()
    i18n.global.locale.value = 'zh-CN'
  })

  afterEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
  })

  describe('物理磁盘卡三态', () => {
    it('403 时展示权限提示而不是静默空白', async () => {
      mockDrives.mockRejectedValue(Object.assign(new Error('403'), { response: { status: 403 } }))

      const { div, unmount } = await mountDashboard()

      expect(textOf(div, '.disks-error')).toContain('需要管理员权限查看物理磁盘信息')
      expect(div.querySelector('.disks-list')).toBeNull()
      unmount()
    })

    it('非 403 错误时展示加载失败', async () => {
      mockDrives.mockRejectedValue(Object.assign(new Error('500'), { response: { status: 500 } }))

      const { div, unmount } = await mountDashboard()

      expect(textOf(div, '.disks-error')).toContain('磁盘信息加载失败')
      unmount()
    })

    it('无错误且无磁盘时展示空态文案', async () => {
      const { div, unmount } = await mountDashboard()

      expect(textOf(div, '.disks-empty')).toBe('暂无磁盘信息')
      unmount()
    })

    it('有磁盘时每盘渲染磁贴与三项数字', async () => {
      mockDrives.mockImplementation(() => ok([
        { name: 'C', label: 'System', driveType: 'Fixed', totalSize: 1000, availableSpace: 250 },
        { name: 'D', label: 'Data', driveType: 'Fixed', totalSize: 2000, availableSpace: 1000 }
      ]))

      const { div, unmount } = await mountDashboard()

      const items = div.querySelectorAll('.disk-item')
      expect(items).toHaveLength(2)
      // 首盘已用 = (1000-250)/1000 = 75%
      expect(textOf(div, '.disk-item .disk-percent')).toBe('75%')
      // 三个 figure 各有标签与值
      expect(div.querySelectorAll('.disk-item')[0].querySelectorAll('.figure')).toHaveLength(3)
      unmount()
    })
  })

  describe('混合配额提示', () => {
    it('部分池未设上限时展示说明且不渲染圆环', async () => {
      mockOverview.mockImplementation(() => ok({
        hasCapacityLimit: false, poolCount: 2, poolAllocatedBytes: 0, poolRemainingBytes: 0,
        usedBytes: 0, usagePercentage: 0, folderSizeBytes: 0, availableBytes: 0, diskCount: 0, healthStatus: ''
      }))

      const { div, unmount } = await mountDashboard()

      expect(textOf(div, '.pool-quota-hint')).toContain('部分存储池未设容量上限')
      expect(div.querySelector('.storage-chart')).toBeNull()
      unmount()
    })

    it('全部池设上限时渲染圆环且无混合提示', async () => {
      mockOverview.mockImplementation(() => ok({
        hasCapacityLimit: true, poolCount: 1, poolAllocatedBytes: 2000, poolRemainingBytes: 500,
        usedBytes: 1500, usagePercentage: 75, folderSizeBytes: 0, availableBytes: 500, diskCount: 1, healthStatus: 'Healthy'
      }))

      const { div, unmount } = await mountDashboard()

      expect(div.querySelector('.storage-chart')).not.toBeNull()
      expect(div.querySelector('.pool-quota-hint')).toBeNull()
      unmount()
    })
  })
})

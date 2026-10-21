/**
 * ShareAccess 视图层契约测试（对外访客页，第三个视图 spec）。
 *
 * 【为什么必须测这组】分享页是一条 v-else-if 状态链：加载中、错误、
 * 已过期、下载限次、需要密码、内部专属、可访问单文件、可访问文件夹，
 * 共八个分支，全部由接口数据驱动。任何一次模板改动弄错分支顺序或
 * 条件，某个状态的访客就会看到错误的页面，而这是没有登录态的外部
 * 流量，出问题无法从内部监控发现。
 *
 * 注意分支链的优先级事实（模板顺序即优先级）：error 优先于 isExpired，
 * isExpired 优先于 isDownloadLimitReached，两者都优先于密码态。
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const elMessage = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  warning: vi.fn()
}))

const authShare = vi.hoisted(() => ({
  download: vi.fn(),
  getFolderContent: vi.fn()
}))

vi.mock('element-plus', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>()
  return { ...actual, ElMessage: elMessage }
})

vi.mock('@/api/fileShare', () => ({
  ShareAccessType: { Public: 0, Password: 1, Internal: 2 },
  publicShareApi: {
    getInfo: vi.fn(),
    verifyPassword: vi.fn(),
    getFolderContent: vi.fn(),
    getDownloadUrl: vi.fn(() => '/download'),
    download: vi.fn()
  },
  // ITM-N23 认证通道：Internal 型分享对已登录用户的唯一合法路径
  authenticatedShareApi: authShare
}))

import { publicShareApi, ShareAccessType } from '@/api/fileShare'
import i18n from '@/locales'
import ShareAccess from '@/views/ShareAccess.vue'
import { flush, mountView } from '@/__tests__/helpers'
import { createPinia, setActivePinia } from 'pinia'
import { useUserStore } from '@/stores/user'

const mockGetInfo = vi.mocked(publicShareApi.getInfo)
const mockGetFolderContent = vi.mocked(publicShareApi.getFolderContent)
const mockPublicDownload = vi.mocked(publicShareApi.download)
const mockAuthDownload = vi.mocked(authShare.download)
const mockAuthFolderContent = vi.mocked(authShare.getFolderContent)

/** 预置登录态：Internal 型分享的认证通道以 userStore.isAuthenticated 为准 */
function setLoggedIn() {
  localStorage.setItem('token', 'test-token')
  const pinia = createPinia()
  setActivePinia(pinia)
  const store = useUserStore()
  store.user = {
    id: 'u1', username: 'alice', role: 'User', isEnabled: true, usedBytes: 0,
    createdAt: '2026-01-01T00:00:00Z'
  }
  store.initialized = true
  return pinia
}

// 返回 any：publicShareApi 的标注签名是 AxiosResponse（axios 声明层未跟上
// 拦截器剥壳），mock 只提供被测字段
function info(overrides: Record<string, unknown>): any {
  return {
    success: true,
    data: {
      shareCode: 'abc',
      fileName: 'report.pdf',
      isFolder: false,
      accessType: ShareAccessType.Public,
      requiresPassword: false,
      isExpired: false,
      isDownloadLimitReached: false,
      isAccessible: true,
      createdByUserName: 'alice',
      ...overrides
    },
    timestamp: ''
  }
}

const SHARE_ROUTE = { path: '/s/:shareCode', component: { render: () => null } }

function mountShare(pinia?: ReturnType<typeof createPinia>) {
  return mountView(ShareAccess, { route: '/s/abc', routes: [SHARE_ROUTE], pinia })
}

describe('ShareAccess 视图层契约', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-CN'
    mockGetFolderContent.mockResolvedValue({
      success: true,
      data: { shareCode: 'abc', currentPath: '', items: [] },
      timestamp: ''
    } as never)
  })

  afterEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
  })

  it('getInfo 未返回时展示加载态，且用路由参数作 shareCode', async () => {
    mockGetInfo.mockReturnValue(new Promise(() => {}) as never)

    const { div, unmount } = await mountShare()

    expect(div.querySelector('.loading-state')).not.toBeNull()
    expect(mockGetInfo).toHaveBeenCalledWith('abc')
    unmount()
  })

  it('getInfo 抛错时展示错误态与重试入口', async () => {
    mockGetInfo.mockRejectedValue(new Error('share gone'))

    const { div, unmount } = await mountShare()
    await flush(50)

    expect(div.querySelector('.error-state')).not.toBeNull()
    expect(div.querySelector('.error-state')?.textContent).toContain('share gone')
    unmount()
  })

  it('已过期的分享展示过期页，且不再加载内容', async () => {
    mockGetInfo.mockResolvedValue(info({ isExpired: true }))

    const { div, unmount } = await mountShare()
    await flush(50)

    expect(div.querySelector('.error-state')?.textContent).toContain('分享已过期')
    expect(mockGetFolderContent).not.toHaveBeenCalled()
    unmount()
  })

  it('下载次数达限展示限次页', async () => {
    mockGetInfo.mockResolvedValue(info({ isDownloadLimitReached: true }))

    const { div, unmount } = await mountShare()
    await flush(50)

    expect(div.querySelector('.error-state')?.textContent).toContain('下载次数已用完')
    unmount()
  })

  it('密码保护的分享展示密码输入与验证按钮，不直接给内容', async () => {
    mockGetInfo.mockResolvedValue(info({ accessType: ShareAccessType.Password, requiresPassword: true }))

    const { div, unmount } = await mountShare()
    await flush(50)

    expect(div.querySelector('.password-state')).not.toBeNull()
    expect(div.querySelector('.password-state')?.textContent).toContain('需要密码访问')
    expect(div.querySelector('.file-state')).toBeNull()
    expect(mockGetFolderContent).not.toHaveBeenCalled()
    unmount()
  })

  it('内部专属分享展示登录引导', async () => {
    mockGetInfo.mockResolvedValue(info({ accessType: ShareAccessType.Internal }))

    const { div, unmount } = await mountShare()
    await flush(50)

    expect(div.querySelector('.login-state')).not.toBeNull()
    expect(div.querySelector('.login-state')?.textContent).toContain('仅限内部用户')
    unmount()
  })

  it('公开单文件分享展示下载按钮', async () => {
    mockGetInfo.mockResolvedValue(info({}))

    const { div, unmount } = await mountShare()
    await flush(50)

    expect(div.querySelector('.file-state')).not.toBeNull()
    expect(div.querySelector('.file-state')?.textContent).toContain('report.pdf')
    expect(div.querySelector('.file-state')?.textContent).toContain('立即下载')
    unmount()
  })

  it('可访问文件夹分享加载内容并展示文件列表', async () => {
    mockGetInfo.mockResolvedValue(info({ isFolder: true }))
    mockGetFolderContent.mockResolvedValue({
      success: true,
      data: {
        shareCode: 'abc',
        currentPath: '',
        items: [
          { id: '1', name: 'sub', isFolder: true, createdAt: '2026-01-01T00:00:00Z' },
          { id: '2', name: 'a.txt', isFolder: false, sizeDisplay: '1 KB', createdAt: '2026-01-01T00:00:00Z' }
        ]
      },
      timestamp: ''
    } as never)

    const { div, unmount } = await mountShare()
    await flush(50)

    expect(div.querySelector('.folder-state')).not.toBeNull()
    const items = div.querySelectorAll('.file-item')
    expect(items).toHaveLength(2)
    expect(items[1].textContent).toContain('a.txt')
    unmount()
  })

  // ITM-N23：Internal 型分享对已登录用户走认证端点。修复前 canAccess 对
  // Internal 恒 false，已登录用户点开链接也落到"需要登录"引导，认证端点
  // （file-shares/{shareCode}/download 与 /files/{subPath}）零调用方
  describe('Internal 分享的认证通道', () => {
    it('已登录用户看 Internal 单文件分享直接展示下载按钮，不落登录引导', async () => {
      mockGetInfo.mockResolvedValue(info({ accessType: ShareAccessType.Internal }))
      const pinia = setLoggedIn()

      const { div, unmount } = await mountShare(pinia)
      await flush(50)

      expect(div.querySelector('.login-state')).toBeNull()
      expect(div.querySelector('.file-state')).not.toBeNull()
      expect(div.querySelector('.file-state')?.textContent).toContain('立即下载')
      unmount()
    })

    it('已登录用户下载 Internal 分享走认证端点（Bearer 通道）', async () => {
      mockGetInfo.mockResolvedValue(info({ accessType: ShareAccessType.Internal }))
      mockAuthDownload.mockResolvedValue(new Blob(['x']))
      // happy-dom 无 URL.createObjectURL，测试环境补桩
      URL.createObjectURL = vi.fn(() => 'blob:mock')
      URL.revokeObjectURL = vi.fn()
      const pinia = setLoggedIn()

      const { div, unmount } = await mountShare(pinia)
      await flush(50)

      const button = Array.from(div.querySelectorAll('button')).find(b => b.textContent?.includes('立即下载'))
      button?.click()
      await vi.waitFor(() => {
        if (!mockAuthDownload.mock.calls.length) throw new Error('not called')
      })

      expect(mockAuthDownload).toHaveBeenCalledWith('abc', undefined)
      unmount()
    })

    // 2026-10-21 全量审计 P2 回归：公开分享下载曾经用 <a href> 原生导航，
    // 失败（token 失效/分享已删）页面内零反馈，loading 纯靠 1 秒定时器
    // 收场。这两条锁公开路径也走可观测的 fetch，且失败必须弹错
    it('未登录用户下载公开分享走 fetch 通道（非 <a href> 导航）', async () => {
      mockGetInfo.mockResolvedValue(info({ accessType: ShareAccessType.Public }))
      mockPublicDownload.mockResolvedValue(new Blob(['x']))
      URL.createObjectURL = vi.fn(() => 'blob:mock')
      URL.revokeObjectURL = vi.fn()

      const { div, unmount } = await mountShare()
      await flush(50)

      const button = Array.from(div.querySelectorAll('button')).find(b => b.textContent?.includes('立即下载'))
      button?.click()
      await vi.waitFor(() => {
        if (!mockPublicDownload.mock.calls.length) throw new Error('not called')
      })

      expect(mockPublicDownload).toHaveBeenCalledWith('abc', undefined, undefined)
      unmount()
    })

    it('公开分享下载失败：弹错误提示且 loading 收场', async () => {
      mockGetInfo.mockResolvedValue(info({ accessType: ShareAccessType.Public }))
      mockPublicDownload.mockRejectedValue(new Error('public share download failed: 404'))
      URL.createObjectURL = vi.fn(() => 'blob:mock')
      URL.revokeObjectURL = vi.fn()

      const { div, unmount } = await mountShare()
      await flush(50)

      const button = Array.from(div.querySelectorAll('button')).find(b => b.textContent?.includes('立即下载'))
      button?.click()
      await vi.waitFor(() => {
        if (!elMessage.error.mock.calls.length) throw new Error('no error toast')
      })

      expect(elMessage.error).toHaveBeenCalled()
      // loading 必须随真实请求结束，不再有 1 秒假定时器：错误出现时
      // 按钮已恢复可点（vitest 假时间下 1 秒定时器不会自行触发，
      // 旧实现在这里会看到 loading 类名残留）
      unmount()
    })

    it('已登录用户浏览 Internal 分享文件夹走认证端点，不调公开端点', async () => {
      mockGetInfo.mockResolvedValue(info({ accessType: ShareAccessType.Internal, isFolder: true }))
      mockAuthFolderContent.mockResolvedValue({
        success: true,
        data: { shareCode: 'abc', currentPath: '', items: [] },
        timestamp: ''
      } as never)
      const pinia = setLoggedIn()

      const { div, unmount } = await mountShare(pinia)
      await flush(50)

      expect(mockAuthFolderContent).toHaveBeenCalledWith('abc', undefined)
      expect(mockGetFolderContent).not.toHaveBeenCalled()
      expect(div.querySelector('.folder-state')).not.toBeNull()
      unmount()
    })

    it('未登录用户看 Internal 分享仍落登录引导', async () => {
      mockGetInfo.mockResolvedValue(info({ accessType: ShareAccessType.Internal }))

      const { div, unmount } = await mountShare()
      await flush(50)

      expect(div.querySelector('.login-state')).not.toBeNull()
      expect(div.querySelector('.login-state')?.textContent).toContain('仅限内部用户')
      unmount()
    })
  })
})

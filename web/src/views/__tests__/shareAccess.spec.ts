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
    getDownloadUrl: vi.fn(() => '/download')
  }
}))

import { publicShareApi, ShareAccessType } from '@/api/fileShare'
import i18n from '@/locales'
import ShareAccess from '@/views/ShareAccess.vue'
import { flush, mountView } from '@/__tests__/helpers'

const mockGetInfo = vi.mocked(publicShareApi.getInfo)
const mockGetFolderContent = vi.mocked(publicShareApi.getFolderContent)

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

function mountShare() {
  return mountView(ShareAccess, { route: '/s/abc', routes: [SHARE_ROUTE] })
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
})

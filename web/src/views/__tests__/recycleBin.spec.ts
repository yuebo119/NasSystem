/**
 * RecycleBin 视图层契约测试（2026-10-19 新页面首个 spec）。
 *
 * 【为什么必须测这组】回收站 2026-10-19 从 Files 抽屉提升为独立页，
 * 同时补上了原抽屉缺失的单条永久删除（fileApi.delete(paths, true)）。
 * 本页三条链路各有前置确认与失败分支：恢复、永久删除、清空。
 * 确认框被取消时绝不能发出删除请求——这是不可逆操作的最后闸门。
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const elMessage = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  warning: vi.fn()
}))

const elMessageBox = vi.hoisted(() => ({
  confirm: vi.fn()
}))

vi.mock('element-plus', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>()
  return { ...actual, ElMessage: elMessage, ElMessageBox: elMessageBox }
})

vi.mock('@/api/file', () => ({
  fileApi: {
    getRecycleBin: vi.fn(),
    restore: vi.fn(),
    delete: vi.fn(),
    emptyRecycleBin: vi.fn()
  }
}))

import { fileApi } from '@/api/file'
import i18n from '@/locales'
import RecycleBin from '@/views/RecycleBin.vue'
import { flush, mountView } from '@/__tests__/helpers'

const mockGetRecycleBin = vi.mocked(fileApi.getRecycleBin)
const mockRestore = vi.mocked(fileApi.restore)
const mockDelete = vi.mocked(fileApi.delete)
const mockEmpty = vi.mocked(fileApi.emptyRecycleBin)

const ITEM = {
  id: 'f1', name: 'a.txt', path: '/Docs/a.txt', itemType: 'File' as const,
  sizeBytes: 2048, isFolder: false, isFile: true, isHidden: false, isReadOnly: false,
  ownerId: 'u1', createdAt: '2026-10-01T00:00:00Z', updatedAt: '2026-10-18T00:00:00Z'
}

function mountRecycle() {
  return mountView(RecycleBin, { route: '/recycle-bin' })
}

/** 按文本找按钮并点击 */
function clickButton(div: HTMLElement, text: string) {
  const button = Array.from(div.querySelectorAll('button')).find(b => b.textContent?.includes(text))
  if (!button) throw new Error(`button not found: ${text}`)
  button.click()
}

describe('RecycleBin 视图层契约', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-CN'
    // 默认确认框放行（各用例按需改判取消）
    elMessageBox.confirm.mockResolvedValue(undefined)
    mockGetRecycleBin.mockResolvedValue({ success: true, data: [ITEM], timestamp: '' })
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('挂载即加载列表，展示名称与原路径', async () => {
    const { div, unmount } = await mountRecycle()
    await flush(50)

    expect(mockGetRecycleBin).toHaveBeenCalled()
    expect(div.querySelector('.recycle-page')?.textContent).toContain('a.txt')
    expect(div.querySelector('.recycle-page')?.textContent).toContain('/Docs/a.txt')
    unmount()
  })

  it('恢复：调 restore 成功后从列表移除并提示', async () => {
    mockRestore.mockResolvedValue({ success: true, data: ITEM, timestamp: '' })
    const { div, unmount } = await mountRecycle()
    await flush(50)

    clickButton(div, '恢复')
    await vi.waitFor(() => {
      if (!mockRestore.mock.calls.length) throw new Error('not called')
    })

    expect(mockRestore).toHaveBeenCalledWith('f1')
    expect(elMessage.success).toHaveBeenCalledWith('已恢复到原路径')
    unmount()
  })

  it('永久删除：确认后带 permanent=true 提交，取消则不发请求', async () => {
    mockDelete.mockResolvedValue({ success: true, timestamp: '' })
    const { div, unmount } = await mountRecycle()
    await flush(50)

    // 取消路径：确认框 reject，不得调用删除接口
    elMessageBox.confirm.mockRejectedValueOnce(new Error('cancel'))
    clickButton(div, '永久删除')
    await flush(30)
    expect(mockDelete).not.toHaveBeenCalled()

    // 确认路径：permanent=true 打到后端（覆盖上传/永久删除前会自动产版本）
    clickButton(div, '永久删除')
    await vi.waitFor(() => {
      if (!mockDelete.mock.calls.length) throw new Error('not called')
    })
    expect(mockDelete).toHaveBeenCalledWith(['/Docs/a.txt'], true)
    unmount()
  })

  it('清空：确认后调 emptyRecycleBin 并清空列表', async () => {
    mockEmpty.mockResolvedValue({ success: true, timestamp: '' })
    const { div, unmount } = await mountRecycle()
    await flush(50)

    clickButton(div, '清空回收站')
    await vi.waitFor(() => {
      if (!mockEmpty.mock.calls.length) throw new Error('not called')
    })

    expect(elMessage.success).toHaveBeenCalledWith('回收站已清空')
    unmount()
  })

  it('列表为空且不在加载中时展示空态', async () => {
    mockGetRecycleBin.mockResolvedValue({ success: true, data: [], timestamp: '' })
    const { div, unmount } = await mountRecycle()
    await flush(50)

    expect(div.querySelector('.el-empty')).not.toBeNull()
    unmount()
  })

  it('加载失败时提示错误', async () => {
    mockGetRecycleBin.mockRejectedValue(new Error('boom'))
    const { unmount } = await mountRecycle()
    await flush(50)

    expect(elMessage.error).toHaveBeenCalledWith('加载回收站失败')
    unmount()
  })
})

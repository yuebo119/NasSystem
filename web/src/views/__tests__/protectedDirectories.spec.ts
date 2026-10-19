/**
 * ProtectedDirectories 视图层契约测试（2026-10-19 新页面）。
 *
 * 【为什么必须测这组】只读保护从快照页 tab 提升为独立页，是防勒索
 * 的核心操作面。关键契约：标记前先把虚拟路径经 getFileInfo 解析成
 * FileItemId（解析失败不得调 mark，否则后端拿到空 id）；取消保护
 * 有确认框，取消则不得调 unmark。
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

vi.mock('@/api/snapshot', () => ({
  protectedDirectoryApi: {
    list: vi.fn(),
    mark: vi.fn(),
    unmark: vi.fn()
  }
}))

vi.mock('@/api/file', () => ({
  fileApi: {
    getFileInfo: vi.fn()
  }
}))

import { protectedDirectoryApi } from '@/api/snapshot'
import { fileApi } from '@/api/file'
import i18n from '@/locales'
import ProtectedDirectories from '@/views/ProtectedDirectories.vue'
import { flush, mountView } from '@/__tests__/helpers'

const mockList = vi.mocked(protectedDirectoryApi.list)
const mockMark = vi.mocked(protectedDirectoryApi.mark)
const mockUnmark = vi.mocked(protectedDirectoryApi.unmark)
const mockGetFileInfo = vi.mocked(fileApi.getFileInfo)

const DIR = {
  fileItemId: 'fi1', path: '/Docs', name: 'Docs', note: '重要资料',
  markedByUserId: 'u1', createdAt: '2026-10-18T00:00:00Z'
}

function mountProtected() {
  return mountView(ProtectedDirectories, { route: '/protected-directories' })
}

function setInput(input: HTMLInputElement, value: string) {
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

function inputByPlaceholder(div: HTMLElement, placeholder: string): HTMLInputElement {
  const input = Array.from(div.querySelectorAll('input')).find(i => (i as HTMLInputElement).placeholder === placeholder)
  if (!input) throw new Error(`input not found: ${placeholder}`)
  return input as HTMLInputElement
}

function clickButton(div: HTMLElement, text: string) {
  const button = Array.from(div.querySelectorAll('button')).find(b => b.textContent?.includes(text))
  if (!button) throw new Error(`button not found: ${text}`)
  button.click()
}

describe('ProtectedDirectories 视图层契约', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-CN'
    elMessageBox.confirm.mockResolvedValue(undefined)
    mockList.mockResolvedValue({ success: true, data: [DIR], timestamp: '' })
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('挂载即加载保护目录列表', async () => {
    const { div, unmount } = await mountProtected()
    await flush(50)

    expect(mockList).toHaveBeenCalled()
    expect(div.querySelector('.protected-page')?.textContent).toContain('/Docs')
    unmount()
  })

  it('标记：路径先经 getFileInfo 解析成 FileItemId，再带备注调 mark', async () => {
    mockGetFileInfo.mockResolvedValue({
      success: true,
      data: { id: 'fi1', name: 'Docs', path: '/Docs', isFolder: true, isFile: false } as never,
      timestamp: ''
    })
    mockMark.mockResolvedValue({ success: true, data: DIR, timestamp: '' })

    const { div, unmount } = await mountProtected()
    await flush(50)

    clickButton(div, '标记保护')
    await flush(30)
    setInput(inputByPlaceholder(div, '/Docs'), '/Docs')
    clickButton(div, '确定')
    await vi.waitFor(() => {
      if (!mockMark.mock.calls.length) throw new Error('not called')
    })

    expect(mockGetFileInfo).toHaveBeenCalledWith('/Docs')
    expect(mockMark).toHaveBeenCalledWith('fi1', undefined)
    unmount()
  })

  it('标记：路径解析失败（不存在或不是目录）不得调 mark', async () => {
    mockGetFileInfo.mockResolvedValue({ success: false, error: { code: 'NotFound', message: 'not found' }, timestamp: '' })

    const { div, unmount } = await mountProtected()
    await flush(50)

    clickButton(div, '标记保护')
    await flush(30)
    setInput(inputByPlaceholder(div, '/Docs'), '/NoSuch')
    clickButton(div, '确定')
    await vi.waitFor(() => {
      if (!mockGetFileInfo.mock.calls.length) throw new Error('not called')
    })
    await flush(30)

    expect(mockMark).not.toHaveBeenCalled()
    expect(elMessage.error).toHaveBeenCalledWith('路径不存在或不是目录')
    unmount()
  })

  it('取消保护：确认框取消则不得调 unmark', async () => {
    elMessageBox.confirm.mockRejectedValueOnce(new Error('cancel'))

    const { div, unmount } = await mountProtected()
    await flush(50)

    clickButton(div, '取消保护')
    await flush(30)

    expect(mockUnmark).not.toHaveBeenCalled()
    unmount()
  })

  it('取消保护：确认后按 FileItemId 调 unmark', async () => {
    mockUnmark.mockResolvedValue({ success: true, data: null, timestamp: '' })

    const { div, unmount } = await mountProtected()
    await flush(50)

    clickButton(div, '取消保护')
    await vi.waitFor(() => {
      if (!mockUnmark.mock.calls.length) throw new Error('not called')
    })

    expect(mockUnmark).toHaveBeenCalledWith('fi1')
    unmount()
  })
})

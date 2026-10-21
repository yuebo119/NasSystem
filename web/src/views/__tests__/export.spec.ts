/**
 * Export 视图层契约测试（2026-10-19 新页面）。
 *
 * 【为什么必须测这组】3-2-1 导出从快照页工具栏对话框提升为独立页，
 * 是数据保护"最后一公里"的操作面。两条关键契约：根路径为空不得
 * 发起导出；密码非空时产物是 .zip.enc（下载文件名后缀），空密码
 * 是 .zip。流式 ZIP 由后端边扫边压，前端只负责触发下载。
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

vi.mock('@/api/snapshot', () => ({
  exportApi: {
    exportDirectory: vi.fn()
  }
}))

import { exportApi } from '@/api/snapshot'
import i18n from '@/locales'
import Export from '@/views/Export.vue'
import { flush, mountView } from '@/__tests__/helpers'

const mockExport = vi.mocked(exportApi.exportDirectory)

function mountExport() {
  return mountView(Export, { route: '/export' })
}

/** 填 input：写值并派发 input 事件驱动 v-model */
function setInput(input: HTMLInputElement, value: string) {
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

/** 按 placeholder 定位输入框 */
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

describe('Export 视图层契约', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-CN'
    mockExport.mockResolvedValue(new Blob(['zip-bytes']))
    URL.createObjectURL = vi.fn(() => 'blob:mock')
    URL.revokeObjectURL = vi.fn()
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('根路径为空时警告且不发导出请求', async () => {
    const { div, unmount } = await mountExport()
    await flush(30)

    setInput(inputByPlaceholder(div, '/'), '   ')
    clickButton(div, '开始导出')
    await flush(30)

    expect(elMessage.warning).toHaveBeenCalledWith('根路径不能为空')
    expect(mockExport).not.toHaveBeenCalled()
    unmount()
  })

  it('正常导出：以根路径调接口，无密码走明文 ZIP 分支', async () => {
    const { div, unmount } = await mountExport()
    await flush(30)

    setInput(inputByPlaceholder(div, '/'), '/Docs')
    clickButton(div, '开始导出')
    await vi.waitFor(() => {
      if (!mockExport.mock.calls.length) throw new Error('not called')
    })

    // 第二参空串 = 不加密（后端据空串跳过 AES 分支）
    expect(mockExport).toHaveBeenCalledWith('/Docs', '')
    expect(elMessage.success).toHaveBeenCalledWith('导出完成，下载已开始')
    unmount()
  })

  it('提供密码时密码原样传入，产物为 .zip.enc（解密命令在页面上有说明）', async () => {
    const { div, unmount } = await mountExport()
    await flush(30)

    setInput(inputByPlaceholder(div, '/'), '/')
    // 8 位以上（2026-10-21 新增最低长度约束）且两次确认一致。
    // 确认框是 v-if 条件渲染，写完密码要等一个 tick 才出现
    setInput(inputByPlaceholder(div, '留空则不加密'), 's3cretPass')
    await flush(30)
    setInput(inputByPlaceholder(div, '再次输入密码'), 's3cretPass')
    clickButton(div, '开始导出')
    await vi.waitFor(() => {
      if (!mockExport.mock.calls.length) throw new Error('not called')
    })

    expect(mockExport).toHaveBeenCalledWith('/', 's3cretPass')
    // 加密格式说明必须随表单可见：用户拿到 .zip.enc 后需要解密命令
    expect(div.querySelector('.export-page')?.textContent).toContain('openssl enc -d -aes-256-cbc')
    unmount()
  })

  it('加密密码不足 8 位时阻断且不发导出请求', async () => {
    const { div, unmount } = await mountExport()
    await flush(30)

    setInput(inputByPlaceholder(div, '/'), '/')
    setInput(inputByPlaceholder(div, '留空则不加密'), 'short')
    clickButton(div, '开始导出')
    await flush(30)

    expect(elMessage.warning).toHaveBeenCalledWith('加密密码至少 8 位')
    expect(mockExport).not.toHaveBeenCalled()
    unmount()
  })

  it('两次密码不一致时阻断且不发导出请求', async () => {
    const { div, unmount } = await mountExport()
    await flush(30)

    setInput(inputByPlaceholder(div, '/'), '/')
    setInput(inputByPlaceholder(div, '留空则不加密'), 's3cretPass')
    await flush(30)
    setInput(inputByPlaceholder(div, '再次输入密码'), 'differentPass')
    clickButton(div, '开始导出')
    await flush(30)

    expect(elMessage.warning).toHaveBeenCalledWith('两次输入的密码不一致')
    expect(mockExport).not.toHaveBeenCalled()
    unmount()
  })

  it('导出失败时不弹成功提示（错误由 http 拦截器统一呈现）', async () => {
    mockExport.mockRejectedValue(new Error('network'))
    const { div, unmount } = await mountExport()
    await flush(30)

    setInput(inputByPlaceholder(div, '/'), '/Docs')
    clickButton(div, '开始导出')
    await vi.waitFor(() => {
      if (!mockExport.mock.calls.length) throw new Error('not called')
    })
    await flush(30)

    expect(elMessage.success).not.toHaveBeenCalled()
    unmount()
  })
})

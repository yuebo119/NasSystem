/**
 * Ocr 视图层契约测试（2026-10-19 新页面）。
 *
 * 【为什么必须测这组】文字识别页把批量识别从文件管理工具栏
 * （恰好选中一个文件夹才可用）提为常规入口。关键契约：
 * ① 路径为空不发请求；② 批量结果四态计数渲染；
 * ③ 自动识别开关仅管理员可见，切换走整键提交。
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

vi.mock('@/api/ocr', () => ({
  ocrApi: {
    recognizeDirectory: vi.fn()
  }
}))

vi.mock('@/api/systemConfig', () => ({
  systemConfigApi: {
    get: vi.fn(),
    update: vi.fn()
  }
}))

import { ocrApi } from '@/api/ocr'
import { systemConfigApi } from '@/api/systemConfig'
import i18n from '@/locales'
import { useUserStore } from '@/stores/user'
import Ocr from '@/views/Ocr.vue'
import { flush, mountView } from '@/__tests__/helpers'

const mockBatch = vi.mocked(ocrApi.recognizeDirectory)
const mockConfigGet = vi.mocked(systemConfigApi.get)
const mockConfigUpdate = vi.mocked(systemConfigApi.update)

function mountOcr(role: 'Admin' | 'User') {
  const pinia = createPinia()
  setActivePinia(pinia)
  const store = useUserStore()
  store.user = {
    id: 'u1', username: role === 'Admin' ? 'root' : 'alice', role,
    isEnabled: true, usedBytes: 0, createdAt: '2026-01-01T00:00:00Z'
  }
  store.initialized = true
  return mountView(Ocr, { route: '/ocr', pinia })
}

function setInput(input: HTMLInputElement, value: string) {
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

function clickButton(div: HTMLElement, text: string) {
  const button = Array.from(div.querySelectorAll('button')).find(b => b.textContent?.includes(text))
  if (!button) throw new Error(`button not found: ${text}`)
  button.click()
}

describe('Ocr 视图层契约', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-CN'
    mockConfigGet.mockResolvedValue({
      success: true,
      data: {
        groups: [{
          group: 'ocr',
          titleKey: 'config.groupOcr',
          items: [
            { key: 'ocr.auto-recognize', labelKey: 'config.ocrAutoRecognize', type: 1, value: 'false', defaultValue: 'false', min: null, max: null, descriptionKey: 'config.ocrAutoRecognizeDesc' }
          ]
        }],
        lastUpdatedAt: null
      },
      timestamp: ''
    } as never)
    mockConfigUpdate.mockResolvedValue({ success: true, timestamp: '' } as never)
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('路径为空时警告且不发批量请求', async () => {
    const { div, unmount } = await mountOcr('Admin')
    await flush(50)

    const pathInput = div.querySelector('.path-input input') as HTMLInputElement
    setInput(pathInput, '  ')
    clickButton(div, '开始识别')
    await flush(30)

    expect(elMessage.warning).toHaveBeenCalledWith('目录路径不能为空')
    expect(mockBatch).not.toHaveBeenCalled()
    unmount()
  })

  it('批量识别：路径与 force 原样传入，四态计数渲染', async () => {
    mockBatch.mockResolvedValue({
      success: true,
      data: { total: 12, recognized: 8, skipped: 3, failed: 1 },
      timestamp: ''
    } as never)
    const { div, unmount } = await mountOcr('Admin')
    await flush(50)

    const pathInput = div.querySelector('.path-input input') as HTMLInputElement
    setInput(pathInput, '/Docs')
    clickButton(div, '开始识别')
    await vi.waitFor(() => {
      if (!div.querySelector('.batch-result')) throw new Error('not done')
    })

    expect(mockBatch).toHaveBeenCalledWith('/Docs', false)
    const result = div.querySelector('.batch-result')?.textContent ?? ''
    expect(result).toContain('图片总数 12')
    expect(result).toContain('新识别 8')
    expect(result).toContain('跳过 3')
    expect(result).toContain('失败 1')
    unmount()
  })

  it('自动识别开关仅管理员可见，切换时按整键提交', async () => {
    const { div, unmount } = await mountOcr('Admin')
    await flush(50)

    // 开关卡存在（管理员）
    expect(div.querySelector('.ocr-page')?.textContent).toContain('上传后自动识别')
    const switchEl = div.querySelector('.el-switch') as HTMLElement
    switchEl.click()
    await vi.waitFor(() => {
      if (!mockConfigUpdate.mock.calls.length) throw new Error('not called')
    })

    expect(mockConfigUpdate).toHaveBeenCalledWith({ 'ocr.auto-recognize': 'true' })
    // 等状态回写完成再卸载：异步链尾段会触发 el-switch 对 input 的写入，
    // 卸载早于它就变成对已卸载组件的 null 写（unhandled rejection）
    await vi.waitFor(() => {
      if (!elMessage.success.mock.calls.length) throw new Error('not done')
    })
    unmount()
  })

  it('普通用户看不到自动识别开关，但批量工具可用', async () => {
    mockBatch.mockResolvedValue({
      success: true,
      data: { total: 2, recognized: 2, skipped: 0, failed: 0 },
      timestamp: ''
    } as never)
    const { div, unmount } = await mountOcr('User')
    await flush(50)

    // 开关卡整卡不渲染
    expect(div.querySelector('.ocr-page')?.textContent).not.toContain('上传后自动识别')
    expect(div.querySelector('.el-switch')).toBeNull()
    // 批量工具仍可用
    const pathInput = div.querySelector('.path-input input') as HTMLInputElement
    setInput(pathInput, '/')
    clickButton(div, '开始识别')
    await vi.waitFor(() => {
      if (!mockBatch.mock.calls.length) throw new Error('not called')
    })
    unmount()
  })
})

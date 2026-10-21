/**
 * AuthImage 组件契约测试（2026-10-21 全量审计新增）。
 *
 * 【为什么必须测这组】缩略图占位图标有三种成因：加载中失败（瞬时）、
 * 格式不支持（永久）、权限问题。审计前失败后只落一个通用 Picture 图标，
 * 用户无法区分"图碎了"与"这格式本来就没缩略图"。接线
 * /thumbnails/supported 后失败路径多打一次探测，按答案给不同 title——
 * 这条 spec 锁住探测真的发生了、答案真的影响了展示。
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'

vi.mock('element-plus', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>()
  return { ...actual }
})

import i18n from '@/locales'
import AuthImage from '@/components/AuthImage.vue'
import { flush, mountView } from '@/__tests__/helpers'

const fetchMock = vi.fn()
vi.stubGlobal('fetch', fetchMock)

/** AuthImage 需要 path/size props，用 render 函数包一层传给 mountView */
function wrapper(path = '/probe.png') {
  return defineComponent({
    setup() {
      return () => h(AuthImage, { path, size: 64 })
    }
  })
}

/** 缩略图请求成功 */
function thumbnailOk() {
  return { ok: true, blob: () => Promise.resolve(new Blob(['x'])) }
}

/** 缩略图失败 + supported 探测答 isSupported */
function thumbnailFailThenSupport(isSupported: boolean) {
  fetchMock
    .mockResolvedValueOnce({ ok: false, status: 500 })
    .mockResolvedValueOnce({
      ok: true,
      json: () => Promise.resolve({ success: true, data: { isSupported } })
    })
}

describe('AuthImage 缩略图占位', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.setItem('token', 'test-token')
    // happy-dom 的 navigator.language 是 en-US，固定中文让断言对齐 zh-CN 包
    i18n.global.locale.value = 'zh-CN'
    URL.createObjectURL = vi.fn(() => 'blob:mock')
    URL.revokeObjectURL = vi.fn()
  })

  afterEach(() => {
    localStorage.clear()
  })

  it('缩略图请求成功：渲染 img，不落占位图标', async () => {
    fetchMock.mockResolvedValueOnce(thumbnailOk())
    const { div, unmount } = await mountView(wrapper())
    await flush(50)

    expect(div.querySelector('img')).not.toBeNull()
    expect(div.querySelector('.auth-image-fallback')).toBeNull()
    unmount()
  })

  it('缩略图失败且格式不支持：占位图标带"不支持"说明', async () => {
    thumbnailFailThenSupport(false)
    const { div, unmount } = await mountView(wrapper())
    await flush(50)

    const fallback = div.querySelector('.auth-image-fallback')
    expect(fallback).not.toBeNull()
    expect(fallback?.getAttribute('title')).toBe('此格式不支持生成缩略图')
    unmount()
  })

  it('缩略图失败但格式支持：占位图标带"可重试"说明', async () => {
    thumbnailFailThenSupport(true)
    const { div, unmount } = await mountView(wrapper())
    await flush(50)

    const fallback = div.querySelector('.auth-image-fallback')
    expect(fallback).not.toBeNull()
    expect(fallback?.getAttribute('title')).toBe('缩略图加载失败（可重试）')
    unmount()
  })

  it('探测接口自身失败：按"非格式问题"展示，不误报不支持', async () => {
    fetchMock
      .mockResolvedValueOnce({ ok: false, status: 500 })
      .mockResolvedValueOnce({ ok: false, status: 500 })
    const { div, unmount } = await mountView(wrapper())
    await flush(50)

    const fallback = div.querySelector('.auth-image-fallback')
    expect(fallback?.getAttribute('title')).toBe('缩略图加载失败（可重试）')
    unmount()
  })
})

/**
 * useIsMobile 组合函数测试：锁定"断点探测 + change 监听 + 卸载摘 listener"契约。
 * 该标志驱动所有模态抽屉的宽屏定宽/窄屏全宽切换，探测失灵会让抽屉在手机上溢出。
 *
 * 不引 @vue/test-utils：用 createApp 挂载一个只调用该组合函数的最小组件即可，
 * 组合函数的 onMounted/onUnmounted 在真实组件实例里才会执行。
 */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h } from 'vue'
import { useIsMobile } from '@/utils/useIsMobile'

type Listener = (event: MediaQueryListEvent) => void

// 构造可控的 matchMedia 假件：记录 add/removeEventListener，支持手动触发 change
function installMatchMedia(initialMatches: boolean) {
  const listeners: Listener[] = []
  const mediaQuery = {
    matches: initialMatches,
    media: '(max-width: 768px)',
    addEventListener: vi.fn((_: string, listener: Listener) => listeners.push(listener)),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn()
  }
  window.matchMedia = vi.fn(() => mediaQuery) as unknown as typeof window.matchMedia
  return {
    mediaQuery,
    emit: (matches: boolean) => {
      mediaQuery.matches = matches
      listeners.forEach(l => l({ matches } as MediaQueryListEvent))
    }
  }
}

// 挂载探针组件，返回其内部的 isMobile 引用与卸载函数
function mountProbe() {
  let isMobile: { value: boolean } | undefined
  const Probe = defineComponent({
    setup() {
      isMobile = useIsMobile()
      return () => h('div')
    }
  })
  const app = createApp(Probe)
  app.mount(document.createElement('div'))
  return {
    get isMobile() { return isMobile! },
    unmount: () => app.unmount()
  }
}

describe('useIsMobile', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('窄屏断点时挂载后为 true', () => {
    installMatchMedia(true)

    const probe = mountProbe()

    expect(probe.isMobile.value).toBe(true)
    probe.unmount()
  })

  it('宽屏断点时挂载后为 false', () => {
    installMatchMedia(false)

    const probe = mountProbe()

    expect(probe.isMobile.value).toBe(false)
    probe.unmount()
  })

  it('断点穿越时值随 change 事件翻转', () => {
    const { emit } = installMatchMedia(false)

    const probe = mountProbe()
    expect(probe.isMobile.value).toBe(false)

    emit(true)
    expect(probe.isMobile.value).toBe(true)

    emit(false)
    expect(probe.isMobile.value).toBe(false)
    probe.unmount()
  })

  it('卸载后移除 change 监听，不再泄漏', () => {
    const { mediaQuery } = installMatchMedia(false)

    const probe = mountProbe()
    probe.unmount()

    expect(mediaQuery.removeEventListener).toHaveBeenCalled()
  })
})

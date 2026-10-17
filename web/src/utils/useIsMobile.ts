import { ref, onMounted, onUnmounted, type Ref } from 'vue'

// 与项目既有 CSS 移动断点口径一致（Storage/Shares/Users 的响应式规则同用 768px）
const MOBILE_QUERY = '(max-width: 768px)'

/**
 * 响应式视口探测：当前是否为窄屏（手机/窄窗口）。
 *
 * 模态统一右侧抽屉后，宽屏保持原有定宽、窄屏取全宽，
 * 靠这个标志切换。监听 matchMedia 而非 resize 事件：
 * matchMedia 在断点穿越时精确触发一次，resize 在拖动过程中持续触发。
 */
export function useIsMobile(): Ref<boolean> {
  const isMobile = ref(false)
  let mediaQuery: MediaQueryList | null = null

  const onChange = (event: MediaQueryListEvent) => {
    isMobile.value = event.matches
  }

  onMounted(() => {
    if (typeof window === 'undefined' || !window.matchMedia) return
    mediaQuery = window.matchMedia(MOBILE_QUERY)
    isMobile.value = mediaQuery.matches
    mediaQuery.addEventListener('change', onChange)
  })

  onUnmounted(() => {
    mediaQuery?.removeEventListener('change', onChange)
    mediaQuery = null
  })

  return isMobile
}

/**
 * 抽屉尺寸助手：窄屏全宽；宽屏用 clamp(下限, 视口比例, 上限)。
 *
 * 为什么用 clamp 而不是"宽屏一个固定像素"：固定值只在 768px 断点两侧
 * 跳变，768~1100px 的窄桌面窗口里 700px 的抽屉会占掉大半个屏幕，
 * 而 1440px 上 500px 又显得保守。clamp 让尺寸随视口连续缩放，
 * 中段（如 40vw）平滑过渡，两端被 min/max 夹住不失真。
 * 内联 width 接受 clamp()（已实测），断点变化时 Vue 重设内联值，
 * 其余时候浏览器原生响应窗口拖动，不需要 JS 参与。
 */
export function useDrawerSize() {
  const isMobile = useIsMobile()
  return (min: string, vw: string, max: string): string =>
    isMobile.value ? '100%' : `clamp(${min}, ${vw}, ${max})`
}

import { ref, onMounted, onUnmounted, type Ref } from 'vue'

// 与项目既有 CSS 移动断点口径一致（Storage/Shares/Users 的响应式规则同用 768px）
const MOBILE_QUERY = '(max-width: 768px)'

/**
 * 响应式视口探测：当前是否为窄屏（手机/窄窗口）。
 *
 * 模态窗统一改用右侧抽屉后，宽屏保持原有定宽、窄屏取全宽，
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

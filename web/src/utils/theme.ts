// 主题管理工具

export type ThemeType = 'light' | 'dark'

// 获取保存的主题设置
export const getDefaultTheme = (): ThemeType => {
  const saved = localStorage.getItem('theme')
  if (saved === 'light' || saved === 'dark') {
    return saved
  }
  // 检测系统主题偏好
  if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
    return 'dark'
  }
  return 'light'
}

// 应用主题（仅切 DOM class，不落盘——ITM-M7：无条件写 localStorage 会让
// initTheme 的"未手动设置才跟随系统"判断永久失效）
export const applyTheme = (theme: ThemeType) => {
  const html = document.documentElement
  if (theme === 'dark') {
    html.classList.add('dark')
  } else {
    html.classList.remove('dark')
  }
}

// 用户主动设置主题：应用并持久化
export const setTheme = (theme: ThemeType): ThemeType => {
  applyTheme(theme)
  localStorage.setItem('theme', theme)
  return theme
}

// 切换主题（用户主动操作，持久化）
export const toggleTheme = (): ThemeType => {
  const current = document.documentElement.classList.contains('dark') ? 'dark' : 'light'
  const newTheme: ThemeType = current === 'dark' ? 'light' : 'dark'
  return setTheme(newTheme)
}

// 获取当前主题
export const getCurrentTheme = (): ThemeType => {
  return document.documentElement.classList.contains('dark') ? 'dark' : 'light'
}

// 系统主题偏好监听：实例与具名 handler 存模块级。存引用是 disposeThemeWatcher
// 能卸载的前提——addEventListener 与 removeEventListener 必须是同一个
// MediaQueryList 和同一个函数引用，只传新闭包进去是卸不掉的
let themeMediaQuery: MediaQueryList | null = null
let themeChangeHandler: ((e: MediaQueryListEvent) => void) | null = null

// 初始化主题（在应用启动时调用）
export const initTheme = () => {
  const theme = getDefaultTheme()
  applyTheme(theme)

  // 先卸上一轮的再挂：HMR 与测试会反复调 initTheme，不先卸就会往同一个
  // MediaQueryList 上叠多个回调，一次系统主题变更触发 N 次 applyTheme
  disposeThemeWatcher()

  // 监听系统主题变化
  if (window.matchMedia) {
    themeMediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
    themeChangeHandler = (e: MediaQueryListEvent) => {
      // 只有在用户没有手动设置主题时才跟随系统
      if (!localStorage.getItem('theme')) {
        applyTheme(e.matches ? 'dark' : 'light')
      }
    }
    themeMediaQuery.addEventListener('change', themeChangeHandler)
  }
}

/**
 * 卸载系统主题监听（幂等，重复调用安全）。
 *
 * main.ts 不调用：initTheme 是应用级单例，监听器与页面同生命周期，
 * 没有"组件卸载"这个卸载时机，平常挂着不卸是正确的。
 * 存在的意义是给测试与 HMR——它们会反复执行 initTheme，没有这个出口
 * 的话每次都会新增一个匿名监听器，只加不减
 */
export const disposeThemeWatcher = () => {
  if (themeMediaQuery && themeChangeHandler) {
    themeMediaQuery.removeEventListener('change', themeChangeHandler)
  }
  themeMediaQuery = null
  themeChangeHandler = null
}

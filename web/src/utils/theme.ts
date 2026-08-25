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

// 初始化主题（在应用启动时调用）
export const initTheme = () => {
  const theme = getDefaultTheme()
  applyTheme(theme)
  
  // 监听系统主题变化
  if (window.matchMedia) {
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', (e) => {
      // 只有在用户没有手动设置主题时才跟随系统
      if (!localStorage.getItem('theme')) {
        applyTheme(e.matches ? 'dark' : 'light')
      }
    })
  }
}

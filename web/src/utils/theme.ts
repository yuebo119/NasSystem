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

// 应用主题
export const applyTheme = (theme: ThemeType) => {
  const html = document.documentElement
  if (theme === 'dark') {
    html.classList.add('dark')
  } else {
    html.classList.remove('dark')
  }
  localStorage.setItem('theme', theme)
}

// 切换主题
export const toggleTheme = (): ThemeType => {
  const current = document.documentElement.classList.contains('dark') ? 'dark' : 'light'
  const newTheme: ThemeType = current === 'dark' ? 'light' : 'dark'
  applyTheme(newTheme)
  return newTheme
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

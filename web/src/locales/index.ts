import { createI18n } from 'vue-i18n'
import zhCN from './zh-CN'
import enUS from './en-US'

export type LocaleType = 'zh-CN' | 'en-US'

export const localeOptions = [
  { value: 'zh-CN', label: '简体中文' },
  { value: 'en-US', label: 'English' }
]

// 获取保存的语言设置，默认中文
const getDefaultLocale = (): LocaleType => {
  const saved = localStorage.getItem('locale')
  if (saved === 'zh-CN' || saved === 'en-US') {
    return saved
  }
  // 检测浏览器语言
  const browserLang = navigator.language
  if (browserLang.startsWith('zh')) {
    return 'zh-CN'
  }
  return 'en-US'
}

const i18n = createI18n({
  legacy: false, // 使用 Composition API 模式
  locale: getDefaultLocale(),
  fallbackLocale: 'zh-CN',
  messages: {
    'zh-CN': zhCN,
    'en-US': enUS
  }
})

// 切换语言
export const setLocale = (locale: LocaleType) => {
  i18n.global.locale.value = locale
  localStorage.setItem('locale', locale)
  document.documentElement.setAttribute('lang', locale)
}

// 获取当前语言
export const getLocale = (): LocaleType => {
  return i18n.global.locale.value as LocaleType
}

export default i18n

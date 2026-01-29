import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'
import 'element-plus/dist/index.css'
import 'element-plus/theme-chalk/dark/css-vars.css'
import './styles/main.scss'

import App from './App.vue'
import router from './router'
import i18n from './locales'
import { useUserStore } from './stores/user'
import { initTheme } from './utils/theme'

const app = createApp(App)
const pinia = createPinia()

// 注册 Element Plus 图标
for (const [key, component] of Object.entries(ElementPlusIconsVue)) {
  app.component(key, component)
}

app.use(pinia)
app.use(router)
app.use(i18n)
app.use(ElementPlus, { size: 'default', zIndex: 3000 })

// 初始化主题
initTheme()

// 初始化：检查并恢复登录状态
const userStore = useUserStore()
userStore.checkAuth().finally(() => {
  app.mount('#app')
})

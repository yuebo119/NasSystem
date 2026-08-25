import { createApp } from 'vue'
import { createPinia } from 'pinia'
// ITM-N5/G18：Element Plus 按需引入——模板组件由 unplugin resolvers 自动导入，
// 此处仅补齐"非模板形态"（反馈 API 与指令）的样式，全量 CSS 已移除
import 'element-plus/es/components/message/style/css'
import 'element-plus/es/components/message-box/style/css'
import 'element-plus/es/components/notification/style/css'
import 'element-plus/es/components/loading/style/css'
import 'element-plus/theme-chalk/dark/css-vars.css'
import './styles/main.scss'

import App from './App.vue'
import router from './router'
import i18n from './locales'
import { useUserStore } from './stores/user'
import { initTheme } from './utils/theme'

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)
app.use(router)
app.use(i18n)

// 初始化主题
initTheme()

// 初始化：检查并恢复登录状态
const userStore = useUserStore()
userStore.checkAuth().finally(() => {
  app.mount('#app')
})

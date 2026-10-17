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

// D1：先挂载再恢复会话。原实现 await checkAuth().finally(mount)，首屏可交互
// 时间被一个 /users/me 往返拖住；现在挂载与请求并行，登录页/框架立即可见。
// 路由守卫内部 await 的是 store 里同一个 in-flight checkAuth（并发已合并），
// 受保护路由的跳转判定不受影响；无 token 时守卫直接放行去登录页
app.mount('#app')

const userStore = useUserStore()
void userStore.checkAuth()

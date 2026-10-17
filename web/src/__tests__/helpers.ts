/**
 * 视图层测试的公共挂载样板。
 *
 * 【为什么抽这里】第二个视图 spec（Login）出现时，挂载一个视图要重复
 * 四件事：真 pinia、真 i18n（t 走真实语言包，不 mock）、RouterLink 替身
 * （测试环境无 router-link 全局注册）、等 onMounted 异步链走完的刷新。
 * 每个 spec 各写一份必然漂移，收口到此处。
 *
 * router 用 memory history 的真实例而非 mock push：这样"登录成功跳转
 * redirect"这类断言验的是真实路由流转（currentRoute 变化），比断言
 * "push 被调用且参数正确"更强，也不会漏掉 push 之外的导航失败。
 */
import { createApp, h, type Component } from 'vue'
import { createPinia } from 'pinia'
import { createRouter, createMemoryHistory, type RouteRecordRaw } from 'vue-router'
import i18n from '@/locales'

const FALLBACK_ROUTE: RouteRecordRaw[] = [
  { path: '/:pathMatch(.*)*', component: { render: () => h('div') } }
]

export interface MountedView {
  app: ReturnType<typeof createApp>
  div: HTMLElement
  router: ReturnType<typeof createRouter>
  unmount: () => void
}

/**
 * 挂载一个视图组件。
 * route 为初始路由（可带 query，如 '/login?redirect=/files'）；
 * routes 为视图所需的路由表（如 ShareAccess 的 /s/:shareCode）。
 */
export async function mountView(
  component: Component,
  options: { route?: string; routes?: RouteRecordRaw[] } = {}
): Promise<MountedView> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: options.routes ?? FALLBACK_ROUTE
  })
  // 必须显式 push 一次（默认 '/'）：初始导航由 push 或 app.mount 触发，
  // 而在 mount 前调 isReady 时若无 push，初始导航迟迟不开始，
  // isReady 的 promise 永不 resolve（实测挂死 5s 超时）
  await router.push(options.route ?? '/')
  await router.isReady()

  const app = createApp({ render: () => h(component) })
  app.use(createPinia())
  app.use(i18n)
  app.use(router)
  // 模板里的 router-link 在没有真实路由记录时可解析，避免 Vue warn
  app.component('RouterLink', { props: ['to'], render: () => h('a') })

  const div = document.createElement('div')
  app.mount(div)
  return { app, div, router, unmount: () => app.unmount() }
}

/** 等 onMounted 的 Promise.all 与后续微任务链走完 */
export function flush(ms = 30): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms))
}

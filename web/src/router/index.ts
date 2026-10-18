import { createRouter, createWebHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'
import NProgress from 'nprogress'
import 'nprogress/nprogress.css'
import { useUserStore } from '@/stores/user'

// 路由配置
const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('@/views/Login.vue'),
    meta: { title: '登录', requiresAuth: false }
  },
  {
    path: '/s/:shareCode',
    name: 'ShareAccess',
    component: () => import('@/views/ShareAccess.vue'),
    meta: { title: '分享', requiresAuth: false }
  },
  {
    path: '/',
    component: () => import('@/layouts/MainLayout.vue'),
    redirect: '/dashboard',
    meta: { requiresAuth: true },
    children: [
      {
        path: 'dashboard',
        name: 'Dashboard',
        component: () => import('@/views/Dashboard.vue'),
        meta: { title: '仪表盘', icon: 'Odometer' }
      },
      {
        path: 'files/:path*',
        name: 'Files',
        component: () => import('@/views/Files.vue'),
        meta: { title: '文件管理', icon: 'Folder' }
      },
      {
        path: 'shares',
        name: 'Shares',
        component: () => import('@/views/Shares.vue'),
        meta: { title: '共享管理', icon: 'Share' }
      },
      {
        path: 'backup',
        name: 'Backup',
        component: () => import('@/views/Backup.vue'),
        meta: { title: '移动备份', icon: 'Iphone' }
      },
      {
        path: 'media',
        name: 'Media',
        component: () => import('@/views/Media.vue'),
        meta: { title: '媒体库', icon: 'Picture' }
      },
      {
        path: 'storage',
        name: 'Storage',
        component: () => import('@/views/Storage.vue'),
        meta: { title: '存储管理', icon: 'Box' }
      },
      {
        path: 'users',
        name: 'Users',
        component: () => import('@/views/Users.vue'),
        meta: { title: '用户管理', icon: 'User', requiresAdmin: true }
      },
      {
        path: 'settings',
        name: 'Settings',
        component: () => import('@/views/Settings.vue'),
        meta: { title: '系统设置', icon: 'Setting' }
      },
      {
        path: 'snapshots',
        name: 'Snapshots',
        component: () => import('@/views/Snapshots.vue'),
        meta: { title: '清单快照', icon: 'Camera', requiresAdmin: true }
      },
      {
        path: 'system-config',
        name: 'SystemConfig',
        component: () => import('@/views/SystemConfig.vue'),
        meta: { title: '系统配置', icon: 'Tools', requiresAdmin: true }
      }
    ]
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'NotFound',
    component: () => import('@/views/NotFound.vue'),
    meta: { title: '页面未找到' }
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

// 路由守卫
router.beforeEach(async (to, _from, next) => {
  NProgress.start()
  
  // 设置页面标题
  document.title = `${to.meta.title || 'NAS System'} - NAS System`

  const userStore = useUserStore()
  const requiresAuth = to.meta.requiresAuth !== false
  const requiresAdmin = to.meta.requiresAdmin === true

  // 等待初始化完成
  if (!userStore.initialized && userStore.token) {
    await userStore.checkAuth()
  }

  if (requiresAuth && !userStore.isAuthenticated) {
    // 需要登录但未登录
    next({ name: 'Login', query: { redirect: to.fullPath } })
  } else if (requiresAdmin && !userStore.isAdmin) {
    // 需要管理员权限
    next({ name: 'Dashboard' })
  } else if (to.name === 'Login' && userStore.isAuthenticated) {
    // 已登录访问登录页
    next({ name: 'Dashboard' })
  } else {
    next()
  }
})

router.afterEach(() => {
  NProgress.done()
})

export default router

/*
 * NAS System Service Worker（ROADMAP Phase 1 PWA 入口）
 *
 * 策略：网络优先，缓存兜底。只缓存导航请求（应用外壳），
 * API 请求一律走网络——备份状态、文件列表必须实时，
 * 缓存 API 响应会把陈旧数据当现网数据呈现。
 * 静态资产（哈希文件名）由网络优先自然覆盖，不做预置清单：
 * 预置清单每个构建都要同步更新，漏一个就是线上旧资产。
 */
const CACHE_NAME = 'nas-shell-v1'

// 导航兜底条目：install 时预置，fetch 的 navigate 分支离线时回落到它。
// 淘汰逻辑必须保住它（见 activate），否则离线兜底会失效
const SHELL_ENTRY = '/dashboard'

// 缓存条目上限。静态资产是带内容哈希的文件名：每次部署都会产生一批新条目，
// 而旧哈希条目既不会被 put 覆盖（URL 不同）、也没有任何删除路径——缓存
// 只增不减。origin 存储配额有限，超限后浏览器可能连带驱逐当前版本还要用
// 的条目，表现为"刚更新完就白屏/资源 404"。
// 构建侧没有把版本哈希注入 CACHE_NAME 的链路（无 workbox 类插件，且
// emptyOutDir 每次清空产物），故用数量淘汰兜底
const CACHE_MAX_ENTRIES = 50

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => cache.add(SHELL_ENTRY))
      .then(() => self.skipWaiting())
  )
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((names) => Promise.all(
        names.filter((name) => name !== CACHE_NAME).map((name) => caches.delete(name))
      ))
      // 当前缓存同样要按数量淘汰：只保留最近 N 条，超出的从最旧的开始删。
      // caches.keys() 按插入序返回，故数组头部是最旧条目（哈希文件名随每次
      // 部署变化，天然满足"旧的在前面"）
      .then(() => caches.open(CACHE_NAME))
      .then((cache) => cache.keys().then((keys) => {
        const shellKey = new URL(SHELL_ENTRY, self.location.href).href
        // 兜底条目不参与淘汰竞争，但仍计入总量：上限始终是 N 条
        const shellCount = keys.filter((req) => req.url === shellKey).length
        const evictable = keys.filter((req) => req.url !== shellKey)
        const budget = Math.max(0, CACHE_MAX_ENTRIES - shellCount)
        if (evictable.length <= budget) return
        const stale = evictable.slice(0, evictable.length - budget)
        return Promise.all(stale.map((req) => cache.delete(req)))
      }))
      .then(() => self.clients.claim())
  )
})

self.addEventListener('fetch', (event) => {
  const request = event.request

  // 只处理同源 GET：POST/PUT/DELETE 是上传下载与备份写操作，
  // 任何缓存/重试语义都会造成重复上传
  if (request.method !== 'GET') return

  const url = new URL(request.url)
  if (url.origin !== self.location.origin) return

  // API 与 SignalR 不走 SW：实时性由网络保证
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/hubs/')) return

  if (request.mode === 'navigate') {
    event.respondWith(
      fetch(request).catch(() => caches.match('/dashboard'))
    )
    return
  }

  event.respondWith(
    fetch(request)
      .then((response) => {
        if (response.ok && response.type === 'basic') {
          const clone = response.clone()
          caches.open(CACHE_NAME).then((cache) => cache.put(request, clone))
        }
        return response
      })
      .catch(() => caches.match(request))
  )
})

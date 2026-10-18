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

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => cache.add('/dashboard'))
      .then(() => self.skipWaiting())
  )
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((names) => Promise.all(
        names.filter((name) => name !== CACHE_NAME).map((name) => caches.delete(name))
      ))
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

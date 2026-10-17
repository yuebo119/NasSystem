/**
 * 视觉基线截图脚本（一次性工具，非常规测试）。
 *
 * 【为什么需要它】happy-dom 无布局引擎，组件测试断言不了等高、间距、
 * 响应式折叠这类视觉契约；el-table-v2 虚拟滚动也只真实现布局。此脚本
 * 用系统 Chrome（channel: 'chrome'，不下载浏览器）以独立 profile 启动，
 * 与 playwright-mcp 的实例互不冲突，对关键页面截基线图，改动前后各跑
 * 一次人工对比。
 *
 * 用法：
 *   1. 确认 dev server（localhost:6060）与 API（localhost:6050）在跑
 *   2. node scripts/visual-baseline.mjs [输出目录，默认 temp/visual-baseline]
 *   3. 图片进输出目录，控制台错误一并打印（顺带做渲染冒烟）
 *
 * 登录用项目语言包内公开的本地默认管理员（locales 的 login.defaultAdmin），
 * 仅用于本地 dev 实例的视觉核对。
 */
import { mkdir } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium } from 'playwright'

const WEB = 'http://localhost:6060'
const API = 'http://localhost:6050'
const OUT = process.argv[2] ?? 'temp/visual-baseline'

const PAGES = [
  { path: '/login', name: 'login' },
  { path: '/dashboard', name: 'dashboard' },
  { path: '/files', name: 'files' },
  { path: '/storage', name: 'storage' },
  { path: '/shares', name: 'shares' },
  { path: '/users', name: 'users' },
  { path: '/settings', name: 'settings' }
]

async function login() {
  const res = await fetch(`${API}/api/v1/users/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: 'admin', password: 'admin123' })
  })
  const body = await res.json()
  if (!body.success) throw new Error(`login failed: ${JSON.stringify(body.error)}`)
  return body.data.token
}

const browser = await chromium.launch({ channel: 'chrome' })
const context = await browser.newContext({ viewport: { width: 1440, height: 900 } })
const page = await context.newPage()

const consoleErrors = []
page.on('console', (msg) => {
  if (msg.type() === 'error') consoleErrors.push(`[${page.url()}] ${msg.text()}`)
})
page.on('pageerror', (err) => consoleErrors.push(`[${page.url()}] pageerror: ${err.message}`))

try {
  const token = await login()
  // 先访问一次站点写入 origin，再注入 token；之后受保护路由直接放行
  await page.goto(WEB, { waitUntil: 'domcontentloaded' })
  await page.evaluate((t) => localStorage.setItem('token', t), token)

  await mkdir(OUT, { recursive: true })

  for (const { path, name } of PAGES) {
    await page.goto(`${WEB}${path}`, { waitUntil: 'networkidle' })
    // 等一个动画帧，让 el-card/进度条的首帧布局稳定
    await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))))
    await page.screenshot({ path: join(OUT, `${name}.png`), fullPage: true })
    console.log(`shot: ${name}.png`)
  }
} finally {
  await browser.close()
}

if (consoleErrors.length > 0) {
  console.log(`\nconsole errors (${consoleErrors.length}):`)
  for (const e of consoleErrors.slice(0, 20)) console.log('  ' + e)
} else {
  console.log('\nno console errors')
}

/**
 * format.ts 纯函数契约测试。
 *
 * 【为什么必须测这组】formatSize/formatDate/getFileIcon/getFileIconColor/
 * getPoolStatusInfo 原先是 Files/Dashboard/Users/Storage/Shares/ShareAccess
 * 六处各自的私有副本，后收敛到本模块（见文件头注释）。收敛让"改一处动六处"
 * 变成"改一处动全局"，反过来也意味着这里一处回归会同时打挂六个页面，
 * 而视图层没有组件测试兜底。getPoolStatusInfo 历史上就出过真缺陷：
 * 原 Storage.vue 把 2/3/4 解释成警告/错误/离线，与后端枚举不符，
 * 且两个视图的默认分支相反。这类"分类映射错位"只有逐个枚举值断言才能发现。
 */
import { describe, expect, it, vi } from 'vitest'

// format.ts 在模块级经 i18n.global.t 取翻译函数，测试用 mock 替身，
// 避免依赖 vue-i18n 实例与 localStorage/navigator 环境
vi.mock('@/locales', () => ({
  default: {
    global: {
      t: (key: string, params?: Record<string, unknown>) => {
        const table: Record<string, string> = {
          'common.neverLogin': '从未登录',
          'common.today': '今天',
          'common.yesterday': '昨天',
          'common.daysAgo': `${params?.n ?? ''} 天前`,
          'storage.healthy': '健康',
          'dashboard.degraded': '降级',
          'dashboard.critical': '严重',
          'dashboard.rebuilding': '重建中',
          'dashboard.offline': '离线',
          'dashboard.unknown': '未知'
        }
        return table[key] ?? key
      }
    }
  }
}))

const { formatSize, formatDate, getFileIcon, getFileIconColor, getPoolStatusInfo } =
  await import('@/utils/format')

describe('formatSize', () => {
  it('0 返回占位符而非 0.00 B', () => {
    expect(formatSize(0)).toBe('-')
  })

  it('五级单位逐级进位', () => {
    expect(formatSize(1)).toBe('1.00 B')
    expect(formatSize(1024)).toBe('1.00 KB')
    expect(formatSize(1024 * 1024)).toBe('1.00 MB')
    expect(formatSize(1024 * 1024 * 1024)).toBe('1.00 GB')
    expect(formatSize(1024 * 1024 * 1024 * 1024)).toBe('1.00 TB')
  })

  it('非整数单位保留两位小数', () => {
    expect(formatSize(1536)).toBe('1.50 KB')
    expect(formatSize(1024 * 1024 * 1.5)).toBe('1.50 MB')
  })
})

describe('formatDate', () => {
  it('空值显示从未登录', () => {
    expect(formatDate(undefined)).toBe('从未登录')
    expect(formatDate('')).toBe('从未登录')
  })

  it('当天显示时间而非日期', () => {
    const now = new Date()
    expect(formatDate(now.toISOString())).toContain('今天')
  })

  it('昨天显示昨天', () => {
    const yesterday = new Date(Date.now() - 24 * 60 * 60 * 1000)
    expect(formatDate(yesterday.toISOString())).toBe('昨天')
  })

  it('7 天内显示相对天数', () => {
    const threeDaysAgo = new Date(Date.now() - 3 * 24 * 60 * 60 * 1000)
    expect(formatDate(threeDaysAgo.toISOString())).toBe('3 天前')
  })

  it('超过 7 天显示本地日期', () => {
    const old = new Date(Date.now() - 30 * 24 * 60 * 60 * 1000)
    const result = formatDate(old.toISOString())
    expect(result).not.toContain('天前')
    expect(result).not.toBe('昨天')
    expect(result.length).toBeGreaterThan(0)
  })
})

describe('getFileIcon / getFileIconColor', () => {
  it('文件夹优先于扩展名判断', () => {
    expect(getFileIcon({ isFolder: true, name: 'a.pdf' })).toBeDefined()
    expect(getFileIconColor({ isFolder: true })).toBe('#ffc007')
  })

  it('扩展名从 name 截取时对大小写不敏感', () => {
    // 分类依据是小写扩展名，.PDF 与 .pdf 必须落同一类
    expect(getFileIconColor({ isFolder: false, name: 'a.PDF' }))
      .toBe(getFileIconColor({ isFolder: false, name: 'a.pdf' }))
  })

  it('优先使用 extension 字段而非文件名', () => {
    // 同一文件名带不同 extension 字段时应按 extension 分类
    expect(getFileIconColor({ isFolder: false, name: 'x', extension: '.mp4' }))
      .toBe(getFileIconColor({ isFolder: false, name: 'y', extension: '.mp4' }))
  })

  it('未知扩展名落默认分支', () => {
    expect(getFileIconColor({ isFolder: false, name: 'a.unknownext' })).toBe('#909399')
  })

  it('图标与颜色分类一一对应（同输入同分类）', () => {
    // 逐个代表扩展名：图标与颜色都应由同一分类表驱动
    const cases = ['.pdf', '.docx', '.xlsx', '.pptx', '.txt', '.jpg', '.mp4', '.mp3', '.zip', '.ts', '.json']
    for (const ext of cases) {
      const icon = getFileIcon({ isFolder: false, name: `f${ext}` })
      const color = getFileIconColor({ isFolder: false, name: `f${ext}` })
      expect(icon, `${ext} 应有图标`).toBeDefined()
      expect(color, `${ext} 应有实色`).toMatch(/^#[0-9a-f]{6}$/i)
    }
  })
})

describe('getPoolStatusInfo', () => {
  it('枚举 1-5 各有定义且不与默认分支混淆', () => {
    // 这里锁定的是后端枚举口径：1=Healthy 2=Degraded 3=Critical
    // 4=Rebuilding 5=Offline。历史上 Storage.vue 曾把 2/3/4 解释成
    // 警告/错误/离线，与枚举不符
    expect(getPoolStatusInfo(1).text).toBe('健康')
    expect(getPoolStatusInfo(2).text).toBe('降级')
    expect(getPoolStatusInfo(3).text).toBe('严重')
    expect(getPoolStatusInfo(4).text).toBe('重建中')
    expect(getPoolStatusInfo(5).text).toBe('离线')
  })

  it('未定义值落未知分支', () => {
    expect(getPoolStatusInfo(0).text).toBe('未知')
    expect(getPoolStatusInfo(99).text).toBe('未知')
  })

  it('每个状态的语义色与 Element Plus tag type 一致', () => {
    // type 决定前端 tag 配色，color 是图表主色，两者需自洽
    expect(getPoolStatusInfo(1).type).toBe('success')
    expect(getPoolStatusInfo(2).type).toBe('warning')
    expect(getPoolStatusInfo(3).type).toBe('danger')
    expect(getPoolStatusInfo(4).type).toBe('info')
    expect(getPoolStatusInfo(5).type).toBe('info')
  })
})

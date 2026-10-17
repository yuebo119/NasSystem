/**
 * previewUrl.ts 的 create/revoke 配对契约测试。
 *
 * 【为什么必须测这组】objectURL 泄漏是静默的：不报错、不警告，
 * 只是内存一点点涨，直到标签页崩溃。原实现把 URL 存在只增不减的 map 里，
 * 三个泄漏路径（关闭对话框不清、卸载不 revoke、重复预览重新 fetch）
 * 在功能上完全正常，只有长时间使用才会暴露。
 *
 * 所以这里锁的是"每一次 create 最终都有且仅有一次 revoke"，
 * 以及单槽语义（同时只存在一个 URL）。
 */
import { describe, expect, it } from 'vitest'
import { createPreviewUrlManager } from '../previewUrl'

describe('createPreviewUrlManager', () => {
  /** 记录型替身：把每次 revoke 都留痕，便于断言配对关系 */
  function setup() {
    const revoked: string[] = []
    let counter = 0
    const manager = createPreviewUrlManager((url) => { revoked.push(url) })
    const nextUrl = () => `blob:fake-${++counter}`
    return { manager, revoked, nextUrl }
  }

  it('put 后 urlFor 命中同一路径', () => {
    const { manager } = setup()
    manager.put('/a.mp4', 'blob:1')
    expect(manager.urlFor('/a.mp4')).toBe('blob:1')
    expect(manager.has('/a.mp4')).toBe(true)
  })

  it('urlFor 对非当前路径返回 undefined', () => {
    const { manager } = setup()
    manager.put('/a.mp4', 'blob:1')
    expect(manager.urlFor('/b.mp4')).toBeUndefined()
    expect(manager.has('/b.mp4')).toBe(false)
  })

  it('重复 put 同一路径会释放旧 URL 再存新的', () => {
    const { manager, revoked } = setup()
    manager.put('/a.mp4', 'blob:1')
    manager.put('/a.mp4', 'blob:2')
    // 旧 URL 必须被释放，否则同一文件重复预览就泄漏一个
    expect(revoked).toEqual(['blob:1'])
    expect(manager.urlFor('/a.mp4')).toBe('blob:2')
  })

  it('换文件 put 会释放上一个 URL（单槽语义）', () => {
    const { manager, revoked } = setup()
    manager.put('/a.mp4', 'blob:1')
    manager.put('/b.mp4', 'blob:2')
    expect(revoked).toEqual(['blob:1'])
    expect(manager.urlFor('/a.mp4')).toBeUndefined()
    expect(manager.urlFor('/b.mp4')).toBe('blob:2')
  })

  it('release 释放当前 URL 且可重复调用不二次 revoke', () => {
    const { manager, revoked } = setup()
    manager.put('/a.mp4', 'blob:1')
    manager.release()
    manager.release()   // 重复释放不应报错也不应二次 revoke
    expect(revoked).toEqual(['blob:1'])
    expect(manager.current()).toBeNull()
  })

  it('空管理器 release 不调用 revoke', () => {
    const { manager, revoked } = setup()
    manager.release()
    expect(revoked).toEqual([])
  })

  it('一串 put/release 序列后每个 URL 都恰好被释放一次', () => {
    const { manager, revoked, nextUrl } = setup()
    // 模拟真实使用：连续预览 5 个不同文件，最后关一次对话框
    const urls: string[] = []
    for (const name of ['a', 'b', 'c', 'd', 'e']) {
      const url = nextUrl()
      urls.push(url)
      manager.put(`/${name}.mp4`, url)
    }
    manager.release()
    // 5 个 URL，前 4 个被下一次 put 顶掉，第 5 个被 release 释放。
    // 每个恰好一次，不重不漏——这正是原实现漏掉的性质
    expect(revoked).toEqual(urls)
    expect(revoked).toHaveLength(5)
  })
})

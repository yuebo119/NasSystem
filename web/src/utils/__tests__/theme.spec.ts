/**
 * 主题工具的 DOM 语义守护测试（ITM-M7 回归锚点）。
 *
 * 【为什么这组测试必须存在】M7 缺陷的根因是 applyTheme 曾无条件写 localStorage，
 * 使 initTheme 的"未手动设置才跟随系统"判断永久失效。本文件锁定修复后的契约：
 * applyTheme 只改 DOM；只有 setTheme/toggleTheme（用户主动操作）才持久化。
 */
import { afterEach, describe, expect, it } from 'vitest'
import { applyTheme, getCurrentTheme, setTheme, toggleTheme } from '@/utils/theme'

describe('theme', () => {
  afterEach(() => {
    localStorage.removeItem('theme')
    document.documentElement.classList.remove('dark')
  })

  it('applyTheme 切换 DOM class 但不写入存储', () => {
    applyTheme('dark')

    expect(document.documentElement.classList.contains('dark')).toBe(true)
    expect(getCurrentTheme()).toBe('dark')
    expect(localStorage.getItem('theme')).toBeNull()
  })

  it('setTheme 应用并持久化用户选择', () => {
    setTheme('light')

    expect(document.documentElement.classList.contains('dark')).toBe(false)
    expect(localStorage.getItem('theme')).toBe('light')
  })

  it('toggleTheme 在明暗间翻转并持久化', () => {
    applyTheme('light')

    const next = toggleTheme()

    expect(next).toBe('dark')
    expect(getCurrentTheme()).toBe('dark')
    expect(localStorage.getItem('theme')).toBe('dark')
  })
})

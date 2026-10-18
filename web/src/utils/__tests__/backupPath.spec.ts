/**
 * backupPath 纯函数测试（ROADMAP Phase 1）。
 *
 * 锁住三个边界：整夹选择的相对路径抽取、根级文件（无目录段）、
 * 反斜杠归一。任一边界坏掉的表现是"文件传到了错误目录"，
 * 而上传本身会成功——只有路径换算测试能发现。
 */
import { describe, expect, it } from 'vitest'
import { relativePathOf, destinationDirOf, fileNameOf } from '@/utils/backupPath'

/** 造一个带可选 webkitRelativePath 的 File 替身 */
function fakeFile(name: string, webkitRelativePath?: string): File {
  const file = new File(['x'], name)
  if (webkitRelativePath !== undefined) {
    Object.defineProperty(file, 'webkitRelativePath', { value: webkitRelativePath })
  }
  return file
}

describe('backupPath', () => {
  describe('relativePathOf', () => {
    it('整夹选择时取 webkitRelativePath', () => {
      const file = fakeFile('a.jpg', ' vacation/DCIM/a.jpg'.trim())
      expect(relativePathOf(file)).toBe('vacation/DCIM/a.jpg')
    })

    it('单文件选择时回落文件名', () => {
      expect(relativePathOf(fakeFile('a.jpg'))).toBe('a.jpg')
    })

    it('反斜杠归一为正斜杠', () => {
      const file = fakeFile('a.jpg', 'DCIM\\Camera\\a.jpg')
      expect(relativePathOf(file)).toBe('DCIM/Camera/a.jpg')
    })
  })

  describe('destinationDirOf', () => {
    it('嵌套相对路径拼到备份目录下', () => {
      expect(destinationDirOf('/Backups/phone', 'DCIM/Camera/a.jpg'))
        .toBe('/Backups/phone/DCIM/Camera')
    })

    it('根级文件的目标目录就是备份目录本身', () => {
      expect(destinationDirOf('/Backups/phone', 'a.jpg')).toBe('/Backups/phone')
    })
  })

  describe('fileNameOf', () => {
    it('取最后一段', () => {
      expect(fileNameOf('DCIM/Camera/a.jpg')).toBe('a.jpg')
    })

    it('无目录段时原样返回', () => {
      expect(fileNameOf('a.jpg')).toBe('a.jpg')
    })
  })
})

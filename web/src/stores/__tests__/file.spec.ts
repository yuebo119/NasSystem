/**
 * file store 测试：目录排序 computed 与选择/排序交互契约。
 *
 * 【为什么必须测这组】sortedFiles 是 Files.vue 列表与网格两个视图的唯一
 * 数据源，它有五条容易互相踩的规则：文件夹永远优先于排序键、隐藏文件
 * 过滤、四种排序键、升降序翻转、以及 computed 不得原地改乱后端返回的
 * items 顺序。视图层没有组件测试兜底，这里一处回归直接打挂文件管理页。
 *
 * loadDirectory/createFolder/rename 只测状态机（成功落库、失败保持、
 * finally 复位 isLoading），api 层用 mock 隔开。
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

vi.mock('@/api/file', () => ({
  fileApi: {
    getDirectory: vi.fn(),
    createFolder: vi.fn(),
    rename: vi.fn(),
    copy: vi.fn(),
    move: vi.fn()
  }
}))

import { fileApi } from '@/api/file'
import { useFileStore } from '@/stores/file'
import type { DirectoryContent, FileItem } from '@/types'

const mockGetDirectory = vi.mocked(fileApi.getDirectory)
const mockCreateFolder = vi.mocked(fileApi.createFolder)
const mockRename = vi.mocked(fileApi.rename)
const mockCopy = vi.mocked(fileApi.copy)
const mockMove = vi.mocked(fileApi.move)

function makeFile(overrides: Partial<FileItem> & { id: string; name: string }): FileItem {
  return {
    path: `/${overrides.name}`,
    itemType: 'File',
    sizeBytes: 0,
    ownerId: 'u1',
    isHidden: false,
    isReadOnly: false,
    isFolder: false,
    isFile: true,
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides
  }
}

function makeDir(items: FileItem[]): DirectoryContent {
  return {
    path: '/',
    name: 'root',
    items,
    folderCount: items.filter(i => i.isFolder).length,
    fileCount: items.filter(i => !i.isFolder).length,
    totalSizeBytes: items.reduce((sum, i) => sum + i.sizeBytes, 0),
    breadcrumbs: []
  }
}

/** 直接把目录内容灌进 store，绕开 api，专注 computed 行为 */
function seedDirectory(store: ReturnType<typeof useFileStore>, items: FileItem[]) {
  store.directoryContent = makeDir(items)
}

function names(items: FileItem[]): string[] {
  return items.map(i => i.name)
}

describe('useFileStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  describe('sortedFiles', () => {
    it('无目录内容时返回空数组', () => {
      const store = useFileStore()
      expect(store.sortedFiles).toEqual([])
    })

    it('文件夹永远排在文件之前，与排序键无关', () => {
      const store = useFileStore()
      seedDirectory(store, [
        makeFile({ id: '1', name: 'a.txt', sizeBytes: 999 }),
        makeFile({ id: '2', name: 'zzz', isFolder: true, isFile: false, itemType: 'Folder', sizeBytes: 1 })
      ])

      // 按 size 排时文件夹 size 更小也仍在前，这是优先级而非排序结果
      store.setSort('size')
      expect(names(store.sortedFiles)).toEqual(['zzz', 'a.txt'])
    })

    it('showHidden=false 过滤隐藏文件，true 时全量返回', () => {
      const store = useFileStore()
      seedDirectory(store, [
        makeFile({ id: '1', name: 'visible.txt' }),
        makeFile({ id: '2', name: '.secret', isHidden: true })
      ])

      expect(names(store.sortedFiles)).toEqual(['visible.txt'])
      store.showHidden = true
      expect(names(store.sortedFiles)).toContain('.secret')
    })

    it('按名称升序排序', () => {
      const store = useFileStore()
      seedDirectory(store, [
        makeFile({ id: '1', name: 'cherry.txt' }),
        makeFile({ id: '2', name: 'apple.txt' }),
        makeFile({ id: '3', name: 'banana.txt' })
      ])

      expect(names(store.sortedFiles)).toEqual(['apple.txt', 'banana.txt', 'cherry.txt'])
    })

    it('按大小排序走数值而非字符串', () => {
      const store = useFileStore()
      seedDirectory(store, [
        makeFile({ id: '1', name: 'big', sizeBytes: 9000 }),
        makeFile({ id: '2', name: 'small', sizeBytes: 100 })
      ])
      store.setSort('size')

      expect(names(store.sortedFiles)).toEqual(['small', 'big'])
    })

    it('按创建时间排序', () => {
      const store = useFileStore()
      seedDirectory(store, [
        makeFile({ id: '1', name: 'newer', createdAt: '2026-06-01T00:00:00Z' }),
        makeFile({ id: '2', name: 'older', createdAt: '2026-01-01T00:00:00Z' })
      ])
      store.setSort('createdAt')

      expect(names(store.sortedFiles)).toEqual(['older', 'newer'])
    })

    it('按类型排序时无扩展名文件排在前', () => {
      const store = useFileStore()
      // 注意：排序读的是 extension 字段而非从文件名截取（与 format.ts 的
      // getExtension 不同），后端不下发 extension 时 type 排序全部相等
      seedDirectory(store, [
        makeFile({ id: '1', name: 'a.txt', extension: '.txt' }),
        makeFile({ id: '2', name: 'README' }),
        makeFile({ id: '3', name: 'b.zip', extension: '.zip' })
      ])
      store.setSort('type')

      // README 无 extension（空串最小），随后 .txt 先于 .zip
      expect(names(store.sortedFiles)).toEqual(['README', 'a.txt', 'b.zip'])
    })

    it('读取 sortedFiles 不会原地改乱后端返回的顺序', () => {
      const store = useFileStore()
      const items = [
        makeFile({ id: '1', name: 'cherry.txt' }),
        makeFile({ id: '2', name: 'apple.txt' })
      ]
      seedDirectory(store, items)

      // 先触发一次排序 computed
      expect(names(store.sortedFiles)).toEqual(['apple.txt', 'cherry.txt'])

      // files 是 directoryContent.items 的直读，必须保持后端原序
      expect(names(store.files)).toEqual(['cherry.txt', 'apple.txt'])
    })
  })

  describe('setSort', () => {
    it('同键再次设置翻转升降序', () => {
      const store = useFileStore()
      expect(store.sortBy).toBe('name')
      expect(store.sortDesc).toBe(false)

      store.setSort('name')
      expect(store.sortDesc).toBe(true)

      store.setSort('name')
      expect(store.sortDesc).toBe(false)
    })

    it('切换新键时重置为升序', () => {
      const store = useFileStore()
      store.setSort('name') // 翻成降序
      expect(store.sortDesc).toBe(true)

      store.setSort('size')
      expect(store.sortBy).toBe('size')
      expect(store.sortDesc).toBe(false)
    })
  })

  describe('选择管理', () => {
    it('单选替换当前选择', () => {
      const store = useFileStore()
      const a = makeFile({ id: '1', name: 'a.txt' })
      const b = makeFile({ id: '2', name: 'b.txt' })

      store.selectItem(a)
      store.selectItem(b)

      expect(store.selectedItems).toEqual([b])
      expect(store.selectionCount).toBe(1)
      expect(store.hasSelection).toBe(true)
    })

    it('多选累积，重复点击同一项取消', () => {
      const store = useFileStore()
      const a = makeFile({ id: '1', name: 'a.txt' })
      const b = makeFile({ id: '2', name: 'b.txt' })

      store.selectItem(a, true)
      store.selectItem(b, true)
      expect(store.selectionCount).toBe(2)

      store.selectItem(a, true)
      expect(store.selectedItems.map(i => i.id)).toEqual(['2'])
    })

    it('clearSelection 清空且 hasSelection 归假', () => {
      const store = useFileStore()
      store.selectItem(makeFile({ id: '1', name: 'a.txt' }), true)

      store.clearSelection()

      expect(store.selectedItems).toEqual([])
      expect(store.hasSelection).toBe(false)
    })
  })

  describe('toggleViewMode', () => {
    it('在 list 与 grid 间翻转', () => {
      const store = useFileStore()
      expect(store.viewMode).toBe('list')

      store.toggleViewMode()
      expect(store.viewMode).toBe('grid')

      store.toggleViewMode()
      expect(store.viewMode).toBe('list')
    })
  })

  describe('loadDirectory', () => {
    it('成功时更新路径与内容并清空选择', async () => {
      const items = [makeFile({ id: '1', name: 'a.txt' })]
      mockGetDirectory.mockResolvedValue({ success: true, data: makeDir(items), timestamp: '' })
      const store = useFileStore()
      store.selectItem(makeFile({ id: 'x', name: 'old.txt' }), true)

      await store.loadDirectory('/docs')

      expect(store.currentPath).toBe('/docs')
      expect(store.files).toHaveLength(1)
      expect(store.selectedItems).toEqual([])
      expect(store.isLoading).toBe(false)
    })

    it('接口失败时保持原有内容，isLoading 仍复位', async () => {
      mockGetDirectory.mockResolvedValue({ success: false, timestamp: '' })
      const store = useFileStore()
      seedDirectory(store, [makeFile({ id: '1', name: 'keep.txt' })])

      await store.loadDirectory('/docs')

      expect(store.currentPath).toBe('/')
      expect(names(store.files)).toEqual(['keep.txt'])
      expect(store.isLoading).toBe(false)
    })

    it('接口抛异常时 isLoading 经 finally 复位', async () => {
      mockGetDirectory.mockRejectedValue(new Error('offline'))
      const store = useFileStore()

      await expect(store.loadDirectory('/docs')).rejects.toThrow('offline')

      expect(store.isLoading).toBe(false)
    })
  })

  describe('createFolder / rename', () => {
    it('createFolder 成功后重新加载当前目录', async () => {
      mockCreateFolder.mockResolvedValue({ success: true, timestamp: '' })
      mockGetDirectory.mockResolvedValue({ success: true, data: makeDir([]), timestamp: '' })
      const store = useFileStore()
      store.currentPath = '/docs'

      await store.createFolder('new')

      expect(mockCreateFolder).toHaveBeenCalledWith('/docs', 'new')
      expect(mockGetDirectory).toHaveBeenCalledWith('/docs', false)
    })

    it('createFolder 失败时不触发重载', async () => {
      mockCreateFolder.mockResolvedValue({ success: false, timestamp: '' })
      const store = useFileStore()

      await store.createFolder('new')

      expect(mockGetDirectory).not.toHaveBeenCalled()
    })

    it('rename 成功后用文件路径调用并重载', async () => {
      mockRename.mockResolvedValue({ success: true, timestamp: '' })
      mockGetDirectory.mockResolvedValue({ success: true, data: makeDir([]), timestamp: '' })
      const store = useFileStore()

      await store.rename(makeFile({ id: '1', name: 'old.txt', path: '/docs/old.txt' }), 'new.txt')

      expect(mockRename).toHaveBeenCalledWith('/docs/old.txt', 'new.txt')
      expect(mockGetDirectory).toHaveBeenCalled()
    })
  })

  // 剪贴板自 Files.vue 内迁入：这三测的是原视图层逻辑的契约副本
  describe('剪贴板', () => {
    beforeEach(() => {
      // paste 末尾会重载目录，给 getDirectory 一个默认成功响应
      mockGetDirectory.mockResolvedValue({ success: true, data: makeDir([]), timestamp: '' })
    })

    it('无选择时 setClipboard 返回 false 且不设剪贴板', () => {
      const store = useFileStore()

      expect(store.setClipboard('copy')).toBe(false)
      expect(store.clipboard).toBeNull()
    })

    it('存入的是选择副本，后续选择变化不改写剪贴板', () => {
      const store = useFileStore()
      const a = makeFile({ id: '1', name: 'a.txt' })
      store.selectItem(a, true)

      store.setClipboard('copy')
      store.selectItem(makeFile({ id: '2', name: 'b.txt' }), true)

      expect(store.clipboard?.items.map(i => i.id)).toEqual(['1'])
    })

    it('无剪贴板时 pasteClipboard 返回 null，不发请求', async () => {
      const store = useFileStore()

      expect(await store.pasteClipboard()).toBeNull()
      expect(mockCopy).not.toHaveBeenCalled()
      expect(mockMove).not.toHaveBeenCalled()
    })

    it('copy 全量成功：路径数组一次请求，剪贴板保留，目录重载', async () => {
      mockCopy.mockResolvedValue({ success: true, data: [], timestamp: '' })
      const store = useFileStore()
      store.selectItem(makeFile({ id: '1', name: 'a.txt', path: '/a.txt' }), true)
      store.selectItem(makeFile({ id: '2', name: 'b.txt', path: '/b.txt' }), true)
      store.currentPath = '/docs'
      store.setClipboard('copy')

      const result = await store.pasteClipboard()

      expect(mockCopy).toHaveBeenCalledWith(['/a.txt', '/b.txt'], '/docs')
      expect(result).toEqual({ ok: 2, failed: 0, action: 'copy' })
      expect(store.clipboard).not.toBeNull()
      expect(mockGetDirectory).toHaveBeenCalledWith('/docs', false)
    })

    it('cut 成功后清空剪贴板（移动即搬走），走 move 请求', async () => {
      mockMove.mockResolvedValue({ success: true, data: [], timestamp: '' })
      const store = useFileStore()
      store.selectItem(makeFile({ id: '1', name: 'a.txt', path: '/a.txt' }), true)
      store.setClipboard('cut')

      const result = await store.pasteClipboard()

      expect(mockMove).toHaveBeenCalledWith(['/a.txt'], '/')
      expect(result).toEqual({ ok: 1, failed: 0, action: 'cut' })
      expect(store.clipboard).toBeNull()
    })

    it('cut 部分失败也清空剪贴板（清空与成败无关）', async () => {
      mockMove.mockResolvedValue({ success: true, data: { failedItems: ['/a.txt'] } as never, timestamp: '' })
      const store = useFileStore()
      store.selectItem(makeFile({ id: '1', name: 'a.txt', path: '/a.txt' }), true)
      store.setClipboard('cut')

      const result = await store.pasteClipboard()

      expect(result).toEqual({ ok: 0, failed: 1, action: 'cut' })
      expect(store.clipboard).toBeNull()
    })

    it('copy 部分失败按 failedItems 计数，剪贴板保留供重试', async () => {
      mockCopy.mockResolvedValue({ success: true, data: { failedItems: ['/b.txt'] } as never, timestamp: '' })
      const store = useFileStore()
      store.selectItem(makeFile({ id: '1', name: 'a.txt', path: '/a.txt' }), true)
      store.selectItem(makeFile({ id: '2', name: 'b.txt', path: '/b.txt' }), true)
      store.setClipboard('copy')

      const result = await store.pasteClipboard()

      expect(result).toEqual({ ok: 1, failed: 1, action: 'copy' })
      expect(store.clipboard?.items).toHaveLength(2)
    })

    it('接口抛异常时全部计为失败，目录仍重载', async () => {
      mockCopy.mockRejectedValue(new Error('offline'))
      const store = useFileStore()
      store.selectItem(makeFile({ id: '1', name: 'a.txt', path: '/a.txt' }), true)
      store.selectItem(makeFile({ id: '2', name: 'b.txt', path: '/b.txt' }), true)
      store.setClipboard('copy')

      const result = await store.pasteClipboard()

      expect(result).toEqual({ ok: 0, failed: 2, action: 'copy' })
      expect(mockGetDirectory).toHaveBeenCalled()
    })

    it('clearClipboard 清空剪贴板', () => {
      const store = useFileStore()
      store.selectItem(makeFile({ id: '1', name: 'a.txt' }), true)
      store.setClipboard('copy')

      store.clearClipboard()

      expect(store.clipboard).toBeNull()
    })
  })
})

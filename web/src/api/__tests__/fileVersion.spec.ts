/**
 * fileVersionApi 客户端契约测试。
 *
 * 【为什么必须测这组】版本 UI（Files.vue 的版本抽屉）的恢复/删除/下载
 * 都经这四个方法出站。URL 形一旦漂移（比如漏了 restore 段、下载地址
 * 少了 versionNumber），症状是抽屉里点了没反应或 404，而视图层测试
 * mock 掉 api 模块后根本看不见。这里锁住"哪个动作打哪个端点"。
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    delete: vi.fn()
  }
}))

import http from '@/api/http'
import { fileVersionApi } from '@/api/fileVersion'

const mockGet = vi.mocked(http.get)
const mockPost = vi.mocked(http.post)
const mockDelete = vi.mocked(http.delete)

describe('fileVersionApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('getVersions 命中 /file-versions/{id}', async () => {
    mockGet.mockResolvedValue({ success: true, data: [] } as never)

    await fileVersionApi.getVersions('file-1')

    expect(mockGet).toHaveBeenCalledWith('/file-versions/file-1')
  })

  it('restore 用 POST 命中 /file-versions/{id}/{version}/restore', async () => {
    mockPost.mockResolvedValue({ success: true } as never)

    await fileVersionApi.restore('file-1', 3)

    expect(mockPost).toHaveBeenCalledWith('/file-versions/file-1/3/restore')
  })

  it('delete 用 DELETE 命中 /file-versions/{id}/{version}', async () => {
    mockDelete.mockResolvedValue({ success: true } as never)

    await fileVersionApi.delete('file-1', 2)

    expect(mockDelete).toHaveBeenCalledWith('/file-versions/file-1/2')
  })

  it('下载 URL 带 /api/v1 前缀与版本号段', () => {
    const url = fileVersionApi.getVersionDownloadUrl('file-1', 5)

    expect(url).toBe('/api/v1/file-versions/file-1/5/download')
  })
})

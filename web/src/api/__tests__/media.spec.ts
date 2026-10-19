/**
 * mediaApi 客户端契约测试（ROADMAP Phase 3）。
 *
 * 锁住端点的 URL 形与参数传递：列表带 type/月份/分页查询串，
 * 元数据是 POST body 的 paths 数组，转码缓存清理是 DELETE。
 * URL 形漂移的症状是媒体库整页空白或按钮 404/405，
 * 而视图层 mock 掉 api 模块后看不见
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
import { mediaApi } from '@/api/media'

const mockGet = vi.mocked(http.get)
const mockPost = vi.mocked(http.post)
const mockDelete = vi.mocked(http.delete)

describe('mediaApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('getList 带 type 与分页查询串', async () => {
    mockGet.mockResolvedValue({ success: true, data: { items: [], totalCount: 0 } } as never)

    await mediaApi.getList('image', { page: 2, pageSize: 60 })

    expect(mockGet).toHaveBeenCalledWith('/media', {
      params: { type: 'image', page: 2, pageSize: 60 }
    })
  })

  it('getList 不带可选参数时只传 type', async () => {
    mockGet.mockResolvedValue({ success: true, data: { items: [], totalCount: 0 } } as never)

    await mediaApi.getList('video')

    expect(mockGet).toHaveBeenCalledWith('/media', { params: { type: 'video' } })
  })

  it('getMetadata 用 POST 传 paths 数组', async () => {
    mockPost.mockResolvedValue({ success: true, data: [] } as never)

    await mediaApi.getMetadata(['/a.jpg', '/b.png'])

    expect(mockPost).toHaveBeenCalledWith('/media/metadata', { paths: ['/a.jpg', '/b.png'] })
  })

  it('clearTranscodeCache 用 DELETE 打 /media/stream/cache', async () => {
    mockDelete.mockResolvedValue({ success: true, data: { freedBytes: 1024 } } as never)

    await mediaApi.clearTranscodeCache()

    // 无路径段才是全局清理（管理员）；带路径会落到 HLS 分片寻址上
    expect(mockDelete).toHaveBeenCalledWith('/media/stream/cache')
  })
})

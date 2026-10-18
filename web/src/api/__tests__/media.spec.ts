/**
 * mediaApi 客户端契约测试（ROADMAP Phase 3）。
 *
 * 锁住两个端点的 URL 形与参数传递：列表带 type/月份/分页查询串，
 * 元数据是 POST body 的 paths 数组。URL 形漂移的症状是媒体库
 * 整页空白，而视图层 mock 掉 api 模块后看不见
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn()
  }
}))

import http from '@/api/http'
import { mediaApi } from '@/api/media'

const mockGet = vi.mocked(http.get)
const mockPost = vi.mocked(http.post)

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
})

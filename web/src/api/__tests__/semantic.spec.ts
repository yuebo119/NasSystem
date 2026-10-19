/**
 * semanticApi 客户端契约测试（手动触发建索引入口）。
 *
 * 锁住 triggerIndex 的 URL 与动词：POST 无 body 打
 * /semantic/index/trigger。漂移的症状是配置页"立即建索引"
 * 按钮 404/405，而按钮有 loading 态容易被当成网络慢
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
import { semanticApi } from '@/api/semantic'

const mockPost = vi.mocked(http.post)

describe('semanticApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('triggerIndex 用 POST 打 /semantic/index/trigger', async () => {
    mockPost.mockResolvedValue({ success: true } as never)

    await semanticApi.triggerIndex()

    expect(mockPost).toHaveBeenCalledWith('/semantic/index/trigger')
  })
})

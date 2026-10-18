/**
 * systemControlApi + thumbnailApi 客户端契约测试（前端入口收口）。
 *
 * 锁住四个端点的 URL 与动词：重启/关机是 POST 无 body，
 * 缩略图清理是 DELETE 带编码路径。动词或 URL 漂移的症状是
 * 点了按钮 404/405，而按钮自己有 loading 态容易被当成网络慢
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
import { systemControlApi } from '@/api/systemControl'
import { thumbnailApi } from '@/api/thumbnail'

const mockPost = vi.mocked(http.post)
const mockDelete = vi.mocked(http.delete)

describe('systemControlApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('restart 用 POST 打 /system/restart', async () => {
    mockPost.mockResolvedValue({ success: true } as never)

    await systemControlApi.restart()

    expect(mockPost).toHaveBeenCalledWith('/system/restart')
  })

  it('shutdown 用 POST 打 /system/shutdown', async () => {
    mockPost.mockResolvedValue({ success: true } as never)

    await systemControlApi.shutdown()

    expect(mockPost).toHaveBeenCalledWith('/system/shutdown')
  })
})

describe('thumbnailApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('clearCache 用 DELETE 打编码后的 /thumbnails/cache/{path}', async () => {
    mockDelete.mockResolvedValue({ success: true } as never)

    await thumbnailApi.clearCache('/photos/a b.jpg')

    expect(mockDelete).toHaveBeenCalledWith('/thumbnails/cache/photos/a b.jpg')
  })

  it('clearAllCache 用 DELETE 打 /thumbnails/cache（无路径段才是全局清理）', async () => {
    mockDelete.mockResolvedValue({ success: true } as never)

    await thumbnailApi.clearAllCache()

    // 与单文件清理的区分就在路径段：少了它后端按全局处理（管理员）
    expect(mockDelete).toHaveBeenCalledWith('/thumbnails/cache')
  })
})

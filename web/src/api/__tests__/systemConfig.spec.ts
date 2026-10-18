/**
 * systemConfigApi 客户端契约测试（配置页）。
 *
 * 锁住两个端点的 URL 与载荷形：GET 无参、PUT 是 { values } 包装。
 * 载荷形漂移的症状是保存静默失败（后端绑不到 values），
 * 而配置页的保存反馈来自响应 success，容易被误判为网络问题
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    put: vi.fn()
  }
}))

import http from '@/api/http'
import { systemConfigApi } from '@/api/systemConfig'

const mockGet = vi.mocked(http.get)
const mockPut = vi.mocked(http.put)

describe('systemConfigApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('get 命中 /system/config', async () => {
    mockGet.mockResolvedValue({ success: true, data: { groups: [] } } as never)

    await systemConfigApi.get()

    expect(mockGet).toHaveBeenCalledWith('/system/config')
  })

  it('update 用 PUT 传 { values } 包装', async () => {
    mockPut.mockResolvedValue({ success: true } as never)

    await systemConfigApi.update({ 'webdav.enabled': 'false' })

    // 后端 UpdateConfigRequest.Values 按这个名字绑定：
    // 少了包装层键名漂移时 400 且消息还是"请求体为空"这类误导信息
    expect(mockPut).toHaveBeenCalledWith('/system/config', {
      values: { 'webdav.enabled': 'false' }
    })
  })
})

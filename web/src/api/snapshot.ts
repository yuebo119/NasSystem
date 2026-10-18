import http from './http'
import type { ApiResponse, PagedResult, Snapshot, SnapshotEntry, SnapshotDiff, ProtectedDirectory } from '@/types'

export const snapshotApi = {
  // 创建快照（同步采集：库内查询 + 批量写，毫秒到秒级）
  async create(rootPath: string, name?: string): Promise<ApiResponse<Snapshot>> {
    return http.post('/snapshots', { rootPath, name })
  },

  // 快照列表（倒序分页）
  async getList(page = 1, pageSize = 20): Promise<ApiResponse<PagedResult<Snapshot>>> {
    return http.get('/snapshots', { params: { page, pageSize } })
  },

  // 快照详情
  async get(id: string): Promise<ApiResponse<Snapshot>> {
    return http.get(`/snapshots/${id}`)
  },

  // 快照条目（按相对路径排序分页）
  async getEntries(id: string, page = 1, pageSize = 100): Promise<ApiResponse<PagedResult<SnapshotEntry>>> {
    return http.get(`/snapshots/${id}/entries`, { params: { page, pageSize } })
  },

  // 对比两个快照（base=旧，target=新）
  async diff(
    baseSnapshotId: string,
    targetSnapshotId: string,
    page = 1,
    pageSize = 200
  ): Promise<ApiResponse<SnapshotDiff>> {
    return http.get('/snapshots/diff', {
      params: { baseSnapshotId, targetSnapshotId, page, pageSize }
    })
  },

  // 删除快照（连带条目）
  async remove(id: string): Promise<ApiResponse<null>> {
    return http.delete(`/snapshots/${id}`)
  }
}

export const protectedDirectoryApi = {
  // 标记目录为只读保护（幂等）
  async mark(fileItemId: string, note?: string): Promise<ApiResponse<ProtectedDirectory>> {
    return http.post('/protected-directories', { fileItemId, note })
  },

  // 受保护目录列表
  async list(): Promise<ApiResponse<ProtectedDirectory[]>> {
    return http.get('/protected-directories')
  },

  // 取消保护
  async unmark(fileItemId: string): Promise<ApiResponse<null>> {
    return http.delete(`/protected-directories/${fileItemId}`)
  }
}

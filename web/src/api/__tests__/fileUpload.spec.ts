/**
 * fileApi.upload 的 overwrite 参数契约（2026-10-21 E2E 旅程实证的流程断裂回归）。
 *
 * 【为什么必须测这组】E2E 旅程发现：覆盖上传在 UI 上必然 409——后端
 * /files/upload 支持 overwrite 字段（true 时走替换路径并触发自动版本，
 * 是版本功能的唯一触发点），但前端 upload() 从不发送它。修后这条锁：
 * ① overwrite 值如实进入 FormData；② 字段顺序 overwrite 先于 file
 *（后端 MultipartReader 流式解析，file part 的流出现时就要立即消费，
 * 拿不到后面的字段——顺序错则 overwrite 恒为默认 false，功能静默失效）
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'

const httpPost = vi.hoisted(() => vi.fn())
vi.mock('@/api/http', () => ({
  default: { post: httpPost }
}))

import { fileApi } from '@/api/file'

/** 从 FormData 取键序与值：顺序是本次断言的关键 */
function formDataSnapshot(fd: FormData) {
  return [...fd.keys()].map(key => `${key}=${String(fd.get(key))}`)
}

describe('fileApi.upload', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    httpPost.mockResolvedValue({ success: true, data: {}, timestamp: '' })
  })

  it('缺省不覆盖：overwrite=false 且先于 file', async () => {
    await fileApi.upload(new File(['x'], 'a.txt'), '/docs')

    const formData = httpPost.mock.calls[0][1] as FormData
    const snapshot = formDataSnapshot(formData)
    expect(snapshot).toContain('overwrite=false')
    expect(snapshot.indexOf('overwrite=false')).toBeLessThan(snapshot.findIndex(s => s.startsWith('file=')))
  })

  it('显式覆盖：overwrite=true 且先于 file', async () => {
    await fileApi.upload(new File(['x'], 'a.txt'), '/docs', undefined, true)

    const formData = httpPost.mock.calls[0][1] as FormData
    const snapshot = formDataSnapshot(formData)
    expect(snapshot).toContain('overwrite=true')
    expect(snapshot.indexOf('overwrite=true')).toBeLessThan(snapshot.findIndex(s => s.startsWith('file=')))
  })

  it('path 与 overwrite 都先于 file（后端流式解析的字段顺序契约）', async () => {
    await fileApi.upload(new File(['x'], 'a.txt'), '/docs', undefined, true)

    const formData = httpPost.mock.calls[0][1] as FormData
    const keys = [...formData.keys()]
    expect(keys.indexOf('path')).toBeLessThan(keys.indexOf('file'))
    expect(keys.indexOf('overwrite')).toBeLessThan(keys.indexOf('file'))
  })
})

/**
 * 预览用 objectURL 的创建与释放配对管理。
 *
 * 【为什么单独抽一个模块】Files.vue 的媒体预览原先把 objectURL 存在
 * `Record<path, url>` 里只增不减：关闭对话框不清、组件卸载不 revoke、
 * 同一文件重复预览还会重新 fetch。objectURL 指向的 blob 常驻内存，
 * 看几个视频就等于把几个视频的体积钉在堆里，且源数据被 map 强引用无法回收。
 *
 * 抽成模块的两个理由：
 * 1. 单槽语义（同时只保留一个 URL）不再依赖调用方记得清理，换文件即释放上一个；
 * 2. create/revoke 的配对关系可以用纯单元测试锁住，不依赖组件挂载
 *    （本项目没有 @vue/test-utils，视图层无组件测试兜底）。
 *
 * 故意不做多文件缓存：预览是"看一个"的交互，同时缓存多个只会放大内存峰值，
 * 而重复进入同一文件的成本是一次 fetch，远小于常驻 blob 的代价。
 */

export interface PreviewUrlSlot {
  path: string
  url: string
}

export interface PreviewUrlManager {
  /** 当前槽位，未创建时为 null */
  current: () => PreviewUrlSlot | null
  /** 取某个文件的 URL，仅当槽位正是该文件时返回，否则 undefined */
  urlFor: (path: string) => string | undefined
  /** 是否已缓存某个文件 */
  has: (path: string) => boolean
  /** 存入新 URL，存入前释放上一个（单槽语义） */
  put: (path: string, url: string) => void  /** 释放当前槽位 */
  release: () => void
}

/**
 * 创建管理器。revokeUrl 可注入，测试里用记录型替身验证 create/revoke 配对。
 * createUrl 不注入：创建由调用方用自己的 createObjectURL 完成后 put 进来，
 * 管理器只负责"何时释放"这一件事。
 */
export function createPreviewUrlManager(
  revokeUrl: (url: string) => void
): PreviewUrlManager {
  let slot: PreviewUrlSlot | null = null

  return {
    current: () => slot,

    urlFor: (path) => (slot?.path === path ? slot.url : undefined),

    has: (path) => slot?.path === path,

    put: (path, url) => {
      // 换文件预览前先释放上一个，避免同时钉住多个 blob
      if (slot) revokeUrl(slot.url)
      slot = { path, url }
    },

    release: () => {
      if (slot) {
        revokeUrl(slot.url)
        slot = null
      }
    }
  }
}

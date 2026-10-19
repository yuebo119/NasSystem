<script setup lang="ts">
/**
 * 带认证的缩略图图片。
 *
 * 为什么需要它：缩略图端点要 Bearer 认证，裸 <img src> 带不上
 * Authorization 头，整站 401——媒体库的缩略图全碎（2026-10-19 E2E
 * 走查实证：5 张图 5 个 401，只剩 alt 文字）。改 fetch 拿 blob 转
 * 对象 URL；路径切换与组件卸载时 revoke，防连续翻页堆对象 URL。
 */
import { ref, watch, onUnmounted } from 'vue'
import { Picture } from '@element-plus/icons-vue'

const props = defineProps<{
  /** 文件虚拟路径（/ 开头） */
  path: string
  /** 缩略图边长，默认 300 */
  size?: number
}>()

const objectUrl = ref('')
const failed = ref(false)
let currentUrl = ''

async function load() {
  // 先 revoke 上一张：路径连续变化时（翻页/切 tab）不堆对象 URL
  if (currentUrl) {
    URL.revokeObjectURL(currentUrl)
    currentUrl = ''
  }
  objectUrl.value = ''
  failed.value = false

  const encodedPath = props.path.replace(/^\//, '')
  const size = props.size ?? 300
  try {
    const token = localStorage.getItem('token')
    const response = await fetch(
      `/api/v1/thumbnails/${encodedPath}?width=${size}&height=${size}`,
      { headers: token ? { Authorization: `Bearer ${token}` } : {} }
    )
    if (!response.ok) throw new Error(String(response.status))
    const blob = await response.blob()
    currentUrl = URL.createObjectURL(blob)
    objectUrl.value = currentUrl
  } catch {
    // 单张失败不阻断整页：落占位图标（生成中/格式不支持/权限问题）
    failed.value = true
  }
}

watch(() => [props.path, props.size], () => void load(), { immediate: true })

onUnmounted(() => {
  if (currentUrl) URL.revokeObjectURL(currentUrl)
})
</script>

<template>
  <img v-if="objectUrl" :src="objectUrl" loading="lazy" :alt="path.split('/').pop() ?? path" />
  <el-icon v-else-if="failed" :size="32" class="auth-image-fallback"><Picture /></el-icon>
</template>

<style scoped>
.auth-image-fallback {
  color: var(--el-text-color-placeholder);
}
</style>

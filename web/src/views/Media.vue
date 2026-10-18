<script setup lang="ts">
/**
 * 媒体库页（ROADMAP Phase 3）
 *
 * 时间线视图：按创建月份分节的网格。列表与元数据分离——
 * 列表纯数据库（滚动不触发解码），元数据按页批量取一次
 * （解析盒/帧头毫秒级）。图片格用现有缩略图端点，
 * 视频/音频显示时长徽章（时长来自托管解析，取不到不显示）。
 */
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Picture, VideoCamera, Headset } from '@element-plus/icons-vue'
import { mediaApi, type MediaType } from '@/api/media'
import { fileApi } from '@/api/file'
import type { MediaItem, MediaMetadata } from '@/types'
import { formatSize } from '@/utils/format'
import { useIsMobile } from '@/utils/useIsMobile'

const { t } = useI18n()
const router = useRouter()
const isMobile = useIsMobile()

const activeType = ref<MediaType>('image')
const items = ref<MediaItem[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = 60
const loading = ref(false)
// 元数据按 path 索引：批量接口一次一页，命中后不再重复请求
const metadataMap = ref<Map<string, MediaMetadata>>(new Map())

const hasMore = computed(() => items.value.length < total.value)

// 月份分组：保持 CreatedAt DESC 的顺序，同月的连续条目归一节
const groups = computed(() => {
  const result: { month: string; label: string; items: MediaItem[] }[] = []
  for (const item of items.value) {
    const last = result[result.length - 1]
    if (last && last.month === item.dateGroup) {
      last.items.push(item)
    } else {
      result.push({ month: item.dateGroup, label: item.dateGroup, items: [item] })
    }
  }
  return result
})

watch(activeType, () => {
  page.value = 1
  items.value = []
  metadataMap.value = new Map()
  void loadMore(true)
})

async function loadMore(reset = false) {
  if (loading.value) return
  if (!reset && !hasMore.value) return
  loading.value = true
  try {
    const result = await mediaApi.getList(activeType.value, { page: page.value, pageSize })
    if (result.success && result.data) {
      items.value = reset ? result.data.items : [...items.value, ...result.data.items]
      total.value = result.data.totalCount
      page.value++
      void loadMetadata(items.value)
    } else {
      ElMessage.error(result.error?.message || t('media.loadFailed'))
    }
  } catch {
    ElMessage.error(t('media.loadFailed'))
  } finally {
    loading.value = false
  }
}

// 批量取当前 items 的元数据（只请求 map 里还没有的路径）
async function loadMetadata(current: MediaItem[]) {
  const pending = current
    .filter(item => !metadataMap.value.has(item.path))
    .map(item => item.path)
  if (pending.length === 0) return
  try {
    const result = await mediaApi.getMetadata(pending)
    if (result.success && result.data) {
      for (const entry of result.data) {
        metadataMap.value.set(entry.path, entry)
      }
    }
  } catch {
    // 元数据失败不阻断浏览：徽章不显示而已
  }
}

function metaOf(item: MediaItem): MediaMetadata | undefined {
  return metadataMap.value.get(item.path)
}

// 时长格式化：秒 → mm:ss / h:mm:ss
function formatDuration(seconds?: number): string {
  if (!seconds || seconds <= 0) return ''
  const total = Math.round(seconds)
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  const pad = (n: number) => String(n).padStart(2, '0')
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`
}

// 尺寸徽章：图片 宽x高，视频/音频 时长
function badgeOf(item: MediaItem): string {
  const meta = metaOf(item)
  if (item.mediaType === 'image') {
    return meta?.width && meta?.height ? `${meta.width}×${meta.height}` : ''
  }
  return formatDuration(meta?.durationSeconds)
}

// 点击进文件管理器对应位置（复用现有浏览/预览能力，不为媒体库造第二套预览）
function openInFiles(item: MediaItem) {
  const dir = item.path.slice(0, item.path.lastIndexOf('/')) || '/'
  void router.push(`/files${dir}`)
}

const typeTabs: { key: MediaType; labelKey: string; icon: typeof Picture }[] = [
  { key: 'image', labelKey: 'media.tabImage', icon: Picture },
  { key: 'video', labelKey: 'media.tabVideo', icon: VideoCamera },
  { key: 'audio', labelKey: 'media.tabAudio', icon: Headset }
]
</script>

<template>
  <div class="media-page">
    <div class="media-header">
      <h2 class="page-title">{{ $t('media.title') }}</h2>
      <el-radio-group v-model="activeType" size="large">
        <el-radio-button v-for="tab in typeTabs" :key="tab.key" :value="tab.key">
          <el-icon style="vertical-align: -2px; margin-right: 4px"><component :is="tab.icon" /></el-icon>
          {{ $t(tab.labelKey) }}
        </el-radio-button>
      </el-radio-group>
    </div>

    <div v-loading="loading" class="media-body">
      <template v-if="groups.length > 0">
        <div v-for="group in groups" :key="group.month" class="month-group">
          <div class="month-title">{{ group.label }}</div>
          <div class="media-grid" :class="{ mobile: isMobile }">
            <div
              v-for="item in group.items"
              :key="item.id"
              class="media-cell"
              @click="openInFiles(item)"
            >
              <div class="cell-thumb">
                <img
                  v-if="item.mediaType === 'image'"
                  :src="fileApi.getThumbnailUrl(item.path, 300)"
                  loading="lazy"
                  :alt="item.name"
                />
                <el-icon v-else :size="40" class="cell-icon">
                  <VideoCamera v-if="item.mediaType === 'video'" />
                  <Headset v-else />
                </el-icon>
                <span v-if="badgeOf(item)" class="cell-badge">{{ badgeOf(item) }}</span>
              </div>
              <div class="cell-name" :title="item.name">{{ item.name }}</div>
              <div class="cell-size">{{ formatSize(item.sizeBytes) }}</div>
            </div>
          </div>
        </div>
        <div class="load-more" v-if="hasMore">
          <el-button :loading="loading" @click="loadMore()">{{ $t('media.loadMore') }}</el-button>
        </div>
      </template>
      <el-empty v-else-if="!loading" :description="$t('media.empty')" />
    </div>
  </div>
</template>

<style lang="scss" scoped>
.media-page {
  padding: 16px;

  .media-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    flex-wrap: wrap;
    gap: 12px;
    margin-bottom: 16px;

    .page-title {
      font-size: 20px;
      font-weight: 600;
      margin: 0;
      color: var(--el-text-color-primary);
    }
  }

  .month-group {
    margin-bottom: 24px;

    .month-title {
      font-size: 14px;
      font-weight: 600;
      color: var(--el-text-color-secondary);
      margin-bottom: 12px;
    }
  }

  .media-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
    gap: 12px;

    &.mobile {
      grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
      gap: 8px;
    }
  }

  .media-cell {
    cursor: pointer;
    border-radius: 10px;
    overflow: hidden;
    border: 1px solid var(--el-border-color-lighter);
    background: var(--el-fill-color-blank);
    transition: transform 0.15s ease, box-shadow 0.15s ease;

    &:hover {
      transform: translateY(-2px);
      box-shadow: var(--el-box-shadow-light);
    }

    .cell-thumb {
      position: relative;
      aspect-ratio: 1;
      display: flex;
      align-items: center;
      justify-content: center;
      background: var(--el-fill-color-light);

      img {
        width: 100%;
        height: 100%;
        object-fit: cover;
      }

      .cell-icon {
        color: var(--el-text-color-secondary);
      }

      .cell-badge {
        position: absolute;
        right: 6px;
        bottom: 6px;
        padding: 1px 6px;
        border-radius: 4px;
        font-size: 11px;
        color: #fff;
        background: rgba(0, 0, 0, 0.6);
      }
    }

    .cell-name {
      padding: 8px 8px 0;
      font-size: 13px;
      color: var(--el-text-color-primary);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .cell-size {
      padding: 2px 8px 8px;
      font-size: 12px;
      color: var(--el-text-color-secondary);
    }
  }

  .load-more {
    text-align: center;
    margin: 20px 0;
  }
}
</style>

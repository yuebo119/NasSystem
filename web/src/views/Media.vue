<script setup lang="ts">
/**
 * 媒体库页（ROADMAP Phase 3）
 *
 * 时间线视图：按创建月份分节的网格。列表与元数据分离——
 * 列表纯数据库（滚动不触发解码），元数据按页批量取一次
 * （解析盒/帧头毫秒级）。图片格用现有缩略图端点，
 * 视频/音频显示时长徽章（时长来自托管解析，取不到不显示）。
 */
import { ref, computed, watch, nextTick } from 'vue'
import Hls from 'hls.js'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Picture, VideoCamera, Headset, Loading, Download } from '@element-plus/icons-vue'
import { mediaApi, type MediaType } from '@/api/media'
import { semanticApi, type SemanticHit } from '@/api/semantic'
import { ocrApi } from '@/api/ocr'
import AuthImage from '@/components/AuthImage.vue'
import OcrImageViewer from '@/components/OcrImageViewer.vue'
import type { MediaItem, MediaMetadata, OcrText } from '@/types'
import { formatSize } from '@/utils/format'
import { useIsMobile, useDrawerSize } from '@/utils/useIsMobile'

const { t } = useI18n()
const router = useRouter()
const isMobile = useIsMobile()
const drawerSize = useDrawerSize()

const activeType = ref<MediaType>('image')
const items = ref<MediaItem[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = 60
const loading = ref(false)
// 元数据按 path 索引：批量接口一次一页，命中后不再重复请求
const metadataMap = ref<Map<string, MediaMetadata>>(new Map())

// 语义搜图（ROADMAP Phase 2）：仅图片 tab 可用。命中结果转 MediaItem
// 形态走同一网格渲染（openInFiles 复用）；模型未就位时后端明确报错，
// 这里把错误提示为"请先到设置页下载模型"
const semanticQuery = ref('')
const semanticHits = ref<SemanticHit[]>([])
const semanticSearching = ref(false)
const semanticActive = computed(() => semanticHits.value.length > 0)

// 语义模式下网格数据源：命中按距离升序（后端口径）平铺展示
const semanticItems = computed<MediaItem[]>(() =>
  semanticHits.value.map(h => ({
    id: h.id,
    name: h.name,
    path: h.path,
    extension: h.path.includes('.') ? '.' + h.path.split('.').pop() : '',
    sizeBytes: h.sizeBytes,
    mediaType: 'image' as const,
    dateGroup: '',
    createdAt: ''
  }))
)

// 视频播放（ROADMAP Phase 3）：点击视频格打开 HLS 播放对话框。
// hls.js 统一路径（不走 video.src 原生 HLS）：原生路径的请求不带
// Authorization header，Safari 下会 401。hls.js 的 xhrSetup 给 m3u8
// 与分片请求都带上 token
const playerVisible = ref(false)
const playingItem = ref<MediaItem | null>(null)
const videoEl = ref<HTMLVideoElement | null>(null)
let hlsInstance: Hls | null = null

function playVideo(item: MediaItem) {
  playingItem.value = item
  playerVisible.value = true
  void nextTick(mountPlayer)
}

function mountPlayer() {
  const video = videoEl.value
  if (!video || !playingItem.value) return
  destroyPlayer()

  const token = localStorage.getItem('token')
  const url = `/api/v1/media/stream/${playingItem.value.path.replace(/^\//, '')}`
  if (Hls.isSupported()) {
    const hls = new Hls({
      xhrSetup: (xhr) => {
        if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`)
      }
    })
    hls.loadSource(url)
    hls.attachMedia(video)
    hlsInstance = hls
  } else {
    // hls.js 不可用（极老浏览器）：直接挂 src，Safari 的 MSE 支持路径
    video.src = url
  }
}

function destroyPlayer() {
  hlsInstance?.destroy()
  hlsInstance = null
}

function closePlayer() {
  destroyPlayer()
  playerVisible.value = false
  playingItem.value = null
}

async function runSemanticSearch() {
  const query = semanticQuery.value.trim()
  if (!query) return
  semanticSearching.value = true
  try {
    const resp = await semanticApi.search(query)
    if (resp.success && resp.data) {
      semanticHits.value = resp.data.items
      if (resp.data.items.length === 0) {
        ElMessage.info(t('media.semanticNoHits'))
      }
    } else if (resp.error?.code === 'Semantic.ModelNotReady') {
      ElMessage.warning(t('media.semanticModelNotReady'))
    }
  } finally {
    semanticSearching.value = false
  }
}

function clearSemanticSearch() {
  semanticHits.value = []
  semanticQuery.value = ''
}

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

// immediate：首屏就要加载首页。没有它时 watch 只在切换 tab 时触发，
// 首访媒体库永远是空的（只有切走再切回或手点"加载更多"才有内容）——
// 2026-10-19 E2E 走查实证：API 有 5 张图，页面显示"暂无媒体文件"
watch(activeType, () => {
  page.value = 1
  items.value = []
  metadataMap.value = new Map()
  void loadMore(true)
}, { immediate: true })

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

// ─── 媒体预览（2026-10-21：图片/音频从"跳文件页"升级为库内预览）───
// 图片走大图预览 + OCR 快速识别（与文件页预览抽屉同款能力，媒体库内
// 直接完成"看图→识字→复制"闭环）；音频走 blob 对象 URL 播放；
// 视频保持既有 HLS 播放对话框
const previewVisible = ref(false)
const previewItem = ref<MediaItem | null>(null)
const previewUrl = ref('')
const audioUrl = ref('')

function openPreview(item: MediaItem) {
  if (item.mediaType === 'video') {
    playVideo(item)
    return
  }
  previewItem.value = item
  previewVisible.value = true
  if (item.mediaType === 'image') {
    void ensureImageUrl(item)
    // 打开即取已有识别结果：识别过就直接展示，不用重复点识别
    ocrText.value = null
    void loadExistingOcrText(item)
  } else {
    void ensureAudioUrl(item)
  }
}

// 图片预览：token 走 Authorization 头（裸 <img> 带不上，整站 401），
// 取 blob 转对象 URL。与 AuthImage 同款，但这里要原图而非缩略图
async function ensureImageUrl(item: MediaItem) {
  revokePreviewUrl()
  try {
    const token = localStorage.getItem('token')
    const response = await fetch(`/api/v1/files/download/${item.path.replace(/^\//, '')}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {}
    })
    if (!response.ok) throw new Error(String(response.status))
    const blob = await response.blob()
    previewUrl.value = URL.createObjectURL(blob)
  } catch {
    ElMessage.error(t('media.previewFailed'))
  }
}

// 音频预览：同款 blob 对象 URL（<audio> 无法带 Authorization 头）。
// 音频文件通常 MB 级，整文件进内存可接受；真有大文件需求再改 Range 流式
async function ensureAudioUrl(item: MediaItem) {
  revokeAudioUrl()
  try {
    const token = localStorage.getItem('token')
    const response = await fetch(`/api/v1/files/download/${item.path.replace(/^\//, '')}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {}
    })
    if (!response.ok) throw new Error(String(response.status))
    const blob = await response.blob()
    audioUrl.value = URL.createObjectURL(blob)
  } catch {
    ElMessage.error(t('media.previewFailed'))
  }
}

function revokePreviewUrl() {
  if (previewUrl.value) {
    URL.revokeObjectURL(previewUrl.value)
    previewUrl.value = ''
  }
}

function revokeAudioUrl() {
  if (audioUrl.value) {
    URL.revokeObjectURL(audioUrl.value)
    audioUrl.value = ''
  }
}

function closePreview() {
  revokePreviewUrl()
  revokeAudioUrl()
  previewItem.value = null
  previewVisible.value = false
}

// 下载当前预览的媒体（图片/音频通用）：fetch 落 blob 再转对象 URL 点击，
// 与文件页下载同型（token 禁拼 URL）
async function downloadPreview(item: MediaItem) {
  try {
    const token = localStorage.getItem('token')
    const response = await fetch(`/api/v1/files/download/${item.path.replace(/^\//, '')}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {}
    })
    if (!response.ok) throw new Error(String(response.status))
    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = item.name
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    URL.revokeObjectURL(url)
  } catch {
    ElMessage.error(t('media.downloadFailed'))
  }
}

// ─── 图片 OCR 快速识别（ROADMAP Phase 2 能力接入媒体库）───
// 与文件页预览抽屉同源同实现：识别结果落库后可被全站搜索命中，
// 这里只做"快速识别"入口——不用为了认一段字专门跑去 OCR 页
const ocrText = ref<OcrText | null>(null)
const ocrLoading = ref(false)

// 打开预览时取已有识别结果（失败静默：没识别过就是没有，不是错误）
async function loadExistingOcrText(item: MediaItem) {
  try {
    const result = await ocrApi.getText(item.path)
    if (result.success) {
      ocrText.value = result.data ?? null
    }
  } catch {
    // 查询失败不影响预览主流程
  }
}

// 执行识别。首次含模型加载（秒级），loading 态必须显式，
// 否则用户以为按钮没响应会重复点
async function runOcr(item: MediaItem) {
  ocrLoading.value = true
  try {
    const result = await ocrApi.recognize(item.path)
    if (result.success && result.data) {
      ocrText.value = result.data
      if (result.data.charCount === 0) {
        ElMessage.info(t('filesView.ocrNoText'))
      } else {
        ElMessage.success(t('filesView.ocrDone', { count: result.data.charCount }))
      }
    } else {
      ElMessage.error(result.error?.message || t('filesView.ocrFailed'))
    }
  } catch {
    ElMessage.error(t('filesView.ocrFailed'))
  } finally {
    ocrLoading.value = false
  }
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
      <div class="page-header"><h1>{{ $t('media.title') }}</h1></div>
      <el-radio-group v-model="activeType" size="large">
        <el-radio-button v-for="tab in typeTabs" :key="tab.key" :value="tab.key">
          <el-icon style="vertical-align: -2px; margin-right: 4px"><component :is="tab.icon" /></el-icon>
          {{ $t(tab.labelKey) }}
        </el-radio-button>
      </el-radio-group>
    </div>

    <!-- 语义搜图（仅图片 tab；模型未就位时后端明确提示） -->
    <div v-if="activeType === 'image'" class="semantic-bar">
      <el-input
        v-model="semanticQuery"
        :placeholder="$t('media.semanticPlaceholder')"
        clearable
        style="max-width: 420px"
        @keyup.enter="runSemanticSearch"
        @clear="clearSemanticSearch"
      />
      <el-button
        type="primary"
        :loading="semanticSearching"
        :disabled="!semanticQuery.trim()"
        @click="runSemanticSearch"
      >
        {{ $t('media.semanticSearch') }}
      </el-button>
      <el-button v-if="semanticActive" @click="clearSemanticSearch">
        {{ $t('media.semanticClear') }}
      </el-button>
      <span v-if="semanticActive" class="semantic-hint">
        {{ $t('media.semanticHits', { n: semanticHits.length }) }}
      </span>
    </div>

    <div v-loading="loading" class="media-body">
      <!-- 语义命中模式：平铺网格（距离口径由后端保证，点击同样跳文件位置） -->
      <template v-if="semanticActive">
        <div class="media-grid" :class="{ mobile: isMobile }">
          <div
            v-for="item in semanticItems"
            :key="item.id"
            class="media-cell"
            @click="openPreview(item)"
          >
            <div class="cell-thumb">
              <AuthImage :path="item.path" :size="300" />
            </div>
            <div class="cell-name" :title="item.name">{{ item.name }}</div>
            <div class="cell-size">{{ formatSize(item.sizeBytes) }}</div>
          </div>
        </div>
      </template>
      <template v-else-if="groups.length > 0">
        <div v-for="group in groups" :key="group.month" class="month-group">
          <div class="month-title">{{ group.label }}</div>
          <div class="media-grid" :class="{ mobile: isMobile }">
            <div
              v-for="item in group.items"
              :key="item.id"
              class="media-cell"
              @click="openPreview(item)"
            >
              <div class="cell-thumb">
                <AuthImage
                  v-if="item.mediaType === 'image'"
                  :path="item.path"
                  :size="300"
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
    <!-- 视频播放对话框（HLS，首次打开触发转码，转码中视频元素自行重试） -->
    <el-dialog
      v-model="playerVisible"
      :title="playingItem?.name ?? ''"
      width="720px"
      align-center
      @closed="closePlayer"
    >
      <video
        v-show="playerVisible"
        ref="videoEl"
        controls
        playsinline
        style="width: 100%; max-height: 60vh; background: #000"
      />
    </el-dialog>

    <!-- 媒体预览抽屉：图片（大图 + OCR 快速识别）与音频（播放器）。
         视频不走这里（HLS 对话框）。与全站模态统一右侧抽屉 -->
    <el-drawer
      v-model="previewVisible"
      :title="previewItem?.name ?? ''"
      :size="drawerSize('560px', '60vw', '760px')"
      :mask-closable="false"
      @closed="closePreview"
    >
      <div v-if="previewItem" class="media-preview">
        <!-- 图片：原图预览 -->
        <div v-if="previewItem.mediaType === 'image'" class="preview-image-wrap">
          <img v-if="previewUrl" :src="previewUrl" :alt="previewItem.name" class="preview-image" />
          <el-icon v-else class="preview-placeholder" :size="48"><Picture /></el-icon>
          <div class="preview-meta">
            {{ formatSize(previewItem.sizeBytes) }}
            <span v-if="metaOf(previewItem)?.width"> · {{ metaOf(previewItem)!.width }}×{{ metaOf(previewItem)!.height }}</span>
          </div>
        </div>

        <!-- 音频：播放器（blob 对象 URL，token 已由 fetch 带上） -->
        <div v-else-if="previewItem.mediaType === 'audio'" class="preview-audio-wrap">
          <div class="audio-icon"><el-icon :size="64"><Headset /></el-icon></div>
          <div class="audio-name">{{ previewItem.name }}</div>
          <audio v-if="audioUrl" :src="audioUrl" controls class="audio-player" />
          <div class="preview-meta">
            {{ formatSize(previewItem.sizeBytes) }}
            <span v-if="badgeOf(previewItem)"> · {{ badgeOf(previewItem) }}</span>
          </div>
        </div>

        <!-- 图片 OCR 快速识别：识别结果落库，可被全站搜索命中 -->
        <div v-if="previewItem.mediaType === 'image'" class="ocr-section">
          <div class="ocr-header">
            <span class="ocr-title">{{ $t('filesView.ocrTitle') }}</span>
            <el-button size="small" :loading="ocrLoading" @click="runOcr(previewItem!)">
              {{ ocrText ? $t('filesView.ocrReRecognize') : $t('filesView.ocrRecognize') }}
            </el-button>
          </div>
          <div v-if="ocrLoading" class="ocr-loading">
            <el-icon class="is-loading"><Loading /></el-icon>
            {{ $t('filesView.ocrRunning') }}
          </div>
          <!-- 有位置区域：叠加查看（文字按坐标叠回图片原位，可选中复制、
               清单与叠加层双向高亮）。图片即上方预览大图，组件只出清单与复制入口 -->
          <div v-else-if="ocrText && ocrText.regions.length > 0" class="ocr-text">
            <OcrImageViewer :image-url="previewUrl" :regions="ocrText.regions" :alt="previewItem!.name" />
            <div class="ocr-meta">
              {{ $t('filesView.ocrMeta', { count: ocrText.charCount, ms: ocrText.elapsedMs }) }}
            </div>
          </div>
          <!-- 存量识别结果（无位置区域）：纯文本降级展示 -->
          <div v-else-if="ocrText && ocrText.charCount > 0" class="ocr-text">
            <pre>{{ ocrText.text }}</pre>
            <div class="ocr-meta">
              {{ $t('filesView.ocrMeta', { count: ocrText.charCount, ms: ocrText.elapsedMs }) }}
            </div>
          </div>
          <div v-else-if="ocrText" class="ocr-empty">{{ $t('filesView.ocrNoText') }}</div>
          <div v-else class="ocr-empty">{{ $t('filesView.ocrHint') }}</div>
        </div>
      </div>
      <template #footer>
        <el-button @click="closePreview">{{ $t('filesView.close') }}</el-button>
        <el-button @click="openInFiles(previewItem!)">{{ $t('media.openInFiles') }}</el-button>
        <el-button type="primary" :icon="Download" @click="downloadPreview(previewItem!)">
          {{ $t('filesView.downloadFileText') }}
        </el-button>
      </template>
    </el-drawer>
  </div>
</template>

<style lang="scss" scoped>
.media-page {
  padding: 16px;

  .media-header {
    // 标题走全局 .page-header 契约（24px/700/h1），此处只负责与 tab 组的两端对齐；
    // 契约类的 24px 底距由本容器接管，避免双重间距
    .page-header { margin-bottom: 0; }
    display: flex;
    justify-content: space-between;
    align-items: center;
    flex-wrap: wrap;
    gap: 12px;
    margin-bottom: 16px;
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
    border-radius: var(--mac-radius-card);
    overflow: hidden;
    border: 1px solid var(--mac-hairline);
    background: var(--el-fill-color-blank);
    transition: transform var(--mac-duration-fast) ease, box-shadow var(--mac-duration-fast) ease;

    &:hover {
      transform: translateY(-2px);
      box-shadow: var(--card-shadow-hover);
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
        border-radius: var(--mac-radius-pill);
        font-size: 12px;
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

  // ─── 预览抽屉（2026-10-21 媒体库预览 + OCR 快速识别）───
  .media-preview {
    display: flex;
    flex-direction: column;
    gap: 24px;
  }

  .preview-image-wrap {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 12px;

    .preview-image {
      max-width: 100%;
      max-height: 55vh;
      border-radius: var(--mac-radius-card);
      border: 1px solid var(--mac-hairline);
      box-shadow: var(--card-shadow);
    }

    .preview-placeholder {
      color: var(--el-text-color-placeholder);
      padding: 48px;
    }

    .preview-meta {
      font-size: 12px;
      color: var(--el-text-color-secondary);
    }
  }

  .preview-audio-wrap {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 16px;
    padding: 32px 0;

    .audio-icon {
      width: 120px;
      height: 120px;
      border-radius: var(--mac-radius-container);
      background: linear-gradient(135deg, var(--el-color-primary), var(--el-color-primary-light-3));
      color: #fff;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .audio-name {
      font-size: 16px;
      font-weight: 600;
      color: var(--el-text-color-primary);
      word-break: break-all;
      text-align: center;
    }

    .audio-player {
      width: 100%;
      max-width: 420px;
    }

    .preview-meta {
      font-size: 12px;
      color: var(--el-text-color-secondary);
    }
  }

  // OCR 区与文件页预览抽屉同款结构（同一功能同一视觉）
  .ocr-section {
    border-top: 1px solid var(--mac-hairline);
    padding-top: 16px;

    .ocr-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 12px;

      .ocr-title {
        font-size: 14px;
        font-weight: 600;
        color: var(--el-text-color-primary);
      }
    }

    .ocr-loading {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      color: var(--el-text-color-secondary);
    }

    .ocr-text {
      pre {
        margin: 0;
        padding: 16px;
        background: var(--el-fill-color-light);
        border-radius: var(--mac-radius-control);
        font-size: 13px;
        line-height: 1.6;
        color: var(--el-text-color-primary);
        white-space: pre-wrap;
        word-break: break-all;
        max-height: 240px;
        overflow: auto;
      }

      .ocr-meta {
        margin-top: 8px;
        font-size: 12px;
        color: var(--el-text-color-secondary);
      }
    }

    .ocr-empty {
      font-size: 13px;
      color: var(--el-text-color-placeholder);
    }
  }
</style>

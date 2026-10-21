<script setup lang="ts">
/**
 * 系统配置页（ROADMAP Phase 0-3 配置收口）。
 *
 * 内容块：
 * ① 协议接入入口卡：WebDAV/SMB 2026-10-19 拆到独立页（/webdav，
 *    含四端挂载指南、认证锁定阈值、SMB 强告警），此处只留导航。
 *    webdav 与 ocr 两组 descriptor 也不再在本页渲染——同一份值
 *    两个入口必然漂移，各组只在自己的页面出现
 * ② 配置表单：按后端 descriptor 分组渲染（int → 数字框，bool → 开关），
 *    新增配置项后端加一条 descriptor 即自动出现，前端零改动
 * ③ 只读环境信息：上传上限等启动期定死的项（appsettings 管）
 * ④ 维护操作：清空缩略图/转码缓存（都是"清了会重建"的可逆动作，
 *    确认文案写明 CPU 与磁盘代价）
 * ⑤ 语义搜图卡：模型四件套状态、索引进度、手动触发一轮建索引
 * ⑥ 危险区：重启/关机（真实调度）
 */
import { ref, onMounted, onUnmounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Link, ArrowRight, Monitor, Warning, Picture, VideoCamera } from '@element-plus/icons-vue'
import { formatSize } from '@/utils/format'
import { systemConfigApi } from '@/api/systemConfig'
import { systemControlApi } from '@/api/systemControl'
import { thumbnailApi } from '@/api/thumbnail'
import { semanticApi } from '@/api/semantic'
import { mediaApi, type TranscodeStatus } from '@/api/media'
import type { SemanticStatus } from '@/api/semantic'
import http from '@/api/http'
import type { SystemConfigSnapshot, SystemConfigItem } from '@/types'

const { t } = useI18n()

const snapshot = ref<SystemConfigSnapshot | null>(null)
// 编辑中的值：key → 字符串。加载时用生效值填充，保存时整单提交
const draft = ref<Record<string, string>>({})
const loading = ref(false)
const saving = ref(false)
// 只读环境信息：上传上限取 /system/settings 的真实值（Kestrel 同源）
const maxUploadMB = ref<number | null>(null)
// 危险区：重启/关机进行中
const controlRunning = ref(false)
// 维护区：清空全部缩略图缓存进行中
const thumbClearingAll = ref(false)
// 语义搜图（ROADMAP Phase 2）：模型状态与索引进度
const semanticStatus = ref<SemanticStatus | null>(null)
const semanticLoading = ref(false)
const semanticDownloading = ref(false)
const semanticTriggering = ref(false)
// 轮询句柄：模型下载（同步长动作，约 606MB）与后台建索引期间，
// 唯一的真值通道是 /semantic/status 轮询——文件就位数与编码进度
// 都在这个响应里，不做假进度条。离页时 onUnmounted 统一清理
let semanticDownloadTimer: number | null = null
let semanticIndexTimer: number | null = null

// 模型状态：设置页打开时拉一次；下载/索引进行中由轮询持续推进
async function loadSemanticStatus() {
  semanticLoading.value = true
  try {
    const resp = await semanticApi.getStatus()
    if (resp.success && resp.data) semanticStatus.value = resp.data
  } finally {
    semanticLoading.value = false
  }
}

// 下载期间轮询：四件套逐个落盘，就位文件数实时更新
function startDownloadPolling() {
  stopDownloadPolling()
  semanticDownloadTimer = window.setInterval(async () => {
    try {
      const resp = await semanticApi.getStatus()
      if (resp.success && resp.data) semanticStatus.value = resp.data
    } catch {
      // 单次轮询失败忽略：下载结束时有一次全量刷新兜底
    }
  }, 3000)
}

function stopDownloadPolling() {
  if (semanticDownloadTimer !== null) {
    window.clearInterval(semanticDownloadTimer)
    semanticDownloadTimer = null
  }
}

// 下载模型四件套（约 606MB）。后端同步执行，前端轮询 status 观察
// 文件逐个就位（下载是幂等慢动作，不做假进度条——文件就位是唯一事实）
async function downloadSemanticModel() {
  semanticDownloading.value = true
  startDownloadPolling()
  try {
    const resp = await semanticApi.downloadModel()
    if (resp.success) {
      ElMessage.success(t('config.semanticDownloadDone'))
    }
  } catch {
    // 错误消息由 http 拦截器统一弹出
  } finally {
    stopDownloadPolling()
    semanticDownloading.value = false
    await loadSemanticStatus()
  }
}

// 索引期间轮询：202 只表示受理，编码在后台跑，
// 每 2s 看一次进度，完成或出错（error 非空是终态）即停
function startIndexPolling() {
  stopIndexPolling()
  semanticIndexTimer = window.setInterval(async () => {
    try {
      const resp = await semanticApi.getStatus()
      if (!resp.success || !resp.data) return
      semanticStatus.value = resp.data
      if (!resp.data.index_.isIndexing) stopIndexPolling()
    } catch {
      // 单次轮询失败忽略：用户可手动点刷新
    }
  }, 2000)
}

function stopIndexPolling() {
  if (semanticIndexTimer !== null) {
    window.clearInterval(semanticIndexTimer)
    semanticIndexTimer = null
  }
}

// 手动触发一轮索引：auto-index 关闭时的唯一驱动，也是开启态下
// "现在就跑"的入口（不必等 5 分钟周期）。202 只表示受理，
// 真正编码在后台——轮询 status 观察进度
async function triggerSemanticIndex() {
  semanticTriggering.value = true
  try {
    const resp = await semanticApi.triggerIndex()
    if (resp.success) {
      ElMessage.success(t('config.semanticTriggerDone'))
      await loadSemanticStatus()
      startIndexPolling()
    }
  } catch {
    // 错误消息由 http 拦截器统一弹出（未启用/模型未就位有明确文案）
  } finally {
    semanticTriggering.value = false
  }
}

// 模型四件套就位文件数：下载期间轮询刷新，给用户一个 n/4 的过程态
const semanticReadyFiles = computed(() =>
  semanticStatus.value?.model.files.filter(f => f.present).length ?? 0
)

// 视频转码（2026-10-19 多码率阶梯）：编码器与阶梯档位，部署环境事实
const transcodeStatus = ref<TranscodeStatus | null>(null)
const transcodeClearing = ref(false)

// 清空全部缩略图缓存：下次访问全量重新生成（CPU 与磁盘压力），
// 确认文案说明代价。失败只提示不阻断（缓存清了大不了再生成）
async function clearAllThumbnails() {  try {
    await ElMessageBox.confirm(
      t('config.clearAllThumbnailsConfirm'),
      t('config.clearAllThumbnails'),
      { type: 'warning' }
    )
  } catch {
    return
  }
  thumbClearingAll.value = true
  try {
    const result = await thumbnailApi.clearAllCache()
    if (result.success) {
      ElMessage.success(t('config.clearAllThumbnailsDone'))
    } else {
      ElMessage.error(result.error?.message || t('common.failed'))
    }
  } catch {
    ElMessage.error(t('common.failed'))
  } finally {
    thumbClearingAll.value = false
  }
}

// 本页只渲染未拆分的组。webdav 与 ocr 两组 2026-10-19 拆到独立页
// （/webdav、/ocr），同一份值两个渲染入口必然漂移，这里不再重复渲染
const visibleGroups = computed(() =>
  snapshot.value?.groups.filter(g => g.group !== 'webdav' && g.group !== 'ocr') ?? []
)

// 转码能力状态：编码器与阶梯是部署环境事实（硬件可达性因机器而异），
// 拉取失败不阻断页面（其余配置照常可用）
async function loadTranscodeStatus() {
  try {
    const resp = await mediaApi.getTranscodeStatus()
    if (resp.success && resp.data) transcodeStatus.value = resp.data
  } catch {
    // 状态展示失败静默：不影响配置表单
  }
}

// 清空 HLS 转码缓存：下次播放按需重建。确认文案说明代价
// （重新转码是 CPU 密集动作），成功后告知实际释放的空间
async function clearTranscodeCache() {
  try {
    await ElMessageBox.confirm(
      t('config.transcodeClearCacheConfirm'),
      t('config.transcodeClearCache'),
      { type: 'warning' }
    )
  } catch {
    return
  }
  transcodeClearing.value = true
  try {
    const resp = await mediaApi.clearTranscodeCache()
    if (resp.success) {
      ElMessage.success(
        t('config.transcodeClearCacheDone', {
          size: formatSize(resp.data?.freedBytes ?? 0)
        })
      )
    }
  } catch {
    // 错误消息由 http 拦截器统一弹出
  } finally {
    transcodeClearing.value = false
  }
}

onMounted(() => {
  loadConfig()
  loadSemanticStatus()
  loadTranscodeStatus()
})

// 离页清理轮询句柄：否则切走后定时器继续打 /status
onUnmounted(() => {
  stopDownloadPolling()
  stopIndexPolling()
})

async function loadConfig() {
  loading.value = true
  try {
    const [configResult, settingsResult] = await Promise.all([
      systemConfigApi.get(),
      http.get('/system/settings') as Promise<any>
    ])
    if (configResult.success && configResult.data) {
      snapshot.value = configResult.data
      draft.value = {}
      // draft 只收本页渲染的组：拆走的组不在 draft 里，
      // 本页保存不会顺带改写它们的值
      for (const group of visibleGroups.value) {
        for (const item of group.items) {
          draft.value[item.key] = item.value
        }
      }
    } else {
      ElMessage.error(configResult.error?.message || t('config.loadFailed'))
    }
    if (settingsResult?.success) {
      maxUploadMB.value = settingsResult.data.maxUploadSizeMB ?? null
    }
  } catch {
    ElMessage.error(t('config.loadFailed'))
  } finally {
    loading.value = false
  }
}

// 重启/关机：确认后调度，页面提示"服务即将断开"。
// 重启后 Docker 的 restart 策略会拉回进程（约数秒），
// 页面不自动跳转，用户手动刷新即可
async function runControl(action: 'restart' | 'shutdown') {
  const isRestart = action === 'restart'
  try {
    await ElMessageBox.confirm(
      isRestart ? t('config.restartConfirm') : t('config.shutdownConfirm'),
      isRestart ? t('config.restart') : t('config.shutdown'),
      { type: 'warning', confirmButtonClass: 'el-button--danger' }
    )
  } catch {
    return
  }
  controlRunning.value = true
  try {
    const result = isRestart
      ? await systemControlApi.restart()
      : await systemControlApi.shutdown()
    if (result.success) {
      ElMessage.warning(
        isRestart ? t('config.restartScheduled') : t('config.shutdownScheduled')
      )
    } else {
      ElMessage.error(result.error?.message || t('common.failed'))
    }
  } catch {
    // 连接被预期断开：进程已开始关停，这个错误本身就是成功信号
    ElMessage.warning(
      isRestart ? t('config.restartScheduled') : t('config.shutdownScheduled')
    )
  } finally {
    controlRunning.value = false
  }
}

// 有改动才亮保存按钮：整份 draft 与快照生效值逐键比对（只覆盖本页渲染的组）
const dirty = computed(() => {
  if (!snapshot.value) return false
  return visibleGroups.value.some(group =>
    group.items.some(item => draft.value[item.key] !== item.value))
})

async function save() {
  saving.value = true
  try {
    const result = await systemConfigApi.update(draft.value)
    if (result.success) {
      ElMessage.success(t('config.saveSuccess'))
      await loadConfig()
    } else {
      ElMessage.error(result.error?.message || t('config.saveFailed'))
    }
  } catch {
    ElMessage.error(t('config.saveFailed'))
  } finally {
    saving.value = false
  }
}

function reset() {
  if (!snapshot.value) return
  for (const group of visibleGroups.value) {
    for (const item of group.items) {
      draft.value[item.key] = item.value
    }
  }
}

// 开关型 item 的当前编辑值（el-switch 绑 bool，存取时转字符串）
function boolOf(item: SystemConfigItem): boolean {
  return draft.value[item.key] === 'true'
}

function setBool(item: SystemConfigItem, value: string | number | boolean) {
  draft.value[item.key] = value ? 'true' : 'false'
}

function strOf(item: SystemConfigItem): string {
  return draft.value[item.key] ?? item.defaultValue
}
function setStr(item: SystemConfigItem, value: string) {
  draft.value[item.key] = value
}
function intOf(item: SystemConfigItem): number {
  return Number.parseInt(draft.value[item.key] ?? item.defaultValue, 10) || 0
}

function setInt(item: SystemConfigItem, value: number | undefined) {
  if (value === undefined) return
  draft.value[item.key] = String(value)
}
</script>

<template>
  <div class="config-page" v-loading="loading">
    <div class="page-header">
      <h1>{{ $t('config.title') }}</h1>
    </div>

    <!-- ① 协议接入入口：WebDAV/SMB 已拆独立页（/webdav），此处只留导航 -->
    <router-link to="/webdav" class="service-row-link">
      <div class="service-card link-card">
        <div class="service-head">
          <el-icon :size="20"><Link /></el-icon>
          <span class="service-name">{{ $t('config.webdavService') }}</span>
          <el-icon class="link-arrow"><ArrowRight /></el-icon>
        </div>
        <p class="service-desc">{{ $t('config.serviceEntryHint') }}</p>
      </div>
    </router-link>

    <!-- ② 配置表单（descriptor 驱动） -->
    <div v-for="group in visibleGroups" :key="group.group" class="config-card">
      <h3 class="card-title">{{ $t(group.titleKey) }}</h3>
      <el-form label-position="top" class="config-form">
        <el-form-item
          v-for="item in group.items"
          :key="item.key"
          :label="$t(item.labelKey)"
        >
          <!-- 开关型（Bool=1） -->
          <el-switch
            v-if="item.type === 1"
            :model-value="boolOf(item)"
            @update:model-value="(v: string | number | boolean) => setBool(item, v)"
          />
          <!-- 字符串型（String=2，如 cron 表达式；长度界复用 min/max） -->
          <el-input
            v-else-if="item.type === 2"
            :model-value="strOf(item)"
            :maxlength="item.max ?? undefined"
            :placeholder="$t('config.cronPlaceholder')"
            @update:model-value="(v: string) => setStr(item, v)"
          />
          <!-- 整数型（Int=0） -->
          <el-input-number
            v-else
            :model-value="intOf(item)"
            :min="item.min"
            :max="item.max"
            @update:model-value="(v: number | undefined) => setInt(item, v)"
          />
          <div class="item-desc" v-if="item.descriptionKey">{{ $t(item.descriptionKey) }}</div>
        </el-form-item>
      </el-form>
    </div>

    <!-- ③ 只读环境信息 -->
    <div class="config-card readonly-card">
      <h3 class="card-title">
        <el-icon style="vertical-align: -2px; margin-right: 4px"><Monitor /></el-icon>
        {{ $t('config.readonlyTitle') }}
      </h3>
      <p class="readonly-desc">{{ $t('config.readonlyDesc') }}</p>
      <ul class="readonly-list">
        <li v-if="maxUploadMB !== null">
          {{ $t('config.readonlyUploadLimit', { size: maxUploadMB }) }}
        </li>
        <li>{{ $t('config.readonlyJwt') }}</li>
        <li>{{ $t('config.readonlyCors') }}</li>
        <li>{{ $t('config.readonlyThumbnail') }}</li>
      </ul>
    </div>

    <!-- ④ 维护操作 -->
    <div class="config-card">
      <h3 class="card-title">{{ $t('config.maintenanceTitle') }}</h3>
      <p class="readonly-desc">{{ $t('config.maintenanceDesc') }}</p>
      <div class="danger-actions">
        <el-button :loading="thumbClearingAll" @click="clearAllThumbnails">
          {{ $t('config.clearAllThumbnails') }}
        </el-button>
      </div>
    </div>

    <!-- ④-b 语义搜图：模型四件套状态与索引（ROADMAP Phase 2） -->
    <div class="config-card" v-if="semanticStatus">
      <h3 class="card-title">
        <el-icon style="vertical-align: -2px; margin-right: 4px"><Picture /></el-icon>
        {{ $t('config.semanticTitle') }}
      </h3>
      <p class="readonly-desc">{{ $t('config.semanticDesc') }}</p>

      <div class="semantic-files">
        <div v-for="file in semanticStatus.model.files" :key="file.name" class="semantic-file">
          <el-tag :type="file.present ? 'success' : 'info'" size="small">
            {{ file.present ? $t('config.semanticFilePresent') : $t('config.semanticFileMissing') }}
          </el-tag>
          <span class="semantic-file-name">{{ file.name }}</span>
          <span v-if="file.present" class="semantic-file-size">{{ formatSize(file.sizeBytes) }}</span>
        </div>
      </div>

      <div class="semantic-progress" v-if="semanticStatus.model.ready">
        <span>{{ $t('config.semanticIndexProgress', {
          indexed: semanticStatus.index_.indexedImages,
          total: semanticStatus.index_.totalImages
        }) }}</span>
        <el-progress
          v-if="semanticStatus.index_.totalImages > 0"
          :percentage="Math.min(100, Math.round(semanticStatus.index_.indexedImages / semanticStatus.index_.totalImages * 100))"
          :stroke-width="6"
          style="margin-top: 8px; max-width: 420px"
        />
        <el-tag v-if="semanticStatus.index_.isIndexing" type="warning" size="small">
          {{ $t('config.semanticIndexing') }}
        </el-tag>
        <span v-if="semanticStatus.index_.error" class="semantic-error">
          {{ semanticStatus.index_.error }}
        </span>
      </div>

      <p v-if="semanticDownloading" class="readonly-desc">
        {{ $t('config.semanticDownloading', {
          ready: semanticReadyFiles,
          total: semanticStatus.model.files.length
        }) }}
      </p>

      <div class="danger-actions">
        <el-button
          type="primary"
          :loading="semanticDownloading"
          :disabled="semanticStatus.model.ready"
          @click="downloadSemanticModel"
        >
          {{ semanticStatus.model.ready ? $t('config.semanticModelReady') : $t('config.semanticDownload') }}
        </el-button>
        <!-- 手动建索引：auto-index 关闭时的唯一驱动；索引进行中重复触发无意义 -->
        <el-button
          :loading="semanticTriggering"
          :disabled="!semanticStatus.model.ready || semanticStatus.index_.isIndexing"
          @click="triggerSemanticIndex"
        >
          {{ $t('config.semanticTriggerIndex') }}
        </el-button>
        <el-button :loading="semanticLoading" @click="loadSemanticStatus">
          {{ $t('common.refresh') }}
        </el-button>
      </div>
    </div>

    <!-- ④-c 视频转码：编码器与码率阶梯（2026-10-19 多码率升级） -->
    <div class="config-card" v-if="transcodeStatus">
      <h3 class="card-title">
        <el-icon style="vertical-align: -2px; margin-right: 4px"><VideoCamera /></el-icon>
        {{ $t('config.transcodeTitle') }}
      </h3>
      <p class="readonly-desc">{{ $t('config.transcodeDesc') }}</p>

      <div class="transcode-status">
        <el-tag :type="transcodeStatus.ffmpegAvailable ? 'success' : 'danger'" size="small">
          {{ transcodeStatus.ffmpegAvailable ? $t('config.transcodeFfmpegOk') : $t('config.transcodeFfmpegMissing') }}
        </el-tag>
        <el-tag :type="transcodeStatus.isHardwareEncoder ? 'success' : 'info'" size="small" v-if="transcodeStatus.ffmpegAvailable">
          {{ transcodeStatus.encoder }}
          {{ transcodeStatus.isHardwareEncoder ? $t('config.transcodeHardware') : $t('config.transcodeSoftware') }}
        </el-tag>
      </div>

      <div class="transcode-rungs" v-if="transcodeStatus.rungs?.length">
        <span class="rungs-label">{{ $t('config.transcodeLadder') }}</span>
        <el-tag v-for="rung in transcodeStatus.rungs" :key="rung.height" size="small" type="primary" effect="plain">
          {{ rung.name }} · {{ (rung.maxBitrateKbps / 1000).toFixed(1) }} Mbps
        </el-tag>
      </div>

      <div class="danger-actions">
        <el-button :loading="transcodeClearing" @click="clearTranscodeCache">
          {{ $t('config.transcodeClearCache') }}
        </el-button>
      </div>
    </div>

    <!-- ⑤ 危险区：重启/关机（真实调度，非计划消息） -->
    <div class="config-card danger-card">
      <h3 class="card-title">
        <el-icon style="vertical-align: -2px; margin-right: 4px"><Warning /></el-icon>
        {{ $t('config.dangerTitle') }}
      </h3>
      <p class="readonly-desc">{{ $t('config.dangerDesc') }}</p>
      <div class="danger-actions">
        <el-button :loading="controlRunning" @click="runControl('restart')">
          {{ $t('config.restart') }}
        </el-button>
        <el-button type="danger" :loading="controlRunning" @click="runControl('shutdown')">
          {{ $t('config.shutdown') }}
        </el-button>
      </div>
    </div>

    <!-- 保存栏：有改动才亮 -->
    <div class="save-bar" :class="{ dirty }">
      <span class="dirty-hint" v-if="dirty">{{ $t('config.unsaved') }}</span>
      <span class="saved-hint" v-else-if="snapshot?.lastUpdatedAt">
        {{ $t('config.lastUpdated', { time: new Date(snapshot.lastUpdatedAt).toLocaleString() }) }}
      </span>
      <div class="save-actions">
        <el-button :disabled="!dirty" @click="reset">{{ $t('config.reset') }}</el-button>
        <el-button type="primary" :disabled="!dirty" :loading="saving" @click="save">
          {{ $t('config.save') }}
        </el-button>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
.config-page {
  max-width: 860px;
  margin: 0 auto;
  padding: 16px;

  

  .service-row-link {
    display: block;
    margin-bottom: 16px;
    text-decoration: none;
  }

  .service-card {
    border: 1px solid var(--el-border-color-lighter);
    border-radius: var(--mac-radius-card);
    padding: 14px;
    background: var(--el-bg-color);

    &.link-card {
      transition: border-color 0.15s ease;

      &:hover {
        border-color: var(--el-color-primary);
      }
    }

    .link-arrow {
      color: var(--el-text-color-secondary);
    }

    .service-head {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 8px;

      .service-name {
        font-weight: 600;
        font-size: 14px;
        flex: 1;
        color: var(--el-text-color-primary);
      }
    }

    .service-url {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 10px;
      background: var(--el-fill-color-light);
      border-radius: var(--mac-radius-control);
      margin-bottom: 8px;

      code {
        flex: 1;
        font-size: 13px;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        color: var(--el-text-color-regular);
      }
    }

    .service-desc {
      margin: 0;
      font-size: 12px;
      line-height: 1.6;
      color: var(--el-text-color-secondary);
    }
  }

  .config-card {
    border: 1px solid var(--el-border-color-lighter);
    border-radius: var(--mac-radius-card);
    padding: 16px;
    margin-bottom: 16px;
    background: var(--el-bg-color);

    .card-title {
      margin: 0 0 12px;
      font-size: 16px;
      font-weight: 600;
      color: var(--el-text-color-primary);
    }
  }

  .config-form {
    max-width: 480px;
  }

  .item-desc {
    font-size: 12px;
    color: var(--el-text-color-secondary);
    line-height: 1.5;
    margin-top: 2px;
  }

  .readonly-desc {
    margin: 0 0 8px;
    font-size: 13px;
    color: var(--el-text-color-secondary);
  }

  .readonly-list {
    margin: 0;
    padding-left: 16px;
    font-size: 13px;
    color: var(--el-text-color-regular);

    li {
      margin-bottom: 4px;
    }
  }

  .transcode-status {
    display: flex;
    gap: 8px;
    flex-wrap: wrap;
    margin-bottom: 12px;
  }

  .transcode-rungs {
    display: flex;
    align-items: center;
    gap: 8px;
    flex-wrap: wrap;

    .rungs-label {
      font-size: 13px;
      color: var(--el-text-color-regular);
    }
  }

  .danger-actions {
    display: flex;
    gap: 12px;
    flex-wrap: wrap;
  }

  .danger-card {
    border-color: var(--el-color-danger-light-5);

    .card-title {
      color: var(--el-color-danger);
    }
  }

  
}
</style>

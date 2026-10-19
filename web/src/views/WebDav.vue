<script setup lang="ts">
/**
 * WebDAV 挂载服务页（2026-10-19 从系统配置页拆出，仅管理员）。
 *
 * 拆页动机不只是"把两个开关搬出来"：原系统配置页只有挂载地址与开关，
 * 缺三块真实需要的内容——四端客户端挂载步骤、认证锁定阈值的可配置化
 * （原为 Api 层硬编码 10 次/5 分钟）、SMB 直通的强告警。本页一次讲清
 * "怎么从外面访问这个 NAS"。
 *
 * 配置仍走 descriptor 单一事实源：本页只渲染 webdav 组的 descriptor，
 * 系统配置页不再重复渲染该组（同一份值两个入口必然漂移）。
 */
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Link, CopyDocument, FolderOpened, Monitor, Platform, Cellphone } from '@element-plus/icons-vue'
import { systemConfigApi } from '@/api/systemConfig'
import type { SystemConfigSnapshot, SystemConfigItem } from '@/types'

const { t } = useI18n()

const snapshot = ref<SystemConfigSnapshot | null>(null)
// 编辑中的值：key → 字符串。加载时用生效值填充，保存时整单提交
const draft = ref<Record<string, string>>({})
const loading = ref(false)
const saving = ref(false)

const davUrl = computed(() => `${window.location.origin}/dav`)

// webdav 组的 descriptor 项（本页只渲染这一组）
const webdavItems = computed<SystemConfigItem[]>(() =>
  snapshot.value?.groups.find(g => g.group === 'webdav')?.items ?? []
)

onMounted(loadConfig)

async function loadConfig() {
  loading.value = true
  try {
    const result = await systemConfigApi.get()
    if (result.success && result.data) {
      snapshot.value = result.data
      draft.value = {}
      for (const item of webdavItems.value) {
        draft.value[item.key] = item.value
      }
    } else {
      ElMessage.error(result.error?.message || t('config.loadFailed'))
    }
  } catch {
    ElMessage.error(t('config.loadFailed'))
  } finally {
    loading.value = false
  }
}

// 有改动才亮保存按钮：整份 draft 与快照生效值逐键比对
const dirty = computed(() => {
  if (!snapshot.value) return false
  return webdavItems.value.some(item => draft.value[item.key] !== item.value)
})

async function save() {
  saving.value = true
  try {
    // 只提交本页负责的键：整单提交的是 draft 中 webdav 组的键，
    // 其他组的键不在 draft 里，不会被本页误改
    const values: Record<string, string> = {}
    for (const item of webdavItems.value) {
      values[item.key] = draft.value[item.key]
    }
    const result = await systemConfigApi.update(values)
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
  for (const item of webdavItems.value) {
    draft.value[item.key] = item.value
  }
}

async function copyText(text: string) {
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success(t('config.copied'))
  } catch {
    ElMessage.error(t('config.copyFailed'))
  }
}

// 控件存取助手：el-switch 绑 bool、el-input-number 绑 number，存取时转字符串
function boolOf(item: SystemConfigItem): boolean {
  return draft.value[item.key] === 'true'
}
function setBool(item: SystemConfigItem, value: string | number | boolean) {
  draft.value[item.key] = value ? 'true' : 'false'
}
function intOf(item: SystemConfigItem): number {
  return Number.parseInt(draft.value[item.key] ?? item.defaultValue, 10) || 0
}
function setInt(item: SystemConfigItem, value: number | undefined) {
  if (value === undefined) return
  draft.value[item.key] = String(value)
}

// 按 key 取项（阈值两个数字框不按 v-for 渲染，label 各带说明）
function itemOf(key: string): SystemConfigItem | undefined {
  return webdavItems.value.find(i => i.key === key)
}
</script>

<template>
  <div class="webdav-page" v-loading="loading">
    <div class="page-header">
      <div class="header-left">
        <h2>{{ $t('webdav.title') }}</h2>
        <p>{{ $t('webdav.subtitle') }}</p>
      </div>
    </div>

    <!-- ① 服务卡：挂载地址 + 启用开关 -->
    <div class="card">
      <div class="card-head">
        <el-icon :size="20"><Link /></el-icon>
        <span class="card-name">{{ $t('webdav.serviceTitle') }}</span>
        <el-tag :type="boolOf(itemOf('webdav.enabled')!) ? 'success' : 'info'" size="small" v-if="itemOf('webdav.enabled')">
          {{ boolOf(itemOf('webdav.enabled')!) ? $t('config.statusEnabled') : $t('config.statusDisabled') }}
        </el-tag>
      </div>
      <div class="service-url">
        <code>{{ davUrl }}</code>
        <el-button link type="primary" @click="copyText(davUrl)">
          <el-icon><CopyDocument /></el-icon>{{ $t('config.copyUrl') }}
        </el-button>
      </div>
      <p class="card-desc">{{ $t('webdav.serviceDesc') }}</p>

      <div class="switch-row" v-if="itemOf('webdav.enabled')">
        <div class="switch-info">
          <span class="switch-label">{{ $t(itemOf('webdav.enabled')!.labelKey) }}</span>
          <span class="switch-desc">{{ $t(itemOf('webdav.enabled')!.descriptionKey ?? '') }}</span>
        </div>
        <el-switch :model-value="boolOf(itemOf('webdav.enabled')!)" @update:model-value="(v: string | number | boolean) => setBool(itemOf('webdav.enabled')!, v)" />
      </div>
      <div class="switch-row" v-if="itemOf('webdav.allow-http')">
        <div class="switch-info">
          <span class="switch-label">{{ $t(itemOf('webdav.allow-http')!.labelKey) }}</span>
          <span class="switch-desc">{{ $t(itemOf('webdav.allow-http')!.descriptionKey ?? '') }}</span>
        </div>
        <el-switch :model-value="boolOf(itemOf('webdav.allow-http')!)" @update:model-value="(v: string | number | boolean) => setBool(itemOf('webdav.allow-http')!, v)" />
      </div>
    </div>

    <!-- ② 客户端挂载指南：四端步骤（ROADMAP Phase 0 验收四端） -->
    <div class="card">
      <h3 class="card-title">{{ $t('webdav.guideTitle') }}</h3>
      <el-collapse class="guide-collapse">
        <el-collapse-item name="windows">
          <template #title>
            <el-icon><Monitor /></el-icon><span>{{ $t('webdav.guideWindows') }}</span>
          </template>
          <ol class="guide-steps">
            <li>{{ $t('webdav.guideWindowsStep1') }}</li>
            <li>{{ $t('webdav.guideWindowsStep2', { url: davUrl }) }}</li>
            <li>{{ $t('webdav.guideWindowsStep3') }}</li>
          </ol>
        </el-collapse-item>
        <el-collapse-item name="macos">
          <template #title>
            <el-icon><Platform /></el-icon><span>{{ $t('webdav.guideMacos') }}</span>
          </template>
          <ol class="guide-steps">
            <li>{{ $t('webdav.guideMacosStep1') }}</li>
            <li>{{ $t('webdav.guideMacosStep2', { url: davUrl }) }}</li>
          </ol>
        </el-collapse-item>
        <el-collapse-item name="linux">
          <template #title>
            <el-icon><Platform /></el-icon><span>{{ $t('webdav.guideLinux') }}</span>
          </template>
          <ol class="guide-steps">
            <li>{{ $t('webdav.guideLinuxStep1') }}</li>
            <li>{{ $t('webdav.guideLinuxStep2', { url: davUrl }) }}</li>
          </ol>
        </el-collapse-item>
        <el-collapse-item name="ios">
          <template #title>
            <el-icon><Cellphone /></el-icon><span>{{ $t('webdav.guideIos') }}</span>
          </template>
          <ol class="guide-steps">
            <li>{{ $t('webdav.guideIosStep1') }}</li>
            <li>{{ $t('webdav.guideIosStep2', { url: davUrl }) }}</li>
          </ol>
        </el-collapse-item>
      </el-collapse>
    </div>

    <!-- ③ 认证防护：失败锁定阈值（原硬编码，现可配置） -->
    <div class="card">
      <h3 class="card-title">{{ $t('webdav.authTitle') }}</h3>
      <p class="card-desc">{{ $t('webdav.authDesc') }}</p>
      <div class="threshold-grid">
        <div class="threshold-item" v-if="itemOf('webdav.auth-max-failures')">
          <span class="threshold-label">{{ $t(itemOf('webdav.auth-max-failures')!.labelKey) }}</span>
          <el-input-number
            :model-value="intOf(itemOf('webdav.auth-max-failures')!)"
            :min="itemOf('webdav.auth-max-failures')!.min ?? 1"
            :max="itemOf('webdav.auth-max-failures')!.max ?? 100"
            @update:model-value="(v: number | undefined) => setInt(itemOf('webdav.auth-max-failures')!, v)"
          />
          <span class="threshold-desc">{{ $t(itemOf('webdav.auth-max-failures')!.descriptionKey ?? '') }}</span>
        </div>
        <div class="threshold-item" v-if="itemOf('webdav.auth-window-minutes')">
          <span class="threshold-label">{{ $t(itemOf('webdav.auth-window-minutes')!.labelKey) }}</span>
          <el-input-number
            :model-value="intOf(itemOf('webdav.auth-window-minutes')!)"
            :min="itemOf('webdav.auth-window-minutes')!.min ?? 1"
            :max="itemOf('webdav.auth-window-minutes')!.max ?? 60"
            @update:model-value="(v: number | undefined) => setInt(itemOf('webdav.auth-window-minutes')!, v)"
          />
          <span class="threshold-desc">{{ $t(itemOf('webdav.auth-window-minutes')!.descriptionKey ?? '') }}</span>
        </div>
      </div>
    </div>

    <!-- ④ SMB 直通（sidecar，只读信息 + 强告警） -->
    <div class="card smb-card">
      <div class="card-head">
        <el-icon :size="20"><FolderOpened /></el-icon>
        <span class="card-name">{{ $t('webdav.smbTitle') }}</span>
        <el-tag type="info" size="small">{{ $t('config.smbOptional') }}</el-tag>
      </div>
      <div class="service-url">
        <code>\\{{ $t('config.hostPlaceholder') }}\nas</code>
        <el-button link type="primary" @click="copyText('nas')">
          <el-icon><CopyDocument /></el-icon>{{ $t('config.copyShareName') }}
        </el-button>
      </div>
      <p class="card-desc">{{ $t('webdav.smbDesc') }}</p>
      <el-alert type="warning" :closable="false" show-icon :title="$t('webdav.smbWarning')" class="smb-warning" />
      <div class="smb-steps">
        <p class="steps-title">{{ $t('webdav.smbEnableTitle') }}</p>
        <pre class="smb-command">docker compose --profile smb up -d</pre>
      </div>
    </div>

    <!-- ⑤ 约束说明 -->
    <div class="card">
      <h3 class="card-title">{{ $t('webdav.constraintsTitle') }}</h3>
      <ul class="constraint-list">
        <li>{{ $t('webdav.constraintQuota') }}</li>
        <li>{{ $t('webdav.constraintRecycle') }}</li>
        <li>{{ $t('webdav.constraintRealtime') }}</li>
        <li>{{ $t('webdav.constraintSize') }}</li>
      </ul>
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

<style scoped>
.webdav-page {
  padding: 16px;
  max-width: 860px;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 16px;
  gap: 12px;
  flex-wrap: wrap;

  .header-left {
    h2 {
      margin: 0 0 4px;
      font-size: 20px;
      font-weight: 600;
      color: var(--el-text-color-primary);
    }
    p {
      margin: 0;
      font-size: 13px;
      color: var(--el-text-color-secondary);
    }
  }
}

.card {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 12px;
  padding: 16px;
  margin-bottom: 16px;
  background: var(--el-bg-color);

  .card-title {
    margin: 0 0 12px;
    font-size: 15px;
    font-weight: 600;
    color: var(--el-text-color-primary);
  }

  .card-desc {
    margin: 0 0 8px;
    font-size: 13px;
    color: var(--el-text-color-secondary);
    line-height: 1.6;
  }
}

.card-head {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 10px;

  .card-name {
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
  border-radius: 8px;
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

.switch-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
  padding: 12px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);

  &:last-child {
    border-bottom: none;
    padding-bottom: 0;
  }

  .switch-info {
    .switch-label {
      display: block;
      font-size: 14px;
      font-weight: 500;
    }
    .switch-desc {
      display: block;
      font-size: 12px;
      color: var(--el-text-color-secondary);
      margin-top: 2px;
    }
  }
}

.guide-collapse {
  :deep(.el-collapse-item__header) {
    font-size: 13px;
    font-weight: 500;
  }

  :deep(.el-collapse-item__content) {
    padding-bottom: 12px;
  }
}

.guide-steps {
  margin: 0;
  padding-left: 20px;
  font-size: 13px;
  color: var(--el-text-color-regular);
  line-height: 1.8;

  li {
    word-break: break-all;
  }
}

.threshold-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;

  @media (max-width: 768px) {
    grid-template-columns: 1fr;
  }
}

.threshold-item {
  display: flex;
  flex-direction: column;
  gap: 6px;

  .threshold-label {
    font-size: 13px;
    font-weight: 500;
  }

  .threshold-desc {
    font-size: 12px;
    color: var(--el-text-color-secondary);
    line-height: 1.5;
  }
}

.smb-card {
  border-color: var(--el-color-warning-light-5);
}

.smb-warning {
  margin-bottom: 12px;
}

.smb-steps {
  .steps-title {
    margin: 0 0 6px;
    font-size: 13px;
    font-weight: 500;
  }

  .smb-command {
    margin: 0;
    padding: 10px 12px;
    background: var(--el-fill-color-light);
    border-radius: 8px;
    font-size: 12px;
    overflow-x: auto;
    white-space: pre;
  }
}

.constraint-list {
  margin: 0;
  padding-left: 20px;
  font-size: 13px;
  color: var(--el-text-color-regular);
  line-height: 1.8;
}

.save-bar {
  position: sticky;
  bottom: 0;
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  border-radius: 12px;
  background: var(--el-bg-color);
  border: 1px solid var(--el-border-color-lighter);
  transition: border-color 0.2s;

  &.dirty {
    border-color: var(--el-color-primary);
  }

  .dirty-hint {
    font-size: 13px;
    color: var(--el-color-primary);
  }

  .saved-hint {
    font-size: 12px;
    color: var(--el-text-color-secondary);
  }

  .save-actions {
    display: flex;
    gap: 8px;
  }
}
</style>

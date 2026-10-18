<script setup lang="ts">
/**
 * 系统配置页（ROADMAP Phase 0-3 配置收口）。
 *
 * 三块内容：
 * ① 服务状态卡（WebDAV / SMB）：挂载地址、当前状态、使用方式。
 *    只读信息 + 复制按钮，可调项统一走下方表单（不在两处放同一份状态）
 * ② 配置表单：按后端 descriptor 分组渲染（int → 数字框，bool → 开关），
 *    新增配置项后端加一条 descriptor 即自动出现，前端零改动
 * ③ 只读环境信息：上传上限等启动期定死的项（appsettings 管）
 */
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Link, CopyDocument, FolderOpened, Monitor } from '@element-plus/icons-vue'
import { systemConfigApi } from '@/api/systemConfig'
import type { SystemConfigSnapshot, SystemConfigItem } from '@/types'

const { t } = useI18n()

const snapshot = ref<SystemConfigSnapshot | null>(null)
// 编辑中的值：key → 字符串。加载时用生效值填充，保存时整单提交
const draft = ref<Record<string, string>>({})
const loading = ref(false)
const saving = ref(false)

const davUrl = computed(() => `${window.location.origin}/dav`)

onMounted(loadConfig)

async function loadConfig() {
  loading.value = true
  try {
    const result = await systemConfigApi.get()
    if (result.success && result.data) {
      snapshot.value = result.data
      draft.value = {}
      for (const group of result.data.groups) {
        for (const item of group.items) {
          draft.value[item.key] = item.value
        }
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
  return snapshot.value.groups.some(group =>
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
  for (const group of snapshot.value.groups) {
    for (const item of group.items) {
      draft.value[item.key] = item.value
    }
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

// 开关型 item 的当前编辑值（el-switch 绑 bool，存取时转字符串）
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

function statusOf(key: string): boolean {
  return draft.value[key] === 'true'
}
</script>

<template>
  <div class="config-page" v-loading="loading">
    <h2 class="page-title">{{ $t('config.title') }}</h2>

    <!-- ① 服务状态卡 -->
    <div class="service-row">
      <div class="service-card">
        <div class="service-head">
          <el-icon :size="20"><Link /></el-icon>
          <span class="service-name">{{ $t('config.webdavService') }}</span>
          <el-tag :type="statusOf('webdav.enabled') ? 'success' : 'info'" size="small">
            {{ statusOf('webdav.enabled') ? $t('config.statusEnabled') : $t('config.statusDisabled') }}
          </el-tag>
        </div>
        <div class="service-url">
          <code>{{ davUrl }}</code>
          <el-button link type="primary" @click="copyText(davUrl)">
            <el-icon><CopyDocument /></el-icon>{{ $t('config.copyUrl') }}
          </el-button>
        </div>
        <p class="service-desc">{{ $t('config.webdavDesc') }}</p>
      </div>

      <div class="service-card">
        <div class="service-head">
          <el-icon :size="20"><FolderOpened /></el-icon>
          <span class="service-name">{{ $t('config.smbService') }}</span>
          <el-tag type="info" size="small">{{ $t('config.smbOptional') }}</el-tag>
        </div>
        <div class="service-url">
          <code>\\{{ $t('config.hostPlaceholder') }}\nas</code>
          <el-button link type="primary" @click="copyText('nas')">
            <el-icon><CopyDocument /></el-icon>{{ $t('config.copyShareName') }}
          </el-button>
        </div>
        <p class="service-desc">{{ $t('config.smbDesc') }}</p>
      </div>
    </div>

    <!-- ② 配置表单（descriptor 驱动） -->
    <div v-for="group in snapshot?.groups ?? []" :key="group.group" class="config-card">
      <h3 class="card-title">{{ $t(group.titleKey) }}</h3>
      <el-form label-position="top" class="config-form">
        <el-form-item
          v-for="item in group.items"
          :key="item.key"
          :label="$t(item.labelKey)"
        >
          <!-- 开关型 -->
          <el-switch
            v-if="item.type === 1"
            :model-value="boolOf(item)"
            @update:model-value="(v: string | number | boolean) => setBool(item, v)"
          />
          <!-- 整数型 -->
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
        <li>{{ $t('config.readonlyUploadLimit') }}</li>
        <li>{{ $t('config.readonlyJwt') }}</li>
        <li>{{ $t('config.readonlyCors') }}</li>
        <li>{{ $t('config.readonlyThumbnail') }}</li>
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

<style lang="scss" scoped>
.config-page {
  max-width: 860px;
  margin: 0 auto;
  padding: 16px;

  .page-title {
    font-size: 20px;
    font-weight: 600;
    margin: 0 0 16px;
    color: var(--el-text-color-primary);
  }

  .service-row {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 12px;
    margin-bottom: 16px;

    @media (max-width: 768px) {
      grid-template-columns: 1fr;
    }
  }

  .service-card {
    border: 1px solid var(--el-border-color-lighter);
    border-radius: 12px;
    padding: 14px;
    background: var(--el-bg-color);

    .service-head {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 10px;

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

    .service-desc {
      margin: 0;
      font-size: 12px;
      line-height: 1.6;
      color: var(--el-text-color-secondary);
    }
  }

  .config-card {
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
    padding-left: 20px;
    font-size: 13px;
    color: var(--el-text-color-regular);

    li {
      margin-bottom: 4px;
    }
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
}
</style>

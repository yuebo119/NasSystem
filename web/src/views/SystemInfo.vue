<script setup lang="ts">
/**
 * 系统信息页（2026-10-19 从"系统设置"页拆出，仅管理员）。
 *
 * 原 Settings 页把个人设置与系统管理混在一列，非管理员也能看到
 * 外网访问卡（靠 v-if 藏内容但卡片结构仍在）。拆分后：
 * 个人设置归 /settings，系统信息与外网访问归本页。
 */
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Refresh, InfoFilled, Monitor } from '@element-plus/icons-vue'
import http from '@/api/http'
import { getLocale } from '@/locales'
import type { SystemInfo, SystemStatus } from '@/types'

const { t } = useI18n()

const loading = ref(false)
const systemInfo = ref<SystemInfo | null>(null)
const systemStatus = ref<SystemStatus | null>(null)

// 外网访问配置（ROADMAP Phase 3）
const remoteProvider = ref('cloudflare')
const remoteHostname = ref('')
// 本地端口：留空用后端默认 6050（与服务端同口径），可改
const remoteLocalPort = ref<number | undefined>(undefined)
const remoteConfig = ref<RemoteAccessConfig | null>(null)
const remoteLoading = ref(false)

/** 外网访问生成结果：后端生成的配置文件全文 + 分步操作说明 */
interface RemoteAccessConfig {
  provider: string
  fileName: string
  config: string
  steps: string[]
}

async function loadSystemInfo() {
  loading.value = true
  try {
    const [infoRes, statusRes] = await Promise.all([
      http.get<SystemInfo>('/system/info'),
      http.get<SystemStatus>('/system/status')
    ])

    if (infoRes.success && infoRes.data) systemInfo.value = infoRes.data
    if (statusRes.success && statusRes.data) systemStatus.value = statusRes.data
  } catch (e) {
    console.error('Failed to load system info', e)
  } finally {
    loading.value = false
  }
}

// 生成外网访问配置
const generateRemoteConfig = async () => {
  remoteLoading.value = true
  try {
    const res = await http.get<RemoteAccessConfig>('/system/remote-access/config', {
      params: {
        provider: remoteProvider.value,
        hostname: remoteHostname.value || undefined,
        localPort: remoteLocalPort.value || undefined,
        // 配置注释与步骤按界面语言出（后端生成器双语；插值在后端算，
        // 前端拿不到生效域名的回退值，所以文本不能放前端 i18n）
        lang: getLocale() === 'zh-CN' ? 'zh' : 'en'
      }
    })
    if (res.success && res.data) {
      remoteConfig.value = res.data
    } else {
      ElMessage.error(res.error?.message || t('remoteAccess.generateFailed'))
    }
  } catch {
    ElMessage.error(t('remoteAccess.generateFailed'))
  } finally {
    remoteLoading.value = false
  }
}

// 复制配置到剪贴板
const copyRemoteConfig = async () => {
  if (!remoteConfig.value) return
  try {
    await navigator.clipboard.writeText(remoteConfig.value.config)
    ElMessage.success(t('remoteAccess.copied'))
  } catch {
    ElMessage.error(t('remoteAccess.copyFailed'))
  }
}

// 格式化运行时间
const formatUptime = (uptime: string) => {
  if (!uptime) return '-'
  return uptime
}

// 格式化内存
const formatMemory = (mb: number) => {
  if (!mb) return '-'
  if (mb >= 1024) return `${(mb / 1024).toFixed(2)} GB`
  return `${mb} MB`
}

onMounted(loadSystemInfo)
</script>

<template>
  <div class="system-info-page" v-loading="loading">
    <div class="page-header page-header--with-actions">
      <div class="header-left">
        <h1>{{ $t('systemInfo.title') }}</h1>
        <p>{{ $t('systemInfo.subtitle') }}</p>
      </div>
      <div class="header-actions">
        <el-button :icon="Refresh" :loading="loading" @click="loadSystemInfo">{{ $t('common.refresh') }}</el-button>
      </div>
    </div>

    <!-- 系统信息卡：字段跟随 /system/info 的分组结构，保留最常看的六项；
         全量八组模块在仪表盘 -->
    <div class="info-card">
      <div class="card-header">
        <div class="card-icon system"><el-icon :size="20"><Monitor /></el-icon></div>
        <div class="card-title">
          <h3>{{ $t('systemInfo.cardTitle') }}</h3>
          <p>{{ $t('systemInfo.cardDesc') }}</p>
        </div>
      </div>
      <div class="card-content">
        <div class="info-grid" v-if="systemInfo">
          <div class="info-item">
            <span class="info-label">{{ $t('systemInfo.systemName') }}</span>
            <span class="info-value">{{ systemInfo.name }}</span>
          </div>
          <div class="info-item">
            <span class="info-label">{{ $t('systemInfo.version') }}</span>
            <span class="info-value">{{ systemInfo.version }}</span>
          </div>
          <div class="info-item">
            <span class="info-label">{{ $t('systemInfo.os') }}</span>
            <span class="info-value">{{ systemInfo.operatingSystem?.description || '-' }}</span>
          </div>
          <div class="info-item">
            <span class="info-label">{{ $t('systemInfo.hostname') }}</span>
            <span class="info-value">{{ systemInfo.machineName }}</span>
          </div>
          <div class="info-item">
            <span class="info-label">{{ $t('systemInfo.cpuCores') }}</span>
            <span class="info-value">{{ systemInfo.cpu?.logicalCores }} {{ $t('dashboard.cores') }}</span>
          </div>
          <div class="info-item">
            <span class="info-label">{{ $t('systemInfo.dotnetVersion') }}</span>
            <span class="info-value">.NET {{ systemInfo.runtime?.version }}</span>
          </div>
        </div>

        <el-divider v-if="systemStatus" />

        <div class="status-grid" v-if="systemStatus">
          <div class="status-item">
            <div class="status-icon running"><el-icon><InfoFilled /></el-icon></div>
            <div class="status-info">
              <span class="status-label">{{ $t('systemInfo.runningStatus') }}</span>
              <span class="status-value success">{{ $t('systemInfo.running') }}</span>
            </div>
          </div>
          <div class="status-item">
            <div class="status-icon time"><el-icon><InfoFilled /></el-icon></div>
            <div class="status-info">
              <span class="status-label">{{ $t('systemInfo.uptime') }}</span>
              <span class="status-value">{{ formatUptime(systemStatus.uptime) }}</span>
            </div>
          </div>
          <div class="status-item">
            <div class="status-icon memory"><el-icon><InfoFilled /></el-icon></div>
            <div class="status-info">
              <span class="status-label">{{ $t('systemInfo.memoryUsage') }}</span>
              <span class="status-value">{{ formatMemory(systemStatus.memoryUsageMB) }}</span>
            </div>
          </div>
          <div class="status-item">
            <div class="status-icon threads"><el-icon><InfoFilled /></el-icon></div>
            <div class="status-info">
              <span class="status-label">{{ $t('systemInfo.threadCount') }}</span>
              <span class="status-value">{{ systemStatus.threadCount }}</span>
            </div>
          </div>
          <div class="status-item">
            <div class="status-icon cpu"><el-icon><InfoFilled /></el-icon></div>
            <div class="status-info">
              <span class="status-label">{{ $t('systemInfo.cpuUsage') }}</span>
              <span class="status-value">{{ systemInfo?.cpu?.processCpuPercent?.toFixed(1) ?? '-' }}%</span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 外网访问配置卡（ROADMAP Phase 3）：不自建中继云，生成配置 + 操作步骤 -->
    <div class="info-card">
      <div class="card-header">
        <div class="card-title">
          <h3>{{ $t('remoteAccess.title') }}</h3>
          <p>{{ $t('remoteAccess.desc') }}</p>
        </div>
      </div>
      <div class="card-content">
        <div class="remote-form">
          <el-select v-model="remoteProvider" style="width: 180px">
            <el-option value="cloudflare" :label="$t('remoteAccess.providerCloudflare')" />
            <el-option value="frp" :label="$t('remoteAccess.providerFrp')" />
            <el-option value="tailscale" :label="$t('remoteAccess.providerTailscale')" />
          </el-select>
          <el-input
            v-model="remoteHostname"
            :placeholder="$t('remoteAccess.hostnamePlaceholder')"
            style="flex: 1; min-width: 200px"
          />
          <el-input-number
            v-model="remoteLocalPort"
            :min="1"
            :max="65535"
            :controls="false"
            :placeholder="$t('remoteAccess.localPortPlaceholder')"
            style="width: 120px"
          />
          <el-button type="primary" :loading="remoteLoading" @click="generateRemoteConfig">
            {{ $t('remoteAccess.generate') }}
          </el-button>
        </div>
        <!-- 域名回退行为明示：留空时后端用示例域名占位，用户无从知晓
             回退发生过；不写清楚会拿到一份指向假域名的配置 -->
        <div class="form-hint">{{ $t('remoteAccess.hostnameHint') }}</div>

        <div v-if="remoteConfig" class="remote-result">
          <div class="remote-config-header">
            <span class="remote-file-name">{{ remoteConfig.fileName }}</span>
            <el-button link type="primary" @click="copyRemoteConfig">
              {{ $t('remoteAccess.copy') }}
            </el-button>
          </div>
          <pre class="remote-config-body">{{ remoteConfig.config }}</pre>
          <ol class="remote-steps">
            <li v-for="(step, index) in remoteConfig.steps" :key="index">{{ step }}</li>
          </ol>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.system-info-page {
  padding: 16px;
  max-width: 860px;
}



.info-card {
  background: var(--el-bg-color);
  border-radius: var(--mac-radius-container);
  border: 1px solid var(--el-border-color-lighter);
  overflow: hidden;
  margin-bottom: 16px;

  .card-header {
    display: flex;
    align-items: center;
    gap: 16px;
    padding: 20px 24px;
    border-bottom: 1px solid var(--el-border-color-lighter);

    .card-icon {
      width: 44px;
      height: 44px;
      border-radius: var(--mac-radius-card);
      display: flex;
      align-items: center;
      justify-content: center;
      color: white;
      flex-shrink: 0;

      &.system { background: linear-gradient(135deg, var(--el-color-primary), var(--el-color-primary-light-3)); }
    }

    .card-title {
      h3 { margin: 0; font-size: 16px; font-weight: 600; }
      p { margin: 4px 0 0; font-size: 13px; color: var(--el-text-color-secondary); }
    }
  }

  .card-content { padding: 24px; }
}

.info-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.info-item { display: flex; flex-direction: column; gap: 4px;
  .info-label { font-size: 12px; color: var(--el-text-color-secondary); }
  .info-value { font-size: 14px; font-weight: 500; word-break: break-all; }
}

.status-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.status-item { display: flex; align-items: center; gap: 12px;
  .status-icon { width: 36px; height: 36px; border-radius: var(--mac-radius-control); display: flex; align-items: center; justify-content: center;
    &.running { background: color-mix(in srgb, var(--el-color-success) 12%, transparent); color: var(--el-color-success); }
    &.time { background: color-mix(in srgb, var(--el-color-primary) 12%, transparent); color: var(--el-color-primary); }
    &.memory { background: color-mix(in srgb, var(--el-color-warning) 12%, transparent); color: var(--el-color-warning); }
    &.threads { background: color-mix(in srgb, var(--el-color-primary) 12%, transparent); color: var(--el-color-primary); }
    &.cpu { background: color-mix(in srgb, var(--el-color-danger) 12%, transparent); color: var(--el-color-danger); }
  }
  .status-info { .status-label { display: block; font-size: 12px; color: var(--el-text-color-secondary); } .status-value { display: block; font-size: 14px; font-weight: 500; &.success { color: var(--el-color-success); } } }
}

.remote-form {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 16px;
}

.remote-result {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--mac-radius-control);
  padding: 12px;
}

.remote-config-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;

  .remote-file-name {
    font-family: monospace;
    font-size: 13px;
    color: var(--el-text-color-primary);
  }
}

.remote-config-body {
  margin: 0 0 12px;
  padding: 12px;
  background: var(--el-fill-color-light);
  border-radius: var(--mac-radius-pill);
  font-size: 12px;
  line-height: 1.5;
  overflow-x: auto;
  white-space: pre;
}

.remote-steps {
  margin: 0;
  padding-left: 16px;
  font-size: 13px;
  color: var(--el-text-color-regular);

  li {
    margin-bottom: 6px;
    word-break: break-all;
  }
}

@media (max-width: 768px) {
  .info-grid { grid-template-columns: 1fr; }
  .status-grid { grid-template-columns: 1fr; }
}
</style>

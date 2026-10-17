<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUserStore } from '@/stores/user'
import { storageApi } from '@/api/storage'
import { fileApi } from '@/api/file'
import http from '@/api/http'
import { 
  Files, User, Box, Share,
  Monitor, Timer, Cpu, Connection, Refresh,
  Picture, VideoPlay, Headset, Document, FolderOpened, Warning
} from '@element-plus/icons-vue'
import { formatSize, getPoolStatusInfo } from '@/utils/format'

const { t } = useI18n()
const userStore = useUserStore()

// 存储概览（池账目口径：已分配/剩余只累计设了上限的启用池，
// 物理磁盘信息不在这里，见 loadDrives 的物理磁盘卡）
const storageOverview = ref({
  folderSizeBytes: 0,
  usedBytes: 0,
  availableBytes: 0,
  usagePercentage: 0,
  hasCapacityLimit: false,
  poolAllocatedBytes: 0,
  poolRemainingBytes: 0,
  poolCount: 0,
  diskCount: 0,
  healthStatus: ''
})

// 物理磁盘列表（/system/drives 真实盘信息，与存储池是两个概念）
const drives = ref<any[]>([])
const drivesError = ref('')

// 存储池列表
const storagePools = ref<any[]>([])

// 文件统计
const fileStats = ref({
  totalFiles: 0,
  totalFolders: 0,
  totalSizeBytes: 0,
  totalSizeDisplay: '0 B',
  imageCount: 0,
  videoCount: 0,
  audioCount: 0,
  documentCount: 0,
  otherCount: 0,
  recentFilesCount: 0
})

// 系统状态
const systemStatus = ref({
  status: 'Unknown',
  uptime: '0.00:00:00',
  memoryUsageMB: 0,
  cpuTime: '00:00:00',
  threadCount: 0,
  handleCount: 0
})

// 系统信息
const systemInfo = ref({
  name: 'NAS System',
  version: '1.0.0',
  os: '',
  machineName: '',
  processorCount: 0,
  dotNetVersion: '',
  startTime: ''
})

const loading = ref(false)
const lastUpdateTime = ref<Date | null>(null)
const autoRefresh = ref(true)
let refreshTimer: ReturnType<typeof setInterval> | null = null

// 格式化运行时间
const formatUptime = (uptime: string): string => {
  const parts = uptime.split('.')
  if (parts.length === 2) {
    const days = parseInt(parts[0])
    const time = parts[1]
    if (days > 0) {
      return `${days}${t('dashboard.days')} ${time}`
    }
    return time
  }
  return uptime
}

// 加载存储概览
const loadStorageOverview = async () => {
  try {
    const response = await storageApi.getOverview() as any
    if (response.success && response.data) {
      storageOverview.value = response.data
    }
  } catch (e) {
    console.error('加载存储概览失败', e)
  }
}

// 加载存储池列表
const loadStoragePools = async () => {
  try {
    const response = await storageApi.getPools() as any
    if (response.success && response.data) {
      storagePools.value = response.data
    }
  } catch (e) {
    console.error('加载存储池失败', e)
  }
}

// 加载物理磁盘列表。与存储池概览分开：这里是 DriveInfo 的真实盘信息
// （总容量/剩余/用量），池的"已分配/已用"是库内账目，两者不可互相推导。
// 该端点要求管理员权限，非管理员拿到 403 时展示权限提示而不是静默空白
const loadDrives = async () => {
  drivesError.value = ''
  try {
    const response = await storageApi.getDrives() as any
    if (response.success && response.data) {
      drives.value = response.data
    }
  } catch (e: any) {
    drives.value = []
    drivesError.value = e?.response?.status === 403
      ? t('dashboard.drivesNoPermission')
      : t('dashboard.drivesLoadFailed')
  }
}

// 磁盘用量比例（0-100）：已用 = 总容量 − 可用
const driveUsedPercent = (drive: any): number => {
  if (!drive.totalSize) return 0
  return Math.round(((drive.totalSize - drive.availableSpace) / drive.totalSize) * 1000) / 10
}

// 磁盘进度条配色：与存储池同一组阈值口径
const driveBarColor = (percent: number): string => {
  if (percent >= 90) return '#ef4444'
  if (percent >= 70) return '#f59e0b'
  return '#10b981'
}

// 加载文件统计
const loadFileStats = async () => {
  try {
    const response = await fileApi.getStatistics() as any
    if (response.success && response.data) {
      fileStats.value = response.data
    }
  } catch (e) {
    console.error('加载文件统计失败', e)
  }
}

// 加载系统状态
const loadSystemStatus = async () => {
  try {
    const response = await http.get('/system/status') as any
    if (response.success && response.data) {
      systemStatus.value = response.data
    }
  } catch (e) {
    console.error('加载系统状态失败', e)
  }
}

// 加载系统信息
const loadSystemInfo = async () => {
  try {
    const response = await http.get('/system/info') as any
    if (response.success && response.data) {
      systemInfo.value = response.data
    }
  } catch (e) {
    console.error('加载系统信息失败', e)
  }
}

// 加载所有数据
const loadAllData = async () => {
  loading.value = true
  try {
    await Promise.all([
      loadStorageOverview(),
      loadStoragePools(),
      loadDrives(),
      loadFileStats(),
      loadSystemStatus(),
      loadSystemInfo()
    ])
    lastUpdateTime.value = new Date()
  } finally {
    loading.value = false
  }
}

// 手动刷新
const handleRefresh = () => {
  loadAllData()
}

// 切换自动刷新
const toggleAutoRefresh = () => {
  autoRefresh.value = !autoRefresh.value
  if (autoRefresh.value) {
    startAutoRefresh()
  } else {
    stopAutoRefresh()
  }
}

// 启动自动刷新（ITM-N19：后台标签页暂停，避免不可见页面的无效请求风暴）
// 间隔 60s：这三个数据（容量概览、文件统计、进程状态）都不是秒级精度，
// 30s 时每秒都在重跑全池目录遍历与 5 条聚合 SQL，收益不抵成本
const REFRESH_INTERVAL_MS = 60000
const startAutoRefresh = () => {
  if (refreshTimer) return
  const tick = () => {
    if (document.visibilityState === 'hidden') return
    loadStorageOverview()
    loadStoragePools()
    loadDrives()
    loadFileStats()
    loadSystemStatus()
    lastUpdateTime.value = new Date()
  }
  // 不在这里立即 tick：onMounted 的 loadAllData 刚取过同一批数据，
  // 立即重发一遍是纯重复（实测首屏 11 个请求里有 3 个是重复的）
  refreshTimer = setInterval(tick, REFRESH_INTERVAL_MS)
}
// 标签页可见性变化时恢复自动刷新（具名函数，与 onUnmounted 的 removeEventListener 必须是同一引用，否则每次进入页面叠加一个匿名监听器）
const handleVisibilityChange = () => {
  if (document.visibilityState === 'visible') startAutoRefresh()
}
document.addEventListener('visibilitychange', handleVisibilityChange)

// 停止自动刷新
const stopAutoRefresh = () => {
  if (refreshTimer) {
    clearInterval(refreshTimer)
    refreshTimer = null
  }
}

// 格式化最后更新时间
const formattedLastUpdate = computed(() => {
  if (!lastUpdateTime.value) return '-'
  return lastUpdateTime.value.toLocaleTimeString('zh-CN')
})

onMounted(() => {
  loadAllData()
  if (autoRefresh.value) {
    startAutoRefresh()
  }
})

onUnmounted(() => {
  stopAutoRefresh()
  document.removeEventListener('visibilitychange', handleVisibilityChange)
})
</script>

<template>
  <div class="dashboard-page">
    <!-- 头部 -->
    <div class="dashboard-header">
      <div class="welcome-section">
        <h1>{{ t('dashboard.welcome') }}，{{ userStore.displayName || userStore.username }}</h1>
        <p>NAS System {{ t('dashboard.subtitle') }}</p>
      </div>
      <div class="header-actions">
        <span class="last-update">{{ t('dashboard.lastUpdate') }}: {{ formattedLastUpdate }}</span>
        <el-button-group>
          <el-button :icon="Refresh" @click="handleRefresh" :loading="loading">
            {{ t('common.refresh') }}
          </el-button>
          <el-button 
            :type="autoRefresh ? 'primary' : 'default'"
            @click="toggleAutoRefresh"
          >
            {{ autoRefresh ? t('dashboard.autoRefreshing') : t('dashboard.autoRefresh') }}
          </el-button>
        </el-button-group>
      </div>
    </div>

    <!-- 系统状态卡片 -->
    <el-row :gutter="16" class="stats-row">
      <el-col :xs="12" :sm="6" :md="6">
        <el-card shadow="hover" class="stat-card status-card">
          <div class="stat-content">
            <div class="stat-icon" style="background: linear-gradient(135deg, #67C23A, #85ce61)">
              <el-icon :size="28"><Monitor /></el-icon>
            </div>
            <div class="stat-info">
              <div class="stat-value">{{ systemStatus.status }}</div>
              <div class="stat-label">{{ t('dashboard.systemStatus') }}</div>
            </div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="6" :md="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-content">
            <div class="stat-icon" style="background: linear-gradient(135deg, #409EFF, #66b1ff)">
              <el-icon :size="28"><Timer /></el-icon>
            </div>
            <div class="stat-info">
              <div class="stat-value">{{ formatUptime(systemStatus.uptime) }}</div>
              <div class="stat-label">{{ t('settings.uptime') }}</div>
            </div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="6" :md="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-content">
            <div class="stat-icon" style="background: linear-gradient(135deg, #E6A23C, #ebb563)">
              <el-icon :size="28"><Cpu /></el-icon>
            </div>
            <div class="stat-info">
              <div class="stat-value">{{ systemStatus.memoryUsageMB }} MB</div>
              <div class="stat-label">{{ t('settings.memoryUsage') }}</div>
            </div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="6" :md="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-content">
            <div class="stat-icon" style="background: linear-gradient(135deg, #909399, #a6a9ad)">
              <el-icon :size="28"><Connection /></el-icon>
            </div>
            <div class="stat-info">
              <div class="stat-value">{{ systemStatus.threadCount }}</div>
              <div class="stat-label">{{ t('dashboard.activeThreads') }}</div>
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <!-- 存储池（账目口径：分配与使用，物理磁盘见下方独立卡片） -->
    <el-row :gutter="16">
      <el-col :xs="24" :md="12">
        <el-card class="storage-overview-card">
          <template #header>
            <div class="card-header">
              <span><el-icon><Box /></el-icon> {{ t('storage.pools') }}</span>
              <el-tag v-if="storageOverview.hasCapacityLimit" :type="storageOverview.usagePercentage > 90 ? 'danger' : storageOverview.usagePercentage > 70 ? 'warning' : 'success'">
                {{ t('storage.usagePercent') }} {{ storageOverview.usagePercentage.toFixed(1) }}%
              </el-tag>
            </div>
          </template>
          
          <div class="storage-main">
            <div class="storage-chart" v-if="storageOverview.hasCapacityLimit">
              <el-progress
                type="dashboard"
                :percentage="storageOverview.usagePercentage"
                :width="160"
                :stroke-width="12"
                :color="[
                  { color: '#67C23A', percentage: 60 },
                  { color: '#E6A23C', percentage: 80 },
                  { color: '#F56C6C', percentage: 100 }
                ]"
              >
                <template #default="{ percentage }">
                  <div class="chart-inner">
                    <span class="percentage">{{ percentage.toFixed(1) }}%</span>
                    <span class="label">{{ t('storage.usedSpace') }}</span>
                  </div>
                </template>
              </el-progress>
            </div>

            <div class="storage-details">
              <div class="detail-item">
                <div class="detail-label">
                  <span class="dot" style="background: #409EFF"></span>
                  {{ t('storage.poolCount') }}
                </div>
                <div class="detail-value">{{ storageOverview.poolCount }}</div>
              </div>
              <div class="detail-item">
                <div class="detail-label">
                  <span class="dot" style="background: #E6A23C"></span>
                  {{ t('dashboard.poolAllocated') }}
                </div>
                <div class="detail-value">
                  {{ storageOverview.poolAllocatedBytes > 0 ? formatSize(storageOverview.poolAllocatedBytes) : t('dashboard.noQuotaSet') }}
                </div>
              </div>
              <div class="detail-item">
                <div class="detail-label">
                  <span class="dot" style="background: #F56C6C"></span>
                  {{ t('storage.usedSpace') }}
                </div>
                <div class="detail-value">{{ formatSize(storageOverview.usedBytes) }}</div>
              </div>
              <div class="detail-item">
                <div class="detail-label">
                  <span class="dot" style="background: #67C23A"></span>
                  {{ t('dashboard.poolRemaining') }}
                </div>
                <div class="detail-value">
                  {{ storageOverview.poolAllocatedBytes > 0 ? formatSize(storageOverview.poolRemainingBytes) : t('dashboard.noQuotaSet') }}
                </div>
              </div>
            </div>
          </div>
        </el-card>
      </el-col>
      
      <el-col :xs="24" :md="12">
        <el-card class="file-stats-card">
          <template #header>
            <div class="card-header">
              <span><el-icon><Files /></el-icon> {{ t('dashboard.fileStats') }}</span>
              <el-tag type="info">
                {{ t('dashboard.total') }} {{ fileStats.totalFiles + fileStats.totalFolders }} {{ t('dashboard.items') }}
              </el-tag>
            </div>
          </template>
          
          <div class="file-stats-main">
            <div class="file-stats-summary">
              <div class="summary-item">
                <el-icon :size="24" color="#409EFF"><FolderOpened /></el-icon>
                <div class="summary-info">
                  <span class="count">{{ fileStats.totalFolders }}</span>
                  <span class="label">{{ t('files.folder') }}</span>
                </div>
              </div>
              <div class="summary-item">
                <el-icon :size="24" color="#67C23A"><Document /></el-icon>
                <div class="summary-info">
                  <span class="count">{{ fileStats.totalFiles }}</span>
                  <span class="label">{{ t('files.file') }}</span>
                </div>
              </div>
              <div class="summary-item">
                <el-icon :size="24" color="#E6A23C"><Timer /></el-icon>
                <div class="summary-info">
                  <span class="count">{{ fileStats.recentFilesCount }}</span>
                  <span class="label">{{ t('dashboard.recentAdded') }}</span>
                </div>
              </div>
            </div>
            
            <el-divider />
            
            <div class="file-type-stats">
              <div class="type-item">
                <div class="type-icon" style="background: #67C23A">
                  <el-icon><Picture /></el-icon>
                </div>
                <div class="type-info">
                  <span class="type-name">{{ t('files.image') }}</span>
                  <span class="type-count">{{ fileStats.imageCount }}</span>
                </div>
              </div>
              <div class="type-item">
                <div class="type-icon" style="background: #409EFF">
                  <el-icon><VideoPlay /></el-icon>
                </div>
                <div class="type-info">
                  <span class="type-name">{{ t('files.video') }}</span>
                  <span class="type-count">{{ fileStats.videoCount }}</span>
                </div>
              </div>
              <div class="type-item">
                <div class="type-icon" style="background: #E6A23C">
                  <el-icon><Headset /></el-icon>
                </div>
                <div class="type-info">
                  <span class="type-name">{{ t('files.audio') }}</span>
                  <span class="type-count">{{ fileStats.audioCount }}</span>
                </div>
              </div>
              <div class="type-item">
                <div class="type-icon" style="background: #909399">
                  <el-icon><Document /></el-icon>
                </div>
                <div class="type-info">
                  <span class="type-name">{{ t('files.document') }}</span>
                  <span class="type-count">{{ fileStats.documentCount }}</span>
                </div>
              </div>
              <div class="type-item">
                <div class="type-icon" style="background: #C0C4CC">
                  <el-icon><Files /></el-icon>
                </div>
                <div class="type-info">
                  <span class="type-name">{{ t('files.other') }}</span>
                  <span class="type-count">{{ fileStats.otherCount }}</span>
                </div>
              </div>
            </div>
            
            <div class="total-size">
              <span class="label">{{ t('dashboard.totalFileSize') }}:</span>
              <span class="value">{{ fileStats.totalSizeDisplay || formatSize(fileStats.totalSizeBytes) }}</span>
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <!-- 物理磁盘：DriveInfo 真实盘信息，与上方存储池账目是两个概念，分开展示 -->
    <el-card class="disks-card">
      <template #header>
        <div class="card-header">
          <span><el-icon><Monitor /></el-icon> {{ t('dashboard.physicalDisks') }}</span>
          <el-tag type="info">{{ drives.length }}</el-tag>
        </div>
      </template>

      <div v-if="drivesError" class="disks-error">
        <el-icon><Warning /></el-icon>
        <span>{{ drivesError }}</span>
      </div>
      <div v-else-if="drives.length === 0 && !loading" class="disks-empty">
        {{ t('dashboard.drivesEmpty') }}
      </div>
      <div v-else class="disks-list">
        <div v-for="drive in drives" :key="drive.name" class="disk-item">
          <div class="disk-icon">
            <el-icon :size="24"><Monitor /></el-icon>
          </div>
          <div class="disk-info">
            <div class="disk-name">
              <span class="disk-letter">{{ drive.name }}</span>
              <!-- 无卷标时后端 label 回落为驱动类型（如 Fixed），两者相同只显示一个 -->
              <el-tag size="small" effect="plain">{{ drive.label }}</el-tag>
              <el-tag v-if="drive.label !== drive.driveType" size="small" effect="plain" type="info">{{ drive.driveType }}</el-tag>
            </div>
            <div class="disk-bar">
              <div class="bar-track">
                <div
                  class="bar-fill"
                  :style="{ width: driveUsedPercent(drive) + '%', background: driveBarColor(driveUsedPercent(drive)) }"
                ></div>
              </div>
              <span class="disk-percent" :style="{ color: driveBarColor(driveUsedPercent(drive)) }">
                {{ driveUsedPercent(drive) }}%
              </span>
            </div>
          </div>
          <div class="disk-figures">
            <div class="figure">
              <span class="figure-label">{{ t('dashboard.driveTotal') }}</span>
              <span class="figure-value">{{ formatSize(drive.totalSize) }}</span>
            </div>
            <div class="figure">
              <span class="figure-label">{{ t('dashboard.driveUsed') }}</span>
              <span class="figure-value">{{ formatSize(drive.totalSize - drive.availableSpace) }}</span>
            </div>
            <div class="figure">
              <span class="figure-label">{{ t('dashboard.driveFree') }}</span>
              <span class="figure-value success">{{ formatSize(drive.availableSpace) }}</span>
            </div>
          </div>
        </div>
      </div>
    </el-card>

    <!-- 存储池列表 -->
    <el-card class="pools-card">
      <template #header>
        <div class="card-header">
          <span><el-icon><Box /></el-icon> {{ t('dashboard.poolStatus') }}</span>
          <router-link to="/storage">
            <el-button type="primary" link>{{ t('dashboard.viewAll') }}</el-button>
          </router-link>
        </div>
      </template>
      
      <el-table :data="storagePools" v-loading="loading" style="width: 100%">
        <el-table-column prop="name" :label="t('storage.poolName')" min-width="120">
          <template #default="{ row }">
            <div class="pool-name">
              <el-tag v-if="row.isDefault" size="small" type="success">{{ t('storage.default') }}</el-tag>
              <span>{{ row.name }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="status" :label="t('common.status')" width="100">
          <template #default="{ row }">
            <el-tag :color="getPoolStatusInfo(row.status).color" effect="dark" size="small">
              {{ getPoolStatusInfo(row.status).text }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('dashboard.capacity')" min-width="200">
          <template #default="{ row }">
            <div class="capacity-cell">
              <div class="capacity-text">
                {{ formatSize(row.usedBytes) }}
                <span v-if="row.maxCapacityBytes"> / {{ formatSize(row.maxCapacityBytes) }}</span>
                <span v-else class="no-quota">{{ t('dashboard.noQuotaSet') }}</span>
              </div>
              <el-progress
                v-if="row.maxCapacityBytes"
                :percentage="row.usagePercentage"
                :stroke-width="6"
                :show-text="false"
                :color="row.usagePercentage > 90 ? '#F56C6C' : row.usagePercentage > 70 ? '#E6A23C' : '#67C23A'"
              />
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="usagePercentage" :label="t('storage.usagePercent')" width="100">
          <template #default="{ row }">
            <span v-if="row.maxCapacityBytes" :style="{ color: row.usagePercentage > 90 ? '#F56C6C' : row.usagePercentage > 70 ? '#E6A23C' : '#67C23A' }">
              {{ row.usagePercentage.toFixed(1) }}%
            </span>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column prop="mountPath" :label="t('storage.mountPath')" min-width="150" />
      </el-table>
      
      <el-empty v-if="storagePools.length === 0 && !loading" :description="t('storage.noPools')" />
    </el-card>

    <!-- 快捷操作和系统信息 -->
    <el-row :gutter="16">
      <el-col :xs="24" :md="12">
        <el-card class="quick-actions-card">
          <template #header>
            <span>{{ t('dashboard.quickAccess') }}</span>
          </template>
          <div class="quick-actions">
            <router-link to="/files" class="action-item">
              <el-icon :size="32" color="#409EFF"><Files /></el-icon>
              <span>{{ t('nav.files') }}</span>
            </router-link>
            <router-link to="/shares" class="action-item">
              <el-icon :size="32" color="#67C23A"><Share /></el-icon>
              <span>{{ t('nav.shares') }}</span>
            </router-link>
            <router-link to="/storage" class="action-item">
              <el-icon :size="32" color="#E6A23C"><Box /></el-icon>
              <span>{{ t('nav.storage') }}</span>
            </router-link>
            <router-link to="/users" class="action-item" v-if="userStore.isAdmin">
              <el-icon :size="32" color="#F56C6C"><User /></el-icon>
              <span>{{ t('nav.users') }}</span>
            </router-link>
          </div>
        </el-card>
      </el-col>
      
      <el-col :xs="24" :md="12">
        <el-card class="system-info-card">
          <template #header>
            <span><el-icon><Monitor /></el-icon> {{ t('settings.system') }}</span>
          </template>
          <div class="info-list">
            <div class="info-item">
              <span class="label">{{ t('settings.systemName') }}</span>
              <span class="value">{{ systemInfo.name }}</span>
            </div>
            <div class="info-item">
              <span class="label">{{ t('settings.version') }}</span>
              <span class="value">{{ systemInfo.version }}</span>
            </div>
            <div class="info-item">
              <span class="label">{{ t('settings.hostname') }}</span>
              <span class="value">{{ systemInfo.machineName }}</span>
            </div>
            <div class="info-item">
              <span class="label">{{ t('settings.os') }}</span>
              <span class="value">{{ systemInfo.os }}</span>
            </div>
            <div class="info-item">
              <span class="label">{{ t('settings.cpuCores') }}</span>
              <span class="value">{{ systemInfo.processorCount }} {{ t('dashboard.cores') }}</span>
            </div>
            <div class="info-item">
              <span class="label">{{ t('settings.dotnetVersion') }}</span>
              <span class="value">{{ systemInfo.dotNetVersion }}</span>
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<style lang="scss" scoped>
.dashboard-page {
  padding: 0;
}

.dashboard-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 20px;
  flex-wrap: wrap;
  gap: 16px;
  
  .welcome-section {
    h1 {
      font-size: 24px;
      margin: 0 0 4px 0;
      color: var(--el-text-color-primary);
    }
    
    p {
      margin: 0;
      color: var(--el-text-color-secondary);
      font-size: 14px;
    }
  }
  
  .header-actions {
    display: flex;
    align-items: center;
    gap: 16px;
    
    .last-update {
      font-size: 12px;
      color: var(--el-text-color-secondary);
    }
  }
}

.stats-row {
  margin-bottom: 16px;
}

.stat-card {
  margin-bottom: 16px;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  
  &:hover {
    transform: translateY(-2px);
  }
  
  :deep(.el-card__body) {
    padding: 20px;
  }
  
  .stat-content {
    display: flex;
    align-items: center;
    gap: 16px;
  }
  
  .stat-icon {
    width: 56px;
    height: 56px;
    border-radius: 14px;
    display: flex;
    align-items: center;
    justify-content: center;
    color: #fff;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  }
  
  .stat-info {
    flex: 1;
    min-width: 0;
    
    .stat-value {
      font-size: 22px;
      font-weight: 700;
      color: var(--el-text-color-primary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      letter-spacing: -0.5px;
    }
    
    .stat-label {
      font-size: 13px;
      color: var(--el-text-color-secondary);
      margin-top: 4px;
    }
  }
}

.storage-overview-card {
  margin-bottom: 16px;
  
  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }
  
  .storage-main {
    display: flex;
    align-items: center;
    gap: 40px;
    flex-wrap: wrap;
    
    .storage-chart {
      .chart-inner {
        display: flex;
        flex-direction: column;
        align-items: center;
        
        .percentage {
          font-size: 28px;
          font-weight: 600;
          color: var(--el-text-color-primary);
        }
        
        .label {
          font-size: 14px;
          color: var(--el-text-color-secondary);
        }
      }
    }
    
    .storage-details {
      flex: 1;
      min-width: 200px;
      
      .detail-item {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: 8px 0;
        
        .detail-label {
          display: flex;
          align-items: center;
          gap: 8px;
          color: var(--el-text-color-secondary);
          
          .dot {
            width: 8px;
            height: 8px;
            border-radius: 50%;
          }
        }
        
        .detail-value {
          font-weight: 500;
          color: var(--el-text-color-primary);
        }
      }
    }
  }
}

// 物理磁盘卡：与存储池卡分开的第二个概念，盘符 + 真实容量/用量
.disks-card {
  margin-bottom: 16px;

  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }

  .disks-error {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 16px;
    color: var(--el-text-color-secondary);
    font-size: 13px;
  }

  .disks-empty {
    padding: 24px;
    text-align: center;
    color: var(--el-text-color-secondary);
    font-size: 13px;
  }

  .disks-list {
    display: flex;
    flex-direction: column;
    gap: 16px;

    .disk-item {
      display: flex;
      align-items: center;
      gap: 16px;

      .disk-icon {
        width: 44px;
        height: 44px;
        border-radius: 10px;
        background: linear-gradient(135deg, #64748b, #94a3b8);
        display: flex;
        align-items: center;
        justify-content: center;
        color: white;
        flex-shrink: 0;
      }

      .disk-info {
        flex: 1;
        min-width: 0;

        .disk-name {
          display: flex;
          align-items: center;
          gap: 8px;
          margin-bottom: 8px;

          .disk-letter {
            font-size: 15px;
            font-weight: 600;
          }
        }

        .disk-bar {
          display: flex;
          align-items: center;
          gap: 12px;

          .bar-track {
            flex: 1;
            height: 8px;
            background: var(--el-fill-color);
            border-radius: 4px;
            overflow: hidden;

            .bar-fill {
              height: 100%;
              border-radius: 3px;
              transition: width 0.3s;
            }
          }

          .disk-percent {
            font-size: 13px;
            font-weight: 600;
            min-width: 44px;
            text-align: right;
          }
        }
      }

      .disk-figures {
        display: flex;
        gap: 24px;
        flex-shrink: 0;

        .figure {
          display: flex;
          flex-direction: column;
          gap: 2px;
          min-width: 88px;

          .figure-label {
            font-size: 12px;
            color: var(--el-text-color-secondary);
          }

          .figure-value {
            font-size: 14px;
            font-weight: 600;

            &.success {
              color: #10b981;
            }
          }
        }
      }
    }
  }
}

.file-stats-card {
  margin-bottom: 16px;
  
  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }
  .file-stats-main {
    .file-stats-summary {
      display: flex;
      justify-content: space-around;
      
      .summary-item {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 8px;
        
        .summary-info {
          text-align: center;
          
          .count {
            display: block;
            font-size: 24px;
            font-weight: 600;
            color: var(--el-text-color-primary);
          }
          
          .label {
            font-size: 12px;
            color: var(--el-text-color-secondary);
          }
        }
      }
    }
    
    .file-type-stats {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      justify-content: center;
      
      .type-item {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 8px 12px;
        background: var(--el-fill-color-light);
        border-radius: 8px;
        min-width: 80px;
        
        .type-icon {
          width: 28px;
          height: 28px;
          border-radius: 6px;
          display: flex;
          align-items: center;
          justify-content: center;
          color: #fff;
        }
        
        .type-info {
          .type-name {
            display: block;
            font-size: 12px;
            color: var(--el-text-color-secondary);
          }
          
          .type-count {
            display: block;
            font-size: 16px;
            font-weight: 600;
            color: var(--el-text-color-primary);
          }
        }
      }
    }
    
    .total-size {
      margin-top: 16px;
      padding-top: 12px;
      border-top: 1px solid var(--el-border-color-lighter);
      display: flex;
      justify-content: space-between;
      
      .label {
        color: var(--el-text-color-secondary);
      }
      
      .value {
        font-weight: 600;
        color: var(--el-color-primary);
      }
    }
  }
}

.disk-health-card {
  margin-bottom: 16px;
  
  .health-stats {
    display: flex;
    justify-content: space-around;
    
    .health-item {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
      
      &.healthy {
        color: #67C23A;
      }
      
      &.warning {
        color: #E6A23C;
      }
      
      &.critical {
        color: #F56C6C;
      }
      
      .health-info {
        text-align: center;
        
        .count {
          display: block;
          font-size: 24px;
          font-weight: 600;
        }
        
        .label {
          font-size: 12px;
          color: var(--el-text-color-secondary);
        }
      }
    }
  }
  
  .health-progress {
    .progress-label {
      display: flex;
      justify-content: space-between;
      margin-bottom: 8px;
      font-size: 14px;
      color: var(--el-text-color-secondary);
    }
  }
}

.pools-card {
  margin-bottom: 16px;
  
  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }
  
  .pool-name {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  
  .capacity-cell {
    .capacity-text {
      font-size: 12px;
      margin-bottom: 4px;

      .no-quota {
        color: var(--el-text-color-secondary);
      }
    }
  }
}

.quick-actions-card {
  margin-bottom: 16px;
  
  .quick-actions {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(100px, 1fr));
    gap: 16px;
    
    .action-item {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      padding: 24px 16px;
      border-radius: 12px;
      background: var(--el-fill-color-light);
      text-decoration: none;
      color: var(--el-text-color-primary);
      transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
      border: 1px solid transparent;
      
      &:hover {
        background: linear-gradient(135deg, rgba(99, 102, 241, 0.1), rgba(139, 92, 246, 0.1));
        border-color: rgba(99, 102, 241, 0.3);
        transform: translateY(-4px);
        box-shadow: 0 8px 24px rgba(99, 102, 241, 0.15);
      }
      
      span {
        font-size: 13px;
        font-weight: 500;
      }
    }
  }
}

.system-info-card {
  margin-bottom: 16px;
  
  .info-list {
    .info-item {
      display: flex;
      justify-content: space-between;
      padding: 10px 0;
      border-bottom: 1px solid var(--el-border-color-lighter);
      
      &:last-child {
        border-bottom: none;
      }
      
      .label {
        color: var(--el-text-color-secondary);
      }
      
      .value {
        color: var(--el-text-color-primary);
        font-weight: 500;
        max-width: 60%;
        text-align: right;
        word-break: break-all;
      }
    }
  }
}
</style>

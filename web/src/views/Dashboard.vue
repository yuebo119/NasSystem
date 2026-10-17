<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUserStore } from '@/stores/user'
import { ElMessage } from 'element-plus'
import { storageApi } from '@/api/storage'
import { fileApi } from '@/api/file'
import http from '@/api/http'
import {
  Files, User, Box, Share,
  Monitor, Timer, Cpu, Connection, Refresh,
  Picture, VideoPlay, Headset, Document, FolderOpened, Warning, InfoFilled, Pointer,
  Upload, FolderAdd, Delete, Search, Download
} from '@element-plus/icons-vue'
import { formatSize, getPoolStatusInfo, getFileIcon, getFileIconColor, formatDate } from '@/utils/format'
import { useIsMobile } from '@/utils/useIsMobile'
import { fileShareApi } from '@/api/fileShare'

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

// 分享概览（/file-shares 一次全量，仪表盘只取聚合数字）
const shares = ref<any[]>([])

// 最近文件（/files/recent，倒序前 6 条）
const recentFiles = ref<any[]>([])

// 回收站（只取条目数与总大小，恢复/清空操作在文件管理页）
const recycleBinItems = ref<any[]>([])

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
// 窄屏适配：池状态表列宽收紧、路径列隐藏
const isMobile = useIsMobile()
const lastUpdateTime = ref<Date | null>(null)
const autoRefresh = ref(true)
let refreshTimer: ReturnType<typeof setInterval> | null = null

// 格式化运行时间。空值防御：接口字段变动或首次加载未回填时返回 '-'，
// 不让 uptime.split 以 TypeError 的形式挂在渲染函数里
const formatUptime = (uptime: string): string => {
  if (!uptime) return '-'
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

// 加载分享列表（概览卡聚合用）
const loadShares = async () => {
  try {
    const response = await fileShareApi.getMyShares() as any
    if (response.success && response.data) {
      shares.value = response.data
    }
  } catch (e) {
    console.error('加载分享失败', e)
  }
}

// 加载最近文件
const loadRecentFiles = async () => {
  try {
    const response = await fileApi.getRecentFiles(6) as any
    if (response.success && response.data) {
      recentFiles.value = response.data
    }
  } catch (e) {
    console.error('加载最近文件失败', e)
  }
}

// 加载回收站（条目数 + 总大小）
const loadRecycleBin = async () => {
  try {
    const response = await fileApi.getRecycleBin() as any
    if (response.success && response.data) {
      recycleBinItems.value = response.data
    }
  } catch (e) {
    console.error('加载回收站失败', e)
  }
}

// 快捷操作：强制刷新容量记账（POST /storage/refresh，绕过 60s 节流窗口）。
// 与顶部"刷新"的区别：顶部只重新读取，这个是让后端立即重算并落库
const refreshingCapacity = ref(false)
const handleRefreshCapacity = async () => {
  refreshingCapacity.value = true
  try {
    await storageApi.refreshCapacity() as any
    ElMessage.success(t('dashboard.capacityRefreshed'))
    await Promise.all([loadStorageOverview(), loadStoragePools(), loadDrives()])
  } catch {
    ElMessage.error(t('common.failed'))
  } finally {
    refreshingCapacity.value = false
  }
}

// 快捷操作：扫描同步（物理文件入库）。耗时操作，按钮 loading 常驻
const scanning = ref(false)
const handleScanSync = async () => {
  scanning.value = true
  try {
    const response = await fileApi.scanAndSync() as any
    if (response.success) {
      const r = response.data
      ElMessage.success(t('dashboard.scanDone', {
        added: (r?.addedFiles ?? 0) + (r?.addedFolders ?? 0),
        updated: r?.updatedFiles ?? 0
      }))
      await Promise.all([loadFileStats(), loadRecentFiles(), loadStorageOverview(), loadStoragePools()])
    } else {
      ElMessage.error(response.message || t('common.failed'))
    }
  } catch (e: any) {
    ElMessage.error(e.message || t('common.failed'))
  } finally {
    scanning.value = false
  }
}

// 分享概览聚合：有效 = 启用且未过期且未达下载上限
const shareStats = computed(() => {
  const list = shares.value
  return {
    total: list.length,
    active: list.filter((s: any) => s.isEnabled && !s.isExpired && !s.isDownloadLimitReached).length,
    expired: list.filter((s: any) => s.isExpired).length,
    downloads: list.reduce((sum: number, s: any) => sum + (s.downloadCount || 0), 0)
  }
})

// 回收站聚合
const recycleBinStats = computed(() => ({
  count: recycleBinItems.value.length,
  size: recycleBinItems.value.reduce((sum: number, i: any) => sum + (i.sizeBytes || 0), 0)
}))

// 加载所有数据
const loadAllData = async () => {
  loading.value = true
  try {
    await Promise.all([
      loadStorageOverview(),
      loadStoragePools(),
      loadDrives(),
      loadFileStats(),
      loadShares(),
      loadRecentFiles(),
      loadRecycleBin(),
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
    loadShares()
    loadRecentFiles()
    loadRecycleBin()
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
    <!-- dash-row-gap：本行四张卡 height:100% 撑满 el-col 后，卡片自身的
       margin-bottom 会溢出到 row 之外、推不开后续卡片（实测行尾与磁盘卡
       间距为 0），行间距改由 row 级 margin 提供；卡片 margin 保留给 xs
       折行场景 -->
    <el-row :gutter="16" class="dash-row-gap">
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
              <!-- 多池混合配置（部分池未设上限）时凑不出诚实的总使用率，说明为何不显示圆环 -->
              <div v-if="storageOverview.poolCount > 0 && !storageOverview.hasCapacityLimit" class="pool-quota-hint">
                <el-icon><InfoFilled /></el-icon>
                <span>{{ t('dashboard.mixedQuotaHint') }}</span>
              </div>
            </div>
          </div>
        </el-card>
      </el-col>

      <!-- 物理磁盘列：与存储池账目左右并列，两个存储概念同屏对照 -->
      <el-col :xs="24" :md="12">
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
            <!-- 多盘自适应网格：每盘一张紧凑磁贴，盘数多时自动分栏，窄屏退化为单列 -->
            <div v-for="drive in drives" :key="drive.name" class="disk-item">
              <div class="disk-head">
                <div class="disk-icon">
                  <el-icon :size="20"><Monitor /></el-icon>
                </div>
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
      </el-col>
    </el-row>

    <!-- 内容统计行：文件统计 + 分享概览 -->
    <el-row :gutter="16" class="dash-row-gap">
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

      <!-- 分享概览：分享是 NAS 的核心对外功能，概览卡补上这块数据 -->
      <el-col :xs="24" :md="12">
        <el-card class="shares-overview-card">
          <template #header>
            <div class="card-header">
              <span><el-icon><Share /></el-icon> {{ t('dashboard.sharesOverview') }}</span>
              <router-link to="/shares">
                <el-button type="primary" link>{{ t('dashboard.viewAll') }}</el-button>
              </router-link>
            </div>
          </template>
          <div class="shares-overview-main">
            <div class="big-stat">
              <div class="big-value">{{ shareStats.active }}</div>
              <div class="big-label">{{ t('dashboard.activeShares') }}</div>
            </div>
            <div class="big-stat">
              <div class="big-value">
                <el-icon :size="22" color="#3b82f6"><Download /></el-icon>
                {{ shareStats.downloads }}
              </div>
              <div class="big-label">{{ t('dashboard.totalDownloads') }}</div>
            </div>
          </div>
          <el-divider />
          <div class="shares-mini-stats">
            <div class="mini-stat">
              <span class="mini-label">{{ t('shares.totalShares') }}</span>
              <span class="mini-value">{{ shareStats.total }}</span>
            </div>
            <div class="mini-stat">
              <span class="mini-label">{{ t('shares.expired') }}</span>
              <span class="mini-value warning">{{ shareStats.expired }}</span>
            </div>
            <div class="mini-stat">
              <span class="mini-label">{{ t('dashboard.passwordProtected') }}</span>
              <span class="mini-value">{{ shares.filter((s: any) => s.accessType === 1).length }}</span>
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <!-- 最近文件 + 回收站 -->
    <el-row :gutter="16" class="dash-row-gap">
      <el-col :xs="24" :md="16">
        <el-card class="recent-files-card">
          <template #header>
            <div class="card-header">
              <span><el-icon><Document /></el-icon> {{ t('dashboard.recentFiles') }}</span>
              <router-link to="/files">
                <el-button type="primary" link>{{ t('dashboard.viewAll') }}</el-button>
              </router-link>
            </div>
          </template>
          <div class="recent-list" v-loading="loading">
            <div v-for="item in recentFiles" :key="item.id" class="recent-item" @click="$router.push('/files')">
              <div class="file-icon" :style="{ background: getFileIconColor(item) + '20', color: getFileIconColor(item) }">
                <el-icon :size="18"><component :is="getFileIcon(item)" /></el-icon>
              </div>
              <div class="recent-info">
                <div class="recent-name" :title="item.name">{{ item.name }}</div>
                <div class="recent-meta">{{ item.path }}</div>
              </div>
              <div class="recent-side">
                <span class="recent-size">{{ item.isFolder ? t('files.folder') : formatSize(item.sizeBytes) }}</span>
                <span class="recent-time">{{ formatDate(item.createdAt) }}</span>
              </div>
            </div>
            <div v-if="recentFiles.length === 0 && !loading" class="recent-empty">
              <el-icon :size="40" color="#d1d5db"><Document /></el-icon>
              <p>{{ t('dashboard.recentEmpty') }}</p>
            </div>
          </div>
        </el-card>
      </el-col>

      <el-col :xs="24" :md="8">
        <el-card class="recycle-card">
          <template #header>
            <div class="card-header">
              <span><el-icon><Delete /></el-icon> {{ t('filesView.recycleBinTitle') }}</span>
            </div>
          </template>
          <div class="recycle-main">
            <div class="recycle-count">{{ recycleBinStats.count }}</div>
            <div class="recycle-meta">
              <div class="recycle-size">{{ t('dashboard.occupiedSpace') }} {{ formatSize(recycleBinStats.size) }}</div>
              <div class="recycle-hint">{{ t('dashboard.recycleHint') }}</div>
            </div>
          </div>
          <router-link to="/files?recycle=1">
            <el-button type="primary" plain class="recycle-btn">{{ t('dashboard.manageRecycle') }}</el-button>
          </router-link>
        </el-card>
      </el-col>
    </el-row>

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
      
      <!-- 多池时表内滚动，避免池多把页面撑得很长。窄屏列宽收紧、
           路径列隐藏（最不关键的信息），让表格在手机上不必横滚 -->
      <el-table :data="storagePools" v-loading="loading" style="width: 100%" max-height="400">
        <el-table-column prop="name" :label="t('storage.poolName')" :min-width="isMobile ? 100 : 120">
          <template #default="{ row }">
            <div class="pool-name">
              <el-tag v-if="row.isDefault" size="small" type="success">{{ t('storage.default') }}</el-tag>
              <span>{{ row.name }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="status" :label="t('common.status')" :width="isMobile ? 70 : 100">
          <template #default="{ row }">
            <el-tag :color="getPoolStatusInfo(row.status).color" effect="dark" size="small">
              {{ getPoolStatusInfo(row.status).text }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('dashboard.capacity')" :min-width="isMobile ? 130 : 200">
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
        <el-table-column prop="usagePercentage" :label="t('storage.usagePercent')" :width="isMobile ? 70 : 100">
          <template #default="{ row }">
            <span v-if="row.maxCapacityBytes" :style="{ color: row.usagePercentage > 90 ? '#F56C6C' : row.usagePercentage > 70 ? '#E6A23C' : '#67C23A' }">
              {{ row.usagePercentage.toFixed(1) }}%
            </span>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column v-if="!isMobile" prop="mountPath" :label="t('storage.mountPath')" min-width="150" />
      </el-table>
      
      <el-empty v-if="storagePools.length === 0 && !loading" :description="t('storage.noPools')" />
    </el-card>

    <!-- 快捷操作：导航 + 实际行动混合。导航类带副标题说明去向，
         动作类（刷新容量/扫描同步）直接调 API 并原地反馈结果 -->
    <el-card class="quick-actions-card">
      <template #header>
        <div class="card-header">
          <span><el-icon><Pointer /></el-icon> {{ t('dashboard.quickActions') }}</span>
        </div>
      </template>
      <div class="quick-actions">
        <router-link to="/files" class="action-item">
          <div class="action-icon files"><el-icon :size="22"><Files /></el-icon></div>
          <div class="action-text">
            <span class="action-name">{{ t('nav.files') }}</span>
            <span class="action-desc">{{ t('dashboard.actionFilesDesc') }}</span>
          </div>
        </router-link>
        <router-link to="/files?upload=1" class="action-item">
          <div class="action-icon upload"><el-icon :size="22"><Upload /></el-icon></div>
          <div class="action-text">
            <span class="action-name">{{ t('dashboard.actionUpload') }}</span>
            <span class="action-desc">{{ t('dashboard.actionUploadDesc') }}</span>
          </div>
        </router-link>
        <router-link to="/files?newFolder=1" class="action-item">
          <div class="action-icon folder"><el-icon :size="22"><FolderAdd /></el-icon></div>
          <div class="action-text">
            <span class="action-name">{{ t('dashboard.actionNewFolder') }}</span>
            <span class="action-desc">{{ t('dashboard.actionNewFolderDesc') }}</span>
          </div>
        </router-link>
        <router-link to="/shares" class="action-item">
          <div class="action-icon share"><el-icon :size="22"><Share /></el-icon></div>
          <div class="action-text">
            <span class="action-name">{{ t('nav.shares') }}</span>
            <span class="action-desc">{{ t('dashboard.actionSharesDesc') }}</span>
          </div>
        </router-link>
        <router-link to="/storage" class="action-item">
          <div class="action-icon storage"><el-icon :size="22"><Box /></el-icon></div>
          <div class="action-text">
            <span class="action-name">{{ t('nav.storage') }}</span>
            <span class="action-desc">{{ t('dashboard.actionStorageDesc') }}</span>
          </div>
        </router-link>
        <router-link to="/users" class="action-item" v-if="userStore.isAdmin">
          <div class="action-icon users"><el-icon :size="22"><User /></el-icon></div>
          <div class="action-text">
            <span class="action-name">{{ t('nav.users') }}</span>
            <span class="action-desc">{{ t('dashboard.actionUsersDesc') }}</span>
          </div>
        </router-link>
        <div class="action-item clickable" @click="handleRefreshCapacity">
          <div class="action-icon refresh"><el-icon :size="22" :class="{ spinning: refreshingCapacity }"><Refresh /></el-icon></div>
          <div class="action-text">
            <span class="action-name">{{ t('dashboard.actionRefreshCapacity') }}</span>
            <span class="action-desc">{{ t('dashboard.actionRefreshCapacityDesc') }}</span>
          </div>
        </div>
        <div class="action-item clickable" @click="handleScanSync">
          <div class="action-icon scan"><el-icon :size="22" :class="{ spinning: scanning }"><Search /></el-icon></div>
          <div class="action-text">
            <span class="action-name">{{ t('dashboard.actionScanSync') }}</span>
            <span class="action-desc">{{ t('dashboard.actionScanSyncDesc') }}</span>
          </div>
        </div>
        <router-link to="/files?recycle=1" class="action-item">
          <div class="action-icon recycle"><el-icon :size="22"><Delete /></el-icon></div>
          <div class="action-text">
            <span class="action-name">{{ t('dashboard.actionRecycle') }}</span>
            <span class="action-desc">{{ t('dashboard.actionRecycleDesc') }}</span>
          </div>
        </router-link>
      </div>
    </el-card>

    <!-- 系统信息横条：静态详情，横向铺开比占半张卡更省地方 -->
    <el-card class="system-info-strip">
      <div class="strip-item">
        <span class="strip-label">{{ t('settings.systemName') }}</span>
        <span class="strip-value">{{ systemInfo.name }}</span>
      </div>
      <div class="strip-item">
        <span class="strip-label">{{ t('settings.version') }}</span>
        <span class="strip-value">v{{ systemInfo.version }}</span>
      </div>
      <div class="strip-item">
        <span class="strip-label">{{ t('settings.hostname') }}</span>
        <span class="strip-value">{{ systemInfo.machineName }}</span>
      </div>
      <div class="strip-item">
        <span class="strip-label">{{ t('settings.os') }}</span>
        <span class="strip-value">{{ systemInfo.os }}</span>
      </div>
      <div class="strip-item">
        <span class="strip-label">{{ t('settings.cpuCores') }}</span>
        <span class="strip-value">{{ systemInfo.processorCount }} {{ t('dashboard.cores') }}</span>
      </div>
      <div class="strip-item">
        <span class="strip-label">{{ t('settings.dotnetVersion') }}</span>
        <span class="strip-value">.NET {{ systemInfo.dotNetVersion }}</span>
      </div>
    </el-card>
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
  // 与卡片间距统一为 16px：此前这里是 20px，页面出现"头部-卡片"与
  // "卡片-卡片"两套间距
  margin-bottom: 16px;
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

// stats-row 不设行级 margin：行间距统一由卡自身的 margin-bottom 提供。
// 早先两者叠加使这一行与下方间距翻倍（32px），且 xs 断点两行卡会紧贴
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

// 并排卡片等高：el-row 的 align-items 默认 stretch 已把 el-col 拉到同行最高，
// 这里让 el-card 撑满 el-col，消除左右两张卡底边不齐。
// header 排版（flex 两端分布、图标与文字间距）四卡统一收口，原先各写各的。
// 注意：这里只挂 height:100%，不挂 margin-bottom——卡片自身 margin 会让
// 百分比高度参照的列高把外边距也算进去（列高 = 卡高 + margin 的反馈），
// 窄屏堆叠时卡内底部撑出一段死空白。行间距由 .dash-row-gap 统一提供
.storage-overview-card,
.file-stats-card,
.disks-card,
.shares-overview-card,
.recent-files-card,
.recycle-card {
  height: 100%;

  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }

  .card-header > span {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    min-width: 0;
  }
}

// 等高行（四张卡）的行级间距：卡片 margin-bottom 被 height:100% 溢出吃掉，
// 推不开行外后续卡片，改由 row 提供，保证行与行之间也是统一的 16px
// 行级间距：margin-bottom 推开下方行（卡片自身 margin 被 height:100% 吃掉，
// 不能用来推间距）；row-gap 管窄屏堆叠时换行列之间的竖向间距——
// el-row 的 gutter 只做水平分栏，竖向往全靠它
.dash-row-gap {
  margin-bottom: 16px;
  row-gap: 16px;
}

.storage-overview-card {
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

      .pool-quota-hint {
        display: flex;
        align-items: center;
        gap: 6px;
        margin-top: 8px;
        padding-top: 12px;
        border-top: 1px dashed var(--el-border-color-lighter);
        font-size: 12px;
        color: var(--el-text-color-secondary);
      }
    }
  }
}

// 物理磁盘卡：与存储池卡分开的第二个概念。多盘自适应网格，
// 盘数多时自动分栏（NAS 动辄 4-8 盘），窄屏退化单列。
// margin-bottom 由 .dash-row-gap 行级提供（原因见等高卡规则处的注释）
.disks-card {
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

  // auto-fit 而非 auto-fill：盘数少于轨道数时剩余轨道折叠、现有磁贴撑满，
  // 单盘不会在宽卡片里孤零零占一列；多盘（4-8）自动分栏，窄屏退化单列
  .disks-list {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
    gap: 16px;

    .disk-item {
      display: flex;
      flex-direction: column;
      gap: 12px;
      padding: 16px;
      border: 1px solid var(--el-border-color-lighter);
      border-radius: 12px;

      .disk-head {
        display: flex;
        align-items: center;
        gap: 8px;
        min-width: 0;

        .disk-icon {
          width: 36px;
          height: 36px;
          border-radius: 9px;
          background: linear-gradient(135deg, #64748b, #94a3b8);
          display: flex;
          align-items: center;
          justify-content: center;
          color: white;
          flex-shrink: 0;
        }

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

      .disk-figures {
        display: flex;
        flex-wrap: wrap;
        gap: 8px 16px;

        .figure {
          display: flex;
          flex-direction: column;
          gap: 2px;
          min-width: 76px;

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

// 快捷操作：全宽网格，磁贴横向排（图标 + 名称 + 副标题），
// 导航与实际行动同一形态，实际行动的图标在执行中旋转
.quick-actions-card {
  margin-bottom: 16px;

  .quick-actions {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
    gap: 12px;

    .action-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 14px 16px;
      border-radius: var(--mac-radius-control);
      background: var(--el-fill-color-light);
      text-decoration: none;
      color: var(--el-text-color-primary);
      transition: all 0.2s ease;
      border: 1px solid transparent;
      cursor: pointer;

      &:hover {
        border-color: color-mix(in srgb, var(--el-color-primary) 30%, transparent);
        transform: translateY(-1px);
        box-shadow: var(--card-shadow-hover);
      }

      .action-icon {
        width: 40px;
        height: 40px;
        border-radius: 10px;
        display: flex;
        align-items: center;
        justify-content: center;
        color: #fff;
        flex-shrink: 0;

        &.files { background: #3b82f6; }
        &.upload { background: #10b981; }
        &.folder { background: #f59e0b; }
        &.share { background: #8b5cf6; }
        &.storage { background: #64748b; }
        &.users { background: #ef4444; }
        &.refresh { background: #0ea5e9; }
        &.scan { background: #14b8a6; }
        &.recycle { background: #94a3b8; }

        .spinning {
          animation: spin 1s linear infinite;
        }
      }

      .action-text {
        display: flex;
        flex-direction: column;
        gap: 2px;
        min-width: 0;

        .action-name {
          font-size: 13px;
          font-weight: 600;
        }

        .action-desc {
          font-size: 12px;
          color: var(--el-text-color-secondary);
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }
      }
    }
  }
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

// 系统信息横条：静态详情横向铺开，发丝线分隔，比占半张卡省地方
.system-info-strip {
  display: flex;
  flex-wrap: wrap;
  margin-bottom: 16px;

  .strip-item {
    display: flex;
    flex-direction: column;
    gap: 4px;
    padding: 4px 24px;
    min-width: 140px;
    flex: 1;

    & + .strip-item {
      border-left: 1px solid var(--mac-hairline);
    }

    .strip-label {
      font-size: 12px;
      color: var(--el-text-color-secondary);
    }

    .strip-value {
      font-size: 13px;
      font-weight: 600;
      word-break: break-all;
    }
  }
}

// 分享概览卡：两个大数字 + 三个小指标
.shares-overview-card {
  height: 100%;
  margin-bottom: 16px;

  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }

  .shares-overview-main {
    display: flex;
    flex-wrap: wrap;
    gap: 16px 48px;

    .big-stat {
      .big-value {
        display: flex;
        align-items: center;
        gap: 6px;
        font-size: 32px;
        font-weight: 700;
        letter-spacing: -0.5px;
      }

      .big-label {
        font-size: 13px;
        color: var(--el-text-color-secondary);
        margin-top: 4px;
      }
    }
  }

  .shares-mini-stats {
    display: flex;
    justify-content: space-between;

    .mini-stat {
      display: flex;
      flex-direction: column;
      gap: 4px;

      .mini-label {
        font-size: 12px;
        color: var(--el-text-color-secondary);
      }

      .mini-value {
        font-size: 16px;
        font-weight: 600;

        &.warning {
          color: #ef4444;
        }
      }
    }
  }
}

// 最近文件列表
.recent-files-card {
  height: 100%;
  margin-bottom: 16px;

  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }

  .recent-list {
    display: flex;
    flex-direction: column;
    min-height: 200px;

    .recent-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 8px;
      border-radius: var(--mac-radius-control);
      cursor: pointer;
      transition: background 0.15s ease;

      & + .recent-item {
        border-top: 1px solid var(--mac-hairline);
      }

      &:hover {
        background: var(--el-fill-color-light);
      }

      .file-icon {
        width: 34px;
        height: 34px;
        border-radius: 8px;
        display: flex;
        align-items: center;
        justify-content: center;
        flex-shrink: 0;
      }

      .recent-info {
        flex: 1;
        min-width: 0;

        .recent-name {
          font-size: 13px;
          font-weight: 500;
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }

        .recent-meta {
          font-size: 12px;
          color: var(--el-text-color-secondary);
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }
      }

      .recent-side {
        display: flex;
        flex-direction: column;
        align-items: flex-end;
        gap: 2px;
        flex-shrink: 0;

        .recent-size {
          font-size: 12px;
          color: var(--el-text-color-secondary);
        }

        .recent-time {
          font-size: 12px;
          color: var(--el-text-color-placeholder);
        }
      }
    }

    .recent-empty {
      flex: 1;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 8px;
      color: var(--el-text-color-secondary);
      font-size: 13px;
    }
  }
}

// 回收站小卡：条目数 + 占用 + 入口
.recycle-card {
  height: 100%;
  margin-bottom: 16px;

  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }

  .recycle-main {
    display: flex;
    align-items: center;
    gap: 16px;
    margin-bottom: 16px;

    .recycle-count {
      font-size: 40px;
      font-weight: 700;
      line-height: 1;
      letter-spacing: -1px;
    }

    .recycle-meta {
      display: flex;
      flex-direction: column;
      gap: 4px;

      .recycle-size {
        font-size: 13px;
        color: var(--el-text-color-regular);
      }

      .recycle-hint {
        font-size: 12px;
        color: var(--el-text-color-secondary);
      }
    }
  }

  .recycle-btn {
    width: 100%;
  }
}
</style>

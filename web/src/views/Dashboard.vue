<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue'
import { useUserStore } from '@/stores/user'
import { storageApi } from '@/api/storage'
import http from '@/api/http'
import { 
  Files, User, Box, Share, Odometer, 
  Monitor, Timer, Cpu, Connection, Refresh,
  SuccessFilled, WarningFilled, CircleCloseFilled
} from '@element-plus/icons-vue'

const userStore = useUserStore()

// 存储概览
const storageOverview = ref({
  totalBytes: 0,
  usedBytes: 0,
  availableBytes: 0,
  usagePercentage: 0,
  poolCount: 0,
  diskCount: 0,
  healthStatus: ''
})

// 存储池列表
const storagePools = ref<any[]>([])

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

// 格式化大小
const formatSize = (bytes: number): string => {
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB', 'PB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
}

// 格式化运行时间
const formatUptime = (uptime: string): string => {
  const parts = uptime.split('.')
  if (parts.length === 2) {
    const days = parseInt(parts[0])
    const time = parts[1]
    if (days > 0) {
      return `${days}天 ${time}`
    }
    return time
  }
  return uptime
}

// 获取存储池状态颜色
const getPoolStatusColor = (status: number): string => {
  switch (status) {
    case 1: return '#67C23A' // Healthy
    case 2: return '#E6A23C' // Degraded
    case 3: return '#F56C6C' // Critical
    case 4: return '#409EFF' // Rebuilding
    default: return '#909399' // Unknown/Offline
  }
}

// 获取存储池状态名称
const getPoolStatusName = (status: number): string => {
  switch (status) {
    case 1: return '健康'
    case 2: return '降级'
    case 3: return '危险'
    case 4: return '重建中'
    case 5: return '离线'
    default: return '未知'
  }
}

// 获取 RAID 级别名称
const getRaidLevelName = (level: number): string => {
  switch (level) {
    case 0: return '无'
    case 1: return 'RAID 0'
    case 2: return 'RAID 1'
    case 3: return 'RAID 5'
    case 4: return 'RAID 6'
    case 5: return 'RAID 10'
    default: return '未知'
  }
}

// 磁盘健康状态统计
const diskHealthStats = computed(() => {
  const total = storageOverview.value.diskCount
  return { total, healthy: total, warning: 0, critical: 0 }
})

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

// 启动自动刷新
const startAutoRefresh = () => {
  if (refreshTimer) return
  refreshTimer = setInterval(() => {
    loadStorageOverview()
    loadSystemStatus()
    lastUpdateTime.value = new Date()
  }, 5000) // 每5秒刷新
}

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
})
</script>

<template>
  <div class="dashboard-page">
    <!-- 头部 -->
    <div class="dashboard-header">
      <div class="welcome-section">
        <h1>欢迎回来，{{ userStore.displayName || userStore.username }}</h1>
        <p>NAS System 网络存储管理系统</p>
      </div>
      <div class="header-actions">
        <span class="last-update">最后更新: {{ formattedLastUpdate }}</span>
        <el-button-group>
          <el-button :icon="Refresh" @click="handleRefresh" :loading="loading">
            刷新
          </el-button>
          <el-button 
            :type="autoRefresh ? 'primary' : 'default'"
            @click="toggleAutoRefresh"
          >
            {{ autoRefresh ? '自动刷新中' : '自动刷新' }}
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
              <div class="stat-label">系统状态</div>
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
              <div class="stat-label">运行时间</div>
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
              <div class="stat-label">内存使用</div>
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
              <div class="stat-label">活动线程</div>
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <!-- 存储概览 -->
    <el-row :gutter="16">
      <el-col :xs="24" :md="16">
        <el-card class="storage-overview-card">
          <template #header>
            <div class="card-header">
              <span><el-icon><Box /></el-icon> 存储概览</span>
              <el-tag :type="storageOverview.usagePercentage > 90 ? 'danger' : storageOverview.usagePercentage > 70 ? 'warning' : 'success'">
                使用率 {{ storageOverview.usagePercentage.toFixed(1) }}%
              </el-tag>
            </div>
          </template>
          
          <div class="storage-main">
            <div class="storage-chart">
              <el-progress 
                type="dashboard" 
                :percentage="storageOverview.usagePercentage" 
                :width="180"
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
                    <span class="label">已使用</span>
                  </div>
                </template>
              </el-progress>
            </div>
            
            <div class="storage-details">
              <div class="detail-item">
                <div class="detail-label">
                  <span class="dot" style="background: #409EFF"></span>
                  总容量
                </div>
                <div class="detail-value">{{ formatSize(storageOverview.totalBytes) }}</div>
              </div>
              <div class="detail-item">
                <div class="detail-label">
                  <span class="dot" style="background: #F56C6C"></span>
                  已使用
                </div>
                <div class="detail-value">{{ formatSize(storageOverview.usedBytes) }}</div>
              </div>
              <div class="detail-item">
                <div class="detail-label">
                  <span class="dot" style="background: #67C23A"></span>
                  可用空间
                </div>
                <div class="detail-value">{{ formatSize(storageOverview.availableBytes) }}</div>
              </div>
              <el-divider />
              <div class="detail-item">
                <div class="detail-label">存储池数量</div>
                <div class="detail-value">{{ storageOverview.poolCount }} 个</div>
              </div>
              <div class="detail-item">
                <div class="detail-label">磁盘总数</div>
                <div class="detail-value">{{ storageOverview.diskCount }} 块</div>
              </div>
            </div>
          </div>
        </el-card>
      </el-col>
      
      <el-col :xs="24" :md="8">
        <el-card class="disk-health-card">
          <template #header>
            <span><el-icon><Odometer /></el-icon> 磁盘健康状态</span>
          </template>
          
          <div class="health-stats">
            <div class="health-item healthy">
              <el-icon :size="24"><SuccessFilled /></el-icon>
              <div class="health-info">
                <span class="count">{{ diskHealthStats.healthy }}</span>
                <span class="label">健康</span>
              </div>
            </div>
            <div class="health-item warning">
              <el-icon :size="24"><WarningFilled /></el-icon>
              <div class="health-info">
                <span class="count">{{ diskHealthStats.warning }}</span>
                <span class="label">警告</span>
              </div>
            </div>
            <div class="health-item critical">
              <el-icon :size="24"><CircleCloseFilled /></el-icon>
              <div class="health-info">
                <span class="count">{{ diskHealthStats.critical }}</span>
                <span class="label">危险</span>
              </div>
            </div>
          </div>
          
          <el-divider />
          
          <div class="health-progress">
            <div class="progress-label">
              <span>整体健康度</span>
              <span>{{ diskHealthStats.total > 0 ? Math.round(diskHealthStats.healthy / diskHealthStats.total * 100) : 100 }}%</span>
            </div>
            <el-progress 
              :percentage="diskHealthStats.total > 0 ? Math.round(diskHealthStats.healthy / diskHealthStats.total * 100) : 100"
              :stroke-width="10"
              :show-text="false"
              :color="[
                { color: '#F56C6C', percentage: 50 },
                { color: '#E6A23C', percentage: 80 },
                { color: '#67C23A', percentage: 100 }
              ]"
            />
          </div>
        </el-card>
      </el-col>
    </el-row>

    <!-- 存储池列表 -->
    <el-card class="pools-card">
      <template #header>
        <div class="card-header">
          <span><el-icon><Box /></el-icon> 存储池状态</span>
          <router-link to="/storage">
            <el-button type="primary" link>查看全部</el-button>
          </router-link>
        </div>
      </template>
      
      <el-table :data="storagePools" v-loading="loading" style="width: 100%">
        <el-table-column prop="name" label="名称" min-width="120">
          <template #default="{ row }">
            <div class="pool-name">
              <el-tag v-if="row.isDefault" size="small" type="success">默认</el-tag>
              <span>{{ row.name }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="status" label="状态" width="100">
          <template #default="{ row }">
            <el-tag :color="getPoolStatusColor(row.status)" effect="dark" size="small">
              {{ getPoolStatusName(row.status) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="raidLevel" label="RAID" width="100">
          <template #default="{ row }">
            {{ getRaidLevelName(row.raidLevel) }}
          </template>
        </el-table-column>
        <el-table-column label="容量" min-width="200">
          <template #default="{ row }">
            <div class="capacity-cell">
              <div class="capacity-text">
                {{ formatSize(row.usedBytes) }} / {{ formatSize(row.totalBytes) }}
              </div>
              <el-progress 
                :percentage="row.usagePercentage" 
                :stroke-width="6"
                :show-text="false"
                :color="row.usagePercentage > 90 ? '#F56C6C' : row.usagePercentage > 70 ? '#E6A23C' : '#67C23A'"
              />
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="usagePercentage" label="使用率" width="100">
          <template #default="{ row }">
            <span :style="{ color: row.usagePercentage > 90 ? '#F56C6C' : row.usagePercentage > 70 ? '#E6A23C' : '#67C23A' }">
              {{ row.usagePercentage.toFixed(1) }}%
            </span>
          </template>
        </el-table-column>
        <el-table-column prop="mountPath" label="挂载路径" min-width="150" />
      </el-table>
      
      <el-empty v-if="storagePools.length === 0 && !loading" description="暂无存储池" />
    </el-card>

    <!-- 快捷操作和系统信息 -->
    <el-row :gutter="16">
      <el-col :xs="24" :md="12">
        <el-card class="quick-actions-card">
          <template #header>
            <span>快捷操作</span>
          </template>
          <div class="quick-actions">
            <router-link to="/files" class="action-item">
              <el-icon :size="32" color="#409EFF"><Files /></el-icon>
              <span>文件管理</span>
            </router-link>
            <router-link to="/shares" class="action-item">
              <el-icon :size="32" color="#67C23A"><Share /></el-icon>
              <span>共享管理</span>
            </router-link>
            <router-link to="/storage" class="action-item">
              <el-icon :size="32" color="#E6A23C"><Box /></el-icon>
              <span>存储管理</span>
            </router-link>
            <router-link to="/users" class="action-item" v-if="userStore.isAdmin">
              <el-icon :size="32" color="#F56C6C"><User /></el-icon>
              <span>用户管理</span>
            </router-link>
          </div>
        </el-card>
      </el-col>
      
      <el-col :xs="24" :md="12">
        <el-card class="system-info-card">
          <template #header>
            <span><el-icon><Monitor /></el-icon> 系统信息</span>
          </template>
          <div class="info-list">
            <div class="info-item">
              <span class="label">系统名称</span>
              <span class="value">{{ systemInfo.name }}</span>
            </div>
            <div class="info-item">
              <span class="label">版本</span>
              <span class="value">{{ systemInfo.version }}</span>
            </div>
            <div class="info-item">
              <span class="label">主机名</span>
              <span class="value">{{ systemInfo.machineName }}</span>
            </div>
            <div class="info-item">
              <span class="label">操作系统</span>
              <span class="value">{{ systemInfo.os }}</span>
            </div>
            <div class="info-item">
              <span class="label">处理器核心</span>
              <span class="value">{{ systemInfo.processorCount }} 核</span>
            </div>
            <div class="info-item">
              <span class="label">.NET 版本</span>
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
  
  :deep(.el-card__body) {
    padding: 16px;
  }
  
  .stat-content {
    display: flex;
    align-items: center;
    gap: 16px;
  }
  
  .stat-icon {
    width: 56px;
    height: 56px;
    border-radius: 12px;
    display: flex;
    align-items: center;
    justify-content: center;
    color: #fff;
  }
  
  .stat-info {
    flex: 1;
    min-width: 0;
    
    .stat-value {
      font-size: 20px;
      font-weight: 600;
      color: var(--el-text-color-primary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    
    .stat-label {
      font-size: 13px;
      color: var(--el-text-color-secondary);
      margin-top: 2px;
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
      gap: 8px;
      padding: 20px 16px;
      border-radius: 8px;
      background: var(--el-fill-color-light);
      text-decoration: none;
      color: var(--el-text-color-primary);
      transition: all 0.3s;
      
      &:hover {
        background: var(--el-color-primary-light-9);
        transform: translateY(-2px);
      }
      
      span {
        font-size: 13px;
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

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { Plus, Refresh, Setting, Delete, Check } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { StoragePool, StorageOverview } from '@/api/storage'

// 状态
const loading = ref(false)
const overview = ref<StorageOverview | null>(null)
const pools = ref<StoragePool[]>([])
const createDialogVisible = ref(false)
const newPool = ref({
  name: '',
  mountPath: '',
  raidLevel: 0 as number,
  description: ''
})

// RAID 级别选项
const raidLevels: { value: number; label: string }[] = [
  { value: 0, label: '无 RAID (JBOD)' },
  { value: 1, label: 'RAID 0 (条带化)' },
  { value: 2, label: 'RAID 1 (镜像)' },
  { value: 5, label: 'RAID 5 (分布式奇偶校验)' },
  { value: 6, label: 'RAID 6 (双重奇偶校验)' },
  { value: 10, label: 'RAID 10 (镜像条带化)' }
]

// 格式化大小
const formatSize = (bytes: number): string => {
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB', 'PB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
}

// 获取状态颜色
const getStatusType = (status: number): 'primary' | 'success' | 'warning' | 'info' | 'danger' => {
  switch (status) {
    case 0: return 'success' // Healthy
    case 1: return 'info'    // Initializing
    case 2: return 'warning' // Degraded
    case 3: return 'warning' // Rebuilding
    case 4: return 'danger'  // Failed
    case 5: return 'info'    // Offline
    default: return 'info'
  }
}

// 加载数据
const loadData = async () => {
  loading.value = true
  try {
    // 模拟数据 - 实际应调用 API
    overview.value = {
      totalBytes: 4 * 1024 * 1024 * 1024 * 1024, // 4TB
      usedBytes: 1.5 * 1024 * 1024 * 1024 * 1024, // 1.5TB
      availableBytes: 2.5 * 1024 * 1024 * 1024 * 1024,
      usagePercentage: 37.5,
      poolCount: 1,
      diskCount: 4,
      healthStatus: 'Healthy'
    }
    pools.value = [
      {
        id: '1',
        name: 'MainPool',
        description: '主存储池',
        mountPath: '/mnt/storage',
        raidLevel: 5,
        raidLevelName: 'RAID 5',
        status: 0,
        statusName: '健康',
        totalBytes: 4 * 1024 * 1024 * 1024 * 1024,
        usedBytes: 1.5 * 1024 * 1024 * 1024 * 1024,
        availableBytes: 2.5 * 1024 * 1024 * 1024 * 1024,
        usagePercentage: 37.5,
        isDefault: true,
        isEnabled: true,
        enableDeduplication: false,
        enableCompression: true,
        diskCount: 4,
        createdAt: new Date().toISOString()
      }
    ]
  } finally {
    loading.value = false
  }
}

// 创建存储池
const handleCreatePool = async () => {
  if (!newPool.value.name || !newPool.value.mountPath) {
    ElMessage.warning('请填写存储池名称和挂载路径')
    return
  }
  try {
    // 调用 API 创建
    ElMessage.success('存储池创建成功')
    createDialogVisible.value = false
    newPool.value = { name: '', mountPath: '', raidLevel: 0, description: '' }
    await loadData()
  } catch (error) {
    ElMessage.error('创建失败')
  }
}

// 删除存储池
const handleDeletePool = async (pool: StoragePool) => {
  try {
    await ElMessageBox.confirm(
      `确定要删除存储池 "${pool.name}" 吗？此操作不可恢复。`,
      '确认删除',
      { type: 'warning' }
    )
    // 调用 API 删除
    ElMessage.success('存储池已删除')
    await loadData()
  } catch {
    // 用户取消
  }
}

// 设置为默认
const handleSetDefault = async (pool: StoragePool) => {
  try {
    // 调用 API
    ElMessage.success(`已将 "${pool.name}" 设置为默认存储池`)
    await loadData()
  } catch (error) {
    ElMessage.error('设置失败')
  }
}

onMounted(() => {
  loadData()
})
</script>

<template>
  <div class="storage-page">
    <el-page-header title="返回" @back="$router.back()">
      <template #content>
        <span class="page-title">存储管理</span>
      </template>
      <template #extra>
        <el-button :icon="Refresh" @click="loadData" :loading="loading">刷新</el-button>
      </template>
    </el-page-header>

    <!-- 存储概览 -->
    <el-row :gutter="20" class="overview-row">
      <el-col :span="6">
        <el-card shadow="hover">
          <template #header>总容量</template>
          <div class="stat-value">{{ overview ? formatSize(overview.totalBytes) : '--' }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover">
          <template #header>已使用</template>
          <div class="stat-value">{{ overview ? formatSize(overview.usedBytes) : '--' }}</div>
          <el-progress 
            v-if="overview" 
            :percentage="overview.usagePercentage" 
            :stroke-width="6"
            :color="overview.usagePercentage > 90 ? '#f56c6c' : overview.usagePercentage > 70 ? '#e6a23c' : '#67c23a'"
          />
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover">
          <template #header>可用空间</template>
          <div class="stat-value text-success">{{ overview ? formatSize(overview.availableBytes) : '--' }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover">
          <template #header>磁盘健康</template>
          <div class="stat-value" :class="overview?.healthStatus === 'Healthy' ? 'text-success' : 'text-warning'">
            {{ overview?.healthStatus === 'Healthy' ? '良好' : overview?.healthStatus || '--' }}
          </div>
        </el-card>
      </el-col>
    </el-row>

    <!-- 存储池列表 -->
    <el-card class="page-card">
      <template #header>
        <div class="card-header">
          <span>存储池 ({{ pools.length }})</span>
          <el-button type="primary" @click="createDialogVisible = true">
            <el-icon><Plus /></el-icon>
            创建存储池
          </el-button>
        </div>
      </template>

      <el-table :data="pools" v-loading="loading" style="width: 100%">
        <el-table-column prop="name" label="名称" min-width="150">
          <template #default="{ row }">
            <div class="pool-name">
              <span>{{ row.name }}</span>
              <el-tag v-if="row.isDefault" type="primary" size="small">默认</el-tag>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="raidLevelName" label="RAID 级别" width="150" />
        <el-table-column prop="statusName" label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="getStatusType(row.status)" size="small">{{ row.statusName }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="容量" width="200">
          <template #default="{ row }">
            <div class="capacity-info">
              <span>{{ formatSize(row.usedBytes) }} / {{ formatSize(row.totalBytes) }}</span>
              <el-progress :percentage="row.usagePercentage" :stroke-width="4" :show-text="false" />
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="diskCount" label="磁盘数" width="80" align="center" />
        <el-table-column prop="mountPath" label="挂载路径" min-width="150" />
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <el-button-group size="small">
              <el-tooltip content="设置为默认" v-if="!row.isDefault">
                <el-button :icon="Check" @click="handleSetDefault(row)" />
              </el-tooltip>
              <el-tooltip content="设置">
                <el-button :icon="Setting" />
              </el-tooltip>
              <el-tooltip content="删除">
                <el-button :icon="Delete" type="danger" @click="handleDeletePool(row)" />
              </el-tooltip>
            </el-button-group>
          </template>
        </el-table-column>
      </el-table>

      <el-empty v-if="pools.length === 0 && !loading" description="暂无存储池，请创建一个存储池" />
    </el-card>

    <!-- 创建存储池对话框 -->
    <el-dialog v-model="createDialogVisible" title="创建存储池" width="500">
      <el-form :model="newPool" label-width="100px">
        <el-form-item label="名称" required>
          <el-input v-model="newPool.name" placeholder="请输入存储池名称" />
        </el-form-item>
        <el-form-item label="挂载路径" required>
          <el-input v-model="newPool.mountPath" placeholder="/mnt/storage" />
        </el-form-item>
        <el-form-item label="RAID 级别">
          <el-select v-model="newPool.raidLevel" style="width: 100%">
            <el-option 
              v-for="item in raidLevels" 
              :key="item.value" 
              :label="item.label" 
              :value="item.value" 
            />
          </el-select>
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="newPool.description" type="textarea" :rows="3" placeholder="可选描述" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleCreatePool">创建</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style lang="scss" scoped>
.storage-page {
  .overview-row {
    margin-top: 20px;
  }

  .stat-value {
    font-size: 28px;
    font-weight: 600;
    color: var(--el-color-primary);

    &.text-success {
      color: var(--el-color-success);
    }
  }

  .page-card {
    margin-top: 20px;
  }

  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }
}
</style>

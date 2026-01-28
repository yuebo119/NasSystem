<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { Plus, Refresh, Delete, Check, FolderOpened, Box, InfoFilled, Edit, Warning, Back, Folder } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { storageApi, type StoragePool, type StorageOverview, type DirectoryItem } from '@/api/storage'

const loading = ref(false)
const overview = ref<StorageOverview | null>(null)
const pools = ref<StoragePool[]>([])
const createDialogVisible = ref(false)
const editDialogVisible = ref(false)
const pathDialogVisible = ref(false)
const editingPool = ref<StoragePool | null>(null)
const creating = ref(false)
const updating = ref(false)

const poolForm = ref({ name: '', mountPath: '', description: '', maxCapacityGB: 0 })

// 目录浏览相关
const browsingPath = ref('')
const parentPath = ref<string | null>(null)
const directories = ref<DirectoryItem[]>([])
const browsingLoading = ref(false)

const formatSize = (bytes: number): string => {
  if (!bytes || bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB', 'PB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
}

const getUsageColor = (percentage: number): string => {
  if (percentage >= 90) return '#ef4444'
  if (percentage >= 70) return '#f59e0b'
  return '#10b981'
}

const getUsageGradient = (percentage: number): string => {
  if (percentage >= 90) return 'linear-gradient(90deg, #ef4444, #f87171)'
  if (percentage >= 70) return 'linear-gradient(90deg, #f59e0b, #fbbf24)'
  return 'linear-gradient(90deg, #10b981, #34d399)'
}

const getStatusInfo = (status: number) => {
  const map: Record<number, { text: string; type: 'success' | 'warning' | 'danger' | 'info'; color: string }> = {
    1: { text: '正常', type: 'success', color: '#10b981' },
    2: { text: '警告', type: 'warning', color: '#f59e0b' },
    3: { text: '错误', type: 'danger', color: '#ef4444' },
    4: { text: '离线', type: 'info', color: '#6b7280' },
  }
  return map[status] || { text: '正常', type: 'success', color: '#10b981' }
}

const healthStatus = computed(() => {
  if (!overview.value) return { text: '未知', color: '#6b7280' }
  const status = overview.value.healthStatus
  if (status === 'Healthy' || status === '正常') return { text: '良好', color: '#10b981' }
  if (status === 'Warning') return { text: '警告', color: '#f59e0b' }
  return { text: status || '未知', color: '#6b7280' }
})

const loadData = async () => {
  loading.value = true
  try {
    const [overviewRes, poolsRes] = await Promise.all([storageApi.getOverview(), storageApi.getPools()]) as any[]
    if (overviewRes.success) overview.value = overviewRes.data as StorageOverview
    if (poolsRes.success) pools.value = poolsRes.data as StoragePool[]
  } catch (e) { console.error('加载失败', e) }
  finally { loading.value = false }
}

const openCreateDialog = () => {
  poolForm.value = { name: '', mountPath: '', description: '', maxCapacityGB: 0 }
  createDialogVisible.value = true
}

const openPathDialog = async () => {
  pathDialogVisible.value = true
  browsingPath.value = poolForm.value.mountPath || ''
  await browseDirectory(browsingPath.value || undefined)
}

const browseDirectory = async (path?: string) => {
  browsingLoading.value = true
  try {
    const result = await storageApi.browseDirectories(path) as any
    if (result.success && result.data) {
      browsingPath.value = result.data.currentPath || ''
      parentPath.value = result.data.parentPath
      directories.value = result.data.directories || []
    }
  } catch (e) { console.error('浏览目录失败', e) }
  finally { browsingLoading.value = false }
}

const selectDirectory = (dir: DirectoryItem) => {
  browseDirectory(dir.path)
}

const goToParent = () => {
  if (parentPath.value) browseDirectory(parentPath.value)
  else browseDirectory(undefined)
}

const confirmPath = () => {
  if (browsingPath.value) {
    poolForm.value.mountPath = browsingPath.value
    pathDialogVisible.value = false
  } else {
    ElMessage.warning('请选择一个目录')
  }
}

const handleCreate = async () => {
  if (!poolForm.value.name.trim()) { ElMessage.warning('请输入存储池名称'); return }
  if (!poolForm.value.mountPath.trim()) { ElMessage.warning('请选择存储路径'); return }
  creating.value = true
  try {
    const maxCapacityBytes = poolForm.value.maxCapacityGB > 0 
      ? poolForm.value.maxCapacityGB * 1024 * 1024 * 1024 
      : undefined
    const result = await storageApi.createPool({
      name: poolForm.value.name.trim(),
      mountPath: poolForm.value.mountPath.trim(),
      raidLevel: 0,
      description: poolForm.value.description.trim(),
      maxCapacityBytes
    }) as any
    if (result.success) {
      ElMessage.success('创建成功')
      createDialogVisible.value = false
      await loadData()
    } else { ElMessage.error(result.message || '创建失败') }
  } catch (e: any) { ElMessage.error(e.message || '创建失败') }
  finally { creating.value = false }
}

const openEditDialog = (pool: StoragePool) => {
  editingPool.value = pool
  const maxCapacityGB = pool.maxCapacityBytes ? Math.round(pool.maxCapacityBytes / 1024 / 1024 / 1024) : 0
  poolForm.value = { name: pool.name, mountPath: pool.mountPath, description: pool.description || '', maxCapacityGB }
  editDialogVisible.value = true
}

const handleUpdate = async () => {
  if (!editingPool.value) return
  if (!poolForm.value.name.trim()) { ElMessage.warning('请输入存储池名称'); return }
  updating.value = true
  try {
    const maxCapacityBytes = poolForm.value.maxCapacityGB > 0 
      ? poolForm.value.maxCapacityGB * 1024 * 1024 * 1024 
      : 0  // 0 表示取消限制
    const result = await storageApi.updatePool(editingPool.value.id, {
      name: poolForm.value.name.trim(),
      description: poolForm.value.description.trim(),
      maxCapacityBytes
    }) as any
    if (result.success) {
      ElMessage.success('更新成功')
      editDialogVisible.value = false
      await loadData()
    } else { ElMessage.error(result.message || '更新失败') }
  } catch (e: any) { ElMessage.error(e.message || '更新失败') }
  finally { updating.value = false }
}

const handleSetDefault = async (pool: StoragePool) => {
  try {
    const result = await storageApi.setDefault(pool.id) as any
    if (result.success) {
      ElMessage.success(`已将 "${pool.name}" 设为默认`)
      await loadData()
    } else { ElMessage.error(result.message || '设置失败') }
  } catch (e: any) { ElMessage.error(e.message || '设置失败') }
}

const handleDelete = async (pool: StoragePool) => {
  if (pool.isDefault) { ElMessage.warning('默认存储池不能删除'); return }
  try {
    await ElMessageBox.confirm(`确定删除存储池 "${pool.name}"？\n删除后该存储池下的文件将无法访问。`, '确认删除', { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' })
    const result = await storageApi.deletePool(pool.id) as any
    if (result.success) { ElMessage.success('删除成功'); await loadData() }
    else { ElMessage.error(result.message || '删除失败') }
  } catch (e: any) { if (e !== 'cancel') ElMessage.error(e.message || '删除失败') }
}

onMounted(() => loadData())
</script>

<template>
  <div class="storage-page">
    <div class="page-header">
      <div class="header-left">
        <h1>存储管理</h1>
        <p>管理系统存储池和存储空间</p>
      </div>
      <div class="header-actions">
        <el-button :icon="Refresh" @click="loadData" :loading="loading">刷新</el-button>
        <el-button type="primary" :icon="Plus" @click="openCreateDialog">新建存储池</el-button>
      </div>
    </div>

    <div class="overview-grid">
      <div class="overview-main">
        <div class="main-header">
          <div class="main-icon"><el-icon :size="28"><Box /></el-icon></div>
          <div class="main-title">
            <h3>存储空间</h3>
            <p>系统总存储使用情况</p>
          </div>
          <div class="main-percentage" :style="{ color: getUsageColor(overview?.usagePercentage || 0) }">
            {{ (overview?.usagePercentage || 0).toFixed(1) }}%
          </div>
        </div>
        <div class="main-bar">
          <div class="bar-track">
            <div class="bar-fill" :style="{ width: `${overview?.usagePercentage || 0}%`, background: getUsageGradient(overview?.usagePercentage || 0) }"></div>
          </div>
        </div>
        <div class="main-stats">
          <div class="stat"><span class="label">已使用</span><span class="value">{{ formatSize(overview?.usedBytes || 0) }}</span></div>
          <div class="stat"><span class="label">可用</span><span class="value success">{{ formatSize(overview?.availableBytes || 0) }}</span></div>
          <div class="stat"><span class="label">总容量</span><span class="value">{{ formatSize(overview?.totalBytes || 0) }}</span></div>
        </div>
      </div>

      <div class="overview-card">
        <div class="card-icon pools"><el-icon :size="24"><FolderOpened /></el-icon></div>
        <div class="card-info"><div class="card-value">{{ overview?.poolCount || 0 }}</div><div class="card-label">存储池</div></div>
      </div>

      <div class="overview-card">
        <div class="card-icon" :style="{ background: healthStatus.color + '20', color: healthStatus.color }">
          <el-icon :size="24"><InfoFilled /></el-icon>
        </div>
        <div class="card-info"><div class="card-value" :style="{ color: healthStatus.color }">{{ healthStatus.text }}</div><div class="card-label">系统状态</div></div>
      </div>
    </div>

    <div class="pools-section">
      <div class="section-header"><h2>存储池</h2><span class="count">{{ pools.length }} 个</span></div>
      
      <div class="pools-list" v-loading="loading">
        <div v-for="pool in pools" :key="pool.id" class="pool-item" :class="{ 'is-default': pool.isDefault }">
          <div class="pool-left">
            <div class="pool-icon"><el-icon :size="28"><FolderOpened /></el-icon></div>
            <div class="pool-info">
              <div class="pool-name">
                {{ pool.name }}
                <el-tag v-if="pool.isDefault" type="primary" size="small" effect="light">默认</el-tag>
                <el-tag :type="getStatusInfo(pool.status).type" size="small">{{ getStatusInfo(pool.status).text }}</el-tag>
              </div>
              <div class="pool-path">{{ pool.mountPath }}</div>
              <div class="pool-desc" v-if="pool.description">{{ pool.description }}</div>
            </div>
          </div>

          <div class="pool-center">
            <div class="usage-info">
              <div class="usage-header">
                <span class="usage-text">{{ formatSize(pool.usedBytes || 0) }} / {{ formatSize(pool.maxCapacityBytes || pool.totalBytes || 0) }}</span>
                <span class="usage-percent" :style="{ color: getUsageColor(pool.usagePercentage || 0) }">{{ (pool.usagePercentage || 0).toFixed(1) }}%</span>
              </div>
              <div class="usage-bar">
                <div class="bar-fill" :style="{ width: `${pool.usagePercentage || 0}%`, background: getUsageGradient(pool.usagePercentage || 0) }"></div>
              </div>
              <div class="capacity-limit" v-if="pool.maxCapacityBytes">
                <el-icon :size="12"><Warning /></el-icon>
                <span>容量限制: {{ formatSize(pool.maxCapacityBytes) }}</span>
              </div>
            </div>
          </div>

          <div class="pool-actions">
            <el-tooltip content="编辑" placement="top">
              <el-button text circle @click="openEditDialog(pool)"><el-icon><Edit /></el-icon></el-button>
            </el-tooltip>
            <el-tooltip v-if="!pool.isDefault" content="设为默认" placement="top">
              <el-button text circle @click="handleSetDefault(pool)"><el-icon><Check /></el-icon></el-button>
            </el-tooltip>
            <el-tooltip v-if="!pool.isDefault" content="删除" placement="top">
              <el-button text circle type="danger" @click="handleDelete(pool)"><el-icon><Delete /></el-icon></el-button>
            </el-tooltip>
          </div>
        </div>

        <div v-if="pools.length === 0 && !loading" class="empty-state">
          <div class="empty-icon"><el-icon :size="64"><Box /></el-icon></div>
          <h3>暂无存储池</h3>
          <p>创建一个存储池来开始管理您的文件</p>
          <el-button type="primary" @click="openCreateDialog"><el-icon><Plus /></el-icon>创建存储池</el-button>
        </div>
      </div>
    </div>

    <!-- 新建存储池对话框 -->
    <el-dialog v-model="createDialogVisible" title="新建存储池" width="500px" :close-on-click-modal="false">
      <el-form :model="poolForm" label-position="top">
        <el-form-item label="存储池名称" required>
          <el-input v-model="poolForm.name" placeholder="例如: 主存储、备份存储" maxlength="50" show-word-limit />
        </el-form-item>
        <el-form-item label="存储路径" required>
          <div class="path-input-wrapper">
            <el-input v-model="poolForm.mountPath" placeholder="点击右侧按钮选择路径" readonly />
            <el-button type="primary" @click="openPathDialog"><el-icon><FolderOpened /></el-icon>选择</el-button>
          </div>
          <div class="form-tip"><el-icon><InfoFilled /></el-icon><span>请选择一个有读写权限的目录</span></div>
        </el-form-item>
        <el-form-item label="最大容量限制">
          <el-input-number v-model="poolForm.maxCapacityGB" :min="0" :max="102400" :step="10" placeholder="0 表示不限制" style="width: 100%" />
          <div class="form-tip"><el-icon><InfoFilled /></el-icon><span>单位: GB，设置为 0 表示不限制（使用磁盘实际容量）</span></div>
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="poolForm.description" type="textarea" :rows="3" placeholder="可选，描述此存储池的用途" maxlength="200" show-word-limit />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleCreate" :loading="creating">创建</el-button>
      </template>
    </el-dialog>

    <!-- 编辑存储池对话框 -->
    <el-dialog v-model="editDialogVisible" title="编辑存储池" width="500px" :close-on-click-modal="false">
      <el-form :model="poolForm" label-position="top">
        <el-form-item label="存储池名称" required>
          <el-input v-model="poolForm.name" placeholder="存储池名称" maxlength="50" show-word-limit />
        </el-form-item>
        <el-form-item label="存储路径">
          <el-input :model-value="poolForm.mountPath" disabled />
          <div class="form-tip warning"><el-icon><Warning /></el-icon><span>存储路径创建后不可修改</span></div>
        </el-form-item>
        <el-form-item label="最大容量限制">
          <el-input-number v-model="poolForm.maxCapacityGB" :min="0" :max="102400" :step="10" placeholder="0 表示不限制" style="width: 100%" />
          <div class="form-tip"><el-icon><InfoFilled /></el-icon><span>单位: GB，设置为 0 表示取消限制</span></div>
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="poolForm.description" type="textarea" :rows="3" placeholder="可选描述" maxlength="200" show-word-limit />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleUpdate" :loading="updating">保存</el-button>
      </template>
    </el-dialog>

    <!-- 目录选择对话框 -->
    <el-dialog v-model="pathDialogVisible" title="选择存储路径" width="600px" :close-on-click-modal="false">
      <div class="path-browser">
        <div class="browser-header">
          <el-button :icon="Back" :disabled="!parentPath && !browsingPath" @click="goToParent" size="small">返回上级</el-button>
          <div class="current-path">
            <el-icon><Folder /></el-icon>
            <span>{{ browsingPath || '我的电脑' }}</span>
          </div>
        </div>
        <div class="browser-content" v-loading="browsingLoading">
          <div v-if="directories.length === 0 && !browsingLoading" class="empty-dir">
            <el-icon :size="48" color="#d1d5db"><Folder /></el-icon>
            <p>此目录为空或无法访问</p>
          </div>
          <div v-for="dir in directories" :key="dir.path" class="dir-item" @dblclick="selectDirectory(dir)">
            <el-icon :size="20" color="#f59e0b"><Folder /></el-icon>
            <span class="dir-name">{{ dir.name }}</span>
            <el-button text size="small" @click.stop="selectDirectory(dir)">打开</el-button>
          </div>
        </div>
      </div>
      <template #footer>
        <div class="path-dialog-footer">
          <div class="selected-path">
            <span class="label">已选择:</span>
            <span class="path">{{ browsingPath || '未选择' }}</span>
          </div>
          <div class="actions">
            <el-button @click="pathDialogVisible = false">取消</el-button>
            <el-button type="primary" @click="confirmPath" :disabled="!browsingPath">确定</el-button>
          </div>
        </div>
      </template>
    </el-dialog>
  </div>
</template>

<style lang="scss" scoped>
.storage-page { padding: 0; }
.page-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 24px;
  .header-left { h1 { margin: 0 0 4px; font-size: 24px; font-weight: 600; } p { margin: 0; color: var(--el-text-color-secondary); font-size: 14px; } }
  .header-actions { display: flex; gap: 12px; }
}

.overview-grid { display: grid; grid-template-columns: 2fr 1fr 1fr; gap: 16px; margin-bottom: 32px; }
.overview-main { background: var(--el-bg-color); border-radius: 16px; padding: 24px; border: 1px solid var(--el-border-color-lighter);
  .main-header { display: flex; align-items: center; gap: 16px; margin-bottom: 20px;
    .main-icon { width: 56px; height: 56px; border-radius: 14px; background: linear-gradient(135deg, #6366f1, #8b5cf6); display: flex; align-items: center; justify-content: center; color: white; }
    .main-title { flex: 1; h3 { margin: 0; font-size: 18px; font-weight: 600; } p { margin: 4px 0 0; font-size: 13px; color: var(--el-text-color-secondary); } }
    .main-percentage { font-size: 32px; font-weight: 700; }
  }
  .main-bar { margin-bottom: 20px;
    .bar-track { height: 12px; background: #e5e7eb; border-radius: 6px; overflow: hidden; border: 1px solid #d1d5db;
      .bar-fill { height: 100%; border-radius: 5px; transition: width 0.3s; }
    }
  }
  .main-stats { display: flex; gap: 32px;
    .stat { .label { display: block; font-size: 13px; color: var(--el-text-color-secondary); margin-bottom: 4px; }
      .value { font-size: 18px; font-weight: 600; &.success { color: #10b981; } }
    }
  }
}
.overview-card { background: var(--el-bg-color); border-radius: 16px; padding: 24px; border: 1px solid var(--el-border-color-lighter); display: flex; flex-direction: column; justify-content: center; align-items: center; text-align: center;
  .card-icon { width: 56px; height: 56px; border-radius: 14px; display: flex; align-items: center; justify-content: center; margin-bottom: 16px;
    &.pools { background: linear-gradient(135deg, #f59e0b, #fbbf24); color: white; }
  }
  .card-info { .card-value { font-size: 28px; font-weight: 700; } .card-label { font-size: 14px; color: var(--el-text-color-secondary); margin-top: 4px; } }
}

.pools-section {
  .section-header { display: flex; align-items: center; gap: 12px; margin-bottom: 16px;
    h2 { margin: 0; font-size: 18px; font-weight: 600; }
    .count { font-size: 14px; color: var(--el-text-color-secondary); background: var(--el-fill-color); padding: 2px 10px; border-radius: 10px; }
  }
}
.pools-list { display: flex; flex-direction: column; gap: 12px; }
.pool-item { background: var(--el-bg-color); border-radius: 12px; padding: 20px 24px; border: 1px solid var(--el-border-color-lighter); display: flex; align-items: center; gap: 24px; transition: all 0.2s;
  &:hover { box-shadow: 0 4px 12px rgba(0,0,0,0.06); }
  &.is-default { border-color: var(--el-color-primary-light-5); background: linear-gradient(135deg, rgba(99,102,241,0.02), rgba(139,92,246,0.02)); }
  .pool-left { display: flex; align-items: center; gap: 16px; flex: 1; min-width: 0;
    .pool-icon { width: 52px; height: 52px; border-radius: 12px; background: linear-gradient(135deg, #fbbf24, #f59e0b); display: flex; align-items: center; justify-content: center; color: white; flex-shrink: 0; }
    .pool-info { min-width: 0;
      .pool-name { display: flex; align-items: center; gap: 8px; font-size: 16px; font-weight: 600; flex-wrap: wrap; }
      .pool-path { font-size: 13px; color: var(--el-text-color-secondary); font-family: monospace; margin-top: 4px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .pool-desc { font-size: 12px; color: var(--el-text-color-secondary); margin-top: 4px; }
    }
  }
  .pool-center { width: 280px; flex-shrink: 0;
    .usage-info {
      .usage-header { display: flex; justify-content: space-between; margin-bottom: 8px;
        .usage-text { font-size: 13px; color: var(--el-text-color-secondary); }
        .usage-percent { font-size: 14px; font-weight: 600; }
      }
      .usage-bar { height: 8px; background: #e5e7eb; border-radius: 4px; overflow: hidden; border: 1px solid #d1d5db;
        .bar-fill { height: 100%; border-radius: 3px; transition: width 0.3s; }
      }
      .capacity-limit { display: flex; align-items: center; gap: 4px; margin-top: 6px; font-size: 11px; color: var(--el-color-warning); }
    }
  }
  .pool-actions { display: flex; gap: 4px; flex-shrink: 0; }
}

.empty-state { text-align: center; padding: 60px 20px; background: var(--el-bg-color); border-radius: 12px; border: 2px dashed var(--el-border-color);
  .empty-icon { color: #d1d5db; margin-bottom: 8px; }
  h3 { margin: 0 0 8px; font-size: 18px; } p { margin: 0 0 20px; color: var(--el-text-color-secondary); }
}

.form-tip { display: flex; align-items: center; gap: 6px; margin-top: 8px; font-size: 12px; color: var(--el-text-color-secondary);
  &.warning { color: var(--el-color-warning); }
}

.path-input-wrapper { display: flex; gap: 8px;
  .el-input { flex: 1; }
}

.path-browser {
  .browser-header { display: flex; align-items: center; gap: 16px; padding-bottom: 12px; border-bottom: 1px solid var(--el-border-color-lighter); margin-bottom: 12px;
    .current-path { display: flex; align-items: center; gap: 8px; font-size: 14px; color: var(--el-text-color-regular); flex: 1; overflow: hidden;
      span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    }
  }
  .browser-content { height: 320px; overflow-y: auto; border: 1px solid var(--el-border-color-lighter); border-radius: 8px; padding: 8px;
    .empty-dir { display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100%; color: var(--el-text-color-secondary);
      p { margin: 12px 0 0; }
    }
    .dir-item { display: flex; align-items: center; gap: 12px; padding: 10px 12px; border-radius: 6px; cursor: pointer; transition: background 0.2s;
      &:hover { background: var(--el-fill-color-light); }
      .dir-name { flex: 1; font-size: 14px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    }
  }
}

.path-dialog-footer { display: flex; justify-content: space-between; align-items: center; width: 100%;
  .selected-path { display: flex; align-items: center; gap: 8px; font-size: 13px; overflow: hidden;
    .label { color: var(--el-text-color-secondary); flex-shrink: 0; }
    .path { color: var(--el-color-primary); font-family: monospace; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  }
  .actions { display: flex; gap: 8px; flex-shrink: 0; }
}

@media (max-width: 1200px) { .overview-grid { grid-template-columns: 1fr; } .pool-item { flex-wrap: wrap; .pool-center { width: 100%; order: 3; } } }
@media (max-width: 768px) { .pool-item .pool-actions { width: 100%; justify-content: flex-end; } }
</style>
<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Plus, Refresh, Delete, Check, FolderOpened, Box, InfoFilled, Edit, Warning, Back, Folder } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { storageApi, type StoragePool, type StorageOverview, type DirectoryItem } from '@/api/storage'

const { t } = useI18n()
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
    1: { text: t('storage.healthy'), type: 'success', color: '#10b981' },
    2: { text: t('common.warning'), type: 'warning', color: '#f59e0b' },
    3: { text: t('common.error'), type: 'danger', color: '#ef4444' },
    4: { text: t('dashboard.offline'), type: 'info', color: '#6b7280' },
  }
  return map[status] || { text: t('storage.healthy'), type: 'success', color: '#10b981' }
}

const healthStatus = computed(() => {
  if (!overview.value) return { text: t('dashboard.unknown'), color: '#6b7280' }
  const status = overview.value.healthStatus
  if (status === 'Healthy' || status === '正常') return { text: t('storage.healthy'), color: '#10b981' }
  if (status === 'Warning') return { text: t('common.warning'), color: '#f59e0b' }
  return { text: status || t('dashboard.unknown'), color: '#6b7280' }
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
    ElMessage.warning(t('storage.pathRequired'))
  }
}

const handleCreate = async () => {
  if (!poolForm.value.name.trim()) { ElMessage.warning(t('storage.nameRequired')); return }
  if (!poolForm.value.mountPath.trim()) { ElMessage.warning(t('storage.pathRequired')); return }
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
      ElMessage.success(t('storage.createSuccess'))
      createDialogVisible.value = false
      await loadData()
    } else { ElMessage.error(result.message || t('common.failed')) }
  } catch (e: any) { ElMessage.error(e.message || t('common.failed')) }
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
  if (!poolForm.value.name.trim()) { ElMessage.warning(t('storage.nameRequired')); return }
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
      ElMessage.success(t('storage.updateSuccess'))
      editDialogVisible.value = false
      await loadData()
    } else { ElMessage.error(result.message || t('common.failed')) }
  } catch (e: any) { ElMessage.error(e.message || t('common.failed')) }
  finally { updating.value = false }
}

const handleSetDefault = async (pool: StoragePool) => {
  try {
    const result = await storageApi.setDefault(pool.id) as any
    if (result.success) {
      ElMessage.success(t('common.success'))
      await loadData()
    } else { ElMessage.error(result.message || t('common.failed')) }
  } catch (e: any) { ElMessage.error(e.message || t('common.failed')) }
}

const handleDelete = async (pool: StoragePool) => {
  if (pool.isDefault) { ElMessage.warning(t('storage.cannotDeleteDefault')); return }
  try {
    await ElMessageBox.confirm(t('storage.deletePoolConfirm').replace('{name}', pool.name), t('common.confirm'), { type: 'warning', confirmButtonText: t('common.delete'), cancelButtonText: t('common.cancel') })
    const result = await storageApi.deletePool(pool.id) as any
    if (result.success) { ElMessage.success(t('storage.deleteSuccess')); await loadData() }
    else { ElMessage.error(result.message || t('common.failed')) }
  } catch (e: any) { if (e !== 'cancel') ElMessage.error(e.message || t('common.failed')) }
}

onMounted(() => loadData())
</script>

<template>
  <div class="storage-page">
    <div class="page-header">
      <div class="header-left">
        <h1>{{ t('storage.title') }}</h1>
        <p>{{ t('storage.subtitle') }}</p>
      </div>
      <div class="header-actions">
        <el-button :icon="Refresh" @click="loadData" :loading="loading">{{ t('common.refresh') }}</el-button>
        <el-button type="primary" :icon="Plus" @click="openCreateDialog">{{ t('storage.createPool') }}</el-button>
      </div>
    </div>

    <div class="overview-grid">
      <div class="overview-main">
        <div class="main-header">
          <div class="main-icon"><el-icon :size="28"><Box /></el-icon></div>
          <div class="main-title">
            <h3>{{ t('storage.overview') }}</h3>
            <p>{{ t('storage.subtitle') }}</p>
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
          <div class="stat"><span class="label">{{ t('storage.usedSpace') }}</span><span class="value">{{ formatSize(overview?.usedBytes || 0) }}</span></div>
          <div class="stat"><span class="label">{{ t('storage.availableSpace') }}</span><span class="value success">{{ formatSize(overview?.availableBytes || 0) }}</span></div>
          <div class="stat"><span class="label">{{ t('storage.totalCapacity') }}</span><span class="value">{{ formatSize(overview?.totalBytes || 0) }}</span></div>
        </div>
      </div>

      <div class="overview-card">
        <div class="card-icon pools"><el-icon :size="24"><FolderOpened /></el-icon></div>
        <div class="card-info"><div class="card-value">{{ overview?.poolCount || 0 }}</div><div class="card-label">{{ t('storage.pools') }}</div></div>
      </div>

      <div class="overview-card">
        <div class="card-icon" :style="{ background: healthStatus.color + '20', color: healthStatus.color }">
          <el-icon :size="24"><InfoFilled /></el-icon>
        </div>
        <div class="card-info"><div class="card-value" :style="{ color: healthStatus.color }">{{ healthStatus.text }}</div><div class="card-label">{{ t('storage.systemStatus') }}</div></div>
      </div>
    </div>

    <div class="pools-section">
      <div class="section-header"><h2>{{ t('storage.pools') }}</h2><span class="count">{{ pools.length }} {{ t('dashboard.pools') }}</span></div>
      
      <div class="pools-list" v-loading="loading">
        <div v-for="pool in pools" :key="pool.id" class="pool-item" :class="{ 'is-default': pool.isDefault }">
          <div class="pool-left">
            <div class="pool-icon"><el-icon :size="28"><FolderOpened /></el-icon></div>
            <div class="pool-info">
              <div class="pool-name">
                {{ pool.name }}
                <el-tag v-if="pool.isDefault" type="primary" size="small" effect="light">{{ t('storage.default') }}</el-tag>
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
                <span>{{ t('storage.capacityLimit') }}: {{ formatSize(pool.maxCapacityBytes) }}</span>
              </div>
            </div>
          </div>

          <div class="pool-actions">
            <el-tooltip :content="t('common.edit')" placement="top">
              <el-button text circle @click="openEditDialog(pool)"><el-icon><Edit /></el-icon></el-button>
            </el-tooltip>
            <el-tooltip v-if="!pool.isDefault" :content="t('storage.setDefault')" placement="top">
              <el-button text circle @click="handleSetDefault(pool)"><el-icon><Check /></el-icon></el-button>
            </el-tooltip>
            <el-tooltip v-if="!pool.isDefault" :content="t('common.delete')" placement="top">
              <el-button text circle type="danger" @click="handleDelete(pool)"><el-icon><Delete /></el-icon></el-button>
            </el-tooltip>
          </div>
        </div>

        <div v-if="pools.length === 0 && !loading" class="empty-state">
          <div class="empty-icon"><el-icon :size="64"><Box /></el-icon></div>
          <h3>{{ t('storage.noPools') }}</h3>
          <p>{{ t('storage.createPoolHint') }}</p>
          <el-button type="primary" @click="openCreateDialog"><el-icon><Plus /></el-icon>{{ t('storage.createPool') }}</el-button>
        </div>
      </div>
    </div>

    <!-- 新建存储池对话框 -->
    <el-dialog v-model="createDialogVisible" :title="t('storage.createPool')" width="500px" :close-on-click-modal="false">
      <el-form :model="poolForm" label-position="top">
        <el-form-item :label="t('storage.poolName')" required>
          <el-input v-model="poolForm.name" :placeholder="t('storage.poolNamePlaceholder')" maxlength="50" show-word-limit />
        </el-form-item>
        <el-form-item :label="t('storage.mountPath')" required>
          <div class="path-input-wrapper">
            <el-input v-model="poolForm.mountPath" :placeholder="t('storage.selectPathHint')" readonly />
            <el-button type="primary" @click="openPathDialog"><el-icon><FolderOpened /></el-icon>{{ t('storage.selectPath') }}</el-button>
          </div>
          <div class="form-tip"><el-icon><InfoFilled /></el-icon><span>{{ t('storage.selectDirHint') }}</span></div>
        </el-form-item>
        <el-form-item :label="t('storage.maxCapacity')">
          <el-input-number v-model="poolForm.maxCapacityGB" :min="0" :max="102400" :step="10" :placeholder="t('storage.noLimitHint')" style="width: 100%" />
          <div class="form-tip"><el-icon><InfoFilled /></el-icon><span>{{ t('storage.noLimitHint') }}</span></div>
        </el-form-item>
        <el-form-item :label="t('storage.description')">
          <el-input v-model="poolForm.description" type="textarea" :rows="3" :placeholder="t('storage.descriptionPlaceholder')" maxlength="200" show-word-limit />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">{{ t('common.cancel') }}</el-button>
        <el-button type="primary" @click="handleCreate" :loading="creating">{{ t('common.create') }}</el-button>
      </template>
    </el-dialog>

    <!-- 编辑存储池对话框 -->
    <el-dialog v-model="editDialogVisible" :title="t('storage.editPool')" width="500px" :close-on-click-modal="false">
      <el-form :model="poolForm" label-position="top">
        <el-form-item :label="t('storage.poolName')" required>
          <el-input v-model="poolForm.name" :placeholder="t('storage.poolName')" maxlength="50" show-word-limit />
        </el-form-item>
        <el-form-item :label="t('storage.mountPath')">
          <el-input :model-value="poolForm.mountPath" disabled />
          <div class="form-tip warning"><el-icon><Warning /></el-icon><span>{{ t('storage.pathCannotModify') }}</span></div>
        </el-form-item>
        <el-form-item :label="t('storage.maxCapacity')">
          <el-input-number v-model="poolForm.maxCapacityGB" :min="0" :max="102400" :step="10" :placeholder="t('storage.cancelLimitHint')" style="width: 100%" />
          <div class="form-tip"><el-icon><InfoFilled /></el-icon><span>{{ t('storage.cancelLimitHint') }}</span></div>
        </el-form-item>
        <el-form-item :label="t('storage.description')">
          <el-input v-model="poolForm.description" type="textarea" :rows="3" :placeholder="t('storage.descriptionPlaceholder')" maxlength="200" show-word-limit />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialogVisible = false">{{ t('common.cancel') }}</el-button>
        <el-button type="primary" @click="handleUpdate" :loading="updating">{{ t('common.save') }}</el-button>
      </template>
    </el-dialog>

    <!-- 目录选择对话框 -->
    <el-dialog v-model="pathDialogVisible" :title="t('storage.selectPath')" width="600px" :close-on-click-modal="false">
      <div class="path-browser">
        <div class="browser-header">
          <el-button :icon="Back" :disabled="!parentPath && !browsingPath" @click="goToParent" size="small">{{ t('storage.goBack') }}</el-button>
          <div class="current-path">
            <el-icon><Folder /></el-icon>
            <span>{{ browsingPath || t('storage.myComputer') }}</span>
          </div>
        </div>
        <div class="browser-content" v-loading="browsingLoading">
          <div v-if="directories.length === 0 && !browsingLoading" class="empty-dir">
            <el-icon :size="48" color="#d1d5db"><Folder /></el-icon>
            <p>{{ t('storage.emptyDir') }}</p>
          </div>
          <div v-for="dir in directories" :key="dir.path" class="dir-item" @dblclick="selectDirectory(dir)">
            <el-icon :size="20" color="#f59e0b"><Folder /></el-icon>
            <span class="dir-name">{{ dir.name }}</span>
            <el-button text size="small" @click.stop="selectDirectory(dir)">{{ t('storage.open') }}</el-button>
          </div>
        </div>
      </div>
      <template #footer>
        <div class="path-dialog-footer">
          <div class="selected-path">
            <span class="label">{{ t('storage.selected') }}:</span>
            <span class="path">{{ browsingPath || t('storage.notSelected') }}</span>
          </div>
          <div class="actions">
            <el-button @click="pathDialogVisible = false">{{ t('common.cancel') }}</el-button>
            <el-button type="primary" @click="confirmPath" :disabled="!browsingPath">{{ t('common.confirm') }}</el-button>
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
    .bar-track { height: 12px; background: var(--el-fill-color); border-radius: 6px; overflow: hidden; border: 1px solid var(--el-border-color-lighter);
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
      .usage-bar { height: 8px; background: var(--el-fill-color); border-radius: 4px; overflow: hidden; border: 1px solid var(--el-border-color-lighter);
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
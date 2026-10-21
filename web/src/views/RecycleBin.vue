<script setup lang="ts">
/**
 * 回收站页（2026-10-19 从 Files 抽屉提升为独立菜单页）。
 *
 * 与原抽屉的差异：新增单条永久删除。fileApi.delete(paths, permanent=true)
 * 后端一直支持（覆盖上传/永久删除前还会自动产版本），但原抽屉只有恢复
 * 与清空，单条永久删除的入口缺失，这里补上。
 */
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Refresh } from '@element-plus/icons-vue'
import { fileApi } from '@/api/file'
import { formatSize, formatDate, getFileIcon, getFileIconColor } from '@/utils/format'
import type { FileItem } from '@/types'

const { t } = useI18n()

const items = ref<FileItem[]>([])
const loading = ref(false)
const removing = ref(false)

async function load() {
  loading.value = true
  try {
    const result = await fileApi.getRecycleBin()
    if (result.success && result.data) {
      items.value = result.data
    } else {
      ElMessage.error(result.error?.message || t('recycle.loadFailed'))
    }
  } catch {
    ElMessage.error(t('recycle.loadFailed'))
  } finally {
    loading.value = false
  }
}

// 恢复：回到原路径后从列表移除
async function restore(item: FileItem) {
  try {
    const result = await fileApi.restore(item.id)
    if (result.success) {
      ElMessage.success(t('recycle.restoreSuccess'))
      items.value = items.value.filter(i => i.id !== item.id)
    } else {
      ElMessage.error(result.error?.message || t('recycle.restoreFailed'))
    }
  } catch {
    ElMessage.error(t('recycle.restoreFailed'))
  }
}

// 永久删除：不可恢复，确认文案明示代价
async function permanentDelete(item: FileItem) {
  try {
    await ElMessageBox.confirm(
      t('recycle.permanentDeleteConfirm', { name: item.name }),
      t('recycle.permanentDelete'),
      { confirmButtonText: t('recycle.permanentDelete'), cancelButtonText: t('common.cancel'), type: 'warning', confirmButtonClass: 'el-button--danger' }
    )
  } catch {
    return
  }
  removing.value = true
  try {
    const result = await fileApi.delete([item.path], true)
    if (result.success) {
      ElMessage.success(t('recycle.deleteDone'))
      items.value = items.value.filter(i => i.id !== item.id)
    } else {
      ElMessage.error(result.error?.message || t('recycle.deleteFailed'))
    }
  } catch {
    ElMessage.error(t('recycle.deleteFailed'))
  } finally {
    removing.value = false
  }
}

// 清空：整站一次性永久删除
async function emptyAll() {
  try {
    await ElMessageBox.confirm(t('recycle.emptyConfirm'), t('recycle.empty'), {
      confirmButtonText: t('recycle.empty'), cancelButtonText: t('common.cancel'), type: 'warning', confirmButtonClass: 'el-button--danger'
    })
  } catch {
    return
  }
  removing.value = true
  try {
    const result = await fileApi.emptyRecycleBin()
    if (result.success) {
      ElMessage.success(t('recycle.emptyDone'))
      items.value = []
    } else {
      ElMessage.error(result.error?.message || t('recycle.emptyFailed'))
    }
  } catch {
    ElMessage.error(t('recycle.emptyFailed'))
  } finally {
    removing.value = false
  }
}

// el-table 插槽的 row 是框架的 DefaultRow（无字段类型），统一收敛成 FileItem 再用
const asItem = (row: any): FileItem => row as FileItem

onMounted(load)
</script>

<template>
  <div class="recycle-page">
    <div class="page-header page-header--with-actions">
      <div class="header-left">
        <h1>{{ $t('recycle.title') }}</h1>
        <p>{{ $t('recycle.subtitle') }}</p>
      </div>
      <div class="header-actions">
        <el-button :icon="Refresh" :loading="loading" @click="load">{{ $t('common.refresh') }}</el-button>
        <el-button type="danger" :icon="Delete" :disabled="items.length === 0" :loading="removing" @click="emptyAll">
          {{ $t('recycle.empty') }}
        </el-button>
      </div>
    </div>

    <el-alert type="info" :closable="false" show-icon :title="$t('recycle.hint')" class="recycle-alert" />

    <el-table :data="items" v-loading="loading">
      <el-table-column :label="$t('recycle.colName')" min-width="220">
        <template #default="{ row }">
          <div class="file-name">
            <el-icon :color="getFileIconColor(asItem(row))">
              <component :is="getFileIcon(asItem(row))" />
            </el-icon>
            <span>{{ asItem(row).name }}</span>
          </div>
        </template>
      </el-table-column>
      <el-table-column prop="path" :label="$t('recycle.colOriginalPath')" min-width="220" />
      <el-table-column :label="$t('recycle.colSize')" width="110">
        <template #default="{ row }">
          {{ asItem(row).isFolder ? $t('common.none') : formatSize(asItem(row).sizeBytes) }}
        </template>
      </el-table-column>
      <el-table-column :label="$t('recycle.colDeletedAt')" width="170">
        <template #default="{ row }">{{ formatDate(asItem(row).updatedAt ?? asItem(row).createdAt) }}</template>
      </el-table-column>
      <el-table-column :label="$t('recycle.colActions')" width="180" fixed="right">
        <template #default="{ row }">
          <el-button size="small" type="primary" @click="restore(asItem(row))">
            {{ $t('recycle.restore') }}
          </el-button>
          <el-button size="small" type="danger" :loading="removing" @click="permanentDelete(asItem(row))">
            {{ $t('recycle.permanentDelete') }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-empty v-if="items.length === 0 && !loading" :description="$t('recycle.emptyState')" />
  </div>
</template>

<style scoped>
.recycle-page { }



.recycle-alert {
  margin-bottom: 16px;
}

.file-name {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;

  span {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
}
</style>

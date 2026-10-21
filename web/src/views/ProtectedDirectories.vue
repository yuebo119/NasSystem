<script setup lang="ts">
/**
 * 只读保护目录页（2026-10-19 从快照页第二个 tab 提升为独立菜单页）。
 *
 * 防勒索最小可行形态：被标记的目录及其全部子孙在应用层禁止覆盖与
 * 删除，网页端与 WebDAV 共用 FileService 同一处拦截，SMB 直通
 * 模式不受此约束（管理员显式选择，文档明示）。
 */
import { ref, reactive, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Lock, Refresh, Plus } from '@element-plus/icons-vue'
import { protectedDirectoryApi } from '@/api/snapshot'
import { fileApi } from '@/api/file'
import { formatDate } from '@/utils/format'
import { useDrawerSize } from '@/utils/useIsMobile'
import type { ProtectedDirectory } from '@/types'

const { t } = useI18n()

const dirs = ref<ProtectedDirectory[]>([])
const loading = ref(false)
const markDialogVisible = ref(false)
// 模态统一右侧抽屉：窄屏全宽，宽屏 clamp 随视口连续缩放（与 Users/Storage 一致）
const drawerSize = useDrawerSize()
const markForm = reactive({ path: '', note: '' })
const marking = ref(false)

async function load() {
  loading.value = true
  try {
    const resp = await protectedDirectoryApi.list()
    if (resp.success && resp.data) {
      dirs.value = resp.data
    }
  } finally {
    loading.value = false
  }
}

async function submitMark() {
  const path = markForm.path.trim()
  if (!path) {
    ElMessage.warning(t('protected.pathRequired'))
    return
  }
  marking.value = true
  try {
    // 路径 → FileItemId：getFileInfo 按虚拟路径查（不存在即报错由后端文案覆盖）
    const info = await fileApi.getFileInfo(path)
    if (!info.success || !info.data) {
      ElMessage.error(t('protected.pathNotFound'))
      return
    }
    const resp = await protectedDirectoryApi.mark(
      info.data.id,
      markForm.note.trim() || undefined
    )
    if (resp.success) {
      ElMessage.success(t('common.success'))
      markDialogVisible.value = false
      markForm.path = ''
      markForm.note = ''
      await load()
    } else {
      ElMessage.error(resp.error?.message || t('common.failed'))
    }
  } finally {
    marking.value = false
  }
}

async function unmark(dir: ProtectedDirectory) {
  try {
    await ElMessageBox.confirm(t('protected.unmarkConfirm', { path: dir.path }), {
      type: 'warning',
      confirmButtonClass: 'el-button--danger'
    })
  } catch {
    return
  }
  const resp = await protectedDirectoryApi.unmark(dir.fileItemId)
  if (resp.success) {
    ElMessage.success(t('common.success'))
    await load()
  } else {
    ElMessage.error(resp.error?.message || t('common.failed'))
  }
}

// el-table 插槽的 row 是框架的 DefaultRow（无字段类型），统一收敛成具体 DTO 再用
const asProtected = (row: any): ProtectedDirectory => row as ProtectedDirectory

onMounted(load)
</script>

<template>
  <div class="protected-page">
    <div class="page-header page-header--with-actions">
      <div class="header-left">
        <h1>{{ $t('protected.title') }}</h1>
        <p>{{ $t('protected.subtitle') }}</p>
      </div>
      <div class="header-actions">
        <el-button :icon="Refresh" :loading="loading" @click="load">{{ $t('common.refresh') }}</el-button>
        <el-button type="primary" :icon="Plus" @click="markDialogVisible = true">
          {{ $t('protected.mark') }}
        </el-button>
      </div>
    </div>

    <el-alert type="info" :closable="false" show-icon :title="$t('protected.desc')" class="protected-alert" />

    <el-table :data="dirs" v-loading="loading">
      <el-table-column prop="path" :label="$t('protected.path')" min-width="220" />
      <el-table-column prop="note" :label="$t('protected.note')" min-width="160">
        <template #default="{ row }">{{ asProtected(row).note || $t('common.none') }}</template>
      </el-table-column>
      <el-table-column :label="$t('protected.createdAt')" width="170">
        <template #default="{ row }">{{ formatDate(asProtected(row).createdAt) }}</template>
      </el-table-column>
      <el-table-column :label="$t('protected.actions')" width="120" fixed="right">
        <template #default="{ row }">
          <el-button link type="danger" @click="unmark(asProtected(row))">
            {{ $t('protected.unmark') }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-empty v-if="dirs.length === 0 && !loading" :description="$t('protected.empty')" />

    <!-- 标记保护抽屉：全站模态统一右侧抽屉（main.scss 的 .el-drawer 覆盖），
         原 el-dialog 是 mac 化改造的漏网之鱼（EP 默认 4px 圆角、无头脚发丝线） -->
    <el-drawer v-model="markDialogVisible" :title="$t('protected.mark')" :size="drawerSize('420px', '38vw', '560px')" :mask-closable="false">
      <el-form label-position="top">
        <el-form-item :label="$t('protected.path')" required>
          <el-input v-model="markForm.path" placeholder="/Docs" />
          <div class="form-hint">{{ $t('protected.pathHint') }}</div>
        </el-form-item>
        <el-form-item :label="$t('protected.note')">
          <el-input v-model="markForm.note" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="markDialogVisible = false">{{ $t('common.cancel') }}</el-button>
        <el-button type="primary" :icon="Lock" :loading="marking" @click="submitMark">
          {{ $t('common.confirm') }}
        </el-button>
      </template>
    </el-drawer>
  </div>
</template>

<style scoped>
.protected-page { }



.protected-alert {
  margin-bottom: 16px;
}

.form-hint {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.5;
  margin-top: 4px;
}
</style>

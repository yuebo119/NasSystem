<script setup lang="ts">
// 清单快照与只读保护管理页（ROADMAP Phase 2）
import { ref, reactive, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Right } from '@element-plus/icons-vue'
import { snapshotApi, protectedDirectoryApi, exportApi } from '@/api/snapshot'
import { fileApi } from '@/api/file'
import { formatSize, formatDate } from '@/utils/format'
import type { Snapshot, SnapshotEntry, SnapshotDiff, SnapshotDiffItem, ProtectedDirectory } from '@/types'

const { t } = useI18n()

// ---- 快照列表 ----
const activeTab = ref('snapshots')
const snapshots = ref<Snapshot[]>([])
const snapshotsTotal = ref(0)
const snapshotsPage = ref(1)
const snapshotsPageSize = ref(20)
const snapshotsLoading = ref(false)

async function loadSnapshots() {
  snapshotsLoading.value = true
  try {
    const resp = await snapshotApi.getList(snapshotsPage.value, snapshotsPageSize.value)
    if (resp.success && resp.data) {
      snapshots.value = resp.data.items
      snapshotsTotal.value = resp.data.totalCount
    } else {
      ElMessage.error(resp.error?.message || t('common.failed'))
    }
  } finally {
    snapshotsLoading.value = false
  }
}

// ---- 创建快照 ----
const createDialogVisible = ref(false)
const createForm = reactive({ rootPath: '/', name: '' })
const creating = ref(false)

async function submitCreate() {
  if (!createForm.rootPath.trim()) {
    ElMessage.warning(t('snapshot.rootPathRequired'))
    return
  }
  creating.value = true
  try {
    const resp = await snapshotApi.create(
      createForm.rootPath.trim(),
      createForm.name.trim() || undefined
    )
    if (resp.success && resp.data) {
      ElMessage.success(
        t('snapshot.createSuccess', {
          files: resp.data.fileCount,
          folders: resp.data.folderCount
        })
      )
      createDialogVisible.value = false
      createForm.name = ''
      await loadSnapshots()
    } else {
      ElMessage.error(resp.error?.message || t('common.failed'))
    }
  } finally {
    creating.value = false
  }
}

// ---- 详情抽屉（条目分页） ----
const detailVisible = ref(false)
const detailSnapshot = ref<Snapshot | null>(null)
const detailEntries = ref<SnapshotEntry[]>([])
const detailTotal = ref(0)
const detailPage = ref(1)
const detailLoading = ref(false)

async function openDetail(snapshot: Snapshot) {
  detailSnapshot.value = snapshot
  detailVisible.value = true
  detailPage.value = 1
  await loadEntries()
}

async function loadEntries() {
  if (!detailSnapshot.value) return
  detailLoading.value = true
  try {
    const resp = await snapshotApi.getEntries(detailSnapshot.value.id, detailPage.value, 100)
    if (resp.success && resp.data) {
      detailEntries.value = resp.data.items
      detailTotal.value = resp.data.totalCount
    }
  } finally {
    detailLoading.value = false
  }
}

// ---- 对比对话框 ----
const diffDialogVisible = ref(false)
const diffBaseId = ref('')
const diffTargetId = ref('')
const diffResult = ref<SnapshotDiff | null>(null)
const diffLoading = ref(false)

function openDiff() {
  if (snapshots.value.length < 2) {
    ElMessage.warning(t('snapshot.needTwoSnapshots'))
    return
  }
  // 默认选最新的两份：target=最新，base=次新
  diffTargetId.value = snapshots.value[0]?.id ?? ''
  diffBaseId.value = snapshots.value[1]?.id ?? ''
  diffResult.value = null
  diffDialogVisible.value = true
}

async function runDiff() {
  if (!diffBaseId.value || !diffTargetId.value) return
  if (diffBaseId.value === diffTargetId.value) {
    ElMessage.warning(t('snapshot.sameSnapshot'))
    return
  }
  diffLoading.value = true
  try {
    const resp = await snapshotApi.diff(diffBaseId.value, diffTargetId.value, 1, 200)
    if (resp.success && resp.data) {
      diffResult.value = resp.data
    } else {
      ElMessage.error(resp.error?.message || t('common.failed'))
    }
  } finally {
    diffLoading.value = false
  }
}

// ---- 删除快照 ----
async function removeSnapshot(snapshot: Snapshot) {
  await ElMessageBox.confirm(t('snapshot.deleteConfirm', { name: snapshotLabel(snapshot) }), {
    type: 'warning',
    confirmButtonClass: 'el-button--danger'
  })
  const resp = await snapshotApi.remove(snapshot.id)
  if (resp.success) {
    ElMessage.success(t('common.success'))
    await loadSnapshots()
  } else {
    ElMessage.error(resp.error?.message || t('common.failed'))
  }
}

// ---- 3-2-1 导出 ----
const exportDialogVisible = ref(false)
const exportForm = reactive({ rootPath: '/', password: '' })
const exporting = ref(false)

async function submitExport() {
  const path = exportForm.rootPath.trim()
  if (!path) {
    ElMessage.warning(t('snapshot.rootPathRequired'))
    return
  }
  exporting.value = true
  try {
    const blob = await exportApi.exportDirectory(path, exportForm.password)
    const suffix = exportForm.password ? '.zip.enc' : '.zip'
    const baseName = path === '/' ? 'all' : path.split('/').filter(Boolean).pop() ?? 'export'
    const fileName = `${baseName}-${Date.now()}${suffix}`
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = fileName
    anchor.click()
    URL.revokeObjectURL(url)
    exportDialogVisible.value = false
    exportForm.password = ''
    ElMessage.success(t('snapshot.exportDone'))
  } catch {
    // 错误消息由 http 拦截器统一弹出（400/404/500 分支）
  } finally {
    exporting.value = false
  }
}

// ---- 只读保护 ----
const protectedDirs = ref<ProtectedDirectory[]>([])
const protectedLoading = ref(false)
const markDialogVisible = ref(false)
const markForm = reactive({ path: '', note: '' })
const marking = ref(false)

async function loadProtected() {
  protectedLoading.value = true
  try {
    const resp = await protectedDirectoryApi.list()
    if (resp.success && resp.data) {
      protectedDirs.value = resp.data
    }
  } finally {
    protectedLoading.value = false
  }
}

async function submitMark() {
  const path = markForm.path.trim()
  if (!path) {
    ElMessage.warning(t('snapshot.protectedPathRequired'))
    return
  }
  marking.value = true
  try {
    // 路径 → FileItemId：getFileInfo 按虚拟路径查（不存在即报错由后端文案覆盖）
    const info = await fileApi.getFileInfo(path)
    if (!info.success || !info.data) {
      ElMessage.error(t('snapshot.pathNotFound'))
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
      await loadProtected()
    } else {
      ElMessage.error(resp.error?.message || t('common.failed'))
    }
  } finally {
    marking.value = false
  }
}

async function unmark(dir: ProtectedDirectory) {
  await ElMessageBox.confirm(t('snapshot.unmarkConfirm', { path: dir.path }), {
    type: 'warning',
    confirmButtonClass: 'el-button--danger'
  })
  const resp = await protectedDirectoryApi.unmark(dir.fileItemId)
  if (resp.success) {
    ElMessage.success(t('common.success'))
    await loadProtected()
  } else {
    ElMessage.error(resp.error?.message || t('common.failed'))
  }
}

// ---- 展示辅助 ----
// el-table 插槽的 row 是框架的 DefaultRow（无字段类型），统一收敛成具体
// DTO 再用，避免每处 TemplateRef 断言散落（对齐 Shares.vue 的 asShare）
const asSnapshot = (row: any): Snapshot => row as Snapshot
const asProtected = (row: any): ProtectedDirectory => row as ProtectedDirectory
const asDiffItem = (row: any): SnapshotDiffItem => row as SnapshotDiffItem
const asEntry = (row: any): SnapshotEntry => row as SnapshotEntry

function snapshotLabel(s: Snapshot): string {
  return s.name || s.rootPath
}

function statusTagType(status: number): 'success' | 'danger' | 'warning' {
  if (status === 1) return 'success'
  if (status === 2) return 'danger'
  return 'warning'
}

function statusText(status: number): string {
  if (status === 1) return t('snapshot.statusCompleted')
  if (status === 2) return t('snapshot.statusFailed')
  return t('snapshot.statusRunning')
}

function snapshotOptionLabel(s: Snapshot): string {
  return `${snapshotLabel(s)}（${formatDate(s.createdAt)}）`
}

type TagType = 'success' | 'danger' | 'warning' | 'info'
const diffKindText: Record<number, { type: TagType; key: string }> = {
  0: { type: 'success', key: 'snapshot.kindAdded' },
  1: { type: 'danger', key: 'snapshot.kindRemoved' },
  2: { type: 'warning', key: 'snapshot.kindModified' },
  3: { type: 'info', key: 'snapshot.kindUnchanged' }
}

function diffTagOf(kind: number): { type: TagType; text: string } {
  const entry = diffKindText[kind] ?? { type: 'info' as TagType, key: 'common.none' }
  return { type: entry.type, text: t(entry.key) }
}

// diff 条目默认只看有差异的（未变的全量在快照详情里看）
const diffOnlyChanged = ref(true)
const visibleDiffItems = () => {
  if (!diffResult.value) return []
  return diffOnlyChanged.value
    ? diffResult.value.items.filter((i) => i.kind !== 3)
    : diffResult.value.items
}

onMounted(() => {
  loadSnapshots()
  loadProtected()
})
</script>

<template>
  <div class="snapshots-page">
    <el-tabs v-model="activeTab">
      <!-- ═══ Tab 1：清单快照 ═══ -->
      <el-tab-pane :label="$t('snapshot.title')" name="snapshots">
        <div class="toolbar">
          <el-button type="primary" @click="createDialogVisible = true">
            {{ $t('snapshot.create') }}
          </el-button>
          <el-button :disabled="snapshots.length < 2" @click="openDiff">
            {{ $t('snapshot.compare') }}
          </el-button>
          <el-button @click="exportDialogVisible = true">
            {{ $t('snapshot.export') }}
          </el-button>
          <el-button :loading="snapshotsLoading" @click="loadSnapshots">
            {{ $t('common.refresh') }}
          </el-button>
        </div>

        <el-table :data="snapshots" v-loading="snapshotsLoading" border>
          <el-table-column :label="$t('snapshot.name')" min-width="160">
            <template #default="{ row }">
              {{ snapshotLabel(asSnapshot(row)) }}
            </template>
          </el-table-column>
          <el-table-column prop="rootPath" :label="$t('snapshot.rootPath')" min-width="140" />
          <el-table-column :label="$t('common.status')" width="90">
            <template #default="{ row }">
              <el-tag :type="statusTagType(asSnapshot(row).status)" size="small">
                {{ statusText(asSnapshot(row).status) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="$t('snapshot.fileCount')" width="90">
            <template #default="{ row }">{{ row.fileCount }}</template>
          </el-table-column>
          <el-table-column :label="$t('snapshot.folderCount')" width="90">
            <template #default="{ row }">{{ row.folderCount }}</template>
          </el-table-column>
          <el-table-column :label="$t('snapshot.totalSize')" width="110">
            <template #default="{ row }">{{ formatSize(row.totalSizeBytes) }}</template>
          </el-table-column>
          <el-table-column :label="$t('snapshot.createdAt')" width="170">
            <template #default="{ row }">{{ formatDate(asProtected(row).createdAt) }}</template>
          </el-table-column>
          <el-table-column :label="$t('common.none')" width="200" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" @click="openDetail(asSnapshot(row))">
                {{ $t('snapshot.detail') }}
              </el-button>
              <el-button link type="danger" @click="removeSnapshot(asSnapshot(row))">
                {{ $t('common.delete') }}
              </el-button>
            </template>
          </el-table-column>
        </el-table>

        <el-pagination
          v-model:current-page="snapshotsPage"
          :page-size="snapshotsPageSize"
          :total="snapshotsTotal"
          layout="total, prev, pager, next"
          @current-change="loadSnapshots"
        />
      </el-tab-pane>

      <!-- ═══ Tab 2：只读保护 ═══ -->
      <el-tab-pane :label="$t('snapshot.protectedTitle')" name="protected">
        <div class="toolbar">
          <el-button type="primary" @click="markDialogVisible = true">
            {{ $t('snapshot.markProtected') }}
          </el-button>
          <el-button :loading="protectedLoading" @click="loadProtected">
            {{ $t('common.refresh') }}
          </el-button>
        </div>

        <el-alert type="info" :closable="false" show-icon
          :title="$t('snapshot.protectedDesc')" class="protected-alert" />

        <el-table :data="protectedDirs" v-loading="protectedLoading" border>
          <el-table-column prop="path" :label="$t('snapshot.protectedPath')" min-width="200" />
          <el-table-column prop="note" :label="$t('snapshot.protectedNote')" min-width="140">
            <template #default="{ row }">{{ asProtected(row).note || $t('common.none') }}</template>
          </el-table-column>
          <el-table-column :label="$t('snapshot.createdAt')" width="170">
            <template #default="{ row }">{{ formatDate(asProtected(row).createdAt) }}</template>
          </el-table-column>
          <el-table-column :label="$t('common.none')" width="120" fixed="right">
            <template #default="{ row }">
              <el-button link type="danger" @click="unmark(asProtected(row))">
                {{ $t('snapshot.unmark') }}
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>

    <!-- 创建快照对话框 -->
    <el-dialog v-model="createDialogVisible" :title="$t('snapshot.create')" width="480px">
      <el-form label-position="top">
        <el-form-item :label="$t('snapshot.rootPath')" required>
          <el-input v-model="createForm.rootPath" placeholder="/" />
          <div class="form-hint">{{ $t('snapshot.rootPathHint') }}</div>
        </el-form-item>
        <el-form-item :label="$t('snapshot.name')">
          <el-input v-model="createForm.name" :placeholder="$t('snapshot.namePlaceholder')" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">{{ $t('common.cancel') }}</el-button>
        <el-button type="primary" :loading="creating" @click="submitCreate">
          {{ $t('common.confirm') }}
        </el-button>
      </template>
    </el-dialog>

    <!-- 快照详情抽屉（条目分页） -->
    <el-drawer v-model="detailVisible" :title="$t('snapshot.detail')" size="520px">
      <template #header>
        <span>{{ detailSnapshot ? snapshotLabel(detailSnapshot) : '' }}</span>
      </template>
      <el-table :data="detailEntries" v-loading="detailLoading" border size="small">
        <el-table-column prop="relativePath" :label="$t('snapshot.path')" min-width="220" />
        <el-table-column :label="$t('snapshot.type')" width="70">
          <template #default="{ row }">
            {{ asEntry(row).isFolder ? $t('snapshot.folder') : $t('snapshot.file') }}
          </template>
        </el-table-column>
        <el-table-column :label="$t('snapshot.totalSize')" width="100">
          <template #default="{ row }">
            {{ asEntry(row).isFolder ? $t('common.none') : formatSize(asEntry(row).sizeBytes) }}
          </template>
        </el-table-column>
      </el-table>
      <el-pagination
        v-model:current-page="detailPage"
        :page-size="100"
        :total="detailTotal"
        layout="total, prev, pager, next"
        @current-change="loadEntries"
      />
    </el-drawer>

    <!-- 对比对话框 -->
    <el-dialog v-model="diffDialogVisible" :title="$t('snapshot.compare')" width="760px">
      <div class="diff-picker">
        <el-select v-model="diffBaseId" :placeholder="$t('snapshot.baseSnapshot')">
          <el-option
            v-for="s in snapshots"
            :key="s.id"
            :value="s.id"
            :label="snapshotOptionLabel(s)"
          />
        </el-select>
        <el-icon class="diff-arrow"><Right /></el-icon>
        <el-select v-model="diffTargetId" :placeholder="$t('snapshot.targetSnapshot')">
          <el-option
            v-for="s in snapshots"
            :key="s.id"
            :value="s.id"
            :label="snapshotOptionLabel(s)"
          />
        </el-select>
        <el-button type="primary" :loading="diffLoading" @click="runDiff">
          {{ $t('snapshot.runCompare') }}
        </el-button>
      </div>

      <template v-if="diffResult">
        <div class="diff-summary">
          <el-tag type="success" size="large">
            {{ $t('snapshot.kindAdded') }} {{ diffResult.addedCount }}
          </el-tag>
          <el-tag type="danger" size="large">
            {{ $t('snapshot.kindRemoved') }} {{ diffResult.removedCount }}
          </el-tag>
          <el-tag type="warning" size="large">
            {{ $t('snapshot.kindModified') }} {{ diffResult.modifiedCount }}
          </el-tag>
          <el-tag type="info" size="large">
            {{ $t('snapshot.kindUnchanged') }} {{ diffResult.unchangedCount }}
          </el-tag>
          <el-checkbox v-model="diffOnlyChanged" class="diff-filter">
            {{ $t('snapshot.onlyChanged') }}
          </el-checkbox>
        </div>

        <el-table :data="visibleDiffItems()" border size="small" max-height="360">
          <el-table-column prop="relativePath" :label="$t('snapshot.path')" min-width="240" />
          <el-table-column :label="$t('snapshot.change')" width="90">
            <template #default="{ row }">
              <el-tag :type="diffTagOf(asDiffItem(row).kind).type" size="small">
                {{ diffTagOf(asDiffItem(row).kind).text }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="$t('snapshot.sizeChange')" min-width="160">
            <template #default="{ row }">
              <template v-if="asDiffItem(row).kind === 0">{{ formatSize(asDiffItem(row).targetSizeBytes) }}</template>
              <template v-else-if="asDiffItem(row).kind === 1">{{ formatSize(asDiffItem(row).baseSizeBytes) }}</template>
              <template v-else-if="asDiffItem(row).kind === 2">
                {{ formatSize(asDiffItem(row).baseSizeBytes) }} → {{ formatSize(asDiffItem(row).targetSizeBytes) }}
              </template>
              <template v-else>{{ formatSize(asDiffItem(row).targetSizeBytes) }}</template>
            </template>
          </el-table-column>
        </el-table>
      </template>
    </el-dialog>

    <!-- 3-2-1 导出对话框 -->
    <el-dialog v-model="exportDialogVisible" :title="$t('snapshot.export')" width="480px">
      <el-form label-position="top">
        <el-form-item :label="$t('snapshot.rootPath')" required>
          <el-input v-model="exportForm.rootPath" placeholder="/" />
          <div class="form-hint">{{ $t('snapshot.exportHint') }}</div>
        </el-form-item>
        <el-form-item :label="$t('snapshot.exportPassword')">
          <el-input
            v-model="exportForm.password"
            type="password"
            show-password
            :placeholder="$t('snapshot.exportPasswordPlaceholder')"
          />
          <div class="form-hint">{{ $t('snapshot.exportPasswordHint') }}</div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="exportDialogVisible = false">{{ $t('common.cancel') }}</el-button>
        <el-button type="primary" :loading="exporting" @click="submitExport">
          {{ $t('common.confirm') }}
        </el-button>
      </template>
    </el-dialog>

    <!-- 标记保护对话框 -->
    <el-dialog v-model="markDialogVisible" :title="$t('snapshot.markProtected')" width="480px">
      <el-form label-position="top">
        <el-form-item :label="$t('snapshot.protectedPath')" required>
          <el-input v-model="markForm.path" placeholder="/Docs" />
          <div class="form-hint">{{ $t('snapshot.markPathHint') }}</div>
        </el-form-item>
        <el-form-item :label="$t('snapshot.protectedNote')">
          <el-input v-model="markForm.note" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="markDialogVisible = false">{{ $t('common.cancel') }}</el-button>
        <el-button type="primary" :loading="marking" @click="submitMark">
          {{ $t('common.confirm') }}
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.snapshots-page {
  padding: 16px;
}

.toolbar {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
}

.el-pagination {
  margin-top: 16px;
  justify-content: flex-end;
}

.protected-alert {
  margin-bottom: 16px;
}

.form-hint {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.5;
  margin-top: 4px;
}

.diff-picker {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 16px;
}

.diff-arrow {
  flex-shrink: 0;
}

.diff-summary {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
  flex-wrap: wrap;
}

.diff-filter {
  margin-left: auto;
}
</style>

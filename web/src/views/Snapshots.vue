<script setup lang="ts">
/**
 * 清单快照管理页（ROADMAP Phase 2）。
 *
 * 2026-10-19 拆分：只读保护提升为独立菜单页 /protected-directories，
 * 3-2-1 导出提升为独立菜单页 /export。本页只负责快照的
 * 创建/列表/详情/对比/删除。
 */
import { ref, reactive, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Right, FolderOpened, Search } from '@element-plus/icons-vue'
import { snapshotApi } from '@/api/snapshot'
import { formatSize, formatDate } from '@/utils/format'
import { useDrawerSize } from '@/utils/useIsMobile'
import DirectoryBrowserDialog from '@/components/DirectoryBrowserDialog.vue'
import type { Snapshot, SnapshotEntry, SnapshotDiff, SnapshotDiffItem } from '@/types'

const { t } = useI18n()

// ---- 快照列表 ----
const snapshots = ref<Snapshot[]>([])
// 模态统一右侧抽屉尺寸（与 Users/Storage 一致）
const drawerSize = useDrawerSize()
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
// 目录浏览器：点选库内目录（与文件页同一棵树），不再纯手打
const browseVisible = ref(false)

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

// 条目搜索：按相对路径过滤当前页（后端 entries 无搜索参数，
// 客户端过滤对单页 100 条量级足够，与 Shares 页同款思路）
const entrySearch = ref('')
const filteredDetailEntries = computed(() => {
  const q = entrySearch.value.trim().toLowerCase()
  if (!q) return detailEntries.value
  return detailEntries.value.filter(e => e.relativePath.toLowerCase().includes(q))
})

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

// ---- 展示辅助 ----
// el-table 插槽的 row 是框架的 DefaultRow（无字段类型），统一收敛成具体
// DTO 再用，避免每处 TemplateRef 断言散落
const asSnapshot = (row: any): Snapshot => row as Snapshot
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

onMounted(loadSnapshots)
</script>

<template>
  <div class="snapshots-page">
    <div class="page-header">
      <div class="header-left">
        <h1>{{ $t('snapshot.title') }}</h1>
        <p>{{ $t('snapshot.subtitle') }}</p>
      </div>
    </div>

    <div class="toolbar">
      <el-button type="primary" @click="createDialogVisible = true">
        {{ $t('snapshot.create') }}
      </el-button>
      <el-button :disabled="snapshots.length < 2" @click="openDiff">
        {{ $t('snapshot.compare') }}
      </el-button>
      <el-button :loading="snapshotsLoading" @click="loadSnapshots">
        {{ $t('common.refresh') }}
      </el-button>
    </div>

    <el-table :data="snapshots" v-loading="snapshotsLoading">
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
        <template #default="{ row }">{{ formatDate(asSnapshot(row).createdAt) }}</template>
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

    <!-- 创建快照对话框 -->
    <el-drawer v-model="createDialogVisible" :title="$t('snapshot.create')" :size="drawerSize('420px', '38vw', '560px')" :mask-closable="false">
      <el-form label-position="top">
        <el-form-item :label="$t('snapshot.rootPath')" required>
          <el-input v-model="createForm.rootPath" placeholder="/">
            <template #append>
              <el-button :icon="FolderOpened" @click="browseVisible = true">{{ $t('ocr.browseDir') }}</el-button>
            </template>
          </el-input>
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
    </el-drawer>

    <!-- 快照详情抽屉（条目分页 + 当前页路径搜索） -->
    <el-drawer v-model="detailVisible" :title="$t('snapshot.detail')" size="520px">
      <template #header>
        <span>{{ detailSnapshot ? snapshotLabel(detailSnapshot) : '' }}</span>
      </template>
      <el-input
        v-model="entrySearch"
        :placeholder="$t('snapshot.searchEntries')"
        clearable
        size="small"
        style="margin-bottom: 12px"
      >
        <template #prefix><el-icon><Search /></el-icon></template>
      </el-input>
      <el-table :data="filteredDetailEntries" v-loading="detailLoading" size="small">
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
        <template #empty>
          <el-empty :description="entrySearch ? $t('shares.noSearchResults') : $t('snapshot.noEntries')" :image-size="60" />
        </template>
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
    <el-drawer v-model="diffDialogVisible" :title="$t('snapshot.compare')" :size="drawerSize('560px', '60vw', '720px')" :mask-closable="false">
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

        <el-table :data="visibleDiffItems()" size="small" max-height="360">
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
    </el-drawer>

    <!-- 目录浏览器：选中后回填创建快照的路径 -->
    <DirectoryBrowserDialog v-model="browseVisible" @select="path => createForm.rootPath = path" />
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

<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  Refresh, Delete, Link, CopyDocument, Edit, View,
  Share, Clock, Download, Lock, Unlock, MoreFilled,
  Search, List as ListIcon, Grid as GridIcon, Sort, SortUp, SortDown, Plus, Folder, Document, ArrowLeft
} from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { fileShareApi, ShareAccessType, PermissionLevel, type FileShareDto, type UpdateFileShareRequest, type CreateFileShareRequest } from '@/api/fileShare'
import { fileApi } from '@/api/file'
import { getFileIcon, getFileIconColor, formatDate, formatSize } from '@/utils/format'
import type { FileItem } from '@/types'
import { useDrawerSize } from '@/utils/useIsMobile'

const { t } = useI18n()
const loading = ref(false)
const shares = ref<FileShareDto[]>([])
const editDialogVisible = ref(false)
const editTarget = ref<FileShareDto | null>(null)
const editForm = ref<UpdateFileShareRequest>({})
const editExpiresOption = ref('keep')
// 模态统一右侧抽屉：窄屏全宽，宽屏 clamp 随视口连续缩放
const drawerSize = useDrawerSize()
const editMaxDownloadsOption = ref('keep')
const editPassword = ref('')
// 编辑过期时间：'custom' 时用自定义日期选择器（与新建侧同款）
const editCustomExpiresAt = ref<string | null>(null)

// ─── 新建分享（2026-10-21 补齐：管理页可直接建，不必绕文件页）───
const createDialogVisible = ref(false)
const creating = ref(false)
const createResult = ref<FileShareDto | null>(null)
const selectedFile = ref<FileItem | null>(null)
const createForm = ref({
  accessType: ShareAccessType.Public as ShareAccessType,
  password: '',
  permission: PermissionLevel.Read as PermissionLevel,
  expiresOption: 'never',
  customExpiresAt: null as string | null,
  maxDownloadsOption: 'unlimited',
  customMaxDownloads: null as number | null
})

// 文件选择器：浏览库内目录（与文件页同款 browse 接口），
// 双击进文件夹、单击行选中。路径 '/' 为根
const filePickerVisible = ref(false)
const pickerPath = ref('/')
const pickerItems = ref<FileItem[]>([])
const pickerLoading = ref(false)

const loadPickerDirectory = async (path: string) => {
  pickerLoading.value = true
  try {
    const result = await fileApi.getDirectory(path)
    if (result.data) {
      pickerPath.value = result.data.path
      pickerItems.value = result.data.items
    }
  } catch {
    ElMessage.error(t('common.failed'))
  } finally {
    pickerLoading.value = false
  }
}

const openFilePicker = () => {
  filePickerVisible.value = true
  void loadPickerDirectory('/')
}

// 面包屑：'/a/b/c' → ['/', '/a', '/a/b', '/a/b/c']
const pickerBreadcrumbs = computed(() => {
  const segments = pickerPath.value.split('/').filter(Boolean)
  const crumbs: { path: string; label: string }[] = [{ path: '/', label: t('filesView.rootCrumb') }]
  let acc = ''
  for (const seg of segments) {
    acc += '/' + seg
    crumbs.push({ path: acc, label: seg })
  }
  return crumbs
})

const openCreateDialog = () => {
  createDialogVisible.value = true
  createResult.value = null
  selectedFile.value = null
  createForm.value = {
    accessType: ShareAccessType.Public,
    password: '',
    permission: PermissionLevel.Read,
    expiresOption: 'never',
    customExpiresAt: null,
    maxDownloadsOption: 'unlimited',
    customMaxDownloads: null
  }
}

const handleCreateShare = async () => {
  if (!selectedFile.value) {
    ElMessage.warning(t('shares.noFileSelected'))
    return
  }
  creating.value = true
  try {
    const request: CreateFileShareRequest = {
      fileItemId: selectedFile.value.id,
      accessType: createForm.value.accessType,
      permission: createForm.value.permission
    }
    if (createForm.value.accessType === ShareAccessType.Password && createForm.value.password) {
      request.password = createForm.value.password
    }
    if (createForm.value.expiresOption === 'custom') {
      if (createForm.value.customExpiresAt) request.expiresAt = createForm.value.customExpiresAt
    } else if (createForm.value.expiresOption !== 'never') {
      const d = new Date()
      d.setDate(d.getDate() + parseInt(createForm.value.expiresOption))
      request.expiresAt = d.toISOString()
    }
    if (createForm.value.maxDownloadsOption === 'custom') {
      if (createForm.value.customMaxDownloads) request.maxDownloads = createForm.value.customMaxDownloads
    } else if (createForm.value.maxDownloadsOption !== 'unlimited') {
      request.maxDownloads = parseInt(createForm.value.maxDownloadsOption)
    }
    const result = await fileShareApi.create(request)
    if (result.data) {
      createResult.value = result.data
      ElMessage.success(t('shares.createSuccess'))
      await loadData()
    }
  } catch (e: any) {
    ElMessage.error(e.message || t('common.failed'))
  } finally {
    creating.value = false
  }
}

const copyCreateResultLink = async () => {
  if (!createResult.value) return
  try {
    await navigator.clipboard.writeText(createResult.value.shareUrl)
    ElMessage.success(t('shares.linkCopied'))
  } catch {
    ElMessage.error(t('common.failed'))
  }
}

const loadData = async () => {
  loading.value = true
  try {
    const result = await fileShareApi.getMyShares()
    if (result.data) shares.value = result.data
  } catch { ElMessage.error(t('common.failed')) }
  finally { loading.value = false }
}

const getAccessTypeText = (type: ShareAccessType) => {
  const map: Record<number, string> = {
    0: t('shares.public'),
    1: t('shares.hasPassword'),
    2: t('shares.private')
  }
  return map[type] || t('dashboard.unknown')
}

// 访问方式徽章的三态色。2026-10-21 设计审计：原 pastel 亮色底（#ecfdf5 等）
// 在亮色模式下文字对比度就只 2.07-2.41:1（AA 需 4.5:1），暗色模式下淡彩块
// 贴在深灰卡上整块违和。改为语义色 + color-mix 透明度派生：亮暗各自可读，
// 且与全站 danger/success/warning/primary 语义对齐
const getAccessTypeInfo = (type: ShareAccessType) => {
  const map: Record<number, any> = {
    0: { icon: Unlock, color: 'var(--el-color-success)', bg: 'color-mix(in srgb, var(--el-color-success) 12%, transparent)' },
    1: { icon: Lock, color: 'var(--el-color-warning)', bg: 'color-mix(in srgb, var(--el-color-warning) 12%, transparent)' },
    2: { icon: Share, color: 'var(--el-color-primary)', bg: 'color-mix(in srgb, var(--el-color-primary) 12%, transparent)' }
  }
  return map[type] || { icon: Share, color: 'var(--el-text-color-placeholder)', bg: 'var(--el-fill-color-light)' }
}

// 扩展名 → 徽章底色。2026-10-21 设计审计：删除本页 75 行私有扩展名表——
// 其与 format.ts 的 getFileIconColor 同值但 folder/fallback 两处已漂移
//（同一种文件夹在两页显示两种琥珀色），单一事实源收回 format.ts。
// 白色 glyph 是全站图标徽章既定语言（MainLayout logo/Dashboard icon-badge 同款）
const getFileIconStyle = (share: FileShareDto): { bg: string; color: string } => ({
  bg: getFileIconColor(share),
  color: '#fff'
})

// ─── 视图切换 / 搜索 / 排序（2026-10-17 重设计新增）───

type ShareSortField = 'fileName' | 'createdAt' | 'downloadCount' | 'expiresAt' | 'accessType'

// 视图偏好落 localStorage：与文件页的 viewMode 同思路，刷新后保持
const VIEW_MODE_STORAGE_KEY = 'shares-view-mode'
const viewMode = ref<'list' | 'grid'>(
  (localStorage.getItem(VIEW_MODE_STORAGE_KEY) as 'list' | 'grid') || 'grid'
)
watch(viewMode, v => localStorage.setItem(VIEW_MODE_STORAGE_KEY, v))

const searchQuery = ref('')
const sortField = ref<ShareSortField>('createdAt')
const sortDesc = ref(true)

// 搜索：文件名 / 路径 / 链接 命中即留。数据是一次性全量拉回来的
// （后端 /file-shares 无分页），客户端过滤对数十条量级足够，无需往返
const filteredShares = computed(() => {
  const q = searchQuery.value.trim().toLowerCase()
  if (!q) return shares.value
  return shares.value.filter(s =>
    s.fileName.toLowerCase().includes(q) ||
    s.filePath.toLowerCase().includes(q) ||
    s.shareUrl.toLowerCase().includes(q))
})

// 排序：不过期的分享 expiresAt 为 null，恒排末尾——"没有过期时间"不是"最早过期"
const sortedShares = computed(() => {
  const dir = sortDesc.value ? -1 : 1
  return [...filteredShares.value].sort((a, b) => {
    switch (sortField.value) {
      case 'fileName':
        return a.fileName.localeCompare(b.fileName, 'zh') * dir
      case 'downloadCount':
        return (a.downloadCount - b.downloadCount) * dir
      case 'accessType':
        return (a.accessType - b.accessType) * dir
      case 'expiresAt': {
        if (!a.expiresAt && !b.expiresAt) return 0
        if (!a.expiresAt) return 1
        if (!b.expiresAt) return -1
        return (new Date(a.expiresAt).getTime() - new Date(b.expiresAt).getTime()) * dir
      }
      default:
        return (new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()) * dir
    }
  })
})

// 排序按钮文案：字段名 + 方向（图标单独渲染）
const sortLabel = computed(() => {
  const labels: Record<ShareSortField, string> = {
    fileName: t('shares.sortName'),
    createdAt: t('shares.sortCreated'),
    downloadCount: t('shares.sortDownloads'),
    expiresAt: t('shares.sortExpires'),
    accessType: t('shares.sortAccessType')
  }
  return labels[sortField.value]
})

// 同字段再点切换方向；换字段时给该字段一个合理默认方向
// （名称/访问方式升序，时间与计数降序）
const setSort = (field: ShareSortField) => {
  if (sortField.value === field) {
    sortDesc.value = !sortDesc.value
    return
  }
  sortField.value = field
  sortDesc.value = field !== 'fileName' && field !== 'accessType'
}

const copyLink = async (share: FileShareDto) => {
  try {
    await navigator.clipboard.writeText(share.shareUrl)
    ElMessage.success(t('shares.linkCopied'))
  } catch { ElMessage.error(t('common.failed')) }
}

const openEditDialog = (share: FileShareDto) => {
  editTarget.value = share
  editForm.value = { accessType: share.accessType, permission: share.permission, isEnabled: share.isEnabled }
  editExpiresOption.value = 'keep'
  editCustomExpiresAt.value = null
  editMaxDownloadsOption.value = 'keep'
  editPassword.value = ''
  editDialogVisible.value = true
}

// 重新生成分享码：旧码立即失效（已发出的链接打不开），
// 用于链接泄露后止损。确认文案必须点明"旧链接失效"，
// 这是个用户容易低估后果的操作。
// 本文件的 http 封装是裸 AxiosInstance + 响应拦截器（错误转 reject），
// 沿用邻近代码的风格：不检查 result.success，靠 try/catch 收错误
const regenerating = ref(false)
const handleRegenerateCode = async () => {
  if (!editTarget.value) return
  try {
    await ElMessageBox.confirm(
      t('shares.regenerateConfirm'),
      t('shares.regenerateCode'),
      { type: 'warning' }
    )
  } catch {
    return
  }
  regenerating.value = true
  const targetId = editTarget.value.id
  try {
    await fileShareApi.regenerateCode(targetId)
    // 刷新列表后把编辑目标换成同 id 的新记录：抽屉里显示的分享码
    // 必须换成新的，否则用户复制到的还是已失效的旧码
    await loadData()
    const refreshed = shares.value.find(s => s.id === targetId)
    if (refreshed) editTarget.value = refreshed
    ElMessage.success(t('shares.regenerateSuccess'))
  } catch (e: any) {
    ElMessage.error(e.message || t('common.failed'))
  } finally {
    regenerating.value = false
  }
}

const handleSaveEdit = async () => {
  if (!editTarget.value) return
  try {
    const request: UpdateFileShareRequest = {
      accessType: editForm.value.accessType,
      permission: editForm.value.permission,
      isEnabled: editForm.value.isEnabled
    }
    if (editForm.value.accessType === ShareAccessType.Password && editPassword.value) {
      request.password = editPassword.value
    } else if (editForm.value.accessType !== ShareAccessType.Password) {
      request.clearPassword = true
    }
    if (editExpiresOption.value === 'clear') request.clearExpiresAt = true
    else if (editExpiresOption.value === 'custom') {
      if (editCustomExpiresAt.value) request.expiresAt = editCustomExpiresAt.value
    } else if (editExpiresOption.value !== 'keep') {
      const d = new Date()
      d.setDate(d.getDate() + parseInt(editExpiresOption.value.replace('d', '')))
      request.expiresAt = d.toISOString()
    }
    if (editMaxDownloadsOption.value === 'clear') request.clearMaxDownloads = true
    else if (editMaxDownloadsOption.value !== 'keep') {
      request.maxDownloads = parseInt(editMaxDownloadsOption.value)
    }
    await fileShareApi.update(editTarget.value.id, request)
    ElMessage.success(t('common.success'))
    editDialogVisible.value = false
    await loadData()
  } catch (e: any) { ElMessage.error(e.message || t('common.failed')) }
}

const handleToggle = async (share: FileShareDto) => {
  try {
    await fileShareApi.update(share.id, { isEnabled: !share.isEnabled })
    share.isEnabled = !share.isEnabled
    ElMessage.success(share.isEnabled ? t('common.enabled') : t('common.disabled'))
  } catch { ElMessage.error(t('common.failed')) }
}

const handleDelete = async (share: FileShareDto) => {
  try {
    await ElMessageBox.confirm(t('shares.deleteConfirm'), t('common.confirm'), { type: 'warning' })
    await fileShareApi.delete(share.id)
    ElMessage.success(t('shares.deleteSuccess'))
    await loadData()
  } catch {}
}

const previewShare = (share: FileShareDto) => window.open(share.shareUrl, '_blank')

// el-table 插槽的 row 是框架的 DefaultRow（无字段类型），列表视图里统一收敛成
// FileShareDto 再用，避免每处 TemplateRef 断言散落
const asShare = (row: any): FileShareDto => row as FileShareDto

// 统计口径基于全量分享（不受搜索过滤影响）：卡片回答"我总共有多少分享"
const stats = computed(() => ({
  total: shares.value.length,
  active: shares.value.filter(s => s.isEnabled && !s.isExpired && !s.isDownloadLimitReached).length,
  expired: shares.value.filter(s => s.isExpired).length,
  downloads: shares.value.reduce((sum, s) => sum + s.downloadCount, 0)
}))

onMounted(() => loadData())
</script>

<template>
  <div class="shares-page">
    <!-- 页头 -->
    <div class="page-header page-header--with-actions">
      <div class="header-left">
        <h1>{{ t('shares.title') }}</h1>
        <p>{{ t('shares.subtitle') }}</p>
      </div>
      <div class="header-actions">
        <el-button type="primary" :icon="Plus" @click="openCreateDialog">{{ t('shares.createShare') }}</el-button>
        <el-button :icon="Refresh" @click="loadData" :loading="loading">{{ t('common.refresh') }}</el-button>
      </div>
    </div>

    <!-- 统计卡：白底发丝线 + 柔和阴影；语义色（有效绿/过期红/下载蓝）保留，
         总数卡改用全站主色令牌，去掉 indigo chrome 色 -->
    <div class="stats-grid">
      <div class="stat-card">
        <div class="stat-icon total"><el-icon :size="22"><Share /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ stats.total }}</div><div class="stat-label">{{ t('shares.totalShares') }}</div></div>
      </div>
      <div class="stat-card">
        <div class="stat-icon active"><el-icon :size="22"><Link /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ stats.active }}</div><div class="stat-label">{{ t('shares.active') }}</div></div>
      </div>
      <div class="stat-card">
        <div class="stat-icon expired"><el-icon :size="22"><Clock /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ stats.expired }}</div><div class="stat-label">{{ t('shares.expired') }}</div></div>
      </div>
      <div class="stat-card">
        <div class="stat-icon downloads"><el-icon :size="22"><Download /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ stats.downloads }}</div><div class="stat-label">{{ t('shares.downloadCount') }}</div></div>
      </div>
    </div>

    <!-- 工具栏：搜索 + 排序 + 视图切换。Finder 风格毛玻璃条，
         两种视图共用同一套搜索/排序状态 -->
    <div class="toolbar">
      <el-input
        v-model="searchQuery"
        :placeholder="t('shares.searchPlaceholder')"
        clearable
        class="search-input"
      >
        <template #prefix><el-icon><Search /></el-icon></template>
      </el-input>
      <div class="toolbar-right">
        <el-dropdown trigger="click" @command="setSort">
          <el-button text class="toolbar-btn">
            <el-icon><Sort /></el-icon>
            <span class="sort-label">{{ sortLabel }}</span>
            <el-icon><SortUp v-if="sortDesc" /><SortDown v-else /></el-icon>
          </el-button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item
                v-for="opt in [
                  { field: 'fileName' as ShareSortField, key: 'sortName' },
                  { field: 'createdAt' as ShareSortField, key: 'sortCreated' },
                  { field: 'downloadCount' as ShareSortField, key: 'sortDownloads' },
                  { field: 'expiresAt' as ShareSortField, key: 'sortExpires' },
                  { field: 'accessType' as ShareSortField, key: 'sortAccessType' }
                ]"
                :key="opt.field"
                :command="opt.field"
                :class="{ 'is-active': sortField === opt.field }"
              >
                {{ t('shares.' + opt.key) }}
                <el-icon v-if="sortField === opt.field"><SortUp v-if="sortDesc" /><SortDown v-else /></el-icon>
              </el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
        <!-- mac 分段控件：灰底容器 + 激活态白底浮起 -->
        <div class="view-switch" role="tablist">
          <button
            role="tab"
            :aria-selected="viewMode === 'list'"
            :class="{ active: viewMode === 'list' }"
            :title="t('shares.listView')"
            @click="viewMode = 'list'"
          ><el-icon><ListIcon /></el-icon></button>
          <button
            role="tab"
            :aria-selected="viewMode === 'grid'"
            :class="{ active: viewMode === 'grid' }"
            :title="t('shares.gridView')"
            @click="viewMode = 'grid'"
          ><el-icon><GridIcon /></el-icon></button>
        </div>
      </div>
    </div>

    <!-- 列表视图 -->
    <div v-if="viewMode === 'list'" v-loading="loading" class="list-view mac-card">
      <el-table :data="sortedShares" style="width: 100%">
        <el-table-column :label="t('filesView.colName')" min-width="240">
          <template #default="{ row }">
            <div class="cell-file">
              <div class="file-icon" :style="{ background: `linear-gradient(135deg, ${getFileIconStyle(asShare(row)).bg}, color-mix(in srgb, ${getFileIconStyle(asShare(row)).bg} 62%, #fff))` }">
                <el-icon :size="18" :color="getFileIconStyle(asShare(row)).color"><component :is="getFileIcon(asShare(row))" /></el-icon>
              </div>
              <div class="file-info">
                <div class="file-name" :title="asShare(row).fileName">{{ asShare(row).fileName }}</div>
                <div class="file-path" :title="asShare(row).filePath">{{ asShare(row).filePath }}</div>
              </div>
            </div>
          </template>
        </el-table-column>
        <el-table-column :label="t('shares.accessType')" width="120">
          <template #default="{ row }">
            <span class="pill" :style="{ background: getAccessTypeInfo(asShare(row).accessType).bg, color: getAccessTypeInfo(asShare(row).accessType).color }">
              <el-icon :size="12"><component :is="getAccessTypeInfo(asShare(row).accessType).icon" /></el-icon>
              {{ getAccessTypeText(asShare(row).accessType) }}
            </span>
          </template>
        </el-table-column>
        <el-table-column :label="t('shares.downloadCount')" width="130">
          <template #default="{ row }">
            <span class="cell-muted">{{ asShare(row).downloadCount }}{{ asShare(row).maxDownloads ? '/' + asShare(row).maxDownloads : '' }} {{ t('shares.times') }}</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('shares.expiresAt')" width="160">
          <template #default="{ row }">
            <span class="cell-muted">{{ asShare(row).expiresAt ? formatDate(asShare(row).expiresAt) : t('shares.neverExpire') }}</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('common.status')" width="90">
          <template #default="{ row }">
            <el-switch :model-value="asShare(row).isEnabled" @change="handleToggle(asShare(row))" size="small" />
          </template>
        </el-table-column>
        <el-table-column :label="t('filesView.colActions')" width="80">
          <template #default="{ row }">
            <el-dropdown trigger="click">
              <el-button text circle><el-icon><MoreFilled /></el-icon></el-button>
              <template #dropdown>
                <el-dropdown-menu>
                  <el-dropdown-item @click="previewShare(asShare(row))"><el-icon><View /></el-icon>{{ t('shares.preview') }}</el-dropdown-item>
                  <el-dropdown-item @click="copyLink(asShare(row))"><el-icon><CopyDocument /></el-icon>{{ t('shares.copyLink') }}</el-dropdown-item>
                  <el-dropdown-item @click="openEditDialog(asShare(row))"><el-icon><Edit /></el-icon>{{ t('common.edit') }}</el-dropdown-item>
                  <el-dropdown-item divided @click="handleDelete(asShare(row))"><el-icon color="var(--el-color-danger)"><Delete /></el-icon><span style="color: var(--el-color-danger)">{{ t('common.delete') }}</span></el-dropdown-item>
                </el-dropdown-menu>
              </template>
            </el-dropdown>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty :description="searchQuery ? t('shares.noSearchResults') : t('shares.noShares')" :image-size="80" />
        </template>
      </el-table>
    </div>

    <!-- 方格视图 -->
    <div v-else class="grid-view" v-loading="loading">
      <div v-for="share in sortedShares" :key="share.id" class="share-card mac-card" :class="{ disabled: !share.isEnabled }">
        <div class="card-top">
          <div class="file-icon" :style="{ background: `linear-gradient(135deg, ${getFileIconStyle(share).bg}, color-mix(in srgb, ${getFileIconStyle(share).bg} 62%, #fff))` }">
            <el-icon :size="22" :color="getFileIconStyle(share).color"><component :is="getFileIcon(share)" /></el-icon>
          </div>
          <div class="file-info">
            <div class="file-name" :title="share.fileName">{{ share.fileName }}</div>
            <div class="file-path" :title="share.filePath">{{ share.filePath }}</div>
          </div>
          <el-dropdown trigger="click">
            <el-button text circle><el-icon><MoreFilled /></el-icon></el-button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item @click="previewShare(share)"><el-icon><View /></el-icon>{{ t('shares.preview') }}</el-dropdown-item>
                <el-dropdown-item @click="copyLink(share)"><el-icon><CopyDocument /></el-icon>{{ t('shares.copyLink') }}</el-dropdown-item>
                <el-dropdown-item @click="openEditDialog(share)"><el-icon><Edit /></el-icon>{{ t('common.edit') }}</el-dropdown-item>
                <el-dropdown-item divided @click="handleDelete(share)"><el-icon color="var(--el-color-danger)"><Delete /></el-icon><span style="color: var(--el-color-danger)">{{ t('common.delete') }}</span></el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>

        <div class="card-badges">
          <span class="pill" :style="{ background: getAccessTypeInfo(share.accessType).bg, color: getAccessTypeInfo(share.accessType).color }">
            <el-icon :size="12"><component :is="getAccessTypeInfo(share.accessType).icon" /></el-icon>
            {{ getAccessTypeText(share.accessType) }}
          </span>
          <span v-if="share.isFolder" class="pill folder">{{ t('files.folder') }}</span>
          <span v-if="share.isExpired" class="pill expired">{{ t('shares.expired') }}</span>
          <span v-else-if="share.isDownloadLimitReached" class="pill limit">{{ t('shares.limitReached') }}</span>
          <span v-else-if="!share.isEnabled" class="pill off">{{ t('common.disabled') }}</span>
        </div>

        <div class="card-stats">
          <div class="stat-item">
            <el-icon><Download /></el-icon>
            <span>{{ share.downloadCount }}{{ share.maxDownloads ? '/' + share.maxDownloads : '' }} {{ t('shares.times') }}</span>
          </div>
          <div class="stat-item">
            <el-icon><Clock /></el-icon>
            <span>{{ share.expiresAt ? formatDate(share.expiresAt) : t('shares.neverExpire') }}</span>
          </div>
        </div>

        <div class="card-footer">
          <el-input :model-value="share.shareUrl" readonly size="small" class="link-input">
            <template #suffix>
              <el-button text size="small" @click="copyLink(share)"><el-icon><CopyDocument /></el-icon></el-button>
            </template>
          </el-input>
          <el-switch :model-value="share.isEnabled" @change="handleToggle(share)" size="small" />
        </div>
      </div>

      <!-- 空态：区分"没有分享"与"搜索无结果"两种处境 -->
      <div v-if="sortedShares.length === 0 && !loading" class="empty-state mac-card">
        <div class="empty-icon"><el-icon :size="56"><Share /></el-icon></div>
        <h3>{{ searchQuery ? t('shares.noSearchResults') : t('shares.noShares') }}</h3>
        <p>{{ searchQuery ? t('shares.noSearchResultsHint') : t('shares.noSharesHint') }}</p>
        <el-button v-if="searchQuery" @click="searchQuery = ''">{{ t('shares.clearSearch') }}</el-button>
      </div>
    </div>

    <!-- 编辑分享抽屉 -->
    <el-drawer v-model="editDialogVisible" :title="t('shares.editShare')" :size="drawerSize('420px', '38vw', '560px')" :mask-closable="false">
      <el-form v-if="editTarget" label-position="top">
        <el-form-item :label="t('shares.accessType')">
          <el-radio-group v-model="editForm.accessType">
            <el-radio :value="0">{{ t('shares.public') }}</el-radio>
            <el-radio :value="1">{{ t('shares.hasPassword') }}</el-radio>
            <el-radio :value="2">{{ t('shares.internalOnly') }}</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item :label="t('shares.newPassword')" v-if="editForm.accessType === 1">
          <el-input v-model="editPassword" :placeholder="t('shares.keepPassword')" show-password />
        </el-form-item>
        <el-form-item :label="t('filesView.permissionLabel')">
          <el-radio-group v-model="editForm.permission">
            <el-radio :value="PermissionLevel.Read">{{ $t('filesView.readOnly') }}</el-radio>
            <el-radio :value="PermissionLevel.ReadWrite">{{ $t('filesView.readWrite') }}</el-radio>
            <el-radio :value="PermissionLevel.FullControl">{{ $t('filesView.fullControl') }}</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item :label="t('shares.expiresAt')">
          <el-radio-group v-model="editExpiresOption">
            <el-radio value="keep">{{ t('shares.keepCurrent') }}</el-radio>
            <el-radio value="clear">{{ t('shares.neverExpire') }}</el-radio>
            <el-radio value="7d">7{{ t('shares.days') }}</el-radio>
            <el-radio value="30d">30{{ t('shares.days') }}</el-radio>
            <el-radio value="custom">{{ $t('filesView.custom') }}</el-radio>
          </el-radio-group>
          <el-date-picker
            v-if="editExpiresOption === 'custom'"
            v-model="editCustomExpiresAt"
            type="datetime"
            :placeholder="t('shares.expiresAt')"
            style="margin-top: 8px; width: 100%"
          />
        </el-form-item>
        <el-form-item :label="t('shares.downloadLimit')">
          <el-radio-group v-model="editMaxDownloadsOption">
            <el-radio value="keep">{{ t('shares.keepCurrent') }}</el-radio>
            <el-radio value="clear">{{ t('common.unlimited') }}</el-radio>
            <el-radio value="10">10{{ t('shares.times') }}</el-radio>
            <el-radio value="100">100{{ t('shares.times') }}</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item :label="t('common.status')">
          <el-switch v-model="editForm.isEnabled" :active-text="t('common.enabled')" :inactive-text="t('common.disabled')" />
        </el-form-item>
        <!-- 重新生成分享码：旧链接立即失效，用于泄露后止损 -->
        <el-form-item :label="t('shares.shareCode')">
          <div class="regenerate-row">
            <el-tag class="code-tag">{{ editTarget.shareCode }}</el-tag>
            <el-button size="small" :loading="regenerating" @click="handleRegenerateCode">
              {{ t('shares.regenerateCode') }}
            </el-button>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialogVisible = false">{{ t('common.cancel') }}</el-button>
        <el-button type="primary" @click="handleSaveEdit">{{ t('common.save') }}</el-button>
      </template>
    </el-drawer>

    <!-- 新建分享抽屉：文件选择 + 全部字段（与文件页创建侧同款字段集） -->
    <el-drawer v-model="createDialogVisible" :title="t('shares.createShare')" :size="drawerSize('420px', '38vw', '560px')" :mask-closable="false">
      <!-- 创建成功：展示链接与配置摘要，可复制 -->
      <div v-if="createResult" class="create-result">
        <el-alert type="success" :closable="false" show-icon>
          <template #title>{{ $t('filesView.linkGeneratedTitle') }}</template>
        </el-alert>
        <div class="share-link-box">
          <el-input :model-value="createResult.shareUrl" readonly>
            <template #append>
              <el-button @click="copyCreateResultLink">{{ $t('filesView.copyTip') }}</el-button>
            </template>
          </el-input>
        </div>
        <div class="share-info-list">
          <div class="share-info-item">
            <span class="label">{{ $t('filesView.accessTypeLabel') }}</span>
            <span>{{ createResult.accessType === 0 ? $t('filesView.accessPublicCol') : createResult.accessType === 1 ? $t('filesView.accessPasswordCol') : $t('filesView.accessInternalCol') }}</span>
          </div>
          <div class="share-info-item" v-if="createResult.accessType === 1 && createForm.password">
            <span class="label">{{ $t('filesView.passwordLabel') }}</span>
            <span class="password">{{ createForm.password }}</span>
          </div>
          <div class="share-info-item">
            <span class="label">{{ $t('filesView.permissionLabel') }}</span>
            <span>{{ createResult.permission === 1 ? $t('filesView.permReadOnlyCol') : createResult.permission === 2 ? $t('filesView.permReadWriteCol') : $t('filesView.permFullCol') }}</span>
          </div>
          <div class="share-info-item" v-if="createResult.expiresAt">
            <span class="label">{{ $t('filesView.expiresUntilLabel') }}</span>
            <span>{{ formatDate(createResult.expiresAt) }}</span>
          </div>
          <div class="share-info-item" v-if="createResult.maxDownloads">
            <span class="label">{{ $t('filesView.downloadLimitInfoLabel') }}</span>
            <span>{{ $t('filesView.timesSuffix', { count: createResult.maxDownloads }) }}</span>
          </div>
        </div>
      </div>

      <!-- 创建表单 -->
      <el-form v-else label-position="top">
        <el-form-item :label="t('shares.selectFile')" required>
          <div v-if="selectedFile" class="selected-file">
            <el-icon :size="18" :color="getFileIconColor(selectedFile)"><component :is="getFileIcon(selectedFile)" /></el-icon>
            <div class="selected-file-info">
              <div class="selected-file-name">{{ selectedFile.name }}</div>
              <div class="selected-file-path">{{ selectedFile.path }}</div>
            </div>
            <el-button size="small" @click="openFilePicker">{{ t('shares.changeFile') }}</el-button>
          </div>
          <el-button v-else :icon="Folder" @click="openFilePicker">{{ t('shares.selectFile') }}</el-button>
        </el-form-item>

        <el-form-item :label="t('shares.accessType')">
          <el-radio-group v-model="createForm.accessType">
            <el-radio :value="0">{{ t('shares.public') }}</el-radio>
            <el-radio :value="1">{{ t('shares.hasPassword') }}</el-radio>
            <el-radio :value="2">{{ t('shares.internalOnly') }}</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item :label="t('shares.newPassword')" v-if="createForm.accessType === 1">
          <el-input v-model="createForm.password" show-password :placeholder="t('filesView.passwordSetPlaceholder')" />
        </el-form-item>
        <el-form-item :label="t('filesView.permissionLabel')">
          <el-radio-group v-model="createForm.permission">
            <el-radio :value="PermissionLevel.Read">{{ $t('filesView.readOnly') }}</el-radio>
            <el-radio :value="PermissionLevel.ReadWrite">{{ $t('filesView.readWrite') }}</el-radio>
            <el-radio :value="PermissionLevel.FullControl">{{ $t('filesView.fullControl') }}</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item :label="t('shares.expiresAt')">
          <el-radio-group v-model="createForm.expiresOption">
            <el-radio value="never">{{ $t('filesView.neverExpire') }}</el-radio>
            <el-radio value="1d">{{ $t('filesView.oneDay') }}</el-radio>
            <el-radio value="7d">{{ $t('filesView.sevenDays') }}</el-radio>
            <el-radio value="30d">{{ $t('filesView.thirtyDays') }}</el-radio>
            <el-radio value="custom">{{ $t('filesView.custom') }}</el-radio>
          </el-radio-group>
          <el-date-picker
            v-if="createForm.expiresOption === 'custom'"
            v-model="createForm.customExpiresAt"
            type="datetime"
            :placeholder="t('shares.expiresAt')"
            style="margin-top: 8px; width: 100%"
          />
        </el-form-item>
        <el-form-item :label="t('shares.downloadLimit')">
          <el-radio-group v-model="createForm.maxDownloadsOption">
            <el-radio value="unlimited">{{ $t('filesView.unlimited') }}</el-radio>
            <el-radio value="1">{{ $t('filesView.once') }}</el-radio>
            <el-radio value="10">{{ $t('filesView.times10') }}</el-radio>
            <el-radio value="100">{{ $t('filesView.times100') }}</el-radio>
            <el-radio value="custom">{{ $t('filesView.custom') }}</el-radio>
          </el-radio-group>
          <el-input-number
            v-if="createForm.maxDownloadsOption === 'custom'"
            v-model="createForm.customMaxDownloads"
            :min="1"
            :max="10000"
            style="margin-top: 8px; width: 100%"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">{{ createResult ? $t('filesView.closeText') : t('common.cancel') }}</el-button>
        <el-button v-if="!createResult" type="primary" :loading="creating" @click="handleCreateShare">
          {{ $t('filesView.createShareBtn') }}
        </el-button>
        <el-button v-else type="primary" @click="copyCreateResultLink">
          <el-icon><Link /></el-icon>
          {{ $t('filesView.copyLinkBtn') }}
        </el-button>
      </template>
    </el-drawer>

    <!-- 文件选择器：浏览库内目录，双击进文件夹、单击行选中 -->
    <el-dialog v-model="filePickerVisible" :title="t('shares.filePickerTitle')" width="560px" align-center>
      <div class="picker-breadcrumbs">
        <el-button
          v-if="pickerPath !== '/'"
          size="small"
          :icon="ArrowLeft"
          @click="loadPickerDirectory(pickerPath.split('/').slice(0, -1).join('/') || '/')"
        >
          {{ t('filesView.goBack') }}
        </el-button>
        <el-breadcrumb separator="/">
          <el-breadcrumb-item v-for="crumb in pickerBreadcrumbs" :key="crumb.path">
            <el-button link type="primary" @click="loadPickerDirectory(crumb.path)">{{ crumb.label }}</el-button>
          </el-breadcrumb-item>
        </el-breadcrumb>
      </div>
      <div v-loading="pickerLoading" class="picker-list">
        <div
          v-for="item in pickerItems"
          :key="item.id"
          class="picker-row"
          :class="{ 'is-selected': selectedFile?.id === item.id }"
          @click="selectedFile = item"
          @dblclick="item.isFolder && loadPickerDirectory(item.path)"
        >
          <el-icon :size="18" :color="getFileIconColor(item)">
            <component :is="item.isFolder ? Folder : Document" />
          </el-icon>
          <span class="picker-name">{{ item.name }}</span>
          <span v-if="!item.isFolder" class="picker-size">{{ formatSize(item.sizeBytes) }}</span>
        </div>
        <el-empty v-if="pickerItems.length === 0" :description="t('shares.pickerEmpty')" :image-size="60" />
      </div>
      <template #footer>
        <el-button @click="filePickerVisible = false">{{ t('common.cancel') }}</el-button>
        <el-button
          type="primary"
          :disabled="!selectedFile"
          @click="filePickerVisible = false"
        >
          {{ t('common.confirm') }}
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style lang="scss" scoped>
.shares-page { padding: 0; }

// 页头操作区：新建 + 刷新并排
.header-actions { display: flex; align-items: center; gap: 8px; }

// 新建分享：已选文件行
.selected-file {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  border: 1px solid var(--mac-hairline);
  border-radius: var(--mac-radius-control);
  background: var(--el-fill-color-lighter);

  .selected-file-info { flex: 1; min-width: 0;
    .selected-file-name { font-weight: 500; font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .selected-file-path { font-size: 12px; color: var(--el-text-color-secondary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  }
}

// 新建成功结果区：链接框 + 配置摘要（与文件页创建侧同款布局）
.create-result { display: flex; flex-direction: column; gap: 16px; }

.share-link-box { margin-top: 12px; }

.share-info-list { display: flex; flex-direction: column; gap: 8px; margin-top: 12px; }
.share-info-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 13px;

  .label { color: var(--el-text-color-secondary); }
  .password { font-family: monospace; }
}

// 文件选择器
.picker-breadcrumbs {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
  min-height: 32px;
}

.picker-list {
  max-height: 420px;
  overflow-y: auto;

  .picker-row {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 8px 12px;
    border-radius: var(--mac-radius-control);
    cursor: pointer;

    &:hover { background: var(--el-fill-color-light); }
    &.is-selected { background: color-mix(in srgb, var(--el-color-primary) 12%, transparent); }

    .picker-name { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 13px; }
    .picker-size { font-size: 12px; color: var(--el-text-color-secondary); }
  }
}


// mac 卡片基类：白底 + 发丝线 + 分层柔和阴影（令牌见 main.scss）
.mac-card {
  background: var(--el-bg-color);
  border: 1px solid var(--mac-hairline);
  border-radius: var(--mac-radius-card);
  box-shadow: var(--card-shadow);
}

.stats-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; margin-bottom: 16px; }
// 统计卡与 mac 卡片同基底：不共用 class 是因为 @extend 在 scoped 下会产出
// 带 data-v 的组合选择器，行为不稳定；属性重复一遍更直白
.stat-card { background: var(--el-bg-color); border: 1px solid var(--mac-hairline); border-radius: var(--mac-radius-card); box-shadow: var(--card-shadow); padding: 16px 20px; display: flex; align-items: center; gap: 14px;
  .stat-icon { width: 48px; height: 48px; border-radius: var(--mac-radius-card); display: flex; align-items: center; justify-content: center; color: white; flex-shrink: 0;
    // 总数卡跟随全站主色（原 indigo chrome 色），其余为语义色
    &.total { background: linear-gradient(135deg, var(--el-color-primary), var(--el-color-primary-light-3)); }
    &.active { background: linear-gradient(135deg, var(--el-color-success), var(--el-color-success-light-3)); }
    &.expired { background: linear-gradient(135deg, var(--el-color-danger), var(--el-color-danger-light-3)); }
    &.downloads { background: linear-gradient(135deg, var(--el-color-primary), var(--el-color-primary-light-3)); }
  }
  .stat-info { .stat-value { font-weight: 700; letter-spacing: -0.3px; } .stat-label { font-size: 13px; color: var(--el-text-color-secondary); } }
}

// 工具栏：Finder 风格毛玻璃条，搜索居左、排序与视图切换居右
.toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
  padding: 10px 12px;
  background: var(--mac-vibrancy-bg);
  backdrop-filter: var(--mac-vibrancy-blur);
  -webkit-backdrop-filter: var(--mac-vibrancy-blur);
  border: 1px solid var(--mac-hairline);
  border-radius: var(--mac-radius-card);

  .search-input { max-width: 360px; flex: 1; }

  .toolbar-right { display: flex; align-items: center; gap: 8px; margin-left: auto; }

  .toolbar-btn { display: inline-flex; align-items: center; gap: 6px; color: var(--el-text-color-regular);
    .sort-label { font-size: 13px; }
  }

  // mac 分段控件：灰底容器，激活项白底浮起带阴影
  .view-switch {
    display: inline-flex;
    padding: 2px;
    gap: 2px;
    background: var(--el-fill-color);
    border-radius: var(--mac-radius-control);

    button {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 32px;
      height: 28px;
      border: none;
      border-radius: var(--mac-radius-control);
      background: transparent;
      color: var(--el-text-color-secondary);
      cursor: pointer;
      transition: all 0.15s ease;

      &:hover { color: var(--el-text-color-primary); }
      &.active {
        background: var(--el-bg-color);
        color: var(--el-color-primary);
        border-color: var(--mac-hairline);
      }
    }
  }
}

// 列表视图：表格外壳。列头与行悬停的 mac 化由全局 el-table 规则负责
.list-view { overflow: hidden; }

.cell-file { display: flex; align-items: center; gap: 10px; min-width: 0;
  .file-icon { width: 34px; height: 34px; border-radius: var(--mac-radius-control); display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
  .file-info { min-width: 0;
    .file-name { font-weight: 500; font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .file-path { font-size: 12px; color: var(--el-text-color-secondary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  }
}
.cell-muted { font-size: 13px; color: var(--el-text-color-secondary); }

// 徽章/pill：访问方式与状态共用，淡色底 + 同色系文字（mac tinted pill 观感）
.pill {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 3px 9px;
  border-radius: var(--mac-radius-pill);
  font-size: 12px;
  font-weight: 500;
  line-height: 1.4;

  &.folder { background: color-mix(in srgb, var(--el-color-warning) 12%, transparent); color: var(--el-color-warning); }
  &.expired { background: color-mix(in srgb, var(--el-color-danger) 12%, transparent); color: var(--el-color-danger); }
  &.limit { background: color-mix(in srgb, var(--el-color-warning) 12%, transparent); color: var(--el-color-warning); }
  &.off { background: var(--el-fill-color); color: var(--el-text-color-secondary); }
}

// 方格视图
.grid-view { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 16px; }
.share-card { padding: 16px; display: flex; flex-direction: column; gap: 12px; transition: box-shadow var(--mac-duration-fast) ease, transform var(--mac-duration-fast) ease;
  &:hover { box-shadow: var(--card-shadow-hover); }
  &.disabled { opacity: 0.6; }

  .card-top { display: flex; align-items: center; gap: 12px; min-width: 0;
    .file-icon { width: 42px; height: 42px; border-radius: var(--mac-radius-control); display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .file-info { flex: 1; min-width: 0;
      .file-name { font-weight: 600; font-size: 14px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .file-path { font-size: 12px; color: var(--el-text-color-secondary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    }
  }

  .card-badges { display: flex; gap: 6px; flex-wrap: wrap; }

  .card-stats { display: flex; gap: 16px;
    .stat-item { display: flex; align-items: center; gap: 5px; font-size: 12px; color: var(--el-text-color-secondary); }
  }

  .card-footer { display: flex; align-items: center; gap: 10px; padding-top: 12px; border-top: 1px solid var(--mac-hairline);
    .link-input { flex: 1; :deep(.el-input__wrapper) { background: var(--el-fill-color-lighter); box-shadow: none; } }
  }
}

// 空态：与全站其他页一致，虚线/淡底容器（这里用淡底卡片）
.empty-state { grid-column: 1 / -1; text-align: center; padding: 56px 20px;
  .empty-icon { color: var(--el-text-color-placeholder); margin-bottom: 4px; }
  h3 { margin: 8px 0 6px; font-size: 16px; font-weight: 600; }
  p { margin: 0 0 16px; color: var(--el-text-color-secondary); font-size: 13px; }
}

@media (max-width: 768px) {
  .stats-grid { grid-template-columns: repeat(2, 1fr); }
  .toolbar { flex-wrap: wrap;
    .search-input { max-width: none; }
    .toolbar-right { width: 100%; justify-content: space-between; }
  }
  .grid-view { grid-template-columns: 1fr; }
}
</style>

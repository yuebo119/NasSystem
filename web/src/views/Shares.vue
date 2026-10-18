<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  Refresh, Delete, Link, CopyDocument, Edit, View,
  Share, Clock, Download, Lock, Unlock, MoreFilled,
  Search, List as ListIcon, Grid as GridIcon, Sort, SortUp, SortDown
} from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { fileShareApi, ShareAccessType, type FileShareDto, type UpdateFileShareRequest } from '@/api/fileShare'
import { getFileIcon, formatDate } from '@/utils/format'
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

const getAccessTypeInfo = (type: ShareAccessType) => {
  const map: Record<number, any> = {
    0: { icon: Unlock, color: '#10b981', bg: '#ecfdf5' },
    1: { icon: Lock, color: '#f59e0b', bg: '#fffbeb' },
    2: { icon: Share, color: '#6366f1', bg: '#eef2ff' }
  }
  return map[type] || { icon: Share, color: '#6b7280', bg: '#f3f4f6' }
}

// 扩展名 → 徽章底色。原实现是 11 个分支的数组 includes 顺序判断，每渲染一行就要
// 把整条链走一遍（平均 6 次数组查找）。改成查表后一次哈希命中。
// 表在模块级构建一次，渲染期零分配。
// mac 化（2026-10-17）：渐变改纯色——macOS 视觉是扁平色块加白色 glyph，
// 与全站令牌体系统一；颜色分组本身是文件类型语义，保留不动
const ICON_STYLE_FALLBACK = { bg: '#6b7280', color: '#fff' }

const ICON_STYLES_BY_EXT: Record<string, { bg: string; color: string }> = {
  '.pdf': { bg: '#ef4444', color: '#fff' },
  '.doc': { bg: '#3b82f6', color: '#fff' },
  '.docx': { bg: '#3b82f6', color: '#fff' },
  '.xls': { bg: '#10b981', color: '#fff' },
  '.xlsx': { bg: '#10b981', color: '#fff' },
  '.csv': { bg: '#10b981', color: '#fff' },
  '.ppt': { bg: '#f97316', color: '#fff' },
  '.pptx': { bg: '#f97316', color: '#fff' },
  '.txt': { bg: '#6b7280', color: '#fff' },
  '.md': { bg: '#6b7280', color: '#fff' },
  '.rtf': { bg: '#6b7280', color: '#fff' },
  '.jpg': { bg: '#14b8a6', color: '#fff' },
  '.jpeg': { bg: '#14b8a6', color: '#fff' },
  '.png': { bg: '#14b8a6', color: '#fff' },
  '.gif': { bg: '#14b8a6', color: '#fff' },
  '.webp': { bg: '#14b8a6', color: '#fff' },
  '.bmp': { bg: '#14b8a6', color: '#fff' },
  '.svg': { bg: '#14b8a6', color: '#fff' },
  '.ico': { bg: '#14b8a6', color: '#fff' },
  '.mp4': { bg: '#8b5cf6', color: '#fff' },
  '.avi': { bg: '#8b5cf6', color: '#fff' },
  '.mkv': { bg: '#8b5cf6', color: '#fff' },
  '.mov': { bg: '#8b5cf6', color: '#fff' },
  '.wmv': { bg: '#8b5cf6', color: '#fff' },
  '.flv': { bg: '#8b5cf6', color: '#fff' },
  '.webm': { bg: '#8b5cf6', color: '#fff' },
  '.m4v': { bg: '#8b5cf6', color: '#fff' },
  '.mp3': { bg: '#ec4899', color: '#fff' },
  '.wav': { bg: '#ec4899', color: '#fff' },
  '.flac': { bg: '#ec4899', color: '#fff' },
  '.aac': { bg: '#ec4899', color: '#fff' },
  '.ogg': { bg: '#ec4899', color: '#fff' },
  '.wma': { bg: '#ec4899', color: '#fff' },
  '.m4a': { bg: '#ec4899', color: '#fff' },
  '.zip': { bg: '#a16207', color: '#fff' },
  '.rar': { bg: '#a16207', color: '#fff' },
  '.7z': { bg: '#a16207', color: '#fff' },
  '.tar': { bg: '#a16207', color: '#fff' },
  '.gz': { bg: '#a16207', color: '#fff' },
  '.bz2': { bg: '#a16207', color: '#fff' },
  '.js': { bg: '#6366f1', color: '#fff' },
  '.ts': { bg: '#6366f1', color: '#fff' },
  '.jsx': { bg: '#6366f1', color: '#fff' },
  '.tsx': { bg: '#6366f1', color: '#fff' },
  '.vue': { bg: '#6366f1', color: '#fff' },
  '.py': { bg: '#6366f1', color: '#fff' },
  '.java': { bg: '#6366f1', color: '#fff' },
  '.c': { bg: '#6366f1', color: '#fff' },
  '.cpp': { bg: '#6366f1', color: '#fff' },
  '.h': { bg: '#6366f1', color: '#fff' },
  '.cs': { bg: '#6366f1', color: '#fff' },
  '.go': { bg: '#6366f1', color: '#fff' },
  '.rs': { bg: '#6366f1', color: '#fff' },
  '.php': { bg: '#6366f1', color: '#fff' },
  '.rb': { bg: '#6366f1', color: '#fff' },
  '.swift': { bg: '#6366f1', color: '#fff' },
  '.kt': { bg: '#6366f1', color: '#fff' },
  '.json': { bg: '#475569', color: '#fff' },
  '.xml': { bg: '#475569', color: '#fff' },
  '.yaml': { bg: '#475569', color: '#fff' },
  '.yml': { bg: '#475569', color: '#fff' },
  '.ini': { bg: '#475569', color: '#fff' },
  '.conf': { bg: '#475569', color: '#fff' },
  '.env': { bg: '#475569', color: '#fff' }
}

const FOLDER_ICON_STYLE = { bg: '#f59e0b', color: '#fff' }

// 文件图标样式（纯色徽章， Shares 页专有视觉，保留私有实现）
const getFileIconStyle = (share: FileShareDto) => {
  if (share.isFolder) return FOLDER_ICON_STYLE
  const ext = share.fileName.substring(share.fileName.lastIndexOf('.')).toLowerCase()
  return ICON_STYLES_BY_EXT[ext] ?? ICON_STYLE_FALLBACK
}

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
    else if (editExpiresOption.value !== 'keep') {
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
    <div class="page-header">
      <div class="header-left">
        <h1>{{ t('shares.title') }}</h1>
        <p>{{ t('shares.subtitle') }}</p>
      </div>
      <el-button :icon="Refresh" @click="loadData" :loading="loading">{{ t('common.refresh') }}</el-button>
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
                  <el-dropdown-item divided @click="handleDelete(asShare(row))"><el-icon color="#ef4444"><Delete /></el-icon><span style="color:#ef4444">{{ t('common.delete') }}</span></el-dropdown-item>
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
                <el-dropdown-item divided @click="handleDelete(share)"><el-icon color="#ef4444"><Delete /></el-icon><span style="color:#ef4444">{{ t('common.delete') }}</span></el-dropdown-item>
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
        <el-form-item :label="t('shares.expiresAt')">
          <el-radio-group v-model="editExpiresOption">
            <el-radio value="keep">{{ t('shares.keepCurrent') }}</el-radio>
            <el-radio value="clear">{{ t('shares.neverExpire') }}</el-radio>
            <el-radio value="7d">7{{ t('shares.days') }}</el-radio>
            <el-radio value="30d">30{{ t('shares.days') }}</el-radio>
          </el-radio-group>
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
  </div>
</template>

<style lang="scss" scoped>
.shares-page { padding: 0; }
.page-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 24px;
  .header-left { h1 { margin: 0 0 4px; font-size: 24px; font-weight: 600; letter-spacing: -0.01em; } p { margin: 0; color: var(--el-text-color-secondary); font-size: 14px; } }
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
  .stat-icon { width: 48px; height: 48px; border-radius: 12px; display: flex; align-items: center; justify-content: center; color: white; flex-shrink: 0;
    // 总数卡跟随全站主色（原 indigo chrome 色），其余为语义色
    &.total { background: linear-gradient(135deg, var(--el-color-primary), var(--el-color-primary-light-3)); }
    &.active { background: linear-gradient(135deg, #10b981, #34d399); }
    &.expired { background: linear-gradient(135deg, #ef4444, #f87171); }
    &.downloads { background: linear-gradient(135deg, #3b82f6, #60a5fa); }
  }
  .stat-info { .stat-value { font-size: 22px; font-weight: 600; letter-spacing: -0.01em; } .stat-label { font-size: 13px; color: var(--el-text-color-secondary); } }
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
    border-radius: 9px;

    button {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 32px;
      height: 28px;
      border: none;
      border-radius: 7px;
      background: transparent;
      color: var(--el-text-color-secondary);
      cursor: pointer;
      transition: all 0.15s ease;

      &:hover { color: var(--el-text-color-primary); }
      &.active {
        background: var(--el-bg-color);
        color: var(--el-color-primary);
        box-shadow: 0 1px 3px rgba(0, 0, 0, 0.12);
      }
    }
  }
}

// 列表视图：表格外壳。列头与行悬停的 mac 化由全局 el-table 规则负责
.list-view { overflow: hidden; }

.cell-file { display: flex; align-items: center; gap: 10px; min-width: 0;
  .file-icon { width: 34px; height: 34px; border-radius: 8px; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
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
  border-radius: 6px;
  font-size: 12px;
  font-weight: 500;
  line-height: 1.4;

  &.folder { background: rgba(217, 119, 6, 0.12); color: #d97706; }
  &.expired { background: rgba(239, 68, 68, 0.12); color: #ef4444; }
  &.limit { background: rgba(245, 158, 11, 0.12); color: #f59e0b; }
  &.off { background: var(--el-fill-color); color: var(--el-text-color-secondary); }
}

// 方格视图
.grid-view { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 16px; }
.share-card { padding: 16px; display: flex; flex-direction: column; gap: 12px; transition: box-shadow 0.2s ease, transform 0.2s ease;
  &:hover { box-shadow: var(--card-shadow-hover); }
  &.disabled { opacity: 0.6; }

  .card-top { display: flex; align-items: center; gap: 12px; min-width: 0;
    .file-icon { width: 42px; height: 42px; border-radius: 10px; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
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
  h3 { margin: 8px 0 6px; font-size: 17px; font-weight: 600; }
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

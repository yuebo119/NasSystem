<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { 
  Refresh, Delete, Link, CopyDocument, Edit, View, 
  Share, Clock, Download, Lock, Unlock, MoreFilled
} from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { fileShareApi, ShareAccessType, type FileShareDto, type UpdateFileShareRequest } from '@/api/fileShare'
import { getFileIcon, formatDate } from '@/utils/format'
import { useIsMobile } from '@/utils/useIsMobile'

const { t } = useI18n()
const loading = ref(false)
const shares = ref<FileShareDto[]>([])
const editDialogVisible = ref(false)
const editTarget = ref<FileShareDto | null>(null)
const editForm = ref<UpdateFileShareRequest>({})
const editExpiresOption = ref('keep')
// 模态统一右侧抽屉：宽屏保持原定宽，窄屏（手机）取全宽
const isMobile = useIsMobile()
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

// 扩展名 → 渐变配色。原实现是 11 个分支的数组 includes 顺序判断，每渲染一行就要
// 把整条链走一遍（平均 6 次数组查找）。改成查表后一次哈希命中。
// 表在模块级构建一次，渲染期零分配。
const ICON_STYLE_FALLBACK = { bg: 'linear-gradient(135deg, #6b7280, #9ca3ad)', color: '#fff' }

const ICON_STYLES_BY_EXT: Record<string, { bg: string; color: string }> = {
  '.pdf': { bg: 'linear-gradient(135deg, #ef4444, #f87171)', color: '#fff' },
  '.doc': { bg: 'linear-gradient(135deg, #3b82f6, #60a5fa)', color: '#fff' },
  '.docx': { bg: 'linear-gradient(135deg, #3b82f6, #60a5fa)', color: '#fff' },
  '.xls': { bg: 'linear-gradient(135deg, #10b981, #34d399)', color: '#fff' },
  '.xlsx': { bg: 'linear-gradient(135deg, #10b981, #34d399)', color: '#fff' },
  '.csv': { bg: 'linear-gradient(135deg, #10b981, #34d399)', color: '#fff' },
  '.ppt': { bg: 'linear-gradient(135deg, #f97316, #fb923c)', color: '#fff' },
  '.pptx': { bg: 'linear-gradient(135deg, #f97316, #fb923f)', color: '#fff' },
  '.txt': { bg: 'linear-gradient(135deg, #6b7280, #9ca3af)', color: '#fff' },
  '.md': { bg: 'linear-gradient(135deg, #6b7280, #9ca3af)', color: '#fff' },
  '.rtf': { bg: 'linear-gradient(135deg, #6b7280, #9ca3af)', color: '#fff' },
  '.jpg': { bg: 'linear-gradient(135deg, #14b8a6, #2dd4bf)', color: '#fff' },
  '.jpeg': { bg: 'linear-gradient(135deg, #14b8a6, #2dd4bf)', color: '#fff' },
  '.png': { bg: 'linear-gradient(135deg, #14b8a6, #2dd4bf)', color: '#fff' },
  '.gif': { bg: 'linear-gradient(135deg, #14b8a6, #2dd4bf)', color: '#fff' },
  '.webp': { bg: 'linear-gradient(135deg, #14b8a6, #2dd4bf)', color: '#fff' },
  '.bmp': { bg: 'linear-gradient(135deg, #14b8a6, #2dd4bf)', color: '#fff' },
  '.svg': { bg: 'linear-gradient(135deg, #14b8a6, #2dd4bf)', color: '#fff' },
  '.ico': { bg: 'linear-gradient(135deg, #14b8a6, #2dd4bf)', color: '#fff' },
  '.mp4': { bg: 'linear-gradient(135deg, #8b5cf6, #a78bfa)', color: '#fff' },
  '.avi': { bg: 'linear-gradient(135deg, #8b5cf6, #a78bfa)', color: '#fff' },
  '.mkv': { bg: 'linear-gradient(135deg, #8b5cf6, #a78bfa)', color: '#fff' },
  '.mov': { bg: 'linear-gradient(135deg, #8b5cf6, #a78bfa)', color: '#fff' },
  '.wmv': { bg: 'linear-gradient(135deg, #8b5cf6, #a78bfa)', color: '#fff' },
  '.flv': { bg: 'linear-gradient(135deg, #8b5cf6, #a78bfa)', color: '#fff' },
  '.webm': { bg: 'linear-gradient(135deg, #8b5cf6, #a78bfa)', color: '#fff' },
  '.m4v': { bg: 'linear-gradient(135deg, #8b5cf6, #a78bfa)', color: '#fff' },
  '.mp3': { bg: 'linear-gradient(135deg, #ec4899, #f472b6)', color: '#fff' },
  '.wav': { bg: 'linear-gradient(135deg, #ec4899, #f472b6)', color: '#fff' },
  '.flac': { bg: 'linear-gradient(135deg, #ec4899, #f472b6)', color: '#fff' },
  '.aac': { bg: 'linear-gradient(135deg, #ec4899, #f472b6)', color: '#fff' },
  '.ogg': { bg: 'linear-gradient(135deg, #ec4899, #f472b6)', color: '#fff' },
  '.wma': { bg: 'linear-gradient(135deg, #ec4899, #f472b6)', color: '#fff' },
  '.m4a': { bg: 'linear-gradient(135deg, #ec4899, #f472b6)', color: '#fff' },
  '.zip': { bg: 'linear-gradient(135deg, #a16207, #ca8a04)', color: '#fff' },
  '.rar': { bg: 'linear-gradient(135deg, #a16207, #ca8a04)', color: '#fff' },
  '.7z': { bg: 'linear-gradient(135deg, #a16207, #ca8a04)', color: '#fff' },
  '.tar': { bg: 'linear-gradient(135deg, #a16207, #ca8a04)', color: '#fff' },
  '.gz': { bg: 'linear-gradient(135deg, #a16207, #ca8a04)', color: '#fff' },
  '.bz2': { bg: 'linear-gradient(135deg, #a16207, #ca8a04)', color: '#fff' },
  '.js': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.ts': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.jsx': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.tsx': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.vue': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.py': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.java': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.c': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.cpp': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.h': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.cs': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.go': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.rs': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.php': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.rb': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.swift': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.kt': { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' },
  '.json': { bg: 'linear-gradient(135deg, #475569, #64748b)', color: '#fff' },
  '.xml': { bg: 'linear-gradient(135deg, #475569, #64748b)', color: '#fff' },
  '.yaml': { bg: 'linear-gradient(135deg, #475569, #64748b)', color: '#fff' },
  '.yml': { bg: 'linear-gradient(135deg, #475569, #64748b)', color: '#fff' },
  '.ini': { bg: 'linear-gradient(135deg, #475569, #64748b)', color: '#fff' },
  '.conf': { bg: 'linear-gradient(135deg, #475569, #64748b)', color: '#fff' },
  '.env': { bg: 'linear-gradient(135deg, #475569, #64748b)', color: '#fff' }
}

const FOLDER_ICON_STYLE = { bg: 'linear-gradient(135deg, #fbbf24, #f59e0b)', color: '#fff' }

// 文件图标样式（渐变徽章， Shares 页专有视觉，保留私有实现）
const getFileIconStyle = (share: FileShareDto) => {
  if (share.isFolder) return FOLDER_ICON_STYLE
  const ext = share.fileName.substring(share.fileName.lastIndexOf('.')).toLowerCase()
  return ICON_STYLES_BY_EXT[ext] ?? ICON_STYLE_FALLBACK
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
    <div class="page-header">
      <div class="header-left">
        <h1>{{ t('shares.title') }}</h1>
        <p>{{ t('shares.subtitle') }}</p>
      </div>
      <el-button :icon="Refresh" @click="loadData" :loading="loading">{{ t('common.refresh') }}</el-button>
    </div>

    <div class="stats-grid">
      <div class="stat-card">
        <div class="stat-icon total"><el-icon :size="24"><Share /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ stats.total }}</div><div class="stat-label">{{ t('shares.totalShares') }}</div></div>
      </div>
      <div class="stat-card">
        <div class="stat-icon active"><el-icon :size="24"><Link /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ stats.active }}</div><div class="stat-label">{{ t('shares.active') }}</div></div>
      </div>
      <div class="stat-card">
        <div class="stat-icon expired"><el-icon :size="24"><Clock /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ stats.expired }}</div><div class="stat-label">{{ t('shares.expired') }}</div></div>
      </div>
      <div class="stat-card">
        <div class="stat-icon downloads"><el-icon :size="24"><Download /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ stats.downloads }}</div><div class="stat-label">{{ t('shares.downloadCount') }}</div></div>
      </div>
    </div>

    <div class="shares-grid" v-loading="loading">
      <div v-for="share in shares" :key="share.id" class="share-card" :class="{ disabled: !share.isEnabled }">
        <div class="card-header">
          <div class="file-icon" :style="{ background: getFileIconStyle(share).bg }">
            <el-icon :size="24" :color="getFileIconStyle(share).color"><component :is="getFileIcon(share)" /></el-icon>
          </div>
          <div class="file-info">
            <div class="file-name">{{ share.fileName }}</div>
            <div class="file-path">{{ share.filePath }}</div>
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

        <div class="card-body">
          <div class="share-badges">
            <div class="badge" :style="{ background: getAccessTypeInfo(share.accessType).bg, color: getAccessTypeInfo(share.accessType).color }">
              <el-icon :size="14"><component :is="getAccessTypeInfo(share.accessType).icon" /></el-icon>
              {{ getAccessTypeText(share.accessType) }}
            </div>
            <div v-if="share.isFolder" class="badge folder">{{ t('files.folder') }}</div>
            <div v-if="share.isExpired" class="badge expired">{{ t('shares.expired') }}</div>
            <div v-else-if="share.isDownloadLimitReached" class="badge limit">{{ t('shares.limitReached') }}</div>
            <div v-else-if="!share.isEnabled" class="badge disabled">{{ t('common.disabled') }}</div>
          </div>

          <div class="share-stats">
            <div class="stat-item">
              <el-icon><Download /></el-icon>
              <span>{{ share.downloadCount }}{{ share.maxDownloads ? '/' + share.maxDownloads : '' }} {{ t('shares.times') }}</span>
            </div>
            <div class="stat-item" v-if="share.expiresAt">
              <el-icon><Clock /></el-icon>
              <span>{{ formatDate(share.expiresAt) }}</span>
            </div>
            <div class="stat-item" v-else>
              <el-icon><Clock /></el-icon>
              <span>{{ t('shares.neverExpire') }}</span>
            </div>
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

      <div v-if="shares.length === 0 && !loading" class="empty-state">
        <el-icon :size="64" color="#d1d5db"><Share /></el-icon>
        <h3>{{ t('shares.noShares') }}</h3>
        <p>{{ t('shares.noSharesHint') }}</p>
      </div>
    </div>

    <!-- 编辑分享抽屉 -->
    <el-drawer v-model="editDialogVisible" :title="t('shares.editShare')" :size="isMobile ? '100%' : '480px'" :mask-closable="false">
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
  .header-left { h1 { margin: 0 0 4px; font-size: 24px; font-weight: 600; } p { margin: 0; color: var(--el-text-color-secondary); font-size: 14px; } }
}
.stats-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; margin-bottom: 24px; }
.stat-card { background: var(--el-bg-color); border-radius: 12px; padding: 20px; display: flex; align-items: center; gap: 16px; border: 1px solid var(--el-border-color-lighter);
  .stat-icon { width: 48px; height: 48px; border-radius: 12px; display: flex; align-items: center; justify-content: center; color: white;
    &.total { background: linear-gradient(135deg, #6366f1, #8b5cf6); }
    &.active { background: linear-gradient(135deg, #10b981, #34d399); }
    &.expired { background: linear-gradient(135deg, #ef4444, #f87171); }
    &.downloads { background: linear-gradient(135deg, #3b82f6, #60a5fa); }
  }
  .stat-info { .stat-value { font-size: 24px; font-weight: 600; } .stat-label { font-size: 13px; color: var(--el-text-color-secondary); } }
}
.shares-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(360px, 1fr)); gap: 16px; }
.share-card { background: var(--el-bg-color); border-radius: 12px; border: 1px solid var(--el-border-color-lighter); overflow: hidden; transition: all 0.2s;
  &:hover { box-shadow: 0 4px 12px rgba(0,0,0,0.08); }
  &.disabled { opacity: 0.6; }
  .card-header { display: flex; align-items: center; gap: 12px; padding: 16px 16px 12px;
    .file-icon { width: 44px; height: 44px; border-radius: 10px; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .file-info { flex: 1; min-width: 0;
      .file-name { font-weight: 600; font-size: 15px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .file-path { font-size: 12px; color: var(--el-text-color-secondary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    }
  }
  .card-body { padding: 0 16px 12px;
    .share-badges { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 12px;
      .badge { display: inline-flex; align-items: center; gap: 4px; padding: 4px 10px; border-radius: 6px; font-size: 12px; font-weight: 500;
        &.folder { background: rgba(217, 119, 6, 0.15); color: #d97706; }
        &.expired { background: rgba(239, 68, 68, 0.15); color: #ef4444; }
        &.limit { background: rgba(245, 158, 11, 0.15); color: #f59e0b; }
        &.disabled { background: var(--el-fill-color); color: var(--el-text-color-secondary); }
      }
    }
    .share-stats { display: flex; gap: 16px;
      .stat-item { display: flex; align-items: center; gap: 4px; font-size: 13px; color: var(--el-text-color-secondary); }
    }
  }
  .card-footer { display: flex; align-items: center; gap: 12px; padding: 12px 16px; background: var(--el-fill-color-light); border-top: 1px solid var(--el-border-color-lighter);
    .link-input { flex: 1; :deep(.el-input__wrapper) { background: var(--el-bg-color); } }
  }
}
.empty-state { grid-column: 1 / -1; text-align: center; padding: 60px 20px; background: var(--el-bg-color); border-radius: 12px; border: 2px dashed var(--el-border-color);
  h3 { margin: 16px 0 8px; font-size: 18px; } p { margin: 0; color: var(--el-text-color-secondary); }
}
@media (max-width: 768px) { .stats-grid { grid-template-columns: repeat(2, 1fr); } .shares-grid { grid-template-columns: 1fr; } }
</style>
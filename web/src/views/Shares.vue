<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { 
  Refresh, Delete, Link, CopyDocument, Edit, View, 
  Document, Folder, Picture, VideoPlay, Headset,
  Share, Clock, Download, Lock, Unlock, MoreFilled,
  Files, Tickets, DataBoard, Memo, Reading, Postcard
} from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { fileShareApi, ShareAccessType, type FileShareDto, type UpdateFileShareRequest } from '@/api/fileShare'

const loading = ref(false)
const shares = ref<FileShareDto[]>([])
const editDialogVisible = ref(false)
const editTarget = ref<FileShareDto | null>(null)
const editForm = ref<UpdateFileShareRequest>({})
const editExpiresOption = ref('keep')
const editMaxDownloadsOption = ref('keep')
const editPassword = ref('')

const loadData = async () => {
  loading.value = true
  try {
    const result = await fileShareApi.getMyShares()
    if (result.data) shares.value = result.data
  } catch { ElMessage.error('加载失败') }
  finally { loading.value = false }
}

const getAccessTypeText = (type: ShareAccessType) => {
  const map: Record<number, string> = { 0: '公开', 1: '密码保护', 2: '内部' }
  return map[type] || '未知'
}

const getAccessTypeInfo = (type: ShareAccessType) => {
  const map: Record<number, any> = {
    0: { icon: Unlock, color: '#10b981', bg: '#ecfdf5' },
    1: { icon: Lock, color: '#f59e0b', bg: '#fffbeb' },
    2: { icon: Share, color: '#6366f1', bg: '#eef2ff' }
  }
  return map[type] || { icon: Share, color: '#6b7280', bg: '#f3f4f6' }
}

// 更丰富的文件图标映射
const getFileIcon = (share: FileShareDto) => {
  if (share.isFolder) return Folder
  const ext = share.fileName.substring(share.fileName.lastIndexOf('.')).toLowerCase()
  
  // 文档类
  if (['.pdf'].includes(ext)) return Reading
  if (['.doc', '.docx'].includes(ext)) return Memo
  if (['.xls', '.xlsx', '.csv'].includes(ext)) return DataBoard
  if (['.ppt', '.pptx'].includes(ext)) return Postcard
  if (['.txt', '.md', '.rtf'].includes(ext)) return Document
  
  // 图片类
  if (['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.svg', '.ico'].includes(ext)) return Picture
  
  // 视频类
  if (['.mp4', '.avi', '.mkv', '.mov', '.wmv', '.flv', '.webm', '.m4v'].includes(ext)) return VideoPlay
  
  // 音频类
  if (['.mp3', '.wav', '.flac', '.aac', '.ogg', '.wma', '.m4a'].includes(ext)) return Headset
  
  // 压缩包
  if (['.zip', '.rar', '.7z', '.tar', '.gz', '.bz2'].includes(ext)) return Files
  
  // 代码文件
  if (['.js', '.ts', '.jsx', '.tsx', '.vue', '.py', '.java', '.c', '.cpp', '.h', '.cs', '.go', '.rs', '.php', '.rb', '.swift', '.kt'].includes(ext)) return Tickets
  
  // 配置文件
  if (['.json', '.xml', '.yaml', '.yml', '.ini', '.conf', '.env'].includes(ext)) return DataBoard
  
  return Document
}

const getFileIconStyle = (share: FileShareDto) => {
  if (share.isFolder) return { bg: 'linear-gradient(135deg, #fbbf24, #f59e0b)', color: '#fff' }
  const ext = share.fileName.substring(share.fileName.lastIndexOf('.')).toLowerCase()
  
  // PDF - 红色
  if (['.pdf'].includes(ext)) return { bg: 'linear-gradient(135deg, #ef4444, #f87171)', color: '#fff' }
  
  // Word文档 - 蓝色
  if (['.doc', '.docx'].includes(ext)) return { bg: 'linear-gradient(135deg, #3b82f6, #60a5fa)', color: '#fff' }
  
  // Excel - 绿色
  if (['.xls', '.xlsx', '.csv'].includes(ext)) return { bg: 'linear-gradient(135deg, #10b981, #34d399)', color: '#fff' }
  
  // PPT - 橙色
  if (['.ppt', '.pptx'].includes(ext)) return { bg: 'linear-gradient(135deg, #f97316, #fb923c)', color: '#fff' }
  
  // 文本 - 灰色
  if (['.txt', '.md', '.rtf'].includes(ext)) return { bg: 'linear-gradient(135deg, #6b7280, #9ca3af)', color: '#fff' }
  
  // 图片 - 青绿色
  if (['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.svg', '.ico'].includes(ext)) 
    return { bg: 'linear-gradient(135deg, #14b8a6, #2dd4bf)', color: '#fff' }
  
  // 视频 - 紫色
  if (['.mp4', '.avi', '.mkv', '.mov', '.wmv', '.flv', '.webm', '.m4v'].includes(ext)) 
    return { bg: 'linear-gradient(135deg, #8b5cf6, #a78bfa)', color: '#fff' }
  
  // 音频 - 粉色
  if (['.mp3', '.wav', '.flac', '.aac', '.ogg', '.wma', '.m4a'].includes(ext)) 
    return { bg: 'linear-gradient(135deg, #ec4899, #f472b6)', color: '#fff' }
  
  // 压缩包 - 棕色
  if (['.zip', '.rar', '.7z', '.tar', '.gz', '.bz2'].includes(ext)) 
    return { bg: 'linear-gradient(135deg, #a16207, #ca8a04)', color: '#fff' }
  
  // 代码 - 靛蓝色
  if (['.js', '.ts', '.jsx', '.tsx', '.vue', '.py', '.java', '.c', '.cpp', '.h', '.cs', '.go', '.rs', '.php', '.rb', '.swift', '.kt'].includes(ext)) 
    return { bg: 'linear-gradient(135deg, #6366f1, #818cf8)', color: '#fff' }
  
  // 配置文件 - 深灰
  if (['.json', '.xml', '.yaml', '.yml', '.ini', '.conf', '.env'].includes(ext)) 
    return { bg: 'linear-gradient(135deg, #475569, #64748b)', color: '#fff' }
  
  return { bg: 'linear-gradient(135deg, #6b7280, #9ca3af)', color: '#fff' }
}

const formatDate = (date: string) => new Date(date).toLocaleDateString('zh-CN')

const copyLink = async (share: FileShareDto) => {
  try {
    await navigator.clipboard.writeText(share.shareUrl)
    ElMessage.success('链接已复制')
  } catch { ElMessage.error('复制失败') }
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
    ElMessage.success('更新成功')
    editDialogVisible.value = false
    await loadData()
  } catch (e: any) { ElMessage.error(e.message || '更新失败') }
}

const handleToggle = async (share: FileShareDto) => {
  try {
    await fileShareApi.update(share.id, { isEnabled: !share.isEnabled })
    share.isEnabled = !share.isEnabled
    ElMessage.success(share.isEnabled ? '已启用' : '已禁用')
  } catch { ElMessage.error('操作失败') }
}

const handleDelete = async (share: FileShareDto) => {
  try {
    await ElMessageBox.confirm(`确定删除 "${share.fileName}" 的分享？`, '确认删除', { type: 'warning' })
    await fileShareApi.delete(share.id)
    ElMessage.success('已删除')
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
        <h1>我的分享</h1>
        <p>管理您创建的所有文件分享链接</p>
      </div>
      <el-button :icon="Refresh" @click="loadData" :loading="loading">刷新</el-button>
    </div>

    <div class="stats-grid">
      <div class="stat-card">
        <div class="stat-icon total"><el-icon :size="24"><Share /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ stats.total }}</div><div class="stat-label">总分享</div></div>
      </div>
      <div class="stat-card">
        <div class="stat-icon active"><el-icon :size="24"><Link /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ stats.active }}</div><div class="stat-label">有效</div></div>
      </div>
      <div class="stat-card">
        <div class="stat-icon expired"><el-icon :size="24"><Clock /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ stats.expired }}</div><div class="stat-label">已过期</div></div>
      </div>
      <div class="stat-card">
        <div class="stat-icon downloads"><el-icon :size="24"><Download /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ stats.downloads }}</div><div class="stat-label">下载次数</div></div>
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
                <el-dropdown-item @click="previewShare(share)"><el-icon><View /></el-icon>预览</el-dropdown-item>
                <el-dropdown-item @click="copyLink(share)"><el-icon><CopyDocument /></el-icon>复制链接</el-dropdown-item>
                <el-dropdown-item @click="openEditDialog(share)"><el-icon><Edit /></el-icon>编辑</el-dropdown-item>
                <el-dropdown-item divided @click="handleDelete(share)"><el-icon color="#ef4444"><Delete /></el-icon><span style="color:#ef4444">删除</span></el-dropdown-item>
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
            <div v-if="share.isFolder" class="badge folder">文件夹</div>
            <div v-if="share.isExpired" class="badge expired">已过期</div>
            <div v-else-if="share.isDownloadLimitReached" class="badge limit">已达限制</div>
            <div v-else-if="!share.isEnabled" class="badge disabled">已禁用</div>
          </div>

          <div class="share-stats">
            <div class="stat-item">
              <el-icon><Download /></el-icon>
              <span>{{ share.downloadCount }}{{ share.maxDownloads ? '/' + share.maxDownloads : '' }} 次</span>
            </div>
            <div class="stat-item" v-if="share.expiresAt">
              <el-icon><Clock /></el-icon>
              <span>{{ formatDate(share.expiresAt) }}</span>
            </div>
            <div class="stat-item" v-else>
              <el-icon><Clock /></el-icon>
              <span>永久有效</span>
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
        <h3>暂无分享</h3>
        <p>在文件管理页面右键文件可创建分享</p>
      </div>
    </div>

    <el-dialog v-model="editDialogVisible" title="编辑分享" width="480px">
      <el-form v-if="editTarget" label-position="top">
        <el-form-item label="访问方式">
          <el-radio-group v-model="editForm.accessType">
            <el-radio :value="0">公开访问</el-radio>
            <el-radio :value="1">密码保护</el-radio>
            <el-radio :value="2">仅内部用户</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="新密码" v-if="editForm.accessType === 1">
          <el-input v-model="editPassword" placeholder="留空保持原密码" show-password />
        </el-form-item>
        <el-form-item label="有效期">
          <el-radio-group v-model="editExpiresOption">
            <el-radio value="keep">保持不变</el-radio>
            <el-radio value="clear">永久</el-radio>
            <el-radio value="7d">7天</el-radio>
            <el-radio value="30d">30天</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="下载限制">
          <el-radio-group v-model="editMaxDownloadsOption">
            <el-radio value="keep">保持不变</el-radio>
            <el-radio value="clear">不限</el-radio>
            <el-radio value="10">10次</el-radio>
            <el-radio value="100">100次</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="状态">
          <el-switch v-model="editForm.isEnabled" active-text="启用" inactive-text="禁用" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSaveEdit">保存</el-button>
      </template>
    </el-dialog>
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
        &.folder { background: #fef3c7; color: #d97706; }
        &.expired { background: #fef2f2; color: #ef4444; }
        &.limit { background: #fffbeb; color: #f59e0b; }
        &.disabled { background: #f3f4f6; color: #6b7280; }
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
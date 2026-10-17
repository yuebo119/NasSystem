<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Plus, Refresh, Edit, Delete, Key, User, Search, UserFilled, Setting } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { userApi } from '@/api/user'
import type { User as UserType } from '@/types'
import { formatSize, formatDate } from '@/utils/format'
import { useIsMobile } from '@/utils/useIsMobile'

const { t } = useI18n()
const loading = ref(false)
const users = ref<UserType[]>([])
const totalCount = ref(0)
const currentPage = ref(1)
const pageSize = ref(20)
const searchTerm = ref('')

// 对话框状态
const createDialogVisible = ref(false)
const editDialogVisible = ref(false)
const resetPasswordDialogVisible = ref(false)
const creating = ref(false)
const updating = ref(false)
// 模态统一右侧抽屉：宽屏保持原定宽，窄屏（手机）取全宽
const isMobile = useIsMobile()

// 表单数据
const newUser = ref({
  username: '',
  password: '',
  confirmPassword: '',
  displayName: '',
  email: '',
  role: 'User',
  quotaGB: 0
})

const editUser = ref<UserType | null>(null)
const editForm = ref({
  displayName: '',
  email: '',
  role: '',
  isEnabled: true,
  quotaGB: 0
})

const resetPasswordForm = ref({
  userId: '',
  username: '',
  newPassword: '',
  confirmPassword: ''
})

const roles = [
  { value: 'User', label: () => t('users.roles.user'), desc: () => t('users.roleDesc.user'), color: '#409eff' },
  { value: 'Admin', label: () => t('users.roles.admin'), desc: () => t('users.roleDesc.admin'), color: '#e6a23c' },
  { value: 'SuperAdmin', label: () => t('users.roles.superAdmin'), desc: () => t('users.roleDesc.superAdmin'), color: '#f56c6c' }
]

const getRoleInfo = (role: string) => {
  const r = roles.find(item => item.value === role)
  return r ? { value: role, label: r.label(), desc: r.desc(), color: r.color } : { value: role, label: role, desc: '', color: '#909399' }
}

const getRoleTagType = (role: string): 'danger' | 'warning' | 'info' | 'success' | 'primary' => {
  switch (role) {
    case 'SuperAdmin': return 'danger'
    case 'Admin': return 'warning'
    default: return 'primary'
  }
}

// 统计数据
const stats = computed(() => {
  const total = users.value.length
  const admins = users.value.filter(u => u.role === 'Admin' || u.role === 'SuperAdmin').length
  const enabled = users.value.filter(u => u.isEnabled).length
  const totalUsed = users.value.reduce((sum, u) => sum + (u.usedBytes || 0), 0)
  return { total, admins, enabled, totalUsed }
})

const loadData = async () => {
  loading.value = true
  try {
    const response = await userApi.getUsers({
      page: currentPage.value,
      pageSize: pageSize.value,
      searchTerm: searchTerm.value || undefined
    })
    if (response.success && response.data) {
      users.value = response.data.items
      totalCount.value = response.data.totalCount
    }
  } catch (error) {
    ElMessage.error(t('common.failed'))
  } finally {
    loading.value = false
  }
}

const handleSearch = () => {
  currentPage.value = 1
  loadData()
}

const openCreateDialog = () => {
  newUser.value = { username: '', password: '', confirmPassword: '', displayName: '', email: '', role: 'User', quotaGB: 0 }
  createDialogVisible.value = true
}

const handleCreateUser = async () => {
  if (!newUser.value.username.trim()) { ElMessage.warning(t('users.usernameRequired')); return }
  if (!newUser.value.password) { ElMessage.warning(t('users.passwordRequired')); return }
  if (newUser.value.password.length < 6) { ElMessage.warning(t('users.passwordMinLength')); return }
  if (newUser.value.password !== newUser.value.confirmPassword) { ElMessage.warning(t('users.passwordMismatch')); return }
  
  creating.value = true
  try {
    const quotaBytes = newUser.value.quotaGB > 0 ? newUser.value.quotaGB * 1024 * 1024 * 1024 : undefined
    const response = await userApi.createUser({
      username: newUser.value.username.trim(),
      password: newUser.value.password,
      displayName: newUser.value.displayName.trim() || undefined,
      email: newUser.value.email.trim() || undefined,
      role: newUser.value.role,
      quotaBytes
    })
    
    if (response.success) {
      ElMessage.success(t('users.createSuccess'))
      createDialogVisible.value = false
      await loadData()
    } else {
      ElMessage.error(response.error?.message || t('common.failed'))
    }
  } catch (error) {
    ElMessage.error(t('common.failed'))
  } finally {
    creating.value = false
  }
}

const openEditDialog = (user: UserType) => {
  editUser.value = user
  editForm.value = {
    displayName: user.displayName || '',
    email: user.email || '',
    role: user.role,
    isEnabled: user.isEnabled,
    quotaGB: user.quotaBytes ? Math.round(user.quotaBytes / 1024 / 1024 / 1024) : 0
  }
  editDialogVisible.value = true
}

const handleUpdateUser = async () => {
  if (!editUser.value) return
  
  updating.value = true
  try {
    const quotaBytes = editForm.value.quotaGB > 0 ? editForm.value.quotaGB * 1024 * 1024 * 1024 : 0
    const response = await userApi.updateUser(editUser.value.id, {
      displayName: editForm.value.displayName.trim() || undefined,
      email: editForm.value.email.trim() || undefined,
      role: editForm.value.role,
      isEnabled: editForm.value.isEnabled,
      quotaBytes
    })
    
    if (response.success) {
      ElMessage.success(t('users.updateSuccess'))
      editDialogVisible.value = false
      await loadData()
    } else {
      ElMessage.error(response.error?.message || t('common.failed'))
    }
  } catch (error) {
    ElMessage.error(t('common.failed'))
  } finally {
    updating.value = false
  }
}

const handleToggle = async (user: UserType) => {
  try {
    const response = await userApi.updateUser(user.id, { isEnabled: !user.isEnabled })
    if (response.success) {
      user.isEnabled = !user.isEnabled
      ElMessage.success(user.isEnabled ? t('common.enabled') : t('common.disabled'))
    } else {
      ElMessage.error(response.error?.message || t('common.failed'))
    }
  } catch (error) {
    ElMessage.error(t('common.failed'))
  }
}

const handleDelete = async (user: UserType) => {
  try {
    await ElMessageBox.confirm(t('users.deleteConfirm').replace('{name}', user.username), t('common.confirm'), { type: 'warning', confirmButtonText: t('common.delete'), cancelButtonText: t('common.cancel') })
    const response = await userApi.deleteUser(user.id)
    if (response.success) {
      ElMessage.success(t('users.deleteSuccess'))
      await loadData()
    } else {
      ElMessage.error(response.error?.message || t('common.failed'))
    }
  } catch { /* 用户取消 */ }
}

const openResetPasswordDialog = (user: UserType) => {
  resetPasswordForm.value = { userId: user.id, username: user.username, newPassword: '', confirmPassword: '' }
  resetPasswordDialogVisible.value = true
}

const handleResetPassword = async () => {
  if (!resetPasswordForm.value.newPassword) { ElMessage.warning(t('users.passwordRequired')); return }
  if (resetPasswordForm.value.newPassword.length < 6) { ElMessage.warning(t('users.passwordMinLength')); return }
  if (resetPasswordForm.value.newPassword !== resetPasswordForm.value.confirmPassword) { ElMessage.warning(t('users.passwordMismatch')); return }
  
  try {
    const response = await userApi.resetPassword(resetPasswordForm.value.userId, { newPassword: resetPasswordForm.value.newPassword })
    if (response.success) {
      ElMessage.success(t('users.resetPasswordSuccess'))
      resetPasswordDialogVisible.value = false
    } else {
      ElMessage.error(response.error?.message || t('common.failed'))
    }
  } catch (error) {
    ElMessage.error(t('common.failed'))
  }
}

const handlePageChange = (page: number) => {
  currentPage.value = page
  loadData()
}

onMounted(() => loadData())
</script>

<template>
  <div class="users-page">
    <div class="page-header">
      <div class="header-left">
        <h1>{{ t('users.title') }}</h1>
        <p>{{ t('users.subtitle') }}</p>
      </div>
      <div class="header-actions">
        <el-button :icon="Refresh" @click="loadData" :loading="loading">{{ t('common.refresh') }}</el-button>
        <el-button type="primary" :icon="Plus" @click="openCreateDialog">{{ t('users.createUser') }}</el-button>
      </div>
    </div>

    <!-- 统计卡片 -->
    <div class="stats-grid">
      <div class="stat-card">
        <div class="stat-icon users"><el-icon :size="24"><UserFilled /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ totalCount }}</div><div class="stat-label">{{ t('users.totalUsers') }}</div></div>
      </div>
      <div class="stat-card">
        <div class="stat-icon admins"><el-icon :size="24"><Setting /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ stats.admins }}</div><div class="stat-label">{{ t('users.adminCount') }}</div></div>
      </div>
      <div class="stat-card">
        <div class="stat-icon enabled"><el-icon :size="24"><User /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ stats.enabled }}</div><div class="stat-label">{{ t('users.enabledCount') }}</div></div>
      </div>
      <div class="stat-card">
        <div class="stat-icon storage"><el-icon :size="24"><User /></el-icon></div>
        <div class="stat-info"><div class="stat-value">{{ formatSize(stats.totalUsed) }}</div><div class="stat-label">{{ t('users.totalUsedSpace') }}</div></div>
      </div>
    </div>

    <!-- 搜索栏 -->
    <div class="search-bar">
      <el-input v-model="searchTerm" :placeholder="t('users.searchPlaceholder')" :prefix-icon="Search" clearable @keyup.enter="handleSearch" @clear="handleSearch" style="width: 300px" />
    </div>

    <!-- 用户列表 -->
    <div class="users-list" v-loading="loading">
      <div v-for="user in users" :key="user.id" class="user-card" :class="{ disabled: !user.isEnabled }">
        <div class="user-avatar">
          <el-avatar :size="48" :style="{ background: getRoleInfo(user.role).color }">
            {{ (user.displayName || user.username).charAt(0).toUpperCase() }}
          </el-avatar>
          <div class="status-dot" :class="{ online: user.isEnabled }"></div>
        </div>
        
        <div class="user-info">
          <div class="user-name">
            <span class="name">{{ user.displayName || user.username }}</span>
            <el-tag :type="getRoleTagType(user.role)" size="small">{{ getRoleInfo(user.role).label }}</el-tag>
            <el-tag v-if="!user.isEnabled" type="info" size="small">{{ $t('users.disabledTag') }}</el-tag>
          </div>
          <div class="user-meta">
            <span class="username">@{{ user.username }}</span>
            <span v-if="user.email" class="email">{{ user.email }}</span>
          </div>
        </div>

        <div class="user-stats">
          <div class="stat-item">
            <span class="label">{{ t('users.usedSpace') }}</span>
            <span class="value">{{ formatSize(user.usedBytes || 0) }}</span>
            <div v-if="user.quotaBytes" class="quota-bar">
              <div class="quota-fill" :style="{ width: `${Math.min(100, (user.usedBytes || 0) / user.quotaBytes * 100)}%` }"></div>
            </div>
            <span v-if="user.quotaBytes" class="quota-text">/ {{ formatSize(user.quotaBytes) }}</span>
            <span v-else class="quota-text unlimited">{{ t('common.unlimited') }}</span>
          </div>
          <div class="stat-item">
            <span class="label">{{ t('users.lastLogin') }}</span>
            <span class="value">{{ formatDate(user.lastLoginAt) }}</span>
          </div>
        </div>

        <div class="user-actions">
          <el-tooltip :content="t('common.edit')" placement="top">
            <el-button text circle @click="openEditDialog(user)"><el-icon><Edit /></el-icon></el-button>
          </el-tooltip>
          <el-tooltip :content="t('users.resetPassword')" placement="top">
            <el-button text circle @click="openResetPasswordDialog(user)"><el-icon><Key /></el-icon></el-button>
          </el-tooltip>
          <el-tooltip :content="user.isEnabled ? t('common.disabled') : t('common.enabled')" placement="top">
            <el-switch :model-value="user.isEnabled" @change="handleToggle(user)" :disabled="user.role === 'SuperAdmin'" size="small" style="margin: 0 8px" />
          </el-tooltip>
          <el-tooltip :content="t('common.delete')" placement="top">
            <el-button text circle type="danger" @click="handleDelete(user)" :disabled="user.role === 'SuperAdmin'"><el-icon><Delete /></el-icon></el-button>
          </el-tooltip>
        </div>
      </div>

      <div v-if="users.length === 0 && !loading" class="empty-state">
        <el-icon :size="64" color="#d1d5db"><UserFilled /></el-icon>
        <h3>{{ t('users.noUsers') }}</h3>
        <p>{{ t('users.createFirstUser') }}</p>
        <el-button type="primary" @click="openCreateDialog"><el-icon><Plus /></el-icon>{{ t('users.createUser') }}</el-button>
      </div>
    </div>

    <!-- 分页 -->
    <div class="pagination-wrapper" v-if="totalCount > pageSize">
      <el-pagination :current-page="currentPage" :page-size="pageSize" :total="totalCount" layout="total, prev, pager, next" @current-change="handlePageChange" />
    </div>

    <!-- 创建用户对话框 -->
    <el-drawer v-model="createDialogVisible" :title="t('users.createUser')" :size="isMobile ? '100%' : '500px'" :mask-closable="false">
      <el-form :model="newUser" label-position="top">
        <el-form-item :label="t('users.username')" required>
          <el-input v-model="newUser.username" :placeholder="t('users.usernamePlaceholder')" maxlength="30" show-word-limit />
        </el-form-item>
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item :label="t('users.password')" required>
              <el-input v-model="newUser.password" type="password" :placeholder="t('users.passwordMinLength')" show-password />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('users.confirmPassword')" required>
              <el-input v-model="newUser.confirmPassword" type="password" :placeholder="t('users.confirmPasswordPlaceholder')" show-password />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item :label="t('users.displayName')">
          <el-input v-model="newUser.displayName" :placeholder="t('users.displayNamePlaceholder')" maxlength="50" />
        </el-form-item>
        <el-form-item :label="t('users.email')">
          <el-input v-model="newUser.email" type="email" :placeholder="t('users.optional')" />
        </el-form-item>
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item :label="t('users.role')">
              <el-select v-model="newUser.role" style="width: 100%">
                <el-option v-for="r in roles" :key="r.value" :label="r.label()" :value="r.value">
                  <div style="display: flex; justify-content: space-between; align-items: center">
                    <span>{{ r.label() }}</span>
                    <span style="font-size: 12px; color: #909399">{{ r.desc() }}</span>
                  </div>
                </el-option>
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('users.quota') + ' (GB)'">
              <el-input-number v-model="newUser.quotaGB" :min="0" :max="10240" :step="10" style="width: 100%" />
              <div class="form-tip">{{ t('users.quotaHint') }}</div>
            </el-form-item>
          </el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">{{ t('common.cancel') }}</el-button>
        <el-button type="primary" @click="handleCreateUser" :loading="creating">{{ t('common.create') }}</el-button>
      </template>
    </el-drawer>

    <!-- 编辑用户抽屉 -->
    <el-drawer v-model="editDialogVisible" :title="t('users.editUser')" :size="isMobile ? '100%' : '500px'" :mask-closable="false">
      <el-form :model="editForm" label-position="top">
        <el-form-item :label="t('users.username')">
          <el-input :model-value="editUser?.username" disabled />
        </el-form-item>
        <el-form-item :label="t('users.displayName')">
          <el-input v-model="editForm.displayName" :placeholder="t('users.optional')" maxlength="50" />
        </el-form-item>
        <el-form-item :label="t('users.email')">
          <el-input v-model="editForm.email" type="email" :placeholder="t('users.optional')" />
        </el-form-item>
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item :label="t('users.role')">
              <el-select v-model="editForm.role" style="width: 100%" :disabled="editUser?.role === 'SuperAdmin'">
                <el-option v-for="r in roles" :key="r.value" :label="r.label()" :value="r.value" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('users.quota') + ' (GB)'">
              <el-input-number v-model="editForm.quotaGB" :min="0" :max="10240" :step="10" style="width: 100%" />
              <div class="form-tip">{{ t('users.quotaHint') }}</div>
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item :label="t('common.status')">
          <el-switch v-model="editForm.isEnabled" :disabled="editUser?.role === 'SuperAdmin'" />
          <span style="margin-left: 8px; color: var(--el-text-color-secondary)">{{ editForm.isEnabled ? t('common.enabled') : t('common.disabled') }}</span>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialogVisible = false">{{ t('common.cancel') }}</el-button>
        <el-button type="primary" @click="handleUpdateUser" :loading="updating">{{ t('common.save') }}</el-button>
      </template>
    </el-drawer>

    <!-- 重置密码抽屉 -->
    <el-drawer v-model="resetPasswordDialogVisible" :title="t('users.resetPassword')" :size="isMobile ? '100%' : '400px'" :mask-closable="false">
      <el-form :model="resetPasswordForm" label-position="top">
        <el-form-item :label="t('users.username')">
          <el-input :model-value="resetPasswordForm.username" disabled />
        </el-form-item>
        <el-form-item :label="t('users.newPassword')" required>
          <el-input v-model="resetPasswordForm.newPassword" type="password" :placeholder="t('users.passwordMinLength')" show-password />
        </el-form-item>
        <el-form-item :label="t('users.confirmPassword')" required>
          <el-input v-model="resetPasswordForm.confirmPassword" type="password" :placeholder="t('users.confirmPasswordPlaceholder')" show-password />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="resetPasswordDialogVisible = false">{{ t('common.cancel') }}</el-button>
        <el-button type="primary" @click="handleResetPassword">{{ t('common.confirm') }}</el-button>
      </template>
    </el-drawer>
  </div>
</template>


<style lang="scss" scoped>
.users-page { padding: 0; }

.page-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 24px;
  .header-left { h1 { margin: 0 0 4px; font-size: 24px; font-weight: 600; } p { margin: 0; color: var(--el-text-color-secondary); font-size: 14px; } }
  .header-actions { display: flex; gap: 12px; }
}

.stats-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; margin-bottom: 24px; }
.stat-card { background: var(--el-bg-color); border-radius: 12px; padding: 20px; border: 1px solid var(--el-border-color-lighter); display: flex; align-items: center; gap: 16px;
  .stat-icon { width: 48px; height: 48px; border-radius: 12px; display: flex; align-items: center; justify-content: center; color: white;
    &.users { background: linear-gradient(135deg, #6366f1, #8b5cf6); }
    &.admins { background: linear-gradient(135deg, #f59e0b, #fbbf24); }
    &.enabled { background: linear-gradient(135deg, #10b981, #34d399); }
    &.storage { background: linear-gradient(135deg, #3b82f6, #60a5fa); }
  }
  .stat-info { .stat-value { font-size: 24px; font-weight: 700; } .stat-label { font-size: 13px; color: var(--el-text-color-secondary); } }
}

.search-bar { margin-bottom: 16px; }

.users-list { display: flex; flex-direction: column; gap: 12px; }
.user-card { background: var(--el-bg-color); border-radius: 12px; padding: 20px 24px; border: 1px solid var(--el-border-color-lighter); display: flex; align-items: center; gap: 20px; transition: all 0.2s;
  &:hover { box-shadow: 0 4px 12px rgba(0,0,0,0.06); }
  &.disabled { opacity: 0.6; }
  
  .user-avatar { position: relative;
    .status-dot { position: absolute; bottom: 2px; right: 2px; width: 12px; height: 12px; border-radius: 50%; background: var(--el-text-color-placeholder); border: 2px solid var(--el-bg-color);
      &.online { background: #10b981; }
    }
  }
  
  .user-info { flex: 1; min-width: 0;
    .user-name { display: flex; align-items: center; gap: 8px; margin-bottom: 4px;
      .name { font-size: 16px; font-weight: 600; }
    }
    .user-meta { display: flex; gap: 16px; font-size: 13px; color: var(--el-text-color-secondary);
      .username { font-family: monospace; }
    }
  }
  
  .user-stats { display: flex; gap: 32px;
    .stat-item { display: flex; flex-direction: column; gap: 4px;
      .label { font-size: 12px; color: var(--el-text-color-secondary); }
      .value { font-size: 14px; font-weight: 500; }
      .quota-bar { width: 80px; height: 4px; background: var(--el-fill-color); border-radius: 2px; overflow: hidden;
        .quota-fill { height: 100%; background: linear-gradient(90deg, #10b981, #34d399); border-radius: 2px; }
      }
      .quota-text { font-size: 12px; color: var(--el-text-color-secondary);
        &.unlimited { color: #10b981; }
      }
    }
  }
  
  .user-actions { display: flex; align-items: center; gap: 4px; }
}

.empty-state { text-align: center; padding: 60px 20px; background: var(--el-bg-color); border-radius: 12px; border: 2px dashed var(--el-border-color);
  h3 { margin: 16px 0 8px; font-size: 18px; } p { margin: 0 0 20px; color: var(--el-text-color-secondary); }
}

.pagination-wrapper { margin-top: 20px; display: flex; justify-content: flex-end; }

.form-tip { font-size: 12px; color: var(--el-text-color-secondary); margin-top: 4px; }

@media (max-width: 1200px) { .stats-grid { grid-template-columns: repeat(2, 1fr); } }
@media (max-width: 768px) { 
  .stats-grid { grid-template-columns: 1fr; }
  .user-card { flex-wrap: wrap; .user-stats { width: 100%; order: 3; } .user-actions { width: 100%; justify-content: flex-end; order: 4; } }
}
</style>

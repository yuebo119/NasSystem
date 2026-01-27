<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { Plus, Refresh, Edit, Delete, Key, User } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { userApi } from '@/api/user'
import type { User as UserType } from '@/types'

// 状态
const loading = ref(false)
const users = ref<UserType[]>([])
const totalCount = ref(0)
const currentPage = ref(1)
const pageSize = ref(20)

// 对话框状态
const createDialogVisible = ref(false)
const editDialogVisible = ref(false)
const resetPasswordDialogVisible = ref(false)

// 表单数据
const newUser = ref({
  username: '',
  password: '',
  confirmPassword: '',
  displayName: '',
  email: '',
  role: 'User'
})

const editUser = ref<UserType | null>(null)
const editForm = ref({
  displayName: '',
  email: '',
  role: '',
  isEnabled: true
})

const resetPasswordForm = ref({
  userId: '',
  username: '',
  newPassword: '',
  confirmPassword: ''
})

// 角色选项
const roles = [
  { value: 'Guest', label: '访客' },
  { value: 'User', label: '普通用户' },
  { value: 'Admin', label: '管理员' },
  { value: 'SuperAdmin', label: '超级管理员' }
]

// 角色标签类型
const getRoleTagType = (role: string): 'danger' | 'warning' | 'info' | 'success' | 'primary' => {
  switch (role) {
    case 'SuperAdmin': return 'danger'
    case 'Admin': return 'warning'
    case 'User': return 'info'
    default: return 'info'
  }
}

// 角色显示名称
const getRoleName = (role: string) => {
  const r = roles.find(item => item.value === role)
  return r ? r.label : role
}

// 格式化大小
const formatSize = (bytes: number): string => {
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
}

// 格式化日期
const formatDate = (dateStr?: string): string => {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString('zh-CN')
}

// 加载数据
const loadData = async () => {
  loading.value = true
  try {
    const response = await userApi.getUsers({
      page: currentPage.value,
      pageSize: pageSize.value
    })
    if (response.success && response.data) {
      users.value = response.data.items
      totalCount.value = response.data.totalCount
    }
  } catch (error) {
    ElMessage.error('加载用户列表失败')
  } finally {
    loading.value = false
  }
}

// 创建用户
const handleCreateUser = async () => {
  if (!newUser.value.username || !newUser.value.password) {
    ElMessage.warning('请填写用户名和密码')
    return
  }
  if (newUser.value.password !== newUser.value.confirmPassword) {
    ElMessage.warning('两次输入的密码不一致')
    return
  }
  
  try {
    const response = await userApi.createUser({
      username: newUser.value.username,
      password: newUser.value.password,
      displayName: newUser.value.displayName || undefined,
      email: newUser.value.email || undefined,
      role: newUser.value.role
    })
    
    if (response.success) {
      ElMessage.success('用户创建成功')
      createDialogVisible.value = false
      newUser.value = { username: '', password: '', confirmPassword: '', displayName: '', email: '', role: 'User' }
      await loadData()
    } else {
      ElMessage.error(response.error?.message || '创建失败')
    }
  } catch (error) {
    ElMessage.error('创建失败')
  }
}

// 打开编辑对话框
const openEditDialog = (user: UserType) => {
  editUser.value = user
  editForm.value = {
    displayName: user.displayName || '',
    email: user.email || '',
    role: user.role,
    isEnabled: user.isEnabled
  }
  editDialogVisible.value = true
}

// 更新用户
const handleUpdateUser = async () => {
  if (!editUser.value) return
  
  try {
    const response = await userApi.updateUser(editUser.value.id, {
      displayName: editForm.value.displayName || undefined,
      email: editForm.value.email || undefined,
      role: editForm.value.role,
      isEnabled: editForm.value.isEnabled
    })
    
    if (response.success) {
      ElMessage.success('更新成功')
      editDialogVisible.value = false
      await loadData()
    } else {
      ElMessage.error(response.error?.message || '更新失败')
    }
  } catch (error) {
    ElMessage.error('更新失败')
  }
}

// 切换启用状态
const handleToggle = async (user: UserType) => {
  try {
    const response = await userApi.updateUser(user.id, {
      isEnabled: !user.isEnabled
    })
    
    if (response.success) {
      user.isEnabled = !user.isEnabled
      ElMessage.success(user.isEnabled ? '已启用' : '已禁用')
    } else {
      ElMessage.error(response.error?.message || '操作失败')
    }
  } catch (error) {
    ElMessage.error('操作失败')
  }
}

// 删除用户
const handleDelete = async (user: UserType) => {
  try {
    await ElMessageBox.confirm(
      `确定要删除用户 "${user.username}" 吗？`,
      '确认删除',
      { type: 'warning' }
    )
    
    const response = await userApi.deleteUser(user.id)
    if (response.success) {
      ElMessage.success('用户已删除')
      await loadData()
    } else {
      ElMessage.error(response.error?.message || '删除失败')
    }
  } catch {
    // 用户取消
  }
}

// 打开重置密码对话框
const openResetPasswordDialog = (user: UserType) => {
  resetPasswordForm.value = {
    userId: user.id,
    username: user.username,
    newPassword: '',
    confirmPassword: ''
  }
  resetPasswordDialogVisible.value = true
}

// 重置密码
const handleResetPassword = async () => {
  if (!resetPasswordForm.value.newPassword) {
    ElMessage.warning('请输入新密码')
    return
  }
  if (resetPasswordForm.value.newPassword !== resetPasswordForm.value.confirmPassword) {
    ElMessage.warning('两次输入的密码不一致')
    return
  }
  
  try {
    const response = await userApi.resetPassword(resetPasswordForm.value.userId, {
      newPassword: resetPasswordForm.value.newPassword
    })
    
    if (response.success) {
      ElMessage.success('密码已重置')
      resetPasswordDialogVisible.value = false
    } else {
      ElMessage.error(response.error?.message || '重置失败')
    }
  } catch (error) {
    ElMessage.error('重置失败')
  }
}

// 分页变化
const handlePageChange = (page: number) => {
  currentPage.value = page
  loadData()
}

onMounted(() => {
  loadData()
})
</script>

<template>
  <div class="users-page">
    <el-page-header title="返回" @back="$router.back()">
      <template #content>
        <span class="page-title">用户管理</span>
      </template>
      <template #extra>
        <el-button :icon="Refresh" @click="loadData" :loading="loading">刷新</el-button>
      </template>
    </el-page-header>

    <el-card class="page-card">
      <template #header>
        <div class="card-header">
          <span>用户列表 ({{ totalCount }})</span>
          <el-button type="primary" :icon="Plus" @click="createDialogVisible = true">
            创建用户
          </el-button>
        </div>
      </template>

      <el-table :data="users" v-loading="loading" style="width: 100%">
        <el-table-column prop="username" label="用户名" min-width="120">
          <template #default="{ row }">
            <div class="user-info">
              <el-avatar :size="32" :icon="User" />
              <span>{{ row.username }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="displayName" label="显示名称" min-width="120" />
        <el-table-column prop="email" label="邮箱" min-width="180" />
        <el-table-column prop="role" label="角色" width="120">
          <template #default="{ row }">
            <el-tag :type="getRoleTagType(row.role)">
              {{ getRoleName(row.role) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-switch 
              :model-value="row.isEnabled" 
              @change="handleToggle(row)"
              :disabled="row.role === 'SuperAdmin'"
            />
          </template>
        </el-table-column>
        <el-table-column label="配额使用" width="150">
          <template #default="{ row }">
            <div v-if="row.quotaBytes">
              <span>{{ formatSize(row.usedBytes) }} / {{ formatSize(row.quotaBytes) }}</span>
              <el-progress 
                :percentage="Math.round(row.usedBytes / row.quotaBytes * 100)" 
                :stroke-width="4"
                :show-text="false"
              />
            </div>
            <span v-else class="text-secondary">无限制</span>
          </template>
        </el-table-column>
        <el-table-column prop="lastLoginAt" label="最后登录" width="180">
          <template #default="{ row }">
            {{ formatDate(row.lastLoginAt) }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="180" fixed="right">
          <template #default="{ row }">
            <el-button-group size="small">
              <el-tooltip content="编辑">
                <el-button :icon="Edit" @click="openEditDialog(row)" />
              </el-tooltip>
              <el-tooltip content="重置密码">
                <el-button :icon="Key" @click="openResetPasswordDialog(row)" />
              </el-tooltip>
              <el-tooltip content="删除">
                <el-button 
                  :icon="Delete" 
                  type="danger" 
                  @click="handleDelete(row)"
                  :disabled="row.role === 'SuperAdmin'"
                />
              </el-tooltip>
            </el-button-group>
          </template>
        </el-table-column>
      </el-table>

      <el-empty v-if="users.length === 0 && !loading" description="暂无用户" />
      
      <div class="pagination-wrapper" v-if="totalCount > pageSize">
        <el-pagination
          :current-page="currentPage"
          :page-size="pageSize"
          :total="totalCount"
          layout="total, prev, pager, next"
          @current-change="handlePageChange"
        />
      </div>
    </el-card>

    <!-- 创建用户对话框 -->
    <el-dialog v-model="createDialogVisible" title="创建用户" width="500">
      <el-form :model="newUser" label-width="100px">
        <el-form-item label="用户名" required>
          <el-input v-model="newUser.username" placeholder="请输入用户名" />
        </el-form-item>
        <el-form-item label="密码" required>
          <el-input v-model="newUser.password" type="password" placeholder="请输入密码" show-password />
        </el-form-item>
        <el-form-item label="确认密码" required>
          <el-input v-model="newUser.confirmPassword" type="password" placeholder="请再次输入密码" show-password />
        </el-form-item>
        <el-form-item label="显示名称">
          <el-input v-model="newUser.displayName" placeholder="可选" />
        </el-form-item>
        <el-form-item label="邮箱">
          <el-input v-model="newUser.email" type="email" placeholder="可选" />
        </el-form-item>
        <el-form-item label="角色">
          <el-select v-model="newUser.role" style="width: 100%">
            <el-option 
              v-for="item in roles" 
              :key="item.value" 
              :label="item.label" 
              :value="item.value" 
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleCreateUser">创建</el-button>
      </template>
    </el-dialog>

    <!-- 编辑用户对话框 -->
    <el-dialog v-model="editDialogVisible" title="编辑用户" width="500">
      <el-form :model="editForm" label-width="100px">
        <el-form-item label="用户名">
          <el-input :model-value="editUser?.username" disabled />
        </el-form-item>
        <el-form-item label="显示名称">
          <el-input v-model="editForm.displayName" placeholder="可选" />
        </el-form-item>
        <el-form-item label="邮箱">
          <el-input v-model="editForm.email" type="email" placeholder="可选" />
        </el-form-item>
        <el-form-item label="角色">
          <el-select v-model="editForm.role" style="width: 100%" :disabled="editUser?.role === 'SuperAdmin'">
            <el-option 
              v-for="item in roles" 
              :key="item.value" 
              :label="item.label" 
              :value="item.value" 
            />
          </el-select>
        </el-form-item>
        <el-form-item label="启用状态">
          <el-switch v-model="editForm.isEnabled" :disabled="editUser?.role === 'SuperAdmin'" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleUpdateUser">保存</el-button>
      </template>
    </el-dialog>

    <!-- 重置密码对话框 -->
    <el-dialog v-model="resetPasswordDialogVisible" title="重置密码" width="400">
      <el-form :model="resetPasswordForm" label-width="100px">
        <el-form-item label="用户">
          <el-input :model-value="resetPasswordForm.username" disabled />
        </el-form-item>
        <el-form-item label="新密码" required>
          <el-input v-model="resetPasswordForm.newPassword" type="password" placeholder="请输入新密码" show-password />
        </el-form-item>
        <el-form-item label="确认密码" required>
          <el-input v-model="resetPasswordForm.confirmPassword" type="password" placeholder="请再次输入新密码" show-password />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="resetPasswordDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleResetPassword">确认重置</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style lang="scss" scoped>
.users-page {
  padding: 20px;
  
  .page-card {
    margin-top: 20px;
  }

  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }
  
  .user-info {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  
  .text-secondary {
    color: var(--el-text-color-secondary);
  }
  
  .pagination-wrapper {
    margin-top: 20px;
    display: flex;
    justify-content: flex-end;
  }
}
</style>

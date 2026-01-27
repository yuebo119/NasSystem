<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { Plus, Refresh, Edit, Delete, Link, Setting } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { Share } from '@/api/share'

// 状态
const loading = ref(false)
const shares = ref<Share[]>([])
const createDialogVisible = ref(false)
const newShare = ref({
  name: '',
  path: '',
  shareType: 0 as number,
  storagePoolId: '',
  description: '',
  isPublic: false,
  isReadOnly: false
})

// 共享类型选项
const shareTypes: { value: number; label: string }[] = [
  { value: 0, label: 'SMB/CIFS (Windows)' },
  { value: 1, label: 'NFS (Linux/Unix)' },
  { value: 2, label: 'WebDAV' },
  { value: 3, label: 'FTP' }
]

// 加载数据
const loadData = async () => {
  loading.value = true
  try {
    // 模拟数据 - 实际应调用 API
    shares.value = [
      {
        id: '1',
        name: 'Documents',
        description: '文档共享',
        path: '/shares/documents',
        physicalPath: '/mnt/storage/shares/documents',
        shareType: 0,
        shareTypeName: 'SMB/CIFS',
        isEnabled: true,
        isPublic: false,
        isReadOnly: false,
        enableRecycleBin: true,
        smbShareName: 'Documents',
        createdByUserId: '1',
        createdByUserName: 'admin',
        storagePoolId: '1',
        storagePoolName: 'MainPool',
        createdAt: new Date().toISOString()
      },
      {
        id: '2',
        name: 'Public',
        description: '公共共享',
        path: '/shares/public',
        physicalPath: '/mnt/storage/shares/public',
        shareType: 0,
        shareTypeName: 'SMB/CIFS',
        isEnabled: true,
        isPublic: true,
        isReadOnly: false,
        enableRecycleBin: true,
        smbShareName: 'Public',
        createdByUserId: '1',
        createdByUserName: 'admin',
        storagePoolId: '1',
        storagePoolName: 'MainPool',
        createdAt: new Date().toISOString()
      }
    ]
  } finally {
    loading.value = false
  }
}

// 创建共享
const handleCreateShare = async () => {
  if (!newShare.value.name || !newShare.value.path) {
    ElMessage.warning('请填写共享名称和路径')
    return
  }
  try {
    // 调用 API 创建
    ElMessage.success('共享创建成功')
    createDialogVisible.value = false
    newShare.value = { name: '', path: '', shareType: 0, storagePoolId: '', description: '', isPublic: false, isReadOnly: false }
    await loadData()
  } catch (error) {
    ElMessage.error('创建失败')
  }
}

// 切换启用状态
const handleToggle = async (share: Share) => {
  try {
    // 调用 API
    share.isEnabled = !share.isEnabled
    ElMessage.success(share.isEnabled ? '已启用' : '已禁用')
  } catch (error) {
    ElMessage.error('操作失败')
  }
}

// 删除共享
const handleDelete = async (share: Share) => {
  try {
    await ElMessageBox.confirm(
      `确定要删除共享 "${share.name}" 吗？`,
      '确认删除',
      { type: 'warning' }
    )
    // 调用 API 删除
    ElMessage.success('共享已删除')
    await loadData()
  } catch {
    // 用户取消
  }
}

// 复制链接
const copyLink = (share: Share) => {
  const link = `\\\\${window.location.hostname}\\${share.smbShareName}`
  navigator.clipboard.writeText(link)
  ElMessage.success('链接已复制')
}

onMounted(() => {
  loadData()
})
</script>

<template>
  <div class="shares-page">
    <el-page-header title="返回" @back="$router.back()">
      <template #content>
        <span class="page-title">共享管理</span>
      </template>
      <template #extra>
        <el-button :icon="Refresh" @click="loadData" :loading="loading">刷新</el-button>
      </template>
    </el-page-header>

    <el-card class="page-card">
      <template #header>
        <div class="card-header">
          <span>共享列表 ({{ shares.length }})</span>
          <el-button type="primary" @click="createDialogVisible = true">
            <el-icon><Plus /></el-icon>
            创建共享
          </el-button>
        </div>
      </template>

      <el-table :data="shares" v-loading="loading" style="width: 100%">
        <el-table-column prop="name" label="名称" min-width="150">
          <template #default="{ row }">
            <div class="share-name">
              <span>{{ row.name }}</span>
              <el-tag v-if="row.isPublic" type="success" size="small">公开</el-tag>
              <el-tag v-if="row.isReadOnly" type="info" size="small">只读</el-tag>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="shareTypeName" label="类型" width="120" />
        <el-table-column prop="path" label="路径" min-width="180" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-switch 
              v-model="row.isEnabled" 
              @change="handleToggle(row)"
              active-text="启用"
              inactive-text="禁用"
              inline-prompt
            />
          </template>
        </el-table-column>
        <el-table-column prop="storagePoolName" label="存储池" width="120" />
        <el-table-column prop="createdByUserName" label="创建者" width="100" />
        <el-table-column label="操作" width="180" fixed="right">
          <template #default="{ row }">
            <el-button-group size="small">
              <el-tooltip content="复制链接">
                <el-button :icon="Link" @click="copyLink(row)" />
              </el-tooltip>
              <el-tooltip content="编辑">
                <el-button :icon="Edit" />
              </el-tooltip>
              <el-tooltip content="权限设置">
                <el-button :icon="Setting" />
              </el-tooltip>
              <el-tooltip content="删除">
                <el-button :icon="Delete" type="danger" @click="handleDelete(row)" />
              </el-tooltip>
            </el-button-group>
          </template>
        </el-table-column>
      </el-table>

      <el-empty v-if="shares.length === 0 && !loading" description="暂无共享，请创建一个共享" />
    </el-card>

    <!-- 创建共享对话框 -->
    <el-dialog v-model="createDialogVisible" title="创建共享" width="500">
      <el-form :model="newShare" label-width="100px">
        <el-form-item label="名称" required>
          <el-input v-model="newShare.name" placeholder="请输入共享名称" />
        </el-form-item>
        <el-form-item label="路径" required>
          <el-input v-model="newShare.path" placeholder="/shares/myshare" />
        </el-form-item>
        <el-form-item label="共享类型">
          <el-select v-model="newShare.shareType" style="width: 100%">
            <el-option 
              v-for="item in shareTypes" 
              :key="item.value" 
              :label="item.label" 
              :value="item.value" 
            />
          </el-select>
        </el-form-item>
        <el-form-item label="访问设置">
          <el-checkbox v-model="newShare.isPublic">公开访问（无需登录）</el-checkbox>
          <el-checkbox v-model="newShare.isReadOnly">只读</el-checkbox>
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="newShare.description" type="textarea" :rows="3" placeholder="可选描述" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleCreateShare">创建</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style lang="scss" scoped>
.shares-page {
  .page-card {
    margin-top: 20px;
  }

  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }
}
</style>

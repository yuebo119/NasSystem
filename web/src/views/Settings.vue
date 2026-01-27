<script setup lang="ts">
import { ref } from 'vue'
import { useUserStore } from '@/stores/user'

const userStore = useUserStore()

// 暗色模式切换
const isDarkMode = ref(false)

function toggleDarkMode() {
  isDarkMode.value = !isDarkMode.value
  document.documentElement.classList.toggle('dark', isDarkMode.value)
}
</script>

<template>
  <div class="settings-page">
    <el-page-header title="返回" @back="$router.back()">
      <template #content>
        <span class="page-title">系统设置</span>
      </template>
    </el-page-header>

    <el-row :gutter="20" class="settings-row">
      <!-- 个人设置 -->
      <el-col :span="12">
        <el-card>
          <template #header>
            <div class="card-header">
              <el-icon><User /></el-icon>
              <span>个人设置</span>
            </div>
          </template>

          <el-form label-width="100px">
            <el-form-item label="用户名">
              <el-input :value="userStore.username" disabled />
            </el-form-item>
            <el-form-item label="显示名称">
              <el-input :value="userStore.displayName" />
            </el-form-item>
            <el-form-item label="邮箱">
              <el-input :value="userStore.user?.email" />
            </el-form-item>
            <el-form-item>
              <el-button type="primary">保存修改</el-button>
            </el-form-item>
          </el-form>
        </el-card>
      </el-col>

      <!-- 界面设置 -->
      <el-col :span="12">
        <el-card>
          <template #header>
            <div class="card-header">
              <el-icon><Setting /></el-icon>
              <span>界面设置</span>
            </div>
          </template>

          <el-form label-width="100px">
            <el-form-item label="暗色模式">
              <el-switch v-model="isDarkMode" @change="toggleDarkMode" />
            </el-form-item>
            <el-form-item label="语言">
              <el-select value="zh-CN" style="width: 100%">
                <el-option label="简体中文" value="zh-CN" />
                <el-option label="English" value="en-US" disabled />
              </el-select>
            </el-form-item>
          </el-form>
        </el-card>
      </el-col>
    </el-row>

    <!-- 修改密码 -->
    <el-card class="page-card">
      <template #header>
        <div class="card-header">
          <el-icon><Lock /></el-icon>
          <span>修改密码</span>
        </div>
      </template>

      <el-form label-width="100px" style="max-width: 500px">
        <el-form-item label="当前密码">
          <el-input type="password" show-password />
        </el-form-item>
        <el-form-item label="新密码">
          <el-input type="password" show-password />
        </el-form-item>
        <el-form-item label="确认密码">
          <el-input type="password" show-password />
        </el-form-item>
        <el-form-item>
          <el-button type="primary">修改密码</el-button>
        </el-form-item>
      </el-form>
    </el-card>
  </div>
</template>

<style lang="scss" scoped>
.settings-page {
  .settings-row {
    margin-top: 20px;
  }

  .page-card {
    margin-top: 20px;
  }

  .card-header {
    display: flex;
    align-items: center;
    gap: 8px;
  }
}
</style>

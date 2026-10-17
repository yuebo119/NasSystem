<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { User, Lock, Monitor, Setting, Refresh, InfoFilled } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { useUserStore } from '@/stores/user'
import { userApi } from '@/api/user'
import http from '@/api/http'
import { getCurrentTheme, type ThemeType, setTheme } from '@/utils/theme'
import { setLocale, getLocale, localeOptions, type LocaleType } from '@/locales'

const { t } = useI18n()
const userStore = useUserStore()
const loading = ref(false)
const saving = ref(false)
const changingPassword = ref(false)

// 系统信息
const systemInfo = ref<any>(null)
const systemStatus = ref<any>(null)

// 个人设置表单
const profileForm = ref({
  displayName: '',
  email: ''
})

// 密码修改表单
const passwordForm = ref({
  currentPassword: '',
  newPassword: '',
  confirmPassword: ''
})

// 界面设置
const isDarkMode = ref(false)
const currentLocale = ref<LocaleType>('zh-CN')

// 初始化
onMounted(async () => {
  // 初始化个人信息
  profileForm.value = {
    displayName: userStore.displayName || '',
    email: userStore.user?.email || ''
  }
  
  // 初始化主题状态
  isDarkMode.value = getCurrentTheme() === 'dark'
  
  // 初始化语言
  currentLocale.value = getLocale()
  
  // 加载系统信息
  await loadSystemInfo()
})

const loadSystemInfo = async () => {
  loading.value = true
  try {
    const [infoRes, statusRes] = await Promise.all([
      http.get('/system/info'),
      http.get('/system/status')
    ]) as any[]
    
    if (infoRes.success) systemInfo.value = infoRes.data
    if (statusRes.success) systemStatus.value = statusRes.data
  } catch (e) {
    console.error('Failed to load system info', e)
  } finally {
    loading.value = false
  }
}

// 保存个人信息
const saveProfile = async () => {
  if (!userStore.user) return
  
  saving.value = true
  try {
    const response = await userApi.updateUser(userStore.user.id, {
      displayName: profileForm.value.displayName.trim() || undefined,
      email: profileForm.value.email.trim() || undefined
    })
    
    if (response.success && response.data) {
      userStore.updateUser(response.data)
      ElMessage.success(t('settings.profileSaved'))
    } else {
      ElMessage.error(response.error?.message || t('common.failed'))
    }
  } catch (e) {
    ElMessage.error(t('common.failed'))
  } finally {
    saving.value = false
  }
}

// 修改密码
const changePassword = async () => {
  if (!passwordForm.value.currentPassword) {
    ElMessage.warning(t('settings.currentPasswordRequired'))
    return
  }
  if (!passwordForm.value.newPassword) {
    ElMessage.warning(t('settings.newPasswordRequired'))
    return
  }
  if (passwordForm.value.newPassword.length < 6) {
    ElMessage.warning(t('users.passwordMinLength'))
    return
  }
  if (passwordForm.value.newPassword !== passwordForm.value.confirmPassword) {
    ElMessage.warning(t('users.passwordMismatch'))
    return
  }
  
  if (!userStore.user) return
  
  changingPassword.value = true
  try {
    const response = await userApi.changePassword(userStore.user.id, {
      currentPassword: passwordForm.value.currentPassword,
      newPassword: passwordForm.value.newPassword
    })
    
    if (response.success) {
      ElMessage.success(t('settings.passwordChanged'))
      passwordForm.value = { currentPassword: '', newPassword: '', confirmPassword: '' }
    } else {
      ElMessage.error(response.error?.message || t('common.failed'))
    }
  } catch (e) {
    ElMessage.error(t('common.failed'))
  } finally {
    changingPassword.value = false
  }
}

// 切换暗色模式
const handleThemeChange = (value: boolean) => {
  const theme: ThemeType = value ? 'dark' : 'light'
  // ITM-M7：用户主动切换需持久化（applyTheme 只改 DOM）
  setTheme(theme)
  isDarkMode.value = value
}

// 切换语言
const handleLocaleChange = (locale: LocaleType) => {
  setLocale(locale)
  currentLocale.value = locale
  ElMessage.success(t('common.success'))
}

// 格式化运行时间
const formatUptime = (uptime: string) => {
  if (!uptime) return '-'
  return uptime
}

// 格式化内存
const formatMemory = (mb: number) => {
  if (!mb) return '-'
  if (mb >= 1024) return `${(mb / 1024).toFixed(2)} GB`
  return `${mb} MB`
}

// 当前用户角色
const roleDisplay = computed(() => {
  const role = userStore.user?.role
  switch (role) {
    case 'SuperAdmin': return t('users.roles.superAdmin')
    case 'Admin': return t('users.roles.admin')
    case 'User': return t('users.roles.user')
    default: return role || '-'
  }
})
</script>

<template>
  <div class="settings-page">
    <div class="page-header">
      <div class="header-left">
        <h1>{{ t('settings.title') }}</h1>
        <p>{{ t('settings.subtitle') }}</p>
      </div>
      <div class="header-actions">
        <el-button :icon="Refresh" @click="loadSystemInfo" :loading="loading">{{ t('common.refresh') }}</el-button>
      </div>
    </div>

    <div class="settings-grid">
      <!-- 左侧：个人设置 -->
      <div class="settings-column">
        <!-- 个人信息卡片 -->
        <div class="settings-card">
          <div class="card-header">
            <div class="card-icon profile"><el-icon :size="20"><User /></el-icon></div>
            <div class="card-title">
              <h3>{{ t('settings.profile') }}</h3>
              <p>{{ t('settings.profileDesc') }}</p>
            </div>
          </div>
          <div class="card-content">
            <el-form :model="profileForm" label-position="top">
              <el-form-item :label="t('users.username')">
                <el-input :model-value="userStore.username" disabled>
                  <template #suffix>
                    <el-tag size="small" type="info">{{ t('settings.cannotModify') }}</el-tag>
                  </template>
                </el-input>
              </el-form-item>
              <el-form-item :label="t('users.role')">
                <el-input :model-value="roleDisplay" disabled />
              </el-form-item>
              <el-form-item :label="t('users.displayName')">
                <el-input v-model="profileForm.displayName" :placeholder="t('users.displayName')" maxlength="50" show-word-limit />
              </el-form-item>
              <el-form-item :label="t('users.email')">
                <el-input v-model="profileForm.email" type="email" :placeholder="t('users.email')" />
              </el-form-item>
              <el-form-item>
                <el-button type="primary" @click="saveProfile" :loading="saving">{{ t('settings.saveProfile') }}</el-button>
              </el-form-item>
            </el-form>
          </div>
        </div>

        <!-- 修改密码卡片 -->
        <div class="settings-card">
          <div class="card-header">
            <div class="card-icon security"><el-icon :size="20"><Lock /></el-icon></div>
            <div class="card-title">
              <h3>{{ t('settings.security') }}</h3>
              <p>{{ t('settings.securityDesc') }}</p>
            </div>
          </div>
          <div class="card-content">
            <el-form :model="passwordForm" label-position="top">
              <el-form-item :label="t('users.currentPassword')">
                <el-input v-model="passwordForm.currentPassword" type="password" :placeholder="t('users.currentPassword')" show-password />
              </el-form-item>
              <el-form-item :label="t('users.newPassword')">
                <el-input v-model="passwordForm.newPassword" type="password" :placeholder="t('users.passwordMinLength')" show-password />
              </el-form-item>
              <el-form-item :label="t('users.confirmPassword')">
                <el-input v-model="passwordForm.confirmPassword" type="password" :placeholder="t('users.confirmPassword')" show-password />
              </el-form-item>
              <el-form-item>
                <el-button type="primary" @click="changePassword" :loading="changingPassword">{{ t('settings.changePassword') }}</el-button>
              </el-form-item>
            </el-form>
          </div>
        </div>
      </div>

      <!-- 右侧：系统设置 -->
      <div class="settings-column">
        <!-- 界面设置卡片 -->
        <div class="settings-card">
          <div class="card-header">
            <div class="card-icon appearance"><el-icon :size="20"><Monitor /></el-icon></div>
            <div class="card-title">
              <h3>{{ t('settings.appearance') }}</h3>
              <p>{{ t('settings.appearanceDesc') }}</p>
            </div>
          </div>
          <div class="card-content">
            <div class="setting-item">
              <div class="setting-info">
                <span class="setting-label">{{ t('settings.darkMode') }}</span>
                <span class="setting-desc">{{ t('settings.darkModeDesc') }}</span>
              </div>
              <el-switch v-model="isDarkMode" @change="(val: string | number | boolean) => handleThemeChange(Boolean(val))" />
            </div>
            <div class="setting-item">
              <div class="setting-info">
                <span class="setting-label">{{ t('settings.language') }}</span>
                <span class="setting-desc">{{ t('settings.languageDesc') }}</span>
              </div>
              <el-select v-model="currentLocale" @change="handleLocaleChange" style="width: 140px">
                <el-option v-for="opt in localeOptions" :key="opt.value" :label="opt.label" :value="opt.value" />
              </el-select>
            </div>
          </div>
        </div>

        <!-- 系统信息卡片 -->
        <div class="settings-card">
          <div class="card-header">
            <div class="card-icon system"><el-icon :size="20"><Setting /></el-icon></div>
            <div class="card-title">
              <h3>{{ t('settings.system') }}</h3>
              <p>{{ t('settings.systemDesc') }}</p>
            </div>
          </div>
          <div class="card-content" v-loading="loading">
            <!-- 字段跟随 /system/info 的分组结构（2026-10-17 扩容）；
                 Dashboard 有完整八组模块，这里保留最常看的六项 -->
            <div class="info-grid" v-if="systemInfo">
              <div class="info-item">
                <span class="info-label">{{ t('settings.systemName') }}</span>
                <span class="info-value">{{ systemInfo.name }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">{{ t('settings.version') }}</span>
                <span class="info-value">{{ systemInfo.version }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">{{ t('settings.os') }}</span>
                <span class="info-value">{{ systemInfo.operatingSystem?.description || '-' }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">{{ t('settings.hostname') }}</span>
                <span class="info-value">{{ systemInfo.machineName }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">{{ t('settings.cpuCores') }}</span>
                <span class="info-value">{{ systemInfo.cpu?.logicalCores }} {{ t('dashboard.cores') }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">{{ t('settings.dotnetVersion') }}</span>
                <span class="info-value">.NET {{ systemInfo.runtime?.version }}</span>
              </div>
            </div>
            
            <el-divider v-if="systemStatus" />
            
            <div class="status-grid" v-if="systemStatus">
              <div class="status-item">
                <div class="status-icon running"><el-icon><InfoFilled /></el-icon></div>
                <div class="status-info">
                  <span class="status-label">{{ t('settings.runningStatus') }}</span>
                  <span class="status-value success">{{ t('settings.running') }}</span>
                </div>
              </div>
              <div class="status-item">
                <div class="status-icon time"><el-icon><InfoFilled /></el-icon></div>
                <div class="status-info">
                  <span class="status-label">{{ t('settings.uptime') }}</span>
                  <span class="status-value">{{ formatUptime(systemStatus.uptime) }}</span>
                </div>
              </div>
              <div class="status-item">
                <div class="status-icon memory"><el-icon><InfoFilled /></el-icon></div>
                <div class="status-info">
                  <span class="status-label">{{ t('settings.memoryUsage') }}</span>
                  <span class="status-value">{{ formatMemory(systemStatus.memoryUsageMB) }}</span>
                </div>
              </div>
              <div class="status-item">
                <div class="status-icon threads"><el-icon><InfoFilled /></el-icon></div>
                <div class="status-info">
                  <span class="status-label">{{ t('settings.threadCount') }}</span>
                  <span class="status-value">{{ systemStatus.threadCount }}</span>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- 关于卡片 -->
        <div class="settings-card about-card">
          <div class="about-content">
            <div class="about-logo">
              <el-icon :size="48" color="var(--el-color-primary)"><Setting /></el-icon>
            </div>
            <div class="about-info">
              <h3>NAS System</h3>
              <p>Windows Network Attached Storage</p>
              <div class="about-meta">
                <span>v1.0.0</span>
                <span>•</span>
                <!-- 版本取运行时真实值：原硬编码 ".NET 10" 与实际 net11.0 不符 -->
                <span>.NET {{ systemInfo?.dotNetVersion || '11' }}</span>
                <span>•</span>
                <span>Vue 3</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
.settings-page { padding: 0; }

.page-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 24px;
  .header-left { h1 { margin: 0 0 4px; font-size: 24px; font-weight: 600; } p { margin: 0; color: var(--el-text-color-secondary); font-size: 14px; } }
  .header-actions { display: flex; gap: 12px; }
}

.settings-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 24px; }
.settings-column { display: flex; flex-direction: column; gap: 24px; }

.settings-card { background: var(--el-bg-color); border-radius: 16px; border: 1px solid var(--el-border-color-lighter); overflow: hidden;
  .card-header { display: flex; align-items: center; gap: 16px; padding: 20px 24px; border-bottom: 1px solid var(--el-border-color-lighter);
    .card-icon { width: 44px; height: 44px; border-radius: 12px; display: flex; align-items: center; justify-content: center; color: white;
      &.profile { background: linear-gradient(135deg, #6366f1, #8b5cf6); }
      &.security { background: linear-gradient(135deg, #ef4444, #f87171); }
      &.appearance { background: linear-gradient(135deg, #3b82f6, #60a5fa); }
      &.system { background: linear-gradient(135deg, #10b981, #34d399); }
    }
    .card-title { h3 { margin: 0; font-size: 16px; font-weight: 600; } p { margin: 4px 0 0; font-size: 13px; color: var(--el-text-color-secondary); } }
  }
  .card-content { padding: 24px; }
}

.setting-item { display: flex; justify-content: space-between; align-items: center; padding: 16px 0; border-bottom: 1px solid var(--el-border-color-lighter);
  &:last-child { border-bottom: none; padding-bottom: 0; }
  &:first-child { padding-top: 0; }
  .setting-info { .setting-label { display: block; font-size: 14px; font-weight: 500; } .setting-desc { display: block; font-size: 12px; color: var(--el-text-color-secondary); margin-top: 2px; } }
}

.info-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.info-item { display: flex; flex-direction: column; gap: 4px;
  .info-label { font-size: 12px; color: var(--el-text-color-secondary); }
  .info-value { font-size: 14px; font-weight: 500; word-break: break-all; }
}

.status-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.status-item { display: flex; align-items: center; gap: 12px;
  .status-icon { width: 36px; height: 36px; border-radius: 8px; display: flex; align-items: center; justify-content: center;
    &.running { background: rgba(16, 185, 129, 0.1); color: #10b981; }
    &.time { background: rgba(59, 130, 246, 0.1); color: #3b82f6; }
    &.memory { background: rgba(245, 158, 11, 0.1); color: #f59e0b; }
    &.threads { background: rgba(139, 92, 246, 0.1); color: #8b5cf6; }
  }
  .status-info { .status-label { display: block; font-size: 12px; color: var(--el-text-color-secondary); } .status-value { display: block; font-size: 14px; font-weight: 500; &.success { color: #10b981; } } }
}

.about-card {
  .about-content { display: flex; align-items: center; gap: 20px; padding: 24px;
    .about-logo { width: 80px; height: 80px; border-radius: 20px; background: linear-gradient(135deg, rgba(99, 102, 241, 0.1), rgba(139, 92, 246, 0.1)); display: flex; align-items: center; justify-content: center; }
    .about-info { h3 { margin: 0 0 4px; font-size: 20px; font-weight: 600; } p { margin: 0 0 8px; color: var(--el-text-color-secondary); font-size: 14px; }
      .about-meta { display: flex; gap: 8px; font-size: 12px; color: var(--el-text-color-secondary); }
    }
  }
}

@media (max-width: 1024px) { .settings-grid { grid-template-columns: 1fr; } }
</style>

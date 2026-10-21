<script setup lang="ts">
/**
 * 个人设置页（2026-10-19 从"系统设置"拆分后只保留个人项）。
 *
 * 原页面把个人设置与系统管理（系统信息/外网访问）混在一列，
 * 系统项已迁至 /system-info（仅管理员）。本页对所有登录用户开放：
 * 个人资料、登录密码、界面外观、关于。
 */
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { User, Lock, Monitor, Setting, PieChart } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { useUserStore } from '@/stores/user'
import { userApi } from '@/api/user'
import { getCurrentTheme, type ThemeType, setTheme } from '@/utils/theme'
import { setLocale, getLocale, localeOptions, type LocaleType } from '@/locales'
import { formatSize } from '@/utils/format'

const { t } = useI18n()
const userStore = useUserStore()
const saving = ref(false)
const changingPassword = ref(false)

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
onMounted(() => {
  // 初始化个人信息
  profileForm.value = {
    displayName: userStore.displayName || '',
    email: userStore.user?.email || ''
  }

  // 初始化主题状态
  isDarkMode.value = getCurrentTheme() === 'dark'

  // 初始化语言
  currentLocale.value = getLocale()
})

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

// 个人存储配额（数据来自 users/me：quotaBytes/usedBytes/usagePercentage）。
// 无配额（quotaBytes 为空或 0）时只展示已用空间，不出进度条
const quotaUsed = computed(() => userStore.user?.usedBytes ?? 0)
const quotaLimit = computed(() => {
  const q = userStore.user?.quotaBytes
  return q && q > 0 ? q : null
})
const quotaPercentage = computed(() => userStore.user?.usagePercentage ?? 0)
// 进度条语义色：≥90% 红、≥70% 橙、其余绿（与全站 danger/warning/success 对齐）
const quotaProgressColor = computed(() => {
  if (quotaPercentage.value >= 90) return 'var(--el-color-danger)'
  if (quotaPercentage.value >= 70) return 'var(--el-color-warning)'
  return 'var(--el-color-success)'
})
</script>

<template>
  <div class="settings-page">
    <div class="page-header page-header--with-actions">
      <div class="header-left">
        <h1>{{ t('settings.title') }}</h1>
        <p>{{ t('settings.subtitle') }}</p>
      </div>
    </div>

    <div class="settings-grid">
      <!-- 左列：账号 -->
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

        <!-- 存储配额卡片（2026-10-21 补齐：用户看不到自己的配额用量） -->
        <div class="settings-card">
          <div class="card-header">
            <div class="card-icon quota"><el-icon :size="20"><PieChart /></el-icon></div>
            <div class="card-title">
              <h3>{{ t('settings.quotaTitle') }}</h3>
              <p>{{ t('settings.quotaDesc') }}</p>
            </div>
          </div>
          <div class="card-content">
            <div class="quota-summary">
              <span class="quota-item">
                {{ t('settings.quotaUsed') }}
                <strong>{{ formatSize(quotaUsed) }}</strong>
              </span>
              <span class="quota-item" v-if="quotaLimit">
                {{ t('settings.quotaLimit') }}
                <strong>{{ formatSize(quotaLimit) }}</strong>
              </span>
              <el-tag v-else type="info" size="small">{{ t('settings.quotaUnlimited') }}</el-tag>
            </div>
            <el-progress
              v-if="quotaLimit"
              :percentage="Math.min(100, quotaPercentage)"
              :color="quotaProgressColor"
              :stroke-width="10"
            />
            <div v-else class="quota-hint">{{ t('settings.quotaUnlimitedHint') }}</div>
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

      <!-- 右列：偏好与关于 -->
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
                <span>.NET 11</span>
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



.settings-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 24px; }
.settings-column { display: flex; flex-direction: column; gap: 24px; }

.settings-card { background: var(--el-bg-color); border-radius: var(--mac-radius-container); border: 1px solid var(--el-border-color-lighter); overflow: hidden;
  .card-header { display: flex; align-items: center; gap: 16px; padding: 20px 24px; border-bottom: 1px solid var(--el-border-color-lighter);
    .card-icon { width: 44px; height: 44px; border-radius: var(--mac-radius-card); display: flex; align-items: center; justify-content: center; color: white;
      &.profile { background: linear-gradient(135deg, var(--el-color-primary), var(--el-color-primary-light-3)); }
      &.security { background: linear-gradient(135deg, var(--el-color-danger), var(--el-color-danger-light-3)); }
      &.appearance { background: linear-gradient(135deg, var(--el-color-success), var(--el-color-success-light-3)); }
      &.quota { background: linear-gradient(135deg, var(--el-color-warning), var(--el-color-warning-light-3)); }
    }
    .card-title { h3 { margin: 0; font-size: 16px; font-weight: 600; } p { margin: 4px 0 0; font-size: 13px; color: var(--el-text-color-secondary); } }
  }
  .card-content { padding: 24px; }

  .quota-summary { display: flex; align-items: center; gap: 16px; flex-wrap: wrap; margin-bottom: 16px;
    .quota-item { font-size: 13px; color: var(--el-text-color-secondary);
      strong { display: block; font-size: 18px; color: var(--el-text-color-primary); margin-top: 2px; }
    }
  }

  .quota-hint { font-size: 12px; color: var(--el-text-color-secondary); }
}

.setting-item { display: flex; justify-content: space-between; align-items: center; padding: 16px 0; border-bottom: 1px solid var(--el-border-color-lighter);
  &:last-child { border-bottom: none; padding-bottom: 0; }
  &:first-child { padding-top: 0; }
  .setting-info { .setting-label { display: block; font-size: 14px; font-weight: 500; } .setting-desc { display: block; font-size: 12px; color: var(--el-text-color-secondary); margin-top: 2px; } }
}

.about-card {
  .about-content { display: flex; align-items: center; gap: 24px; padding: 24px;
    .about-logo { width: 80px; height: 80px; border-radius: var(--mac-radius-container); background: linear-gradient(135deg, rgba(99, 102, 241, 0.1), rgba(139, 92, 246, 0.1)); display: flex; align-items: center; justify-content: center; }
    .about-info { h3 { margin: 0 0 4px; font-size: 16px; font-weight: 600; } p { margin: 0 0 8px; color: var(--el-text-color-secondary); font-size: 14px; }
      .about-meta { display: flex; gap: 8px; font-size: 12px; color: var(--el-text-color-secondary); }
    }
  }
}

@media (max-width: 1024px) { .settings-grid { grid-template-columns: 1fr; } }
</style>

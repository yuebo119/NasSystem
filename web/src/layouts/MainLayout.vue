<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useUserStore } from '@/stores/user'
import { ElMessageBox, ElMessage } from 'element-plus'
import { UserFilled, SwitchButton, Box, Moon, Sunny, Fold, Expand, Odometer, Folder, Share, Setting as SettingIcon, User as UserIcon } from '@element-plus/icons-vue'
import { getCurrentTheme, type ThemeType, setTheme } from '@/utils/theme'

const { t } = useI18n()
const router = useRouter()
const route = useRoute()
const userStore = useUserStore()

// 主题状态
const isDarkMode = ref(false)

// 侧边栏菜单
const menuItems = [
  { path: '/dashboard', titleKey: 'nav.dashboard', icon: Odometer },
  { path: '/files', titleKey: 'nav.files', icon: Folder },
  { path: '/shares', titleKey: 'nav.shares', icon: Share },
  { path: '/storage', titleKey: 'nav.storage', icon: Box },
  { path: '/users', titleKey: 'nav.users', icon: UserIcon, admin: true },
  { path: '/settings', titleKey: 'nav.settings', icon: SettingIcon }
]

// 过滤管理员菜单
const visibleMenuItems = computed(() =>
  menuItems.filter(item => !item.admin || userStore.isAdmin)
)

// 当前选中的菜单
const activeMenu = computed(() => route.path)

// 侧边栏折叠
const isCollapsed = ref(false)

// 切换折叠状态
function toggleCollapse() {
  isCollapsed.value = !isCollapsed.value
}

// 切换主题
function toggleTheme() {
  isDarkMode.value = !isDarkMode.value
  const theme: ThemeType = isDarkMode.value ? 'dark' : 'light'
  // ITM-M7：用户主动切换需持久化（applyTheme 只改 DOM）
  setTheme(theme)
}

// 监听主题变化
function handleThemeChange() {
  isDarkMode.value = getCurrentTheme() === 'dark'
}

onMounted(() => {
  isDarkMode.value = getCurrentTheme() === 'dark'
  // 监听 storage 事件以同步主题
  window.addEventListener('storage', handleThemeChange)
})

onUnmounted(() => {
  window.removeEventListener('storage', handleThemeChange)
})

// 退出登录
async function handleLogout() {
  try {
    await ElMessageBox.confirm(t('common.confirm') + '?', t('common.warning'), {
      confirmButtonText: t('common.confirm'),
      cancelButtonText: t('common.cancel'),
      type: 'warning'
    })
    
    await userStore.logout()
    router.push('/login')
    ElMessage.success(t('common.success'))
  } catch {
    // 取消操作
  }
}
</script>

<template>
  <el-container class="main-layout" :class="{ 'dark-mode': isDarkMode }">
    <!-- 侧边栏 -->
    <el-aside :width="isCollapsed ? '72px' : '240px'" class="sidebar">
      <!-- Logo 区域 -->
      <div class="sidebar-header">
        <div class="logo-wrapper">
          <div class="logo-icon">
            <el-icon size="24"><Box /></el-icon>
          </div>
          <transition name="fade-text">
            <span v-if="!isCollapsed" class="logo-text">NAS System</span>
          </transition>
        </div>
      </div>

      <!-- 菜单区域 -->
      <div class="sidebar-menu-wrapper">
        <el-menu
          :default-active="activeMenu"
          :collapse="isCollapsed"
          :collapse-transition="false"
          router
          class="sidebar-menu"
        >
          <el-menu-item
            v-for="item in visibleMenuItems"
            :key="item.path"
            :index="item.path"
            class="menu-item"
          >
            <el-icon class="menu-icon"><component :is="item.icon" /></el-icon>
            <template #title>
              <span class="menu-title">{{ t(item.titleKey) }}</span>
            </template>
          </el-menu-item>
        </el-menu>
      </div>

      <!-- 底部操作区 -->
      <div class="sidebar-footer">
        <!-- 主题切换 -->
        <div class="theme-toggle" @click="toggleTheme" :title="isDarkMode ? t('settings.lightMode') : t('settings.darkMode')">
          <el-icon size="18">
            <Moon v-if="!isDarkMode" />
            <Sunny v-else />
          </el-icon>
          <transition name="fade-text">
            <span v-if="!isCollapsed" class="toggle-text">{{ isDarkMode ? t('settings.lightMode') : t('settings.darkMode') }}</span>
          </transition>
        </div>

        <!-- 折叠按钮 -->
        <div class="collapse-toggle" @click="toggleCollapse">
          <el-icon size="18">
            <Expand v-if="isCollapsed" />
            <Fold v-else />
          </el-icon>
          <transition name="fade-text">
            <span v-if="!isCollapsed" class="toggle-text">{{ t('nav.collapse') }}</span>
          </transition>
        </div>

        <!-- 用户信息 -->
        <el-dropdown trigger="click" @command="handleLogout" class="user-dropdown">
          <div class="user-info">
            <el-avatar size="small" :icon="UserFilled" class="user-avatar" />
            <transition name="fade-text">
              <div v-if="!isCollapsed" class="user-details">
                <span class="username">{{ userStore.displayName }}</span>
                <span class="user-role">{{ userStore.isAdmin ? 'Admin' : 'User' }}</span>
              </div>
            </transition>
          </div>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item command="logout">
                <el-icon><SwitchButton /></el-icon>
                {{ t('nav.logout') }}
              </el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
      </div>
    </el-aside>

    <!-- 主内容区 -->
    <el-main class="main-content">
      <router-view v-slot="{ Component }">
        <transition name="fade" mode="out-in">
          <component :is="Component" />
        </transition>
      </router-view>
    </el-main>
  </el-container>
</template>

<style lang="scss" scoped>
.main-layout {
  height: 100vh;
  overflow: hidden;
}

// 侧边栏
.sidebar {
  display: flex;
  flex-direction: column;
  background: linear-gradient(180deg, #1e293b 0%, #0f172a 100%);
  transition: width 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  overflow: hidden;
  border-right: 1px solid rgba(255, 255, 255, 0.06);
  box-shadow: 4px 0 24px rgba(0, 0, 0, 0.15);
}

// 暗色模式下的侧边栏
.dark-mode .sidebar {
  background: linear-gradient(180deg, #111827 0%, #030712 100%);
  border-right-color: rgba(255, 255, 255, 0.04);
}

// Logo 区域
.sidebar-header {
  height: 64px;
  display: flex;
  align-items: center;
  padding: 0 16px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);

  .logo-wrapper {
    display: flex;
    align-items: center;
    gap: 12px;
    width: 100%;
  }

  .logo-icon {
    width: 40px;
    height: 40px;
    border-radius: 12px;
    background: linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%);
    display: flex;
    align-items: center;
    justify-content: center;
    color: #fff;
    flex-shrink: 0;
    box-shadow: 0 4px 12px rgba(99, 102, 241, 0.4);
  }

  .logo-text {
    font-size: 18px;
    font-weight: 700;
    color: #fff;
    white-space: nowrap;
    letter-spacing: -0.5px;
  }
}

// 菜单区域
.sidebar-menu-wrapper {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 12px 8px;

  &::-webkit-scrollbar {
    width: 4px;
  }

  &::-webkit-scrollbar-thumb {
    background: rgba(255, 255, 255, 0.1);
    border-radius: 2px;
  }
}

.sidebar-menu {
  border-right: none;
  background: transparent;

  :deep(.el-menu-item) {
    height: 44px;
    line-height: 44px;
    margin: 4px 0;
    border-radius: 10px;
    color: rgba(255, 255, 255, 0.65);
    transition: all 0.2s ease;

    .menu-icon {
      font-size: 18px;
      margin-right: 12px;
    }

    .menu-title {
      font-size: 14px;
      font-weight: 500;
    }

    &:hover {
      background: rgba(255, 255, 255, 0.08);
      color: #fff;
    }

    &.is-active {
      background: linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%);
      color: #fff;
      box-shadow: 0 4px 12px rgba(99, 102, 241, 0.35);

      .menu-icon {
        color: #fff;
      }
    }
  }

  // 折叠状态
  &.el-menu--collapse {
    :deep(.el-menu-item) {
      padding: 0 !important;
      justify-content: center;

      .menu-icon {
        margin-right: 0;
      }
    }
  }
}

// 底部操作区
.sidebar-footer {
  padding: 12px 8px;
  border-top: 1px solid rgba(255, 255, 255, 0.06);
  display: flex;
  flex-direction: column;
  gap: 4px;
}

// 主题切换和折叠按钮
.theme-toggle,
.collapse-toggle {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  border-radius: 10px;
  cursor: pointer;
  color: rgba(255, 255, 255, 0.65);
  transition: all 0.2s ease;

  &:hover {
    background: rgba(255, 255, 255, 0.08);
    color: #fff;
  }

  .toggle-text {
    font-size: 13px;
    font-weight: 500;
    white-space: nowrap;
  }
}

// 用户信息
.user-dropdown {
  width: 100%;
}

.user-info {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  border-radius: 10px;
  cursor: pointer;
  color: rgba(255, 255, 255, 0.85);
  transition: all 0.2s ease;

  &:hover {
    background: rgba(255, 255, 255, 0.08);
  }

  .user-avatar {
    background: linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%);
    flex-shrink: 0;
  }

  .user-details {
    display: flex;
    flex-direction: column;
    min-width: 0;
    flex: 1;

    .username {
      font-size: 14px;
      font-weight: 600;
      color: #fff;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .user-role {
      font-size: 11px;
      color: rgba(255, 255, 255, 0.5);
      margin-top: 1px;
    }
  }
}

// 主内容区
.main-content {
  background: var(--el-bg-color-page);
  overflow: auto;
  padding: 24px;
  transition: background-color 0.3s ease;
}

// 暗色模式主内容区
.dark-mode .main-content {
  background: #0f172a;
}

// 文字淡入淡出动画
.fade-text-enter-active,
.fade-text-leave-active {
  transition: opacity 0.15s ease;
}

.fade-text-enter-from,
.fade-text-leave-to {
  opacity: 0;
}

// 页面切换动画
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>

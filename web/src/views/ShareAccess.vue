<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  Download, Lock, Warning, User, ArrowLeft,
  Clock, View, Link as LinkIcon, Folder, FolderOpened
} from '@element-plus/icons-vue'
import { publicShareApi, authenticatedShareApi, ShareAccessType, type PublicShareInfoDto, type ShareFolderContentDto, type ShareFileItemDto } from '@/api/fileShare'
import { getFileIcon, getFileIconColor } from '@/utils/format'
import { useUserStore } from '@/stores/user'

const route = useRoute()
// ITM-M3 收尾：对外访客页接入 i18n（外部用户此前无法切换语言）
const { t } = useI18n()
// 登录态决定 Internal 型分享的通道：已登录走认证端点（ITM-N23），
// 未登录落登录引导。userStore 在分享页是只读消费，不触发初始化拉取
const userStore = useUserStore()
const shareCode = computed(() => route.params.shareCode as string)

// Internal 型且已登录：认证端点放行（密码型仍要求 accessToken，认证不豁免密码）
const isLoggedIn = computed(() => userStore.isAuthenticated)

// 状态
const loading = ref(true)
const shareInfo = ref<PublicShareInfoDto | null>(null)
const error = ref('')
const passwordInput = ref('')
const verifying = ref(false)
const accessToken = ref('')
const folderContent = ref<ShareFolderContentDto | null>(null)
const currentPath = ref('')
const downloading = ref(false)

// 加载分享信息
const loadShareInfo = async () => {
  loading.value = true
  error.value = ''
  try {
    const result = await publicShareApi.getInfo(shareCode.value)
    if (result.data) {
      shareInfo.value = result.data
      // 文件夹只在确实可访问时预载内容：Internal 未登录落登录引导、
      // 密码未验证落密码态，两者都不该提前打文件夹端点（会报错且无意义）
      if (result.data.isAccessible && result.data.isFolder && canAccess.value) {
        await loadFolderContent()
      }
    }
  } catch (e: any) {
    error.value = e.message || t('shareAccess.notFound')
  } finally {
    loading.value = false
  }
}

// 验证密码
const verifyPassword = async () => {
  if (!passwordInput.value) {
    ElMessage.warning(t('shareAccess.enterPassword'))
    return
  }
  verifying.value = true
  try {
    const result = await publicShareApi.verifyPassword(shareCode.value, passwordInput.value)
    if (result.data?.accessToken) {
      accessToken.value = result.data.accessToken
      ElMessage.success(t('shareAccess.verifySuccess'))
      if (shareInfo.value?.isFolder) {
        await loadFolderContent()
      }
    }
  } catch (e: any) {
    ElMessage.error(e.message || t('shareAccess.wrongPassword'))
  } finally {
    verifying.value = false
  }
}

// 加载文件夹内容
const loadFolderContent = async (subPath?: string) => {
  try {
    // Internal 型已登录用户走认证端点；其余（公开/密码）走公开端点
    const result = isInternalAuthenticated.value
      ? await authenticatedShareApi.getFolderContent(shareCode.value, subPath)
      : await publicShareApi.getFolderContent(shareCode.value, accessToken.value || undefined, subPath)
    if (result.data) {
      folderContent.value = result.data
      currentPath.value = subPath || ''
    }
  } catch (e: any) {
    ElMessage.error(t('shareAccess.loadFolderFailed'))
  }
}

// 下载文件名：子路径取末段，否则取分享自身的文件名。
// blob 下载必须显式给 download 属性——留空时浏览器拿 blob UUID
// 当文件名（实测存成 "2f04b985-....txt"）
const downloadFileName = (subPath?: string): string => {
  if (subPath) return subPath.split('/').pop() || subPath
  return shareInfo.value?.fileName || 'download'
}

// 下载文件
const downloadFile = async (subPath?: string) => {
  downloading.value = true
  try {
    // 两条路径都走 fetch + blob：<a href> 原生导航不可观测，token 失效或
    // 分享已删时页面内零反馈，loading 只能靠定时器假装结束（2026-10-21
    // 审计 P2）。fetch 后失败进 catch，loading 随真实请求收场
    const blob = isInternalAuthenticated.value
      ? await authenticatedShareApi.download(shareCode.value, subPath)
      : await publicShareApi.download(shareCode.value, accessToken.value || undefined, subPath)
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = downloadFileName(subPath)
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    URL.revokeObjectURL(url)
  } catch (e: any) {
    ElMessage.error(e.message || t('shareAccess.downloadFailed'))
  } finally {
    downloading.value = false
  }
}

// 进入子目录
const enterFolder = (item: ShareFileItemDto) => {
  if (item.isFolder) {
    const newPath = currentPath.value ? `${currentPath.value}/${item.name}` : item.name
    loadFolderContent(newPath)
  }
}

// 返回上级
const goBack = () => {
  if (!currentPath.value) return
  const parts = currentPath.value.split('/')
  parts.pop()
  loadFolderContent(parts.join('/') || undefined)
}

// Internal 型已登录：认证通道（Bearer 自动带上，密码型不加 token 仍会被后端要求密码）
const isInternalAuthenticated = computed(() =>
  !!shareInfo.value &&
  shareInfo.value.accessType === ShareAccessType.Internal &&
  isLoggedIn.value
)

// 是否可以访问
const canAccess = computed(() => {
  if (!shareInfo.value) return false
  if (shareInfo.value.accessType === ShareAccessType.Public) return true
  if (shareInfo.value.accessType === ShareAccessType.Password && accessToken.value) return true
  if (shareInfo.value.accessType === ShareAccessType.Internal && isLoggedIn.value) return true
  return false
})

// 面包屑路径
const breadcrumbs = computed(() => {
  if (!currentPath.value) return []
  return currentPath.value.split('/')
})

onMounted(() => {
  loadShareInfo()
})
</script>

<template>
  <div class="share-page">
    <div class="share-wrapper">
      <!-- Logo -->
      <div class="brand">
        <div class="brand-icon">
          <el-icon :size="24"><LinkIcon /></el-icon>
        </div>
        <span>NAS Share</span>
      </div>

      <!-- 主卡片 -->
      <div class="share-card">
        <!-- 加载中 -->
        <div v-if="loading" class="state-container loading-state">
          <div class="spinner"></div>
          <p>{{ $t('shareAccess.loading') }}</p>
        </div>

        <!-- 错误状态 -->
        <div v-else-if="error" class="state-container error-state">
          <div class="state-icon error">
            <el-icon :size="48"><Warning /></el-icon>
          </div>
          <h2>{{ $t('shareAccess.cannotAccess') }}</h2>
          <p>{{ error }}</p>
          <el-button type="primary" plain @click="loadShareInfo">{{ $t('shareAccess.retry') }}</el-button>
        </div>

        <!-- 已过期 -->
        <div v-else-if="shareInfo?.isExpired" class="state-container error-state">
          <div class="state-icon warning">
            <el-icon :size="48"><Clock /></el-icon>
          </div>
          <h2>{{ $t('shareAccess.expiredTitle') }}</h2>
          <p>{{ $t('shareAccess.expiredDesc') }}</p>
        </div>

        <!-- 下载限制已达 -->
        <div v-else-if="shareInfo?.isDownloadLimitReached" class="state-container error-state">
          <div class="state-icon warning">
            <el-icon :size="48"><View /></el-icon>
          </div>
          <h2>{{ $t('shareAccess.downloadLimitTitle') }}</h2>
          <p>{{ $t('shareAccess.downloadLimitDesc') }}</p>
        </div>

        <!-- 需要密码 -->
        <div v-else-if="shareInfo?.requiresPassword && !accessToken" class="state-container password-state">
          <div class="file-icon-wrapper">
            <el-icon :size="56" :color="getFileIconColor(shareInfo)">
              <component :is="getFileIcon(shareInfo)" />
            </el-icon>
          </div>
          <h2 class="file-name">{{ shareInfo.fileName }}</h2>
          <p class="file-size" v-if="shareInfo.sizeDisplay">{{ shareInfo.sizeDisplay }}</p>
          
          <div class="password-section">
            <div class="lock-badge">
              <el-icon :size="20"><Lock /></el-icon>
              <span>{{ $t('shareAccess.passwordRequired') }}</span>
            </div>
            <el-input 
              v-model="passwordInput" 
              type="password" 
              :placeholder="$t('shareAccess.passwordPlaceholder')"
              size="large"
              @keyup.enter="verifyPassword"
              show-password
              class="password-input"
            />
            <el-button 
              type="primary" 
              size="large" 
              @click="verifyPassword" 
              :loading="verifying"
              class="verify-btn"
            >
              {{ verifying ? $t('shareAccess.verifying') : $t('shareAccess.verify') }}
            </el-button>
          </div>
          
          <div class="share-info" v-if="shareInfo.createdByUserName">
            <el-icon><User /></el-icon>
            <span>{{ $t('shareAccess.sharedBy', { name: shareInfo.createdByUserName }) }}</span>
          </div>
        </div>

        <!-- 需要登录（仅未登录时展示；已登录的 Internal 分享落可访问分支） -->
        <div v-else-if="shareInfo?.accessType === ShareAccessType.Internal && !isLoggedIn" class="state-container login-state">
          <div class="file-icon-wrapper">
            <el-icon :size="56" :color="getFileIconColor(shareInfo)">
              <component :is="getFileIcon(shareInfo)" />
            </el-icon>
          </div>
          <h2 class="file-name">{{ shareInfo.fileName }}</h2>
          <p class="file-size" v-if="shareInfo.sizeDisplay">{{ shareInfo.sizeDisplay }}</p>
          
          <div class="login-section">
            <div class="lock-badge internal">
              <el-icon :size="20"><User /></el-icon>
              <span>{{ $t('shareAccess.internalOnly') }}</span>
            </div>
            <p class="login-hint">{{ $t('shareAccess.loginRequiredHint') }}</p>
            <!-- 带 redirect 回分享页：登录成功后回到原分享链接，
                 不带则落 dashboard，访客要找回到链接（E2E 走查实证） -->
            <el-button type="primary" size="large" @click="$router.push({ path: '/login', query: { redirect: $route.fullPath } })">
              {{ $t('shareAccess.loginSystem') }}
            </el-button>
          </div>
        </div>

        <!-- 可访问 - 单文件 -->
        <div v-else-if="shareInfo && canAccess && !shareInfo.isFolder" class="state-container file-state">
          <div class="file-icon-wrapper large">
            <el-icon :size="72" :color="getFileIconColor(shareInfo)">
              <component :is="getFileIcon(shareInfo)" />
            </el-icon>
          </div>
          <h2 class="file-name">{{ shareInfo.fileName }}</h2>
          <p class="file-size" v-if="shareInfo.sizeDisplay">{{ $t('shareAccess.fileSizeLabel') }}: {{ shareInfo.sizeDisplay }}</p>
          
          <el-button 
            type="primary" 
            size="large" 
            @click="downloadFile()"
            :loading="downloading"
            class="download-btn"
          >
            <el-icon v-if="!downloading"><Download /></el-icon>
            {{ downloading ? $t('shareAccess.preparingDownload') : $t('shareAccess.downloadNow') }}
          </el-button>
          
          <div class="share-info" v-if="shareInfo.createdByUserName">
            <el-icon><User /></el-icon>
            <span>{{ $t('shareAccess.sharedBy', { name: shareInfo.createdByUserName }) }}</span>
          </div>
        </div>

        <!-- 可访问 - 文件夹 -->
        <div v-else-if="shareInfo && canAccess && shareInfo.isFolder" class="folder-state">
          <div class="folder-header">
            <div class="folder-title">
              <el-icon :size="28" color="#ffc107"><Folder /></el-icon>
              <div class="title-text">
                <h2>{{ shareInfo.fileName }}</h2>
                <div class="breadcrumb" v-if="currentPath">
                  <span class="root" @click="loadFolderContent()">{{ $t('shareAccess.rootDir') }}</span>
                  <template v-for="(crumb, index) in breadcrumbs" :key="index">
                    <span class="separator">/</span>
                    <span 
                      class="crumb" 
                      :class="{ active: index === breadcrumbs.length - 1 }"
                      @click="index < breadcrumbs.length - 1 && loadFolderContent(breadcrumbs.slice(0, index + 1).join('/'))"
                    >
                      {{ crumb }}
                    </span>
                  </template>
                </div>
              </div>
            </div>
            <div class="header-actions">
              <el-button type="primary" @click="downloadFile(currentPath || undefined)" :loading="downloading">
                <el-icon><Download /></el-icon>
                {{ $t('shareAccess.downloadZip') }}
              </el-button>
              <el-button v-if="currentPath" @click="goBack" :icon="ArrowLeft">
                {{ $t('shareAccess.back') }}
              </el-button>
            </div>
          </div>
          
          <div class="folder-content" v-if="folderContent">
            <div 
              v-for="item in folderContent.items" 
              :key="item.id" 
              class="file-item"
              @click="item.isFolder ? enterFolder(item) : null"
              :class="{ clickable: item.isFolder }"
            >
              <div class="item-icon">
                <el-icon :size="24" :color="getFileIconColor(item)">
                  <component :is="getFileIcon(item)" />
                </el-icon>
              </div>
              <div class="item-details">
                <span class="item-name">{{ item.name }}</span>
                <span class="item-meta">{{ item.isFolder ? $t('shareAccess.folder') : item.sizeDisplay }}</span>
              </div>
              <el-button 
                v-if="!item.isFolder" 
                type="primary"
                size="small"
                plain
                @click.stop="downloadFile(currentPath ? `${currentPath}/${item.name}` : item.name)"
              >
                <el-icon><Download /></el-icon>
                {{ $t('shareAccess.download') }}
              </el-button>
              <el-icon v-else class="arrow-icon"><ArrowLeft style="transform: rotate(180deg)" /></el-icon>
            </div>
            
            <div v-if="folderContent.items.length === 0" class="empty-folder">
              <el-icon :size="48" color="#dcdfe6"><FolderOpened /></el-icon>
              <p>{{ $t('shareAccess.emptyFolder') }}</p>
            </div>
          </div>
          
          <div class="share-info" v-if="shareInfo.createdByUserName">
            <el-icon><User /></el-icon>
            <span>{{ $t('shareAccess.sharedBy', { name: shareInfo.createdByUserName }) }}</span>
          </div>
        </div>
      </div>

      <!-- 底部 -->
      <div class="footer">
        <span>Powered by NAS System</span>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
// 对外访客页同样纳入 mac 设计语言：Apple 灰画布 + 居中圆角卡片。
// 原深色渐变与彩色光斑全部移除，仅保留极淡的径向提亮
.share-page {
  min-height: 100vh;
  background: var(--el-bg-color-page);
  background-image: radial-gradient(ellipse 80% 60% at 50% 30%, var(--el-fill-color-light), transparent);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px;
  position: relative;
  overflow: hidden;
}

// 原背景装饰圆移除：mac 风不做彩色光斑，层次由阴影与毛玻璃承担

.share-wrapper {
  width: 100%;
  max-width: 520px;
  position: relative;
  z-index: 1;
}

.brand {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  margin-bottom: 24px;
  color: var(--el-text-color-primary);
  font-size: 18px;
  font-weight: 500;

  .brand-icon {
    width: 40px;
    height: 40px;
    background: linear-gradient(135deg, var(--el-color-primary), var(--el-color-primary-light-3));
    border-radius: 10px;
    display: flex;
    align-items: center;
    justify-content: center;
    color: white;
  }
}

.share-card {
  background: var(--el-bg-color);
  border-radius: 16px;
  box-shadow: var(--card-shadow);
  border: 1px solid var(--mac-hairline);
  overflow: hidden;
}

.state-container {
  padding: 48px 40px;
  text-align: center;
}

// 加载状态
.loading-state {
  .spinner {
    width: 48px;
    height: 48px;
    border: 3px solid #e5e7eb;
    border-top-color: #6366f1;
    border-radius: 50%;
    animation: spin 0.8s linear infinite;
    margin: 0 auto 20px;
  }
  
  p {
    color: #6b7280;
    margin: 0;
  }
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

// 错误状态
.error-state {
  .state-icon {
    width: 88px;
    height: 88px;
    border-radius: 50%;
    display: flex;
    align-items: center;
    justify-content: center;
    margin: 0 auto 20px;
    
    &.error {
      background: linear-gradient(135deg, #fef2f2, #fee2e2);
      color: #ef4444;
    }
    
    &.warning {
      background: linear-gradient(135deg, #fffbeb, #fef3c7);
      color: #f59e0b;
    }
  }
  
  h2 {
    margin: 0 0 8px;
    font-size: 20px;
    color: #1f2937;
  }
  
  p {
    margin: 0 0 24px;
    color: #6b7280;
  }
}

// 文件图标
.file-icon-wrapper {
  width: 100px;
  height: 100px;
  background: linear-gradient(135deg, #f3f4f6, #e5e7eb);
  border-radius: 20px;
  display: flex;
  align-items: center;
  justify-content: center;
  margin: 0 auto 20px;
  
  &.large {
    width: 120px;
    height: 120px;
    border-radius: 24px;
  }
}

.file-name {
  margin: 0 0 8px;
  font-size: 20px;
  font-weight: 600;
  color: #1f2937;
  word-break: break-all;
  line-height: 1.4;
}

.file-size {
  margin: 0 0 24px;
  color: #6b7280;
  font-size: 14px;
}

// 密码区域
.password-section {
  margin-top: 8px;
  
  .lock-badge {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 8px 16px;
    background: linear-gradient(135deg, #fef3c7, #fde68a);
    color: #92400e;
    border-radius: 20px;
    font-size: 13px;
    font-weight: 500;
    margin-bottom: 20px;
  }
  
  .password-input {
    margin-bottom: 16px;
    
    :deep(.el-input__wrapper) {
      border-radius: 12px;
      padding: 4px 16px;
    }
  }
  
  .verify-btn {
    width: 100%;
    border-radius: 12px;
    height: 48px;
    font-size: 16px;
  }
}

// 登录区域
.login-section {
  .lock-badge.internal {
    background: linear-gradient(135deg, #dbeafe, #bfdbfe);
    color: #1e40af;
  }
  
  .login-hint {
    margin: 20px 0;
    color: #6b7280;
  }
  
  .el-button {
    border-radius: 12px;
    height: 48px;
    padding: 0 48px;
    font-size: 16px;
  }
}

// 文件下载
.file-state {
  .download-btn {
    width: 200px;
    height: 52px;
    border-radius: 14px;
    font-size: 16px;
    font-weight: 500;
    background: linear-gradient(135deg, #6366f1, #8b5cf6);
    border: none;
    
    &:hover {
      background: linear-gradient(135deg, #4f46e5, #7c3aed);
    }
  }
}

// 分享信息
.share-info {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  margin-top: 28px;
  padding-top: 20px;
  border-top: 1px solid #e5e7eb;
  color: #9ca3af;
  font-size: 13px;
}

// 文件夹状态
.folder-state {
  padding: 0;
  
  .folder-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 24px 28px;
    border-bottom: 1px solid var(--el-border-color-lighter);
    background: var(--el-fill-color-lighter);
    gap: 16px;
    flex-wrap: wrap;
    
    .folder-title {
      display: flex;
      align-items: center;
      gap: 14px;
      
      .title-text {
        h2 {
          margin: 0;
          font-size: 18px;
          color: #1f2937;
        }
        
        .breadcrumb {
          margin-top: 4px;
          font-size: 12px;
          color: #9ca3af;
          
          .root, .crumb {
            cursor: pointer;
            
            &:hover:not(.active) {
              color: #6366f1;
            }
          }
          
          .active {
            color: #6b7280;
            cursor: default;
          }
          
          .separator {
            margin: 0 4px;
          }
        }
      }
    }
    
    .header-actions {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }
  }
  
  .folder-content {
    max-height: 400px;
    overflow-y: auto;
    padding: 12px;
    
    .file-item {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 14px 16px;
      border-radius: 12px;
      transition: all 0.2s;
      
      &.clickable {
        cursor: pointer;
        
        &:hover {
          background: var(--el-fill-color-light);
        }
      }
      
      .item-icon {
        width: 44px;
        height: 44px;
        background: var(--el-fill-color-light);
        border-radius: 10px;
        display: flex;
        align-items: center;
        justify-content: center;
        flex-shrink: 0;
      }
      
      .item-details {
        flex: 1;
        min-width: 0;
        
        .item-name {
          display: block;
          font-weight: 500;
          color: #1f2937;
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }
        
        .item-meta {
          font-size: 12px;
          color: #9ca3af;
        }
      }
      
      .arrow-icon {
        color: #d1d5db;
      }
      
      .el-button {
        border-radius: 8px;
      }
    }
    
    .empty-folder {
      padding: 48px 20px;
      text-align: center;
      
      p {
        margin: 12px 0 0;
        color: #9ca3af;
      }
    }
  }
  
  .share-info {
    padding: 16px 28px;
    margin: 0;
    border-top: 1px solid #e5e7eb;
  }
}

.footer {
  text-align: center;
  margin-top: 20px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

// 响应式
@media (max-width: 480px) {
  .share-wrapper {
    max-width: 100%;
  }
  
  .state-container {
    padding: 36px 24px;
  }
  
  .file-name {
    font-size: 18px;
  }
  
  .folder-state {
    .folder-header {
      padding: 16px 20px;
      flex-direction: column;
      gap: 12px;
      align-items: flex-start;
    }
    
    .folder-content {
      padding: 8px;
      
      .file-item {
        padding: 12px;
      }
    }
  }
}
</style>

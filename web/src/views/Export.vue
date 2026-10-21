<script setup lang="ts">
/**
 * 3-2-1 导出台（2026-10-19 从快照页工具栏对话框提升为独立菜单页）。
 *
 * 3-2-1 原则：数据留 3 份、存 2 种介质、其中 1 份放异地。本页负责
 * "打包下载到外部存储"这最后一公里：目录边扫边压直接写响应体，
 * 不占服务端磁盘；可选 AES-256-CBC 加密（openssl enc 兼容格式）。
 */
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Download, FolderOpened } from '@element-plus/icons-vue'
import { exportApi } from '@/api/snapshot'
import DirectoryBrowserDialog from '@/components/DirectoryBrowserDialog.vue'

const { t } = useI18n()

const rootPath = ref('/')
const password = ref('')
const passwordConfirm = ref('')
const exporting = ref(false)
// 目录浏览器：点选库内目录（与文件页同一棵树），不再纯手打
const browseVisible = ref(false)

async function submit() {
  const path = rootPath.value.trim()
  if (!path) {
    ElMessage.warning(t('export.pathRequired'))
    return
  }
  // 加密包密码：打错即产出永远打不开的 .zip.enc（备份包典型数月后
  // 才解密），故非空时要求二次确认 + 最低长度，并在提示里写明
  // "遗忘无法恢复"
  if (password.value) {
    if (password.value.length < 8) {
      ElMessage.warning(t('export.passwordTooShort'))
      return
    }
    if (password.value !== passwordConfirm.value) {
      ElMessage.warning(t('export.passwordMismatch'))
      return
    }
  }
  exporting.value = true
  try {
    const blob = await exportApi.exportDirectory(path, password.value)
    const suffix = password.value ? '.zip.enc' : '.zip'
    const baseName = path === '/' ? 'all' : path.split('/').filter(Boolean).pop() ?? 'export'
    const fileName = `${baseName}-${Date.now()}${suffix}`
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = fileName
    anchor.click()
    URL.revokeObjectURL(url)
    password.value = ''
    passwordConfirm.value = ''
    ElMessage.success(t('export.done'))
  } catch {
    // 错误消息由 http 拦截器统一弹出（400/404/500 分支）
  } finally {
    exporting.value = false
  }
}
</script>

<template>
  <div class="export-page">
    <div class="page-header">
      <div class="header-left">
        <h1>{{ $t('export.title') }}</h1>
        <p>{{ $t('export.subtitle') }}</p>
      </div>
    </div>

    <div class="export-card">
      <el-form label-position="top">
        <el-form-item :label="$t('export.rootPath')" required>
          <el-input v-model="rootPath" placeholder="/">
            <template #append>
              <el-button :icon="FolderOpened" @click="browseVisible = true">{{ $t('ocr.browseDir') }}</el-button>
            </template>
          </el-input>
          <div class="form-hint">{{ $t('export.rootPathHint') }}</div>
        </el-form-item>
        <el-form-item :label="$t('export.password')">
          <el-input
            v-model="password"
            type="password"
            show-password
            :placeholder="$t('export.passwordPlaceholder')"
          />
          <div class="form-hint">{{ $t('export.passwordHint') }}</div>
        </el-form-item>
        <!-- 确认密码：仅填写了密码时才出现（不加密的导出无此概念） -->
        <el-form-item v-if="password" :label="$t('export.passwordConfirm')">
          <el-input
            v-model="passwordConfirm"
            type="password"
            show-password
            :placeholder="$t('export.passwordConfirmPlaceholder')"
          />
          <div class="form-hint">{{ $t('export.passwordUnrecoverable') }}</div>
        </el-form-item>
        <el-button type="primary" :icon="Download" :loading="exporting" @click="submit">
          {{ $t('export.submit') }}
        </el-button>
      </el-form>
    </div>

    <div class="export-card principle-card">
      <h3>{{ $t('export.principleTitle') }}</h3>
      <p>{{ $t('export.principleDesc') }}</p>
      <ul>
        <li>{{ $t('export.principleItem1') }}</li>
        <li>{{ $t('export.principleItem2') }}</li>
        <li>{{ $t('export.principleItem3') }}</li>
      </ul>
    </div>

    <!-- 目录浏览器：选中后回填导出路径 -->
    <DirectoryBrowserDialog v-model="browseVisible" @select="path => rootPath = path" />
  </div>
</template>

<style scoped>
.export-page {
  max-width: 720px;
  margin: 0 auto;
}



.export-card {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--mac-radius-card);
  padding: 16px;
  margin-bottom: 16px;
  background: var(--el-bg-color);
}

.form-hint {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.5;
  margin-top: 4px;
}

.principle-card {
  h3 {
    margin: 0 0 8px;
    font-size: 14px;
    font-weight: 600;
    color: var(--el-text-color-primary);
  }

  p {
    margin: 0 0 8px;
    font-size: 13px;
    color: var(--el-text-color-secondary);
    line-height: 1.6;
  }

  ul {
    margin: 0;
    padding-left: 16px;
    font-size: 13px;
    color: var(--el-text-color-regular);

    li {
      margin-bottom: 4px;
    }
  }
}
</style>

<script setup lang="ts">
/**
 * 文字识别（OCR）页（2026-10-19 从系统配置页拆出）。
 *
 * 拆页动机：OCR 此前在系统配置页只有一个"上传后自动识别"开关，
 * 而它真正的日常操作——批量识别整个目录——埋在文件管理工具栏
 * （恰好选中一个文件夹才可用）。本页把开关与操作放进同一视野，
 * 批量工具对全部登录用户开放，开关仅管理员可见。
 */
import { ref, reactive } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Picture, Search, InfoFilled } from '@element-plus/icons-vue'
import { ocrApi } from '@/api/ocr'
import { systemConfigApi } from '@/api/systemConfig'
import { useUserStore } from '@/stores/user'

const { t } = useI18n()
const userStore = useUserStore()

// ---- 批量识别工具（所有登录用户） ----
const batchForm = reactive({ path: '/', force: false })
const batching = ref(false)
const batchResult = ref<{ total: number; recognized: number; skipped: number; failed: number } | null>(null)

async function runBatch() {
  const path = batchForm.path.trim()
  if (!path) {
    ElMessage.warning(t('ocr.pathRequired'))
    return
  }
  batching.value = true
  try {
    const resp = await ocrApi.recognizeDirectory(path, batchForm.force)
    if (resp.success && resp.data) {
      batchResult.value = resp.data
      ElMessage.success(
        t('ocr.batchDone', {
          recognized: resp.data.recognized,
          skipped: resp.data.skipped,
          failed: resp.data.failed
        })
      )
    } else {
      ElMessage.error(resp.error?.message || t('common.failed'))
    }
  } catch {
    // 错误消息由 http 拦截器统一弹出
  } finally {
    batching.value = false
  }
}

// ---- 自动识别开关（仅管理员，descriptor 驱动） ----
const autoRecognize = ref(false)
const switchSaving = ref(false)

async function loadSwitch() {
  if (!userStore.isAdmin) return
  try {
    const resp = await systemConfigApi.get()
    if (resp.success && resp.data) {
      const item = resp.data.groups
        .flatMap(g => g.items)
        .find(i => i.key === 'ocr.auto-recognize')
      if (item) autoRecognize.value = item.value === 'true'
    }
  } catch {
    // 开关加载失败不阻塞页面：批量工具与说明仍可用
  }
}

async function toggleAutoRecognize(value: string | number | boolean) {
  const next = Boolean(value)
  switchSaving.value = true
  try {
    const resp = await systemConfigApi.update({ 'ocr.auto-recognize': next ? 'true' : 'false' })
    if (resp.success) {
      autoRecognize.value = next
      ElMessage.success(t('config.saveSuccess'))
    } else {
      ElMessage.error(resp.error?.message || t('config.saveFailed'))
    }
  } catch {
    ElMessage.error(t('config.saveFailed'))
  } finally {
    switchSaving.value = false
  }
}

loadSwitch()
</script>

<template>
  <div class="ocr-page">
    <div class="page-header">
      <div class="header-left">
        <h2>{{ $t('ocr.title') }}</h2>
        <p>{{ $t('ocr.subtitle') }}</p>
      </div>
    </div>

    <!-- ① 批量识别工具：目录路径 + 强制重识别 -->
    <div class="card">
      <div class="card-head">
        <el-icon :size="20"><Search /></el-icon>
        <span class="card-name">{{ $t('ocr.batchTitle') }}</span>
      </div>
      <p class="card-desc">{{ $t('ocr.batchDesc') }}</p>
      <div class="batch-form">
        <el-input v-model="batchForm.path" placeholder="/" class="path-input">
          <template #prepend>{{ $t('ocr.pathLabel') }}</template>
        </el-input>
        <el-checkbox v-model="batchForm.force">{{ $t('ocr.force') }}</el-checkbox>
        <el-button type="primary" :loading="batching" @click="runBatch">
          {{ $t('ocr.runBatch') }}
        </el-button>
      </div>
      <div class="form-hint">{{ $t('ocr.forceHint') }}</div>

      <div v-if="batchResult" class="batch-result">
        <el-tag type="success" size="large">{{ $t('ocr.resultTotal') }} {{ batchResult.total }}</el-tag>
        <el-tag type="primary" size="large">{{ $t('ocr.resultRecognized') }} {{ batchResult.recognized }}</el-tag>
        <el-tag type="info" size="large">{{ $t('ocr.resultSkipped') }} {{ batchResult.skipped }}</el-tag>
        <el-tag v-if="batchResult.failed > 0" type="danger" size="large">{{ $t('ocr.resultFailed') }} {{ batchResult.failed }}</el-tag>
      </div>
    </div>

    <!-- ② 自动识别开关（仅管理员） -->
    <div class="card" v-if="userStore.isAdmin">
      <div class="card-head">
        <el-icon :size="20"><Picture /></el-icon>
        <span class="card-name">{{ $t('ocr.autoTitle') }}</span>
      </div>
      <div class="switch-row">
        <div class="switch-info">
          <span class="switch-label">{{ $t('ocr.autoRecognize') }}</span>
          <span class="switch-desc">{{ $t('ocr.autoRecognizeDesc') }}</span>
        </div>
        <el-switch :model-value="autoRecognize" :loading="switchSaving" @update:model-value="toggleAutoRecognize" />
      </div>
    </div>

    <!-- ③ 工作原理 -->
    <div class="card">
      <div class="card-head">
        <el-icon :size="20"><InfoFilled /></el-icon>
        <span class="card-name">{{ $t('ocr.howTitle') }}</span>
      </div>
      <ul class="how-list">
        <li>{{ $t('ocr.howEngine') }}</li>
        <li>{{ $t('ocr.howSearch') }}</li>
        <li>{{ $t('ocr.howSingle') }}</li>
      </ul>
    </div>
  </div>
</template>

<style scoped>
.ocr-page {
  padding: 16px;
  max-width: 860px;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 16px;
  gap: 12px;
  flex-wrap: wrap;

  .header-left {
    h2 {
      margin: 0 0 4px;
      font-size: 20px;
      font-weight: 600;
      color: var(--el-text-color-primary);
    }
    p {
      margin: 0;
      font-size: 13px;
      color: var(--el-text-color-secondary);
    }
  }
}

.card {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 12px;
  padding: 16px;
  margin-bottom: 16px;
  background: var(--el-bg-color);

  .card-desc {
    margin: 0 0 12px;
    font-size: 13px;
    color: var(--el-text-color-secondary);
    line-height: 1.6;
  }
}

.card-head {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 10px;

  .card-name {
    font-weight: 600;
    font-size: 14px;
    color: var(--el-text-color-primary);
  }
}

.batch-form {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;

  .path-input {
    flex: 1;
    min-width: 240px;
  }
}

.form-hint {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.5;
  margin-top: 8px;
}

.batch-result {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  margin-top: 16px;
}

.switch-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;

  .switch-info {
    .switch-label {
      display: block;
      font-size: 14px;
      font-weight: 500;
    }
    .switch-desc {
      display: block;
      font-size: 12px;
      color: var(--el-text-color-secondary);
      margin-top: 2px;
    }
  }
}

.how-list {
  margin: 0;
  padding-left: 20px;
  font-size: 13px;
  color: var(--el-text-color-regular);
  line-height: 1.8;
}
</style>

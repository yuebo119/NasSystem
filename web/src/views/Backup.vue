<script setup lang="ts">
/**
 * 移动备份页（ROADMAP Phase 1 PWA 入口）
 *
 * 流程：选文件（或整个文件夹）→ 客户端清单（相对路径 + 体积）→
 * 服务端计划 diff → 逐条走标准上传端点到 /Backups/{设备名}/ →
 * 登记同步完成。备份文件就是普通文件：配额、软删除、实时推送全生效。
 *
 * 移动优先布局：大按钮、单列、进度显式。桌面宽度下自动居中限宽。
 */
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { backupApi } from '@/api/backup'
import { fileApi } from '@/api/file'
import type { BackupDevice, BackupSyncPlan } from '@/types'
import { formatDate } from '@/utils/format'
import { relativePathOf, destinationDirOf } from '@/utils/backupPath'

const { t } = useI18n()

const devices = ref<BackupDevice[]>([])
const loading = ref(false)
const registering = ref(false)
const deviceName = ref('')
const deviceModel = ref('')
const activeDeviceId = ref('')

// 文件选择与备份执行
const selectedFiles = ref<File[]>([])
const syncing = ref(false)
const syncProgress = ref({ done: 0, total: 0, currentName: '' })

onMounted(loadDevices)

async function loadDevices() {
  loading.value = true
  try {
    const result = await backupApi.getDevices()
    if (result.success) {
      devices.value = result.data ?? []
      // 默认选中第一个未吊销设备
      if (!activeDeviceId.value || !devices.value.some(d => d.id === activeDeviceId.value)) {
        const first = devices.value.find(d => !d.isRevoked)
        activeDeviceId.value = first?.id ?? ''
      }
    }
  } catch {
    ElMessage.error(t('backup.loadFailed'))
  } finally {
    loading.value = false
  }
}

async function registerDevice() {
  const name = deviceName.value.trim()
  if (!name) {
    ElMessage.warning(t('backup.nameRequired'))
    return
  }
  registering.value = true
  try {
    const result = await backupApi.register(name, deviceModel.value.trim() || undefined)
    if (result.success && result.data) {
      ElMessage.success(t('backup.registerSuccess'))
      deviceName.value = ''
      deviceModel.value = ''
      await loadDevices()
      activeDeviceId.value = result.data.id
    } else {
      ElMessage.error(result.error?.message || t('backup.registerFailed'))
    }
  } catch {
    ElMessage.error(t('backup.registerFailed'))
  } finally {
    registering.value = false
  }
}

async function revokeDevice(device: BackupDevice) {
  try {
    await ElMessageBox.confirm(
      t('backup.revokeConfirm', { name: device.name }),
      t('backup.revokeTitle'),
      { type: 'warning' }
    )
  } catch {
    return
  }
  const result = await backupApi.revoke(device.id)
  if (result.success) {
    ElMessage.success(t('backup.revokeSuccess'))
    if (activeDeviceId.value === device.id) activeDeviceId.value = ''
    await loadDevices()
  } else {
    ElMessage.error(t('backup.revokeFailed'))
  }
}

// 文件选择：webkitdirectory 整夹备份时相对路径在 webkitRelativePath 上
function onFilesPicked(event: Event) {
  const input = event.target as HTMLInputElement
  selectedFiles.value = input.files ? Array.from(input.files) : []
}

/**
 * 备份主流程。逐条上传、失败续传（不中断整批）：
 * 单张照片失败（网络抖动）不该让整次备份作废，
 * 已完成进度保留，下次同步计划自然会重试剩余条目
 */
async function startBackup() {
  const device = devices.value.find(d => d.id === activeDeviceId.value)
  if (!device || device.isRevoked) {
    ElMessage.warning(t('backup.selectDeviceFirst'))
    return
  }
  if (selectedFiles.value.length === 0) {
    ElMessage.warning(t('backup.selectFilesFirst'))
    return
  }

  syncing.value = true
  const filesByPath = new Map(selectedFiles.value.map(f => [relativePathOf(f), f]))
  try {
    // ① 清单 → 计划
    const planResult = await backupApi.computePlan(
      device.id,
      [...filesByPath].map(([relativePath, file]) => ({ relativePath, sizeBytes: file.size }))
    )
    if (!planResult.success || !planResult.data) {
      ElMessage.error(planResult.error?.message || t('backup.planFailed'))
      return
    }
    const plan: BackupSyncPlan = planResult.data

    // ② 逐条上传待传条目
    syncProgress.value = { done: 0, total: plan.toUpload.length, currentName: '' }
    let uploaded = 0
    let failed = 0
    for (const entry of plan.toUpload) {
      const file = filesByPath.get(entry.relativePath)
      if (!file) {
        failed++
        continue
      }
      syncProgress.value.currentName = entry.relativePath
      try {
        const upload = await fileApi.upload(file, destinationDirOf(plan.backupPath, entry.relativePath))
        if (upload.success) {
          uploaded++
        } else {
          failed++
        }
      } catch {
        failed++
      }
      syncProgress.value.done = ++syncProgress.value.done
    }

    // ③ 登记同步完成（上传数 >= 1 才登记：全量已最新时不该刷新最近同步时间）。
    // 独立 try：登记失败不能把"文件已全部备份成功"的事实报成计划失败——
    // 上传是主目的，登记只是刷新设备的最近同步时间（2026-10-21 审计 P3）
    let recordFailed = false
    if (uploaded > 0) {
      try {
        await backupApi.recordComplete(device.id, uploaded)
      } catch {
        recordFailed = true
      }
    }

    if (failed > 0) {
      ElMessage.warning(t('backup.partialDone', { ok: uploaded, fail: failed }))
    } else if (uploaded > 0) {
      ElMessage.success(t('backup.done', { count: uploaded }))
    } else {
      ElMessage.success(t('backup.allUpToDate'))
    }
    if (recordFailed) {
      // 拦截器已先弹过服务器错误，这里补一句登记未成功的实话，
      // 不让"备份计划失败"掩盖文件已上传成功
      ElMessage.warning(t('backup.recordFailed'))
    }

    selectedFiles.value = []
    const input = document.querySelector<HTMLInputElement>('#backup-file-input')
    if (input) input.value = ''
    await loadDevices()
  } catch {
    ElMessage.error(t('backup.planFailed'))
  } finally {
    syncing.value = false
    syncProgress.value = { done: 0, total: 0, currentName: '' }
  }
}
</script>

<template>
  <div class="backup-page">
    <div class="page-header">
      <h1>{{ $t('backup.title') }}</h1>
    </div>

    <!-- 设备注册 -->
    <el-card class="backup-card">
      <template #header>
        <span>{{ $t('backup.registerTitle') }}</span>
      </template>
      <el-form label-position="top" class="register-form">
        <el-form-item :label="$t('backup.deviceName')" required>
          <el-input
            v-model="deviceName"
            :placeholder="$t('backup.deviceNamePlaceholder')"
            maxlength="50"
          />
        </el-form-item>
        <el-form-item :label="$t('backup.deviceModel')">
          <el-input
            v-model="deviceModel"
            :placeholder="$t('backup.deviceModelPlaceholder')"
            maxlength="100"
          />
        </el-form-item>
        <el-button
          type="primary"
          size="large"
          class="submit-btn"
          :loading="registering"
          @click="registerDevice"
        >
          {{ $t('backup.register') }}
        </el-button>
      </el-form>
    </el-card>

    <!-- 设备列表 -->
    <el-card class="backup-card" v-loading="loading">
      <template #header>
        <span>{{ $t('backup.devicesTitle') }}</span>
      </template>
      <el-empty v-if="devices.length === 0" :description="$t('backup.noDevices')" />
      <div v-else class="device-list">
        <div
          v-for="device in devices"
          :key="device.id"
          class="device-item"
          :class="{ active: device.id === activeDeviceId, revoked: device.isRevoked }"
          @click="!device.isRevoked && (activeDeviceId = device.id)"
        >
          <div class="device-main">
            <div class="device-name">
              {{ device.name }}
              <el-tag v-if="device.isRevoked" type="info" size="small">{{ $t('backup.revoked') }}</el-tag>
              <el-tag v-else-if="device.id === activeDeviceId" type="success" size="small">{{ $t('backup.active') }}</el-tag>
            </div>
            <div class="device-meta">
              {{ device.deviceModel || $t('backup.unknownModel') }} · {{ device.backupPath }}
            </div>
            <div class="device-meta" v-if="device.lastSyncAt">
              {{ $t('backup.lastSync') }}: {{ formatDate(device.lastSyncAt) }}（{{ device.lastSyncFileCount }} {{ $t('backup.filesUnit') }}）
            </div>
            <div class="device-meta" v-else>{{ $t('backup.neverSynced') }}</div>
          </div>
          <el-button
            v-if="!device.isRevoked"
            link
            type="danger"
            @click.stop="revokeDevice(device)"
          >
            {{ $t('backup.revoke') }}
          </el-button>
        </div>
      </div>
    </el-card>

    <!-- 执行备份 -->
    <el-card class="backup-card">
      <template #header>
        <span>{{ $t('backup.runTitle') }}</span>
      </template>
      <div class="run-panel">
        <input
          id="backup-file-input"
          type="file"
          multiple
          class="file-input"
          @change="onFilesPicked"
        />
        <div class="selected-info" v-if="selectedFiles.length > 0">
          {{ $t('backup.selectedInfo', { count: selectedFiles.length }) }}
        </div>
        <el-button
          type="primary"
          size="large"
          class="submit-btn"
          :loading="syncing"
          :disabled="!activeDeviceId || selectedFiles.length === 0"
          @click="startBackup"
        >
          {{ $t('backup.start') }}
        </el-button>
        <div v-if="syncing" class="sync-progress">
          <el-progress
            :percentage="syncProgress.total === 0 ? 0 : Math.round((syncProgress.done / syncProgress.total) * 100)"
            :stroke-width="18"
          />
          <div class="progress-text">
            {{ $t('backup.progressText', { done: syncProgress.done, total: syncProgress.total }) }}
            <span class="current-name" :title="syncProgress.currentName">{{ syncProgress.currentName }}</span>
          </div>
        </div>
      </div>
    </el-card>
  </div>
</template>

<style lang="scss" scoped>
.backup-page {
  max-width: 720px;
  margin: 0 auto;

  

  .backup-card {
    margin-bottom: 16px;
    border-radius: var(--mac-radius-card);
  }

  // 移动优先：主操作按钮全宽、最小高度 44px（手机点击目标）
  .submit-btn {
    width: 100%;
    min-height: 44px;
    margin-top: 8px;
  }

  .file-input {
    width: 100%;
    padding: 12px;
    border: 1px dashed var(--el-border-color);
    border-radius: var(--mac-radius-control);
    margin-bottom: 12px;
  }

  .selected-info {
    font-size: 13px;
    color: var(--el-text-color-secondary);
    margin-bottom: 8px;
  }

  .device-list {
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .device-item {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    padding: 12px 16px;
    border: 1px solid var(--mac-hairline);
    border-radius: var(--mac-radius-card);
    cursor: pointer;
    transition: border-color var(--mac-duration-fast) ease, background var(--mac-duration-fast) ease;

    // 选中态用主色描边 + 低透明度主色底。不用 light-9：那是 mix(主色, 白, 90%)
    // 的近白底，暗色模式下项内近白文字叠近白底实测对比度 1.05:1，选中反馈
    // 反而让文字消失（2026-10-21 设计审计实测）
    &.active {
      border-color: var(--el-color-primary);
      background: color-mix(in srgb, var(--el-color-primary) 14%, transparent);
    }

    &.revoked {
      opacity: 0.6;
      cursor: default;
    }
  }

  .device-main {
    flex: 1;
    min-width: 0;
  }

  .device-name {
    font-weight: 500;
    font-size: 14px;
    display: flex;
    align-items: center;
    gap: 6px;
    color: var(--el-text-color-primary);
  }

  .device-meta {
    font-size: 12px;
    color: var(--el-text-color-secondary);
    margin-top: 4px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .sync-progress {
    margin-top: 16px;

    .progress-text {
      font-size: 13px;
      color: var(--el-text-color-secondary);
      margin-top: 8px;

      .current-name {
        display: block;
        margin-top: 2px;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
    }
  }
}
</style>

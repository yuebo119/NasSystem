<script setup lang="ts">
/**
 * 目录浏览器对话框（2026-10-21 抽自三处重复实现）。
 *
 * Ocr/Export/Snapshots 三页都要"选一个库内目录"，路径此前纯手打——
 * 打错整批失败且无处查证。本组件用文件 browse 接口（库内虚拟路径，
 * 与文件管理页同一棵树）提供点选入口，三个页面共用一份导航逻辑。
 *
 * 语义：浏览的是"选中的当前目录"——双击进子目录，确认按钮选中当前
 * 所在目录（初始为根 /）。文件不参与选择（三个调用点都只要目录）。
 */
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ArrowLeft, FolderOpened, Folder } from '@element-plus/icons-vue'
import { fileApi } from '@/api/file'
import type { FileItem } from '@/types'

const props = defineProps<{
  modelValue: boolean
  title?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  select: [path: string]
}>()

const { t } = useI18n()
const currentPath = ref('/')
const folders = ref<FileItem[]>([])
const loading = ref(false)

async function load(path: string) {
  loading.value = true
  try {
    const resp = await fileApi.getDirectory(path)
    if (resp.success && resp.data) {
      currentPath.value = resp.data.path
      folders.value = resp.data.items.filter(i => i.isFolder)
    }
  } catch {
    // 错误消息由 http 拦截器统一弹出；目录状态保持不变
  } finally {
    loading.value = false
  }
}

// 面包屑：'/a/b' → ['/', '/a', '/a/b']（根目录标签用 i18n）
const breadcrumbs = computed(() => {
  const segments = currentPath.value.split('/').filter(Boolean)
  const crumbs: { path: string; label: string }[] = [{ path: '/', label: t('filesView.rootCrumb') }]
  let acc = ''
  for (const seg of segments) {
    acc += '/' + seg
    crumbs.push({ path: acc, label: seg })
  }
  return crumbs
})

function goUp() {
  const parent = currentPath.value.split('/').slice(0, -1).join('/') || '/'
  void load(parent)
}

function close() {
  emit('update:modelValue', false)
}

function confirm() {
  emit('select', currentPath.value)
  close()
}

// 每次打开重置到根目录（跨页面复用时不留上一次的浏览位置）
watch(
  () => props.modelValue,
  (visible) => {
    if (visible) void load('/')
  }
)
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    :title="title || t('filesView.browseDirTitle')"
    width="560px"
    align-center
    @update:model-value="close"
  >
    <div class="dir-browser">
      <div class="dir-browser-bar">
        <el-button v-if="currentPath !== '/'" size="small" :icon="ArrowLeft" @click="goUp">
          {{ t('filesView.goBack') }}
        </el-button>
        <el-breadcrumb separator="/" class="dir-browser-crumbs">
          <el-breadcrumb-item v-for="crumb in breadcrumbs" :key="crumb.path">
            <el-button link type="primary" @click="load(crumb.path)">{{ crumb.label }}</el-button>
          </el-breadcrumb-item>
        </el-breadcrumb>
      </div>
      <div v-loading="loading" class="dir-browser-list">
        <div
          v-for="folder in folders"
          :key="folder.id"
          class="dir-browser-row"
          @dblclick="load(folder.path)"
        >
          <el-icon :size="18" color="var(--el-color-warning)"><Folder /></el-icon>
          <span class="dir-browser-name">{{ folder.name }}</span>
          <el-button link type="primary" size="small" @click.stop="load(folder.path)">
            {{ t('filesView.enterDir') }}
          </el-button>
        </div>
        <el-empty v-if="folders.length === 0 && !loading" :description="t('filesView.noSubDirs')" :image-size="60" />
      </div>
      <div class="dir-browser-current">
        <el-icon><FolderOpened /></el-icon>
        <span>{{ t('filesView.selectedDir') }}：<strong>{{ currentPath }}</strong></span>
      </div>
    </div>
    <template #footer>
      <el-button @click="close">{{ t('common.cancel') }}</el-button>
      <el-button type="primary" @click="confirm">{{ t('common.confirm') }}</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.dir-browser-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
  min-height: 32px;

  .dir-browser-crumbs { flex: 1; min-width: 0; }
}

.dir-browser-list {
  max-height: 380px;
  overflow-y: auto;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--mac-radius-control);

  .dir-browser-row {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 8px 12px;
    cursor: pointer;

    &:hover { background: var(--el-fill-color-light); }

    .dir-browser-name { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 13px; }
  }
}

.dir-browser-current {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 12px;
  font-size: 13px;
  color: var(--el-text-color-secondary);

  strong { color: var(--el-text-color-primary); font-family: monospace; }
}
</style>

<script setup lang="ts">
/**
 * OCR 叠加查看器（2026-10-21）：图片 + 按坐标叠回原位的文字层。
 *
 * 解决的痛点：识别结果以前是一段 <pre> 纯文本，用户无法对照"图上哪个位置
 * 识别出了什么"。本组件把每行文字按归一化四角坐标叠回图片原位：
 * ① 底色用画布采样该区域在图片里的真实均色（不是统一的半透明底板），
 *    文字色按底色亮度取黑/白——视觉上像"把图里的字描成了干净版本"；
 * ② 字号按区域高度占舞台高度的比例换算，与图内文字大小观感一致；
 * ③ 文字层可选中复制；下方文字清单与叠加层双向高亮联动，点哪行亮哪块。
 *
 * 降级：regions 为空（存量识别结果）时不渲染叠加层与清单，由调用方
 * 走纯文本 <pre> 分支——位置信息是增强，缺了不影响查看。
 */
import { ref, computed, watch, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { CopyDocument } from '@element-plus/icons-vue'
import type { OcrTextRegion } from '@/types'

const props = defineProps<{
  /** 图片对象 URL（blob:，token 已由 fetch 带上） */
  imageUrl: string
  /** 行区域（归一化坐标） */
  regions: OcrTextRegion[]
  /** 图片名（alt 与清单标题用） */
  alt?: string
}>()

const { t } = useI18n()

// 当前高亮的区域索引（叠加层与清单共用）：hover/点击任一侧都会置位
const activeIndex = ref<number | null>(null)

const stageRef = ref<HTMLElement | null>(null)
const imgRef = ref<HTMLImageElement | null>(null)

// 每个区域的布局盒（百分比）+ 采样出的背景/文字色 + 换算后的字号 px。
// 图片加载完成、舞台尺寸变化时统一重算
interface RegionBox {
  left: number
  top: number
  width: number
  height: number
  bg: string
  fg: string
  fontSize: number
}
const boxes = ref<RegionBox[]>([])

/** 四角 → 外接盒（百分比）。斜排文字的框是外接矩形，高亮范围略大但位置正确 */
function quadBox(points: number[]): { left: number; top: number; width: number; height: number } {
  const xs = [points[0], points[2], points[4], points[6]]
  const ys = [points[1], points[3], points[5], points[7]]
  const minX = Math.min(...xs)
  const maxX = Math.max(...xs)
  const minY = Math.min(...ys)
  const maxY = Math.max(...ys)
  return {
    left: minX * 100,
    top: minY * 100,
    width: Math.max((maxX - minX) * 100, 0.5),
    height: Math.max((maxY - minY) * 100, 0.5)
  }
}

// 亮度阈值（0..1）：低于它文字用白，高于用近黑。0.55 偏保守，
// 灰底照片区多数落在近黑文字侧，可读性优先于对比度极限
const LUMINANCE_THRESHOLD = 0.55

function sampleColors() {
  const img = imgRef.value
  if (!img || !img.complete || img.naturalWidth === 0) return

  // 离屏画布按原图尺寸采样（object URL 同源，无 CORS 问题）
  let canvas: HTMLCanvasElement | null = null
  try {
    canvas = document.createElement('canvas')
    canvas.width = img.naturalWidth
    canvas.height = img.naturalHeight
    const ctx = canvas.getContext('2d', { willReadFrequently: true })
    if (!ctx) return
    ctx.drawImage(img, 0, 0)

    const stage = stageRef.value
    const displayedH = stage?.clientHeight ?? img.clientHeight ?? 0

    boxes.value = props.regions.map((region) => {
      const box = quadBox(region.points)
      let bg = 'rgba(255, 255, 255, 0.72)'
      let fg = '#111'
      try {
        // 区域像素范围（原图坐标），采样步长 2px 控成本
        const x0 = Math.floor((box.left / 100) * img.naturalWidth)
        const y0 = Math.floor((box.top / 100) * img.naturalHeight)
        const w = Math.max(Math.floor((box.width / 100) * img.naturalWidth), 1)
        const h = Math.max(Math.floor((box.height / 100) * img.naturalHeight), 1)
        const data = ctx.getImageData(x0, y0, w, h).data
        let r = 0, g = 0, b = 0, n = 0
        const step = Math.max(2, Math.floor(w / 40)) * 4
        for (let i = 0; i < data.length; i += step) {
          r += data[i]
          g += data[i + 1]
          b += data[i + 2]
          n++
        }
        if (n > 0) {
          r = Math.round(r / n)
          g = Math.round(g / n)
          b = Math.round(b / n)
          bg = `rgb(${r}, ${g}, ${b})`
          // Rec.709 相对亮度
          const lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255
          fg = lum < LUMINANCE_THRESHOLD ? '#fff' : '#111'
        }
      } catch {
        // 采样失败（极端情况）保持默认浅底深字，可读优先
      }
      // 字号：区域高度占舞台高度的比例 × 舞台高 × 0.72（行高系数）。
      // 用 0.72 而不是 1.0：字完全撑满框会显得拥挤，图内文字也常有行距
      const fontSize = displayedH > 0 ? (box.height / 100) * displayedH * 0.72 : 12
      return { ...box, bg, fg, fontSize: Math.max(fontSize, 9) }
    })
  } finally {
    canvas = null
  }
}

function onImageLoad() {
  sampleColors()
}

// 舞台尺寸变化（窗口缩放/抽屉开合）重算字号；ResizeObserver 比
// window.resize 精确，不依赖全局监听
let observer: ResizeObserver | null = null
watch(stageRef, (el) => {
  observer?.disconnect()
  observer = null
  if (!el || typeof ResizeObserver === 'undefined') return
  observer = new ResizeObserver(() => sampleColors())
  observer.observe(el)
}, { immediate: true })

onUnmounted(() => {
  observer?.disconnect()
  observer = null
})

// 图片源变化（换文件预览）时重采样
watch(() => props.imageUrl, () => {
  boxes.value = []
  activeIndex.value = null
})

const hasRegions = computed(() => props.regions.length > 0 && boxes.value.length > 0)

async function copyAll() {
  const text = props.regions.map(r => r.text).join('\n')
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success(t('filesView.ocrCopied'))
  } catch {
    ElMessage.error(t('filesView.ocrCopyFailed'))
  }
}
</script>

<template>
  <div class="ocr-viewer">
    <!-- 舞台：图片 + 叠加文字层 -->
    <div ref="stageRef" class="viewer-stage">
      <img ref="imgRef" :src="imageUrl" :alt="alt" class="viewer-image" @load="onImageLoad" />
      <div v-if="hasRegions" class="viewer-overlay">
        <div
          v-for="(box, index) in boxes"
          :key="index"
          class="overlay-line"
          :class="{ active: activeIndex === index }"
          :style="{
            left: box.left + '%',
            top: box.top + '%',
            width: box.width + '%',
            height: box.height + '%',
            background: box.bg,
            color: box.fg,
            fontSize: box.fontSize + 'px'
          }"
          @mouseenter="activeIndex = index"
          @mouseleave="activeIndex = null"
        >{{ regions[index].text }}</div>
      </div>
    </div>

    <!-- 文字清单：与叠加层双向联动（hover/点击任一侧高亮另一侧） -->
    <div v-if="hasRegions" class="viewer-list">
      <div class="list-header">
        <span class="list-title">{{ $t('filesView.ocrTextList') }}</span>
        <el-button size="small" :icon="CopyDocument" @click="copyAll">
          {{ $t('filesView.ocrCopyAll') }}
        </el-button>
      </div>
      <div class="list-body">
        <p
          v-for="(region, index) in regions"
          :key="index"
          class="list-line"
          :class="{ active: activeIndex === index }"
          @mouseenter="activeIndex = index"
          @mouseleave="activeIndex = null"
          @click="activeIndex = index"
        >{{ region.text }}</p>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
.ocr-viewer {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.viewer-stage {
  position: relative;
  display: flex;
  justify-content: center;
  border-radius: var(--mac-radius-card);
  overflow: hidden;
  border: 1px solid var(--mac-hairline);
  box-shadow: var(--card-shadow);
  background: var(--el-fill-color-light);
  line-height: 0;

  .viewer-image {
    max-width: 100%;
    max-height: 55vh;
    display: block;
  }
}

.viewer-overlay {
  position: absolute;
  inset: 0;
  pointer-events: none;
  line-height: 1;
}

.overlay-line {
  position: absolute;
  // 文字层可选中复制（pointer-events 只在元素自身恢复，
  // 容器 none 保证不挡图片的查看操作）
  pointer-events: auto;
  user-select: text;
  -webkit-user-select: text;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: clip;
  display: flex;
  align-items: center;
  border-radius: 2px;
  cursor: text;
  transition: outline-color var(--mac-duration-fast) ease;
  outline: 2px solid transparent;

  // 高亮：主色描边（hover 任一侧或点击清单行时）
  &.active {
    outline-color: var(--el-color-primary);
  }
}

.viewer-list {
  border-top: 1px solid var(--mac-hairline);
  padding-top: 12px;

  .list-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 8px;

    .list-title {
      font-size: 13px;
      font-weight: 600;
      color: var(--el-text-color-primary);
    }
  }

  .list-body {
    max-height: 200px;
    overflow: auto;
  }

  .list-line {
    margin: 0 0 4px;
    padding: 4px 8px;
    border-radius: var(--mac-radius-control);
    font-size: 13px;
    line-height: 1.5;
    color: var(--el-text-color-regular);
    word-break: break-all;
    cursor: pointer;
    transition: background var(--mac-duration-fast) ease;

    &.active {
      background: color-mix(in srgb, var(--el-color-primary) 14%, transparent);
      color: var(--el-text-color-primary);
    }
  }
}
</style>

import type { Component } from 'vue'
import {
  Folder, Document, Picture, VideoPlay, Headset,
  Reading, Memo, DataBoard, Postcard, Files, Tickets
} from '@element-plus/icons-vue'
import i18n from '@/locales'

// 视图层格式化工具收敛点：原 Files/Dashboard/Users/Storage/Shares/ShareAccess 六处私有副本统一至此
// 模块级无组件上下文，经 i18n 全局实例取翻译函数（在渲染/computed 副作用内调用，语言切换可响应）

/**
 * 文件大小格式化：0 返回 '-'，五级单位（B/KB/MB/GB/TB），固定两位小数
 */
export function formatSize(bytes: number): string {
  if (bytes === 0) return '-'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(1024))
  return (bytes / Math.pow(1024, i)).toFixed(2) + ' ' + units[i]
}

/**
 * 日期格式化：空值显示从未登录；当天显时间、昨天、7 天内显相对天数，更早显本地日期
 */
export function formatDate(dateStr?: string): string {
  if (!dateStr) return i18n.global.t('common.neverLogin')
  const date = new Date(dateStr)
  const now = new Date()
  const diff = now.getTime() - date.getTime()
  const days = Math.floor(diff / (1000 * 60 * 60 * 24))
  if (days === 0) return i18n.global.t('common.today') + ' ' + date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
  if (days === 1) return i18n.global.t('common.yesterday')
  if (days < 7) return i18n.global.t('common.daysAgo', { n: days })
  return date.toLocaleDateString()
}

// 图标/颜色函数的统一入参：兼容 FileItem（extension）、FileShareDto/ShareFileItemDto（fileName）、PublicShareInfoDto（name）三种形态
interface IconItem {
  isFolder: boolean
  extension?: string
  name?: string
  fileName?: string
}

// 取小写扩展名：优先 extension 字段，否则从文件名/显示名截取
function getExtension(item: IconItem): string {
  if (item.extension) return item.extension.toLowerCase()
  const name = 'fileName' in item ? item.fileName : item.name
  if (!name) return ''
  return name.substring(name.lastIndexOf('.')).toLowerCase()
}

/**
 * 文件图标：以最全分类为准（文档/表格/演示/图片/视频/音频/压缩包/代码/配置分色）
 */
export function getFileIcon(item: IconItem): Component {
  if (item.isFolder) return Folder
  const ext = getExtension(item)

  // 文档类
  if (['.pdf'].includes(ext)) return Reading
  if (['.doc', '.docx'].includes(ext)) return Memo
  if (['.xls', '.xlsx', '.csv'].includes(ext)) return DataBoard
  if (['.ppt', '.pptx'].includes(ext)) return Postcard
  if (['.txt', '.md', '.rtf'].includes(ext)) return Document

  // 图片类
  if (['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.svg', '.ico'].includes(ext)) return Picture

  // 视频类
  if (['.mp4', '.avi', '.mkv', '.mov', '.wmv', '.flv', '.webm', '.m4v'].includes(ext)) return VideoPlay

  // 音频类
  if (['.mp3', '.wav', '.flac', '.aac', '.ogg', '.wma', '.m4a'].includes(ext)) return Headset

  // 压缩包
  if (['.zip', '.rar', '.7z', '.tar', '.gz', '.bz2'].includes(ext)) return Files

  // 代码文件
  if (['.js', '.ts', '.jsx', '.tsx', '.vue', '.py', '.java', '.c', '.cpp', '.h', '.cs', '.go', '.rs', '.php', '.rb', '.swift', '.kt'].includes(ext)) return Tickets

  // 配置文件
  if (['.json', '.xml', '.yaml', '.yml', '.ini', '.conf', '.env'].includes(ext)) return DataBoard

  return Document
}

/**
 * 文件图标颜色：与 getFileIcon 分类一一对应的实色（取 Shares 页渐变配色的主色）
 */
export function getFileIconColor(item: IconItem): string {
  if (item.isFolder) return '#ffc007'
  const ext = getExtension(item)
  if (['.pdf'].includes(ext)) return '#ef4444'
  if (['.doc', '.docx'].includes(ext)) return '#3b82f6'
  if (['.xls', '.xlsx', '.csv'].includes(ext)) return '#10b981'
  if (['.ppt', '.pptx'].includes(ext)) return '#f97316'
  if (['.txt', '.md', '.rtf'].includes(ext)) return '#6b7280'
  if (['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.svg', '.ico'].includes(ext)) return '#14b8a6'
  if (['.mp4', '.avi', '.mkv', '.mov', '.wmv', '.flv', '.webm', '.m4v'].includes(ext)) return '#8b5cf6'
  if (['.mp3', '.wav', '.flac', '.aac', '.ogg', '.wma', '.m4a'].includes(ext)) return '#ec4899'
  if (['.zip', '.rar', '.7z', '.tar', '.gz', '.bz2'].includes(ext)) return '#a16207'
  if (['.js', '.ts', '.jsx', '.tsx', '.vue', '.py', '.java', '.c', '.cpp', '.h', '.cs', '.go', '.rs', '.php', '.rb', '.swift', '.kt'].includes(ext)) return '#6366f1'
  if (['.json', '.xml', '.yaml', '.yml', '.ini', '.conf', '.env'].includes(ext)) return '#475569'
  return '#909399'
}

// 存储池状态语义（后端枚举口径）：1=Healthy 2=Degraded 3=Critical 4=Rebuilding 5=Offline
// 修正记录：原 Storage.vue 将 2/3/4 分别解释为警告/错误/离线，与枚举不符；默认分支原两视图相反（未知 vs 良好），此处统一
export function getPoolStatusInfo(status: number): { text: string; type: 'success' | 'warning' | 'danger' | 'info'; color: string } {
  switch (status) {
    case 1: return { text: i18n.global.t('storage.healthy'), type: 'success', color: '#67C23A' }
    case 2: return { text: i18n.global.t('dashboard.degraded'), type: 'warning', color: '#E6A23C' }
    case 3: return { text: i18n.global.t('dashboard.critical'), type: 'danger', color: '#F56C6C' }
    case 4: return { text: i18n.global.t('dashboard.rebuilding'), type: 'info', color: '#409EFF' }
    case 5: return { text: i18n.global.t('dashboard.offline'), type: 'info', color: '#909399' }
    default: return { text: i18n.global.t('dashboard.unknown'), type: 'info', color: '#909399' }
  }
}

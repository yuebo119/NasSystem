/**
 * 备份路径纯函数（ROADMAP Phase 1）。
 *
 * 【为什么单独抽出】备份页的上传循环依赖三个路径换算：浏览器 File 的
 * webkitRelativePath → 清单相对路径、相对路径 → 上传目标目录、相对路径
 * → 文件名。这三个换算是纯逻辑且各有边界（空目录、根级文件、反斜杠），
 * 放在组件里只能靠视图层测试覆盖，抽出来可直接单测。
 */

/** 清单相对路径：webkitdirectory 整夹选择时相对路径在 webkitRelativePath 上 */
export function relativePathOf(file: File): string {
  const withPath = file as File & { webkitRelativePath?: string }
  return (withPath.webkitRelativePath || file.name).replace(/\\/g, '/')
}

/** 相对路径 → 标准上传端点的目标目录（不含文件名） */
export function destinationDirOf(backupPath: string, relativePath: string): string {
  const lastSlash = relativePath.lastIndexOf('/')
  const dir = lastSlash > 0 ? relativePath.slice(0, lastSlash) : ''
  return dir ? `${backupPath}/${dir}` : backupPath
}

/** 相对路径 → 文件名（最后一段） */
export function fileNameOf(relativePath: string): string {
  const lastSlash = relativePath.lastIndexOf('/')
  return lastSlash >= 0 ? relativePath.slice(lastSlash + 1) : relativePath
}

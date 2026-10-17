import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'
import AutoImport from 'unplugin-auto-import/vite'
import Components from 'unplugin-vue-components/vite'
import { ElementPlusResolver } from 'unplugin-vue-components/resolvers'
import { fileURLToPath, URL } from 'node:url'

// 插件链与 vite.config.ts 对齐：element-plus 组件解析与自动导入在测试
// 环境必须同在，否则模板里的 el-* 组件未注册，作用域插槽以无参调用直接
// 崩在解构上（实测 "Cannot destructure property 'row' of 'undefined'"）。
// importStyle 关闭：happy-dom 无布局，样式注入反而让 node 侧报
// "Unknown file extension .css"。dts 显式关闭：auto-imports.d.ts 与
// components.d.ts 是 dev server 的产物，测试不得改写工作区文件
export default defineConfig({
  plugins: [
    vue(),
    AutoImport({
      imports: ['vue', 'vue-router', 'pinia'],
      resolvers: [ElementPlusResolver({ importStyle: false })],
      dts: false
    }),
    Components({
      resolvers: [ElementPlusResolver({ importStyle: false })],
      dts: false
    })
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'happy-dom',
    include: ['src/**/*.{test,spec}.ts'],
  },
})

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import AutoImport from 'unplugin-auto-import/vite'
import Components from 'unplugin-vue-components/vite'
import { ElementPlusResolver } from 'unplugin-vue-components/resolvers'
import { fileURLToPath, URL } from 'node:url'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    // 自动导入 Vue API 和 Element Plus
    AutoImport({
      imports: ['vue', 'vue-router', 'pinia'],
      resolvers: [ElementPlusResolver()],
      dts: 'src/auto-imports.d.ts',
    }),
    // 自动注册组件
    Components({
      resolvers: [ElementPlusResolver()],
      dts: 'src/components.d.ts',
    }),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  server: {
    port: 6060,
    proxy: {
      '/api': {
        target: 'http://localhost:6050',
        changeOrigin: true,
      },
      '/hubs': {
        target: 'http://localhost:6050',
        changeOrigin: true,
        ws: true,
      },
    },
  },
  build: {
    outDir: '../src/NasSystem.Api/wwwroot',
    emptyOutDir: true,
    rollupOptions: {
      output: {
        // vite 8（Rolldown）起 manualChunks 对象字面量形式不再入类型，改函数形式；
        // 各 chunk 与升级前的划分保持一致
        manualChunks(id) {
          if (!id.includes('node_modules')) return undefined
          // table-v2 单独成块：约 565KB，只有 Files.vue 的 script 内 h() 用到，
          // 却因「element-plus 全归一块」被强制打进全站共享 chunk——每个页面
          // 首次访问都要为它付体积。拆开后只有文件页拉它。
          // 注意排除 /style/ 路径：main.ts 里有
          // `import 'element-plus/es/components/table-v2/style/css'`（表头顺序
          // 依赖这条副作用），若连它一起归到本块，入口就会静态引入本块，
          // 拆分等于白做（实测会被 preload 进 index.html）。样式留在
          // element-plus 主块，只有 JS 按需
          if (!id.includes('/style/')
            && (id.includes('element-plus/es/components/table-v2')
              || id.includes('element-plus/es/components/virtual-list'))) {
            return 'element-plus-table-v2'
          }
          if (id.includes('element-plus') || id.includes('@element-plus')) return 'element-plus'
          if (id.includes('@microsoft/signalr')) return 'signalr'
          if (id.includes('vue-i18n')) return 'vue-i18n'
          if (id.includes('axios')) return 'axios'
          if (id.includes('/vue/') || id.includes('vue-router') || id.includes('pinia'))
            return 'vue-vendor'
          return undefined
        },
      },
    },
  },
})

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

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
        manualChunks: {
          'element-plus': ['element-plus'],
          'vue-vendor': ['vue', 'vue-router', 'pinia'],
          // 此前 signalr/vue-i18n/axios 全部落入 index chunk，首屏体积虚高
          'signalr': ['@microsoft/signalr'],
          'vue-i18n': ['vue-i18n'],
          'axios': ['axios'],
        },
      },
    },
  },
})

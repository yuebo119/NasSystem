<script setup lang="ts">
import { ElConfigProvider } from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'

// 登录态校验只在 main.ts 的挂载前做一次，结果进 userStore；
// 路由守卫按 initialized 标记决定是否补验。这里原先还有一次无条件
// checkAuth，与 main.ts 那次重复发 /users/me（实测首屏共 3 次），已删。
</script>

<template>
  <el-config-provider :locale="zhCn">
    <router-view />
  </el-config-provider>
</template>

<style lang="scss">
#app {
  width: 100%;
  height: 100vh;
  overflow: hidden;
}
</style>

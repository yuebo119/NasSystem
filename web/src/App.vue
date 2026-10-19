<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElConfigProvider } from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import en from 'element-plus/es/locale/lang/en'

// 登录态校验只在 main.ts 的挂载前做一次，结果进 userStore；
// 路由守卫按 initialized 标记决定是否补验。这里原先还有一次无条件
// checkAuth，与 main.ts 那次重复发 /users/me（实测首屏共 3 次），已删。

// Element Plus 的内置文案（分页/空态/字数统计/日期选择器）走自己的 i18n，
// 与应用语言无关。硬编码中文时英文界面会混中文（E2E 走查实证：显示名的
// 字数统计在英文下显示"个字符"）。按应用语言切换组件语言
const { locale } = useI18n()
const elementLocale = computed(() => (locale.value === 'en-US' ? en : zhCn))
</script>

<template>
  <el-config-provider :locale="elementLocale">
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

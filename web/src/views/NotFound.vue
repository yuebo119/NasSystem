<script setup lang="ts">
import { useRouter } from 'vue-router'

const router = useRouter()
</script>

<template>
  <div class="not-found">
    <el-icon size="120" color="var(--el-color-info)">
      <Warning />
    </el-icon>
    <h1>404</h1>
    <p>抱歉，您访问的页面不存在</p>
    <el-button type="primary" @click="router.push('/')">
      返回首页
    </el-button>
  </div>
</template>

<style lang="scss" scoped>
.not-found {
  height: 100vh;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 16px;

  h1 {
    font-size: 72px;
    font-weight: 700;
    color: var(--el-text-color-primary);
    margin: 0;
  }

  p {
    font-size: 16px;
    color: var(--el-text-color-secondary);
  }
}
</style>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useUserStore } from '@/stores/user'
import { ElMessage } from 'element-plus'
import { User, Lock } from '@element-plus/icons-vue'

const { t } = useI18n()
const router = useRouter()
const route = useRoute()
const userStore = useUserStore()

const loading = ref(false)
const form = reactive({
  username: '',
  password: '',
  rememberMe: false
})

const rules = {
  username: [{ required: true, message: () => t('login.usernameRequired'), trigger: 'blur' }],
  password: [{ required: true, message: () => t('login.passwordRequired'), trigger: 'blur' }]
}

async function handleLogin() {
  // 提交前 guard：el-form 根元素不监听原生 submit，rules 的 trigger:'blur'
  // 只覆盖"聚焦后失焦"路径，用户不聚焦直接点提交时空值会直达后端。
  // 这里用手写 guard 兜底（与 Settings.changePassword 同模式）：不依赖
  // el-form 的校验链，行为确定；el-form rules 保留作聚焦时的即时提示
  if (!form.username || !form.password) {
    ElMessage.warning(!form.username ? t('login.usernameRequired') : t('login.passwordRequired'))
    return
  }

  loading.value = true
  try {
    const success = await userStore.login({
      username: form.username,
      password: form.password
    })
    
    if (success) {
      ElMessage.success(t('login.loginSuccess'))
      const redirect = route.query.redirect as string || '/dashboard'
      router.push(redirect)
    } else {
      ElMessage.error(t('login.loginFailed'))
    }
  } catch {
    ElMessage.error(t('login.loginFailed'))
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-container">
    <div class="login-card">
      <div class="login-header">
        <el-icon size="48" color="var(--el-color-primary)">
          <Box />
        </el-icon>
        <h1>NAS System</h1>
        <p>{{ t('login.subtitle') }}</p>
      </div>

      <el-form
        :model="form"
        :rules="rules"
        class="login-form"
        @submit.prevent="handleLogin"
      >
        <el-form-item prop="username">
          <el-input
            v-model="form.username"
            :placeholder="t('login.usernamePlaceholder')"
            size="large"
            :prefix-icon="User"
          />
        </el-form-item>

        <el-form-item prop="password">
          <el-input
            v-model="form.password"
            type="password"
            :placeholder="t('login.passwordPlaceholder')"
            size="large"
            :prefix-icon="Lock"
            show-password
          />
        </el-form-item>

        <el-form-item>
          <el-checkbox v-model="form.rememberMe">{{ t('login.rememberMe') }}</el-checkbox>
        </el-form-item>

        <el-form-item>
          <el-button
            type="primary"
            size="large"
            :loading="loading"
            native-type="submit"
            class="login-button"
          >
            {{ loading ? t('login.logging') : t('login.loginBtn') }}
          </el-button>
        </el-form-item>
      </el-form>

      <div class="login-footer">
        <p>{{ $t('login.defaultAdmin') }}</p>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
.login-container {
  width: 100%;
  height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
}

.login-card {
  width: 400px;
  padding: 40px;
  background: var(--el-bg-color);
  border-radius: 16px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.15);
  border: 1px solid var(--el-border-color-lighter);
}

.login-header {
  text-align: center;
  margin-bottom: 32px;

  h1 {
    margin: 16px 0 8px;
    font-size: 24px;
    color: var(--el-text-color-primary);
  }

  p {
    color: var(--el-text-color-secondary);
    font-size: 14px;
  }
}

.login-form {
  .login-button {
    width: 100%;
  }
}

.login-footer {
  text-align: center;
  margin-top: 24px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
</style>

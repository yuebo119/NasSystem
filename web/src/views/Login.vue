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
      password: form.password,
      // 记住我：勾选后端按 RememberMeExpirationDays 签更长有效期的令牌。
      // 2026-10-21 审计前这里只传账号密码，复选框是死控件
      rememberMe: form.rememberMe
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
      <!-- macOS 标志性交通灯：纯装饰，尺寸克制 -->
      <div class="traffic-lights" aria-hidden="true">
        <span class="light close"></span>
        <span class="light minimize"></span>
        <span class="light zoom"></span>
      </div>

      <div class="login-header">
        <div class="login-logo">
          <el-icon size="32" color="#fff">
            <Box />
          </el-icon>
        </div>
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
// macOS 登录窗：Apple 灰画布 + 居中圆角卡片 + 柔和分层阴影。
// 装饰性的交通灯是 mac 风识别元素，尺寸与不透明度保持克制
.login-container {
  width: 100%;
  height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--el-bg-color-page);
  // 画面中心微弱提亮，模拟 mac 桌面光感，不抢内容
  background-image: radial-gradient(ellipse 80% 60% at 50% 40%, var(--el-fill-color-light), transparent);
}

.login-card {
  width: 400px;
  padding: 40px;
  background: var(--el-bg-color);
  border-radius: 16px;
  box-shadow: var(--card-shadow);
  border: 1px solid var(--mac-hairline);
  position: relative;
}

.traffic-lights {
  position: absolute;
  top: 16px;
  left: 16px;
  display: flex;
  gap: 8px;

  .light {
    width: 12px;
    height: 12px;
    border-radius: 50%;
    opacity: 0.9;
  }

  .close {
    background: #ff5f57;
  }

  .minimize {
    background: #febc2e;
  }

  .zoom {
    background: #28c840;
  }
}

.login-header {
  text-align: center;
  margin-bottom: 32px;

  .login-logo {
    width: 64px;
    height: 64px;
    margin: 0 auto 16px;
    border-radius: 16px;
    background: linear-gradient(135deg, var(--el-color-primary), var(--el-color-primary-light-3));
    display: flex;
    align-items: center;
    justify-content: center;
    box-shadow: 0 4px 16px color-mix(in srgb, var(--el-color-primary) 32%, transparent);
  }

  h1 {
    margin: 0 0 4px;
    font-size: 22px;
    color: var(--el-text-color-primary);
    letter-spacing: -0.3px;
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

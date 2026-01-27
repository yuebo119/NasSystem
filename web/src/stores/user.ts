import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { User } from '@/types'
import { userApi } from '@/api/user'

export const useUserStore = defineStore('user', () => {
  // 状态
  const user = ref<User | null>(null)
  const token = ref<string | null>(localStorage.getItem('token'))
  const initialized = ref(false)

  // 计算属性 - 只要有 token 就认为可能已认证，等 checkAuth 验证
  const isAuthenticated = computed(() => !!token.value && (!!user.value || !initialized.value))
  const isAdmin = computed(() => user.value?.role === 'SuperAdmin' || user.value?.role === 'Admin')
  const username = computed(() => user.value?.username || '')
  const displayName = computed(() => user.value?.displayName || user.value?.username || '')

  // 方法
  async function login(credentials: { username: string; password: string }) {
    const response = await userApi.login(credentials)
    if (response.success && response.data) {
      token.value = response.data.token
      user.value = response.data.user
      localStorage.setItem('token', response.data.token)
      initialized.value = true
      return true
    }
    return false
  }

  async function logout() {
    try {
      await userApi.logout()
    } finally {
      token.value = null
      user.value = null
      initialized.value = true
      localStorage.removeItem('token')
    }
  }

  async function checkAuth() {
    if (!token.value) {
      initialized.value = true
      return false
    }

    try {
      const response = await userApi.getCurrentUser()
      if (response.success && response.data) {
        user.value = response.data
        initialized.value = true
        return true
      }
    } catch {
      // token 无效，清除
      token.value = null
      user.value = null
      localStorage.removeItem('token')
    }
    initialized.value = true
    return false
  }

  function updateUser(updates: Partial<User>) {
    if (user.value) {
      user.value = { ...user.value, ...updates }
    }
  }

  return {
    user,
    token,
    initialized,
    isAuthenticated,
    isAdmin,
    username,
    displayName,
    login,
    logout,
    checkAuth,
    updateUser
  }
})

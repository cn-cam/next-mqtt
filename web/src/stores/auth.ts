import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { api, TOKEN_KEY, USER_KEY } from '../api/client'

export const useAuthStore = defineStore('auth', () => {
  const token = ref(localStorage.getItem(TOKEN_KEY) ?? '')
  const username = ref(localStorage.getItem(USER_KEY) ?? '')
  const loggingIn = ref(false)

  const isAuthenticated = computed(() => token.value.length > 0)

  async function login(user: string, password: string): Promise<void> {
    loggingIn.value = true

    try {
      const response = await api.login(user, password)
      token.value = response.token
      username.value = response.username
      localStorage.setItem(TOKEN_KEY, response.token)
      localStorage.setItem(USER_KEY, response.username)
    } finally {
      loggingIn.value = false
    }
  }

  function clear(): void {
    token.value = ''
    username.value = ''
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(USER_KEY)
  }

  return { token, username, loggingIn, isAuthenticated, login, clear }
})

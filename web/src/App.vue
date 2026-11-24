<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import ToastHost from './components/ToastHost.vue'
import { useAuthStore } from './stores/auth'
import { t } from './i18n'
import { useToast } from './composables/useToast'

const router = useRouter()
const auth = useAuthStore()
const toast = useToast()

function handleUnauthorized(): void {
  auth.clear()

  if (router.currentRoute.value.name !== 'login') {
    toast.error(t('error.unauthorized'))
    void router.push({ name: 'login' })
  }
}

onMounted(() => window.addEventListener('nextmqtt:unauthorized', handleUnauthorized))
onBeforeUnmount(() => window.removeEventListener('nextmqtt:unauthorized', handleUnauthorized))
</script>

<template>
  <RouterView />
  <ToastHost />
</template>

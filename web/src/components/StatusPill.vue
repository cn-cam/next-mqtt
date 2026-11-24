<script setup lang="ts">
import { computed } from 'vue'
import { t } from '../i18n'

const props = defineProps<{ status: string }>()

const normalized = computed(() => props.status.toLowerCase())

const label = computed(() => {
  switch (normalized.value) {
    case 'alive':
      return t('cluster.alive')
    case 'suspect':
      return t('cluster.suspect')
    case 'dead':
      return t('cluster.dead')
    case 'online':
      return t('common.online')
    case 'connected':
      return t('common.connected')
    case 'offline':
    case 'disconnected':
      return t('common.offline')
    default:
      return props.status
  }
})
</script>

<template>
  <span class="status-pill" :data-status="normalized">
    <span class="status-dot" :class="normalized"></span>
    {{ label }}
  </span>
</template>

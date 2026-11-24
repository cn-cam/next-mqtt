<script setup lang="ts">
import { useToast } from '../composables/useToast'
import Icon from './Icon.vue'

const toast = useToast()
</script>

<template>
  <div class="toast-stack" aria-live="polite">
    <TransitionGroup name="toast">
      <div v-for="item in toast.items" :key="item.id" class="toast" :data-tone="item.tone">
        <Icon :name="item.tone === 'success' ? 'check' : item.tone === 'error' ? 'alert' : 'info'" :size="17" />
        <span>{{ item.message }}</span>
        <button class="toast-close" type="button" @click="toast.dismiss(item.id)">
          <Icon name="close" :size="14" />
        </button>
      </div>
    </TransitionGroup>
  </div>
</template>

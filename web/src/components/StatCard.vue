<script setup lang="ts">
import Icon from './Icon.vue'

withDefaults(
  defineProps<{
    label: string
    value: string | number
    hint?: string
    icon?: string
    tone?: 'cyan' | 'green' | 'violet' | 'amber'
  }>(),
  { tone: 'cyan', icon: 'activity' },
)
</script>

<template>
  <article class="stat-card" :data-tone="tone">
    <div class="stat-icon">
      <Icon :name="icon" :size="20" />
    </div>
    <div class="stat-copy">
      <span class="stat-label">{{ label }}</span>
      <strong class="stat-value">{{ value }}</strong>
      <span v-if="hint" class="stat-hint">{{ hint }}</span>
    </div>
    <slot />
  </article>
</template>

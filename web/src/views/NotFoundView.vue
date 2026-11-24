<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { t } from '../i18n'
</script>

<template>
  <div class="not-found-page">
    <div class="not-found-card">
      <p class="eyebrow">404</p>
      <h1>{{ t('common.noData') }}</h1>
      <p>{{ t('error.loadFailed') }}</p>
      <RouterLink class="button primary" :to="{ name: 'overview' }">{{ t('nav.overview') }}</RouterLink>
    </div>
  </div>
</template>

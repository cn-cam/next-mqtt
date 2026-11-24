<script setup lang="ts">
import { computed } from 'vue'
import { t } from '../i18n'
import Icon from './Icon.vue'

const props = defineProps<{ page: number; pageSize: number; total: number }>()
const emit = defineEmits<{ (event: 'change', page: number): void }>()

const pages = computed(() => Math.max(1, Math.ceil(props.total / props.pageSize)))
const from = computed(() => (props.total === 0 ? 0 : (props.page - 1) * props.pageSize + 1))
const to = computed(() => Math.min(props.page * props.pageSize, props.total))

function go(page: number): void {
  if (page >= 1 && page <= pages.value && page !== props.page) {
    emit('change', page)
  }
}
</script>

<template>
  <div class="pagination">
    <span class="pagination-range">
      {{ t('common.rangeInfo', { from, to, total }) }}
    </span>
    <div class="pagination-controls">
      <button class="icon-button small" type="button" :disabled="page <= 1" @click="go(page - 1)">
        <Icon name="chevronLeft" :size="16" />
      </button>
      <span class="pagination-page">{{ t('common.pageInfo', { page, pages }) }}</span>
      <button class="icon-button small" type="button" :disabled="page >= pages" @click="go(page + 1)">
        <Icon name="chevronRight" :size="16" />
      </button>
    </div>
  </div>
</template>

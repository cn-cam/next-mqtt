<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'
import { t } from '../i18n'
import Icon from './Icon.vue'

const props = withDefaults(
  defineProps<{
    open: boolean
    title: string
    message: string
    confirmLabel?: string
    danger?: boolean
  }>(),
  { confirmLabel: undefined, danger: true },
)

const emit = defineEmits<{ (event: 'confirm'): void; (event: 'cancel'): void }>()

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && props.open) emit('cancel')
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <Teleport to="body">
    <Transition name="modal">
      <div v-if="open" class="modal-backdrop" role="presentation" @click.self="emit('cancel')">
        <div class="modal-card" role="dialog" aria-modal="true" :aria-label="title">
          <div class="modal-icon" :class="{ danger }">
            <Icon :name="danger ? 'alert' : 'info'" :size="22" />
          </div>
          <h2>{{ title }}</h2>
          <p>{{ message }}</p>
          <div class="modal-actions">
            <button class="button ghost" type="button" @click="emit('cancel')">
              {{ t('common.cancel') }}
            </button>
            <button class="button" :class="{ danger }" type="button" @click="emit('confirm')">
              {{ confirmLabel ?? t('common.confirm') }}
            </button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api } from '../api/client'
import type { Paged, RetainedInfo } from '../api/types'
import { useAutoRefresh } from '../composables/useAutoRefresh'
import { useToast } from '../composables/useToast'
import { t } from '../i18n'
import { formatBytes, formatDate, formatQos } from '../utils/format'
import ConfirmDialog from '../components/ConfirmDialog.vue'
import EmptyState from '../components/EmptyState.vue'
import Icon from '../components/Icon.vue'
import Pagination from '../components/Pagination.vue'
import { RouterLink } from 'vue-router'

const toast = useToast()
const page = ref(1)
const pageSize = 15
const search = ref('')
const appliedSearch = ref('')
const deleteTarget = ref<RetainedInfo | null>(null)
const deleteOpen = computed(() => deleteTarget.value !== null)
const data = ref<Paged<RetainedInfo>>({ items: [], total: 0, page: 1, pageSize })

let debounce: number | undefined

async function load(): Promise<void> {
  data.value = await api.retained({
    search: appliedSearch.value,
    page: page.value,
    pageSize,
  })
}

const { refresh, refreshing, error } = useAutoRefresh(load, 5000)

watch(page, () => void refresh())

function onSearchInput(): void {
  window.clearTimeout(debounce)
  debounce = window.setTimeout(() => {
    appliedSearch.value = search.value
    page.value = 1
    void refresh()
  }, 250)
}

function clearSearch(): void {
  search.value = ''
  appliedSearch.value = ''
  page.value = 1
  void refresh()
}

async function confirmDelete(): Promise<void> {
  if (!deleteTarget.value) return
  const topic = deleteTarget.value.topic
  deleteTarget.value = null

  try {
    await api.deleteRetained(topic)
    toast.success(t('retained.deleted'))
    await refresh()
  } catch (cause) {
    toast.error(cause instanceof Error ? cause.message : t('common.error'))
  }
}
</script>

<template>
  <div class="view">
    <header class="page-header">
      <div>
        <p class="eyebrow">{{ t('nav.operations') }}</p>
        <h2>{{ t('retained.title') }}</h2>
        <p>{{ t('retained.subtitle') }}</p>
      </div>
      <div class="header-actions">
        <RouterLink class="button ghost" :to="{ name: 'publish' }">
          <Icon name="plus" :size="16" />
          {{ t('overview.actionPublish') }}
        </RouterLink>
        <button class="button ghost" type="button" :disabled="refreshing" @click="refresh">
          <Icon name="refresh" :size="16" :class="{ spin: refreshing }" />
          {{ t('common.refresh') }}
        </button>
      </div>
    </header>

    <div v-if="error" class="alert error">
      <Icon name="alert" :size="17" />
      <span>{{ error }}</span>
    </div>

    <div class="toolbar">
      <div class="search-field">
        <Icon name="search" :size="16" />
        <input
          v-model="search"
          type="search"
          :placeholder="t('retained.searchPlaceholder')"
          @input="onSearchInput"
        />
        <button v-if="search" class="clear-search" type="button" @click="clearSearch">
          <Icon name="close" :size="14" />
        </button>
      </div>
      <span class="toolbar-hint">{{ data.total }} {{ t('common.total') }}</span>
    </div>

    <section class="panel table-panel">
      <div class="table-scroll">
        <table class="data-table">
          <thead>
            <tr>
              <th>{{ t('retained.topic') }}</th>
              <th class="numeric">{{ t('retained.size') }}</th>
              <th class="numeric">{{ t('retained.qos') }}</th>
              <th>{{ t('retained.storedAt') }}</th>
              <th>{{ t('retained.expires') }}</th>
              <th class="actions-column"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="message in data.items" :key="message.topic">
              <td class="mono strong">{{ message.topic }}</td>
              <td class="numeric">{{ formatBytes(message.payloadBytes) }}</td>
              <td class="numeric"><span class="qos-chip">QoS {{ formatQos(message.qoS) }}</span></td>
              <td>{{ formatDate(message.storedAt) }}</td>
              <td class="muted">{{ message.expiresAt ? formatDate(message.expiresAt) : t('common.never') }}</td>
              <td class="actions-column">
                <button
                  class="icon-button small danger-hover"
                  type="button"
                  :title="t('common.delete')"
                  @click="deleteTarget = message"
                >
                  <Icon name="trash" :size="15" />
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <EmptyState
        v-if="data.items.length === 0"
        icon="retained"
        :title="t('retained.empty')"
        :hint="t('retained.emptyHint')"
      />

      <Pagination
        :page="page"
        :page-size="pageSize"
        :total="data.total"
        @change="page = $event"
      />
    </section>

    <ConfirmDialog
      :open="deleteOpen"
      :title="t('retained.deleteTitle')"
      :message="t('retained.deleteMessage', { topic: deleteTarget?.topic ?? '' })"
      :confirm-label="t('common.delete')"
      @confirm="confirmDelete"
      @cancel="deleteTarget = null"
    />
  </div>
</template>

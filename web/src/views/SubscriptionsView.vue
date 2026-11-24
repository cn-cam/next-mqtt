<script setup lang="ts">
import { ref, watch } from 'vue'
import { api } from '../api/client'
import type { Paged, SubscriptionInfo } from '../api/types'
import { useAutoRefresh } from '../composables/useAutoRefresh'
import { t } from '../i18n'
import { formatQos, formatRetainHandling } from '../utils/format'
import EmptyState from '../components/EmptyState.vue'
import Icon from '../components/Icon.vue'
import Pagination from '../components/Pagination.vue'

const page = ref(1)
const pageSize = 15
const search = ref('')
const appliedSearch = ref('')
const data = ref<Paged<SubscriptionInfo>>({ items: [], total: 0, page: 1, pageSize })

let debounce: number | undefined

async function load(): Promise<void> {
  data.value = await api.subscriptions({
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
</script>

<template>
  <div class="view">
    <header class="page-header">
      <div>
        <p class="eyebrow">{{ t('nav.operations') }}</p>
        <h2>{{ t('subs.title') }}</h2>
        <p>{{ t('subs.subtitle') }}</p>
      </div>
      <div class="header-actions">
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
          :placeholder="t('subs.searchPlaceholder')"
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
              <th>{{ t('subs.client') }}</th>
              <th>{{ t('subs.filter') }}</th>
              <th>{{ t('subs.group') }}</th>
              <th class="numeric">{{ t('subs.qos') }}</th>
              <th>{{ t('subs.options') }}</th>
              <th class="numeric">{{ t('subs.identifier') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="subscription in data.items" :key="`${subscription.clientId}-${subscription.filter}`">
              <td class="mono strong">{{ subscription.clientId }}</td>
              <td>
                <span class="mono">{{ subscription.filter }}</span>
              </td>
              <td>
                <span v-if="subscription.sharedGroup" class="tag violet">{{ subscription.sharedGroup }}</span>
                <span v-else class="muted">—</span>
              </td>
              <td class="numeric"><span class="qos-chip">QoS {{ formatQos(subscription.qoS) }}</span></td>
              <td>
                <div class="chip-row">
                  <span v-if="subscription.noLocal" class="tag">{{ t('subs.noLocal') }}</span>
                  <span v-if="subscription.retainAsPublished" class="tag">{{ t('subs.rap') }}</span>
                  <span v-if="subscription.retainHandling !== 'SendAtSubscribe'" class="tag">
                    RH {{ formatRetainHandling(subscription.retainHandling) }}
                  </span>
                  <span v-if="!subscription.noLocal && !subscription.retainAsPublished && subscription.retainHandling === 'SendAtSubscribe'" class="muted">—</span>
                </div>
              </td>
              <td class="numeric muted">{{ subscription.identifier ?? '—' }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <EmptyState
        v-if="data.items.length === 0"
        icon="subscriptions"
        :title="t('common.noData')"
        :hint="t('subs.subtitle')"
      />

      <Pagination
        :page="page"
        :page-size="pageSize"
        :total="data.total"
        @change="page = $event"
      />
    </section>
  </div>
</template>

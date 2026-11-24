<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api } from '../api/client'
import type { ClientInfo, Paged, SubscriptionInfo } from '../api/types'
import { useAutoRefresh } from '../composables/useAutoRefresh'
import { useToast } from '../composables/useToast'
import { t } from '../i18n'
import { formatDate, formatExpiry, formatRelative, formatQos } from '../utils/format'
import ConfirmDialog from '../components/ConfirmDialog.vue'
import EmptyState from '../components/EmptyState.vue'
import Icon from '../components/Icon.vue'
import Pagination from '../components/Pagination.vue'
import StatusPill from '../components/StatusPill.vue'

const toast = useToast()
const page = ref(1)
const pageSize = 10
const search = ref('')
const appliedSearch = ref('')
const data = ref<Paged<ClientInfo>>({ items: [], total: 0, page: 1, pageSize })
const selected = ref<ClientInfo | null>(null)
const subscriptions = ref<SubscriptionInfo[]>([])
const drawerLoading = ref(false)
const kickTarget = ref<ClientInfo | null>(null)
const kickOpen = computed(() => kickTarget.value !== null)

let debounce: number | undefined

async function load(): Promise<void> {
  data.value = await api.clients({
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

async function openClient(client: ClientInfo): Promise<void> {
  selected.value = client
  drawerLoading.value = true
  subscriptions.value = []

  try {
    const response = await api.subscriptions({ search: client.clientId, pageSize: 200 })
    subscriptions.value = response.items.filter((item) => item.clientId === client.clientId)
  } catch {
    subscriptions.value = []
  } finally {
    drawerLoading.value = false
  }
}

function closeDrawer(): void {
  selected.value = null
}

function requestKick(client: ClientInfo): void {
  kickTarget.value = client
}

async function confirmKick(): Promise<void> {
  if (!kickTarget.value) return
  const target = kickTarget.value
  kickTarget.value = null

  try {
    await api.kickClient(target.clientId)
    toast.success(t('clients.kicked'))
    selected.value = null
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
        <h2>{{ t('clients.title') }}</h2>
        <p>{{ t('clients.subtitle') }}</p>
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
          :placeholder="t('clients.searchPlaceholder')"
          @input="onSearchInput"
        />
        <button v-if="search" class="clear-search" type="button" @click="clearSearch">
          <Icon name="close" :size="14" />
        </button>
      </div>
      <span class="toolbar-hint">{{ t('clients.showingOffline') }}</span>
    </div>

    <section class="panel table-panel">
      <div class="table-scroll">
        <table class="data-table">
          <thead>
            <tr>
              <th>{{ t('common.status') }}</th>
              <th>{{ t('clients.clientId') }}</th>
              <th>{{ t('clients.username') }}</th>
              <th>{{ t('clients.protocol') }}</th>
              <th>{{ t('clients.remote') }}</th>
              <th class="numeric">{{ t('clients.subscriptions') }}</th>
              <th>{{ t('clients.lastSeen') }}</th>
              <th class="actions-column"></th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="client in data.items"
              :key="client.clientId"
              class="clickable"
              @click="openClient(client)"
            >
              <td>
                <StatusPill :status="client.connected ? 'online' : 'offline'" />
              </td>
              <td>
                <span class="mono strong">{{ client.clientId }}</span>
                <span v-if="client.isSuperuser" class="mini-badge">{{ t('clients.superuser') }}</span>
              </td>
              <td>{{ client.username || '—' }}</td>
              <td><span class="code-chip">{{ client.protocolVersion }}</span></td>
              <td class="mono muted">{{ client.remoteAddress || '—' }}</td>
              <td class="numeric">{{ client.subscriptions }}</td>
              <td>{{ formatRelative(client.lastSeen) }}</td>
              <td class="actions-column">
                <button
                  class="icon-button small danger-hover"
                  type="button"
                  :title="t('clients.kick')"
                  @click.stop="requestKick(client)"
                >
                  <Icon name="logout" :size="15" />
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <EmptyState
        v-if="data.items.length === 0"
        icon="clients"
        :title="appliedSearch ? t('common.noData') : t('overview.noClients')"
        :hint="appliedSearch ? t('clients.searchPlaceholder') : t('clients.showingOffline')"
      />

      <Pagination
        :page="page"
        :page-size="pageSize"
        :total="data.total"
        @change="page = $event"
      />
    </section>

    <Teleport to="body">
      <Transition name="drawer">
        <div v-if="selected" class="drawer-backdrop" @click.self="closeDrawer">
          <aside class="drawer-card">
            <header class="drawer-header">
              <div>
                <p class="eyebrow">{{ t('clients.detailsTitle') }}</p>
                <h3 class="mono">{{ selected.clientId }}</h3>
              </div>
              <button class="icon-button" type="button" @click="closeDrawer">
                <Icon name="close" :size="18" />
              </button>
            </header>

            <div class="drawer-status">
              <StatusPill :status="selected.connected ? 'online' : 'offline'" />
              <span class="code-chip">{{ selected.protocolVersion }}</span>
              <span v-if="selected.isSuperuser" class="mini-badge">{{ t('clients.superuser') }}</span>
            </div>

            <dl class="detail-grid">
              <div>
                <dt>{{ t('clients.username') }}</dt>
                <dd>{{ selected.username || '—' }}</dd>
              </div>
              <div>
                <dt>{{ t('clients.remote') }}</dt>
                <dd class="mono">{{ selected.remoteAddress || '—' }}</dd>
              </div>
              <div>
                <dt>{{ t('clients.connectedAt') }}</dt>
                <dd>{{ formatDate(selected.connectedAt) }}</dd>
              </div>
              <div>
                <dt>{{ t('clients.lastSeen') }}</dt>
                <dd>{{ formatDate(selected.lastSeen) }}</dd>
              </div>
              <div>
                <dt>{{ t('clients.expiry') }}</dt>
                <dd>{{ formatExpiry(selected.sessionExpiryInterval) }}</dd>
              </div>
              <div>
                <dt>{{ t('clients.inflight') }} / {{ t('clients.queued') }}</dt>
                <dd>{{ selected.inflightMessages }} / {{ selected.queuedMessages }}</dd>
              </div>
            </dl>

            <h4 class="drawer-section-title">{{ t('clients.subscriptionList') }}</h4>

            <div v-if="drawerLoading" class="inline-loading">{{ t('common.loading') }}…</div>
            <ul v-else-if="subscriptions.length" class="subscription-list">
              <li v-for="subscription in subscriptions" :key="subscription.filter">
                <span class="mono">{{ subscription.filter }}</span>
                <span class="qos-chip">QoS {{ formatQos(subscription.qoS) }}</span>
              </li>
            </ul>
            <p v-else class="muted">{{ t('clients.noSubscriptions') }}</p>

            <footer class="drawer-footer">
              <button class="button danger" type="button" :disabled="!selected.connected" @click="requestKick(selected)">
                <Icon name="logout" :size="16" />
                {{ t('clients.kick') }}
              </button>
            </footer>
          </aside>
        </div>
      </Transition>
    </Teleport>

    <ConfirmDialog
      :open="kickOpen"
      :title="t('clients.kickTitle')"
      :message="t('clients.kickMessage', { client: kickTarget?.clientId ?? '' })"
      :confirm-label="t('clients.kick')"
      @confirm="confirmKick"
      @cancel="kickTarget = null"
    />
  </div>
</template>

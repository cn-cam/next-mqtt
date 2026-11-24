<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { api } from '../api/client'
import type { BrokerStats, ClientInfo, ClusterInfo } from '../api/types'
import { useAutoRefresh } from '../composables/useAutoRefresh'
import { t } from '../i18n'
import { formatBytes, formatNumber, formatRelative } from '../utils/format'
import EmptyState from '../components/EmptyState.vue'
import Icon from '../components/Icon.vue'
import Sparkline from '../components/Sparkline.vue'
import StatCard from '../components/StatCard.vue'
import StatusPill from '../components/StatusPill.vue'

const stats = ref<BrokerStats | null>(null)
const clients = ref<ClientInfo[]>([])
const cluster = ref<ClusterInfo | null>(null)

const { refresh, refreshing, lastUpdated, error } = useAutoRefresh(async () => {
  const [statsResponse, clientsResponse, clusterResponse] = await Promise.all([
    api.stats(),
    api.clients({ pageSize: 5 }),
    api.cluster(),
  ])

  stats.value = statsResponse
  clients.value = clientsResponse.items
  cluster.value = clusterResponse
}, 3000)

const rate = computed(() => stats.value?.messagesPerSecond.at(-1) ?? 0)
const clustered = computed(() => (cluster.value?.nodes.length ?? 0) > 1)
const nodes = computed(() => cluster.value?.nodes ?? [])

const health = computed(() => [
  { label: t('overview.connectionsOpened'), value: formatNumber(stats.value?.connectionsOpened ?? 0) },
  { label: t('overview.authFailures'), value: formatNumber(stats.value?.authFailures ?? 0), alert: (stats.value?.authFailures ?? 0) > 0 },
  { label: t('overview.dropped'), value: formatNumber(stats.value?.droppedMessages ?? 0) },
  { label: t('overview.retransmissions'), value: formatNumber(stats.value?.retransmissions ?? 0) },
  { label: t('overview.bytesIn'), value: formatBytes(stats.value?.bytesReceived ?? 0) },
  { label: t('overview.bytesOut'), value: formatBytes(stats.value?.bytesSent ?? 0) },
])
</script>

<template>
  <div class="view">
    <header class="page-header">
      <div>
        <p class="eyebrow">{{ t('common.live') }}</p>
        <h2>{{ t('overview.title') }}</h2>
        <p>{{ t('overview.subtitle') }}</p>
      </div>
      <div class="header-actions">
        <span v-if="lastUpdated" class="updated-at">{{ formatRelative(lastUpdated.toISOString()) }}</span>
        <button class="button ghost" type="button" :disabled="refreshing" @click="refresh">
          <Icon name="refresh" :size="16" :class="{ spin: refreshing }" />
          {{ t('common.refresh') }}
        </button>
      </div>
    </header>

    <div v-if="error" class="alert error">
      <Icon name="alert" :size="17" />
      <span>{{ error }}</span>
      <button class="button ghost small" type="button" @click="refresh">{{ t('common.retry') }}</button>
    </div>

    <div v-if="stats" class="stat-grid">
      <StatCard
        :label="t('overview.connectedClients')"
        :value="formatNumber(stats.connectedClients)"
        :hint="`${formatNumber(stats.sessions)} ${t('overview.sessions').toLowerCase()}`"
        icon="clients"
        tone="cyan"
      />
      <StatCard
        :label="t('overview.subscriptions')"
        :value="formatNumber(stats.subscriptions)"
        :hint="`${formatNumber(stats.subscriptionsCreated)} ${t('common.total')}`"
        icon="subscriptions"
        tone="violet"
      />
      <StatCard
        :label="t('overview.retained')"
        :value="formatNumber(stats.retainedMessages)"
        icon="retained"
        tone="amber"
      />
      <StatCard
        :label="t('overview.publishRate')"
        :value="`${formatNumber(rate)} ${t('overview.perSecond')}`"
        :hint="`${formatNumber(stats.publishesReceived)} ${t('overview.totalPublished').toLowerCase()}`"
        icon="activity"
        tone="green"
      />
    </div>

    <div v-if="stats" class="content-grid wide-left">
      <section class="panel chart-panel">
        <header class="panel-header">
          <div>
            <h3>{{ t('overview.throughput') }}</h3>
            <p>{{ t('overview.throughputHint') }}</p>
          </div>
          <span class="chart-value">{{ formatNumber(rate) }} <small>{{ t('overview.perSecond') }}</small></span>
        </header>

        <Sparkline :values="stats.messagesPerSecond" :height="132" accent="#22d3ee" />

        <footer class="chart-footer">
          <div>
            <span>{{ t('overview.totalPublished') }}</span>
            <strong>{{ formatNumber(stats.publishesReceived) }}</strong>
          </div>
          <div>
            <span>{{ t('overview.totalDelivered') }}</span>
            <strong>{{ formatNumber(stats.publishesSent) }}</strong>
          </div>
          <div>
            <span>{{ t('overview.bytesOut') }}</span>
            <strong>{{ formatBytes(stats.bytesSent) }}</strong>
          </div>
        </footer>
      </section>

      <section class="panel">
        <header class="panel-header">
          <div>
            <h3>{{ t('overview.brokerHealth') }}</h3>
            <p>{{ stats.collectedAt ? formatRelative(stats.collectedAt) : '' }}</p>
          </div>
          <span class="pulse-dot"></span>
        </header>

        <dl class="metric-list">
          <div v-for="item in health" :key="item.label" class="metric-row">
            <dt>{{ item.label }}</dt>
            <dd :class="{ 'is-alert': item.alert }">{{ item.value }}</dd>
          </div>
        </dl>
      </section>
    </div>

    <div class="content-grid">
      <section class="panel">
        <header class="panel-header">
          <div>
            <h3>{{ t('overview.nodes') }}</h3>
            <p>{{ clustered ? t('cluster.title') : t('overview.standaloneHint') }}</p>
          </div>
          <RouterLink class="text-link" :to="{ name: 'cluster' }">{{ t('common.details') }}</RouterLink>
        </header>

        <ul v-if="nodes.length" class="node-list">
          <li v-for="node in nodes" :key="node.nodeId">
            <span class="node-indicator" :class="node.status"></span>
            <div class="node-copy">
              <strong>{{ node.nodeId }}</strong>
              <span>{{ node.address }}</span>
            </div>
            <StatusPill :status="node.status" />
          </li>
        </ul>
        <EmptyState v-else icon="cluster" :title="t('overview.standalone')" :hint="t('overview.standaloneHint')" />
      </section>

      <section class="panel">
        <header class="panel-header">
          <div>
            <h3>{{ t('overview.recentClients') }}</h3>
            <p>{{ clients.length }} / {{ stats?.connectedClients ?? 0 }}</p>
          </div>
          <RouterLink class="text-link" :to="{ name: 'clients' }">{{ t('overview.viewAll') }}</RouterLink>
        </header>

        <div v-if="clients.length" class="compact-list">
          <RouterLink
            v-for="client in clients"
            :key="client.clientId"
            class="compact-row"
            :to="{ name: 'clients' }"
          >
            <span class="avatar small">{{ client.clientId.slice(0, 1).toUpperCase() }}</span>
            <div class="compact-copy">
              <strong>{{ client.clientId }}</strong>
              <span>{{ client.remoteAddress || t('common.unknown') }} · {{ client.protocolVersion }}</span>
            </div>
            <StatusPill :status="client.connected ? 'online' : 'offline'" />
          </RouterLink>
        </div>
        <EmptyState v-else icon="clients" :title="t('overview.noClients')" />
      </section>
    </div>

    <section class="quick-actions">
      <RouterLink class="quick-card" :to="{ name: 'publish' }">
        <span class="quick-icon"><Icon name="publish" :size="20" /></span>
        <div>
          <strong>{{ t('overview.actionPublish') }}</strong>
          <p>{{ t('overview.actionPublishHint') }}</p>
        </div>
        <Icon name="arrowRight" :size="17" />
      </RouterLink>
      <RouterLink class="quick-card" :to="{ name: 'retained' }">
        <span class="quick-icon"><Icon name="retained" :size="20" /></span>
        <div>
          <strong>{{ t('overview.actionRetained') }}</strong>
          <p>{{ t('overview.actionRetainedHint') }}</p>
        </div>
        <Icon name="arrowRight" :size="17" />
      </RouterLink>
      <RouterLink class="quick-card" :to="{ name: 'cluster' }">
        <span class="quick-icon"><Icon name="cluster" :size="20" /></span>
        <div>
          <strong>{{ t('overview.actionCluster') }}</strong>
          <p>{{ t('overview.actionClusterHint') }}</p>
        </div>
        <Icon name="arrowRight" :size="17" />
      </RouterLink>
    </section>

    <div v-if="!stats && !error" class="skeleton-grid">
      <div v-for="index in 4" :key="index" class="skeleton-card"></div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { api } from '../api/client'
import type { BrokerStats, ClusterInfo, ClusterNode } from '../api/types'
import { useAutoRefresh } from '../composables/useAutoRefresh'
import { t } from '../i18n'
import { formatNumber, formatRelative } from '../utils/format'
import EmptyState from '../components/EmptyState.vue'
import Icon from '../components/Icon.vue'
import StatCard from '../components/StatCard.vue'
import StatusPill from '../components/StatusPill.vue'

const cluster = ref<ClusterInfo | null>(null)
const stats = ref<BrokerStats | null>(null)

const { refresh, refreshing, error } = useAutoRefresh(async () => {
  const [clusterResponse, statsResponse] = await Promise.all([api.cluster(), api.stats()])
  cluster.value = clusterResponse
  stats.value = statsResponse
}, 5000)

const nodes = computed(() => cluster.value?.nodes ?? [])
const self = computed(() => nodes.value.find((node) => node.isSelf))
const peers = computed(() => nodes.value.filter((node) => !node.isSelf))
const clustered = computed(() => (cluster.value?.status ?? 'standalone') === 'clustered')
const alive = computed(() => nodes.value.filter((node) => node.status === 'alive').length)
const degraded = computed(() => nodes.value.some((node) => node.status !== 'alive'))

interface PositionedNode {
  node: ClusterNode
  x: number
  y: number
}

const positionedPeers = computed<PositionedNode[]>(() => {
  const list = peers.value
  if (list.length === 0) return []

  return list.map((node, index) => {
    const angle = (index / list.length) * Math.PI * 2 - Math.PI / 2
    return {
      node,
      x: 50 + 35 * Math.cos(angle),
      y: 50 + 35 * Math.sin(angle),
    }
  })
})

const healthLabel = computed(() => (degraded.value ? t('cluster.degraded') : t('cluster.health')))
</script>

<template>
  <div class="view">
    <header class="page-header">
      <div>
        <p class="eyebrow">{{ t('nav.system') }}</p>
        <h2>{{ t('cluster.title') }}</h2>
        <p>{{ t('cluster.subtitle') }}</p>
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

    <template v-if="cluster">
      <template v-if="clustered">
        <div class="stat-grid three">
          <StatCard :label="t('cluster.nodes')" :value="formatNumber(nodes.length)" icon="cluster" tone="cyan" />
          <StatCard :label="t('cluster.alive')" :value="`${alive} / ${nodes.length}`" icon="activity" tone="green" />
          <StatCard
            :label="t('overview.connectedClients')"
            :value="formatNumber(stats?.connectedClients ?? 0)"
            icon="clients"
            tone="violet"
          />
        </div>

        <div class="cluster-hero panel">
          <div class="cluster-health">
            <span class="pulse-dot" :class="{ warning: degraded }"></span>
            <div>
              <strong>{{ healthLabel }}</strong>
              <p>{{ cluster.nodeId }} · {{ formatNumber(nodes.length) }} {{ t('cluster.nodes').toLowerCase() }}</p>
            </div>
          </div>

          <div class="topology">
            <svg viewBox="0 0 100 100" role="img" :aria-label="t('cluster.topology')">
              <line
                v-for="peer in positionedPeers"
                :key="`line-${peer.node.nodeId}`"
                :x1="50"
                :y1="50"
                :x2="peer.x"
                :y2="peer.y"
                class="topology-link"
                :class="peer.node.status"
              />
              <circle cx="50" cy="50" r="13" class="topology-self-glow" />
              <circle cx="50" cy="50" r="8" class="topology-node self" />
              <text x="50" y="52.2" text-anchor="middle" class="topology-label self-label">SELF</text>

              <g v-for="peer in positionedPeers" :key="peer.node.nodeId">
                <circle :cx="peer.x" :cy="peer.y" r="6" class="topology-node" :class="peer.node.status" />
                <text :x="peer.x" :y="peer.y + 13" text-anchor="middle" class="topology-label">
                  {{ peer.node.nodeId.length > 14 ? `${peer.node.nodeId.slice(0, 12)}…` : peer.node.nodeId }}
                </text>
              </g>
            </svg>
          </div>
        </div>

        <section class="panel table-panel">
          <header class="panel-header">
            <div>
              <h3>{{ t('cluster.topology') }}</h3>
              <p>{{ t('cluster.subtitle') }}</p>
            </div>
          </header>
          <div class="table-scroll">
            <table class="data-table">
              <thead>
                <tr>
                  <th>{{ t('cluster.node') }}</th>
                  <th>{{ t('cluster.address') }}</th>
                  <th>{{ t('common.status') }}</th>
                  <th>{{ t('cluster.role') }}</th>
                  <th>{{ t('cluster.lastSeen') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="node in nodes" :key="node.nodeId">
                  <td>
                    <span class="mono strong">{{ node.nodeId }}</span>
                  </td>
                  <td class="mono muted">{{ node.address }}</td>
                  <td><StatusPill :status="node.status" /></td>
                  <td>
                    <span class="tag" :class="{ cyan: node.isSelf }">
                      {{ node.isSelf ? t('cluster.self') : t('cluster.peer') }}
                    </span>
                  </td>
                  <td>{{ formatRelative(node.lastSeen) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>
      </template>

      <section v-else class="panel standalone-card">
        <span class="standalone-icon"><Icon name="cluster" :size="30" /></span>
        <h3>{{ t('cluster.standalone') }}</h3>
        <p>{{ t('cluster.standaloneHint') }}</p>
        <EmptyState icon="server" :title="self?.nodeId || t('overview.standalone')" :hint="t('cluster.howTo')">
          <pre class="snippet">NEXTMQTT_CLUSTER__ENABLED=true
NEXTMQTT_CLUSTER__NODEID=node-a
NEXTMQTT_CLUSTER__ADVERTISEURL=http://node-a:8080
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
NEXTMQTT_CLUSTER__SECRET=change-me-shared-secret</pre>
          <a class="button ghost" href="https://cn-cam.github.io/next-mqtt/deployment.html#clustering" target="_blank" rel="noreferrer">
            <Icon name="external" :size="15" />
            {{ t('cluster.docs') }}
          </a>
        </EmptyState>
      </section>
    </template>
  </div>
</template>

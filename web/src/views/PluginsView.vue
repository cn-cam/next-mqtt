<script setup lang="ts">
import { ref } from 'vue'
import { api } from '../api/client'
import type { PluginsInfo } from '../api/types'
import { useAutoRefresh } from '../composables/useAutoRefresh'
import { t } from '../i18n'
import EmptyState from '../components/EmptyState.vue'
import Icon from '../components/Icon.vue'

const data = ref<PluginsInfo>({
  enabled: false,
  directory: 'plugins',
  contractVersion: 0,
  authenticatorLoaded: false,
  authorizerLoaded: false,
  instances: 0,
  plugins: [],
})

async function load(): Promise<void> {
  data.value = await api.plugins()
}

const { refresh, refreshing, error } = useAutoRefresh(load, 10_000)

function trail(plugin: PluginsInfo['plugins'][number]): string {
  return plugin.directory
}
</script>

<template>
  <div class="view">
    <header class="page-header">
      <div>
        <p class="eyebrow">{{ t('nav.system') }}</p>
        <h2>{{ t('plugins.title') }}</h2>
        <p>{{ t('plugins.subtitle') }}</p>
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

    <section class="panel">
      <div class="panel-header">
        <div>
          <h3>{{ t('plugins.hooks') }}</h3>
          <p>{{ t('plugins.hooksHint') }}</p>
        </div>
        <span class="tag" :class="data.enabled ? 'green' : ''">
          {{ data.enabled ? t('common.enabled') : t('common.disabled') }}
        </span>
      </div>

      <div class="table-scroll">
        <table class="data-table">
          <tbody>
            <tr>
              <th>{{ t('plugins.directory') }}</th>
              <td class="mono">{{ data.directory }}</td>
            </tr>
            <tr>
              <th>{{ t('plugins.contract') }}</th>
              <td class="mono">v{{ data.contractVersion }}</td>
            </tr>
            <tr>
              <th>{{ t('plugins.authenticator') }}</th>
              <td>
                <span class="mono">{{ data.authenticator || t('plugins.none') }}</span>
                <span class="tag" :class="data.authenticatorLoaded ? 'green' : ''">
                  {{ data.authenticatorLoaded ? t('plugins.resolved') : t('plugins.unresolved') }}
                </span>
              </td>
            </tr>
            <tr>
              <th>{{ t('plugins.authorizer') }}</th>
              <td>
                <span class="mono">{{ data.authorizer || t('plugins.none') }}</span>
                <span class="tag" :class="data.authorizerLoaded ? 'green' : ''">
                  {{ data.authorizerLoaded ? t('plugins.resolved') : t('plugins.unresolved') }}
                </span>
              </td>
            </tr>
            <tr>
              <th>{{ t('plugins.instances') }}</th>
              <td class="mono">{{ data.instances }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <section class="panel table-panel">
      <div class="panel-header">
        <div>
          <h3>{{ t('plugins.list') }}</h3>
          <p>{{ t('plugins.listHint', { count: data.plugins.length }) }}</p>
        </div>
      </div>

      <EmptyState
        v-if="data.plugins.length === 0"
        icon="plugins"
        :title="t('plugins.emptyTitle')"
        :hint="t('plugins.emptyHint')"
      />

      <div v-else class="table-scroll">
        <table class="data-table">
          <thead>
            <tr>
              <th>{{ t('plugins.name') }}</th>
              <th>{{ t('plugins.version') }}</th>
              <th>{{ t('plugins.assembly') }}</th>
              <th>{{ t('plugins.directory') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="plugin in data.plugins" :key="trail(plugin) + plugin.name">
              <td><strong>{{ plugin.name }}</strong></td>
              <td class="mono">{{ plugin.version }}</td>
              <td class="mono">{{ plugin.assembly || '—' }}</td>
              <td class="mono">{{ plugin.directory }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </div>
</template>

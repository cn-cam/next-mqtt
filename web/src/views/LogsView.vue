<script setup lang="ts">
import { computed, ref } from 'vue'
import { api } from '../api/client'
import type { LogsInfo } from '../api/types'
import { useAutoRefresh } from '../composables/useAutoRefresh'
import { t } from '../i18n'
import EmptyState from '../components/EmptyState.vue'
import Icon from '../components/Icon.vue'

const levels = ['Trace', 'Debug', 'Information', 'Warning', 'Error'] as const
const level = ref('Information')
const search = ref('')
const appliedSearch = ref('')
const data = ref<LogsInfo>({ entries: [], total: 0, capacity: 0, level: 'Information' })

let debounce: number | undefined

async function load(): Promise<void> {
  data.value = await api.logs({ level: level.value, search: appliedSearch.value, limit: 300 })
}

const { refresh, refreshing, error } = useAutoRefresh(load, 3000)

function onSearchInput(): void {
  window.clearTimeout(debounce)
  debounce = window.setTimeout(() => {
    appliedSearch.value = search.value
    void refresh()
  }, 250)
}

function onLevelChange(): void {
  void refresh()
}

const visible = computed(() => data.value.entries)

function clock(timestamp: string): string {
  return new Date(timestamp).toLocaleTimeString(undefined, { hour12: false })
}
</script>

<template>
  <div class="view">
    <header class="page-header">
      <div>
        <p class="eyebrow">{{ t('nav.system') }}</p>
        <h2>{{ t('logs.title') }}</h2>
        <p>{{ t('logs.subtitle', { total: data.total, capacity: data.capacity }) }}</p>
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
      <label class="field compact">
        <span>{{ t('logs.level') }}</span>
        <select v-model="level" @change="onLevelChange">
          <option v-for="item in levels" :key="item" :value="item">
            {{ t('logs.level.' + item.toLowerCase()) }}
          </option>
        </select>
      </label>

      <div class="search-field">
        <Icon name="search" :size="16" />
        <input v-model="search" type="search" :placeholder="t('logs.searchPlaceholder')" @input="onSearchInput" />
      </div>

      <span class="toolbar-hint">{{ t('logs.polling') }}</span>
    </div>

    <EmptyState v-if="visible.length === 0" icon="logs" :title="t('logs.emptyTitle')" :hint="t('logs.emptyHint')" />

    <section v-else class="panel log-panel">
      <ol class="log-list">
        <li v-for="(entry, index) in visible" :key="entry.timestamp + index" class="log-row">
          <span class="log-time">{{ clock(entry.timestamp) }}</span>
          <span class="log-level" :class="'level-' + entry.level.toLowerCase()">{{ entry.level }}</span>
          <span class="log-category" :title="entry.category">{{ entry.category }}</span>
          <span class="log-message">
            {{ entry.message }}
            <details v-if="entry.exception">
              <summary>{{ t('logs.stackTrace') }}</summary>
              <pre>{{ entry.exception }}</pre>
            </details>
          </span>
        </li>
      </ol>
    </section>
  </div>
</template>

<style scoped>
.log-panel {
  padding: 0;
  overflow: hidden;
}

.log-list {
  list-style: none;
  margin: 0;
  padding: 0;
  max-height: calc(100vh - 340px);
  overflow: auto;
  font-size: 12.5px;
}

.log-row {
  display: grid;
  grid-template-columns: 88px 92px minmax(160px, 260px) 1fr;
  gap: 12px;
  padding: 7px 16px;
  border-bottom: 1px solid var(--border);
  align-items: start;
}

.log-row:last-child {
  border-bottom: none;
}

.log-time {
  color: var(--dim);
  font-variant-numeric: tabular-nums;
}

.log-level {
  font-weight: 600;
  font-size: 11.5px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.level-information { color: var(--accent); }
.level-warning { color: #d08b1f; }
.level-error,
.level-critical { color: #d64545; }
.level-debug,
.level-trace { color: var(--muted); }

.log-category {
  color: var(--muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.log-message {
  white-space: pre-wrap;
  word-break: break-word;
}

.log-message pre {
  margin: 6px 0 0;
  padding: 8px 10px;
  background: var(--panel-alt, rgba(0, 0, 0, 0.04));
  border-radius: 8px;
  overflow-x: auto;
  font-size: 11.5px;
}

.log-message summary {
  cursor: pointer;
  color: var(--dim);
  font-size: 11.5px;
  margin-top: 4px;
}
</style>

<script setup lang="ts">
import { ref } from 'vue'
import { TOKEN_KEY } from '../api/client'
import { apiEndpoints, type ApiEndpoint } from '../api/endpoints'
import { t } from '../i18n'
import Icon from '../components/Icon.vue'

interface ApiResponse {
  status: number
  statusText: string
  duration: number
  contentType: string
  body: string
}

const selected = ref<ApiEndpoint>(apiEndpoints[0])
const method = ref(selected.value.method)
const path = ref(selected.value.path)
const body = ref(selected.value.body ?? '')
const sending = ref(false)
const error = ref('')
const response = ref<ApiResponse | null>(null)

function select(endpoint: ApiEndpoint): void {
  selected.value = endpoint
  method.value = endpoint.method
  path.value = endpoint.path
  body.value = endpoint.body ?? ''
  response.value = null
  error.value = ''
}

function pretty(value: string): string {
  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
}

async function send(): Promise<void> {
  error.value = ''
  response.value = null

  const trimmed = body.value.trim()

  if (method.value === 'POST' && trimmed) {
    try {
      JSON.parse(trimmed)
    } catch {
      error.value = t('apiConsole.invalidJson')
      return
    }
  }

  const headers = new Headers({ Accept: 'application/json' })

  if (method.value === 'POST' && trimmed) {
    headers.set('Content-Type', 'application/json')
  }

  if (selected.value.auth) {
    const token = localStorage.getItem(TOKEN_KEY)
    if (token) headers.set('Authorization', `Bearer ${token}`)
  }

  sending.value = true
  const started = performance.now()

  try {
    const result = await fetch(path.value, {
      method: method.value,
      headers,
      body: method.value === 'POST' && trimmed ? trimmed : undefined,
    })

    const text = await result.text()
    const contentType = result.headers.get('content-type') ?? ''

    response.value = {
      status: result.status,
      statusText: result.statusText,
      duration: Math.round(performance.now() - started),
      contentType,
      body: contentType.includes('json') ? pretty(text) : text,
    }
  } catch {
    error.value = t('apiConsole.requestFailed')
  } finally {
    sending.value = false
  }
}

function statusClass(status: number): string {
  if (status >= 200 && status < 300) return 'green'
  if (status >= 400) return 'red'
  return ''
}
</script>

<template>
  <div class="view">
    <header class="page-header">
      <div>
        <p class="eyebrow">{{ t('nav.system') }}</p>
        <h2>{{ t('apiConsole.title') }}</h2>
        <p>{{ t('apiConsole.subtitle') }}</p>
      </div>
    </header>

    <div class="api-console">
      <aside class="panel api-list">
        <div class="panel-header">
          <h3>{{ t('apiConsole.endpoints') }}</h3>
        </div>

        <button
          v-for="endpoint in apiEndpoints"
          :key="`${endpoint.method} ${endpoint.path}`"
          type="button"
          class="api-entry"
          :class="{ active: endpoint === selected }"
          @click="select(endpoint)"
        >
          <span class="api-method" :class="endpoint.method.toLowerCase()">{{ endpoint.method }}</span>
          <span class="api-path">{{ endpoint.path }}</span>
        </button>
      </aside>

      <section class="panel api-request">
        <div class="panel-header">
          <div>
            <h3>{{ selected.description }}</h3>
            <p>{{ selected.auth ? t('apiConsole.auth') : t('apiConsole.public') }}</p>
          </div>
        </div>

        <form class="api-form" @submit.prevent="send">
          <label>
            <span>{{ t('apiConsole.method') }}</span>
            <select v-model="method">
              <option value="GET">GET</option>
              <option value="POST">POST</option>
              <option value="DELETE">DELETE</option>
            </select>
          </label>

          <label class="grow">
            <span>{{ t('apiConsole.path') }}</span>
            <input v-model="path" spellcheck="false" autocomplete="off" />
          </label>

          <label v-if="method === 'POST'" class="full">
            <span>{{ t('apiConsole.body') }}</span>
            <textarea v-model="body" rows="8" spellcheck="false"></textarea>
            <small>{{ t('apiConsole.bodyHint') }}</small>
          </label>

          <div class="full actions">
            <button class="button" type="submit" :disabled="sending">
              <Icon name="publish" :size="16" />
              {{ sending ? t('apiConsole.sending') : t('apiConsole.send') }}
            </button>
          </div>
        </form>

        <div v-if="error" class="alert error">
          <Icon name="alert" :size="17" />
          <span>{{ error }}</span>
        </div>

        <div v-if="response" class="api-response">
          <div class="api-response-head">
            <span class="tag" :class="statusClass(response.status)">
              {{ response.status }} {{ response.statusText }}
            </span>
            <span class="api-meta">{{ t('apiConsole.duration') }}: {{ response.duration }} ms</span>
            <span v-if="response.contentType" class="api-meta">{{ response.contentType }}</span>
          </div>
          <pre>{{ response.body || t('apiConsole.emptyResponse') }}</pre>
        </div>
      </section>
    </div>
  </div>
</template>

<style scoped>
.api-console {
  display: grid;
  grid-template-columns: minmax(220px, 320px) 1fr;
  gap: 18px;
  align-items: start;
}

.api-list {
  max-height: 70vh;
  overflow: auto;
  padding-bottom: 8px;
}

.api-entry {
  display: flex;
  width: 100%;
  gap: 8px;
  align-items: baseline;
  padding: 7px 16px;
  border: 0;
  background: transparent;
  color: var(--text);
  text-align: left;
  cursor: pointer;
  font: inherit;
}

.api-entry:hover,
.api-entry.active {
  background: var(--surface-2);
}

.api-method {
  flex: 0 0 52px;
  font-family: var(--mono);
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.04em;
  color: var(--accent);
}

.api-method.post {
  color: #d97706;
}

.api-method.delete {
  color: var(--danger);
}

.api-path {
  font-family: var(--mono);
  font-size: 12px;
  overflow-wrap: anywhere;
}

.api-form {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  padding: 0 16px 16px;
}

.api-form label {
  display: flex;
  flex-direction: column;
  gap: 5px;
  font-size: 12px;
  color: var(--muted);
}

.api-form label.grow {
  flex: 1;
}

.api-form label.full {
  flex: 1 0 100%;
}

.api-form input,
.api-form select,
.api-form textarea {
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
  color: var(--text);
  padding: 8px 10px;
  font: inherit;
}

.api-form textarea {
  font-family: var(--mono);
  font-size: 12px;
  resize: vertical;
}

.actions {
  display: flex;
  justify-content: flex-end;
}

.api-response {
  padding: 0 16px 16px;
}

.api-response-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  margin-bottom: 8px;
}

.api-meta {
  color: var(--muted);
  font-size: 12px;
}

.api-response pre {
  max-height: 46vh;
  margin: 0;
  overflow: auto;
  border: 1px solid var(--border);
  border-radius: 10px;
  background: var(--surface-2);
  padding: 12px;
  font-family: var(--mono);
  font-size: 12px;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

@media (max-width: 880px) {
  .api-console {
    grid-template-columns: 1fr;
  }

  .api-list {
    max-height: 240px;
  }
}
</style>

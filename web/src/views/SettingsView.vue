<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '../api/client'
import type { ConfigInfo, VersionInfo } from '../api/types'
import { useAutoRefresh } from '../composables/useAutoRefresh'
import { useToast } from '../composables/useToast'
import { locale, setLocale, t } from '../i18n'
import { useAuthStore } from '../stores/auth'
import { theme, setTheme } from '../stores/theme'
import { formatBytes, formatNumber } from '../utils/format'
import Icon from '../components/Icon.vue'

const router = useRouter()
const auth = useAuthStore()
const toast = useToast()
const config = ref<ConfigInfo | null>(null)
const version = ref<VersionInfo | null>(null)

const { refresh } = useAutoRefresh(async () => {
  const [configResponse, versionResponse] = await Promise.all([api.config(), api.version()])
  config.value = configResponse
  version.value = versionResponse
}, 15000)

const maskedToken = computed(() => {
  const token = auth.token
  return token ? `${token.slice(0, 12)}…${token.slice(-6)}` : '—'
})

async function copyToken(): Promise<void> {
  try {
    await navigator.clipboard.writeText(auth.token)
    toast.success(t('toast.copied'))
  } catch {
    toast.error(t('common.error'))
  }
}

function logout(): void {
  auth.clear()
  void router.push({ name: 'login' })
}
</script>

<template>
  <div class="view">
    <header class="page-header">
      <div>
        <p class="eyebrow">{{ t('nav.system') }}</p>
        <h2>{{ t('settings.title') }}</h2>
        <p>{{ t('settings.subtitle') }}</p>
      </div>
      <div class="header-actions">
        <button class="button ghost" type="button" @click="refresh">
          <Icon name="refresh" :size="16" />
          {{ t('common.refresh') }}
        </button>
      </div>
    </header>

    <div class="settings-grid">
      <section v-if="config" class="panel">
        <header class="panel-header">
          <div>
            <h3>{{ t('settings.listeners') }}</h3>
            <p>{{ config.server.tcp.host }}</p>
          </div>
          <Icon name="server" :size="18" />
        </header>

        <ul class="listener-list">
          <li>
            <div>
              <strong>{{ t('settings.mqttTcp') }}</strong>
              <span>{{ config.server.tcp.host }}:{{ config.server.tcp.port }}</span>
            </div>
            <span class="tag" :class="config.server.tcp.enabled ? 'green' : ''">
              {{ config.server.tcp.enabled ? t('common.enabled') : t('common.disabled') }}
            </span>
          </li>
          <li>
            <div>
              <strong>{{ t('settings.mqttTls') }}</strong>
              <span>:{{ config.server.tls.port }}</span>
            </div>
            <span class="tag" :class="config.server.tls.enabled ? 'green' : ''">
              {{ config.server.tls.enabled ? t('common.enabled') : t('common.disabled') }}
            </span>
          </li>
          <li>
            <div>
              <strong>{{ t('settings.websocket') }}</strong>
              <span>{{ config.server.websocket.path }}</span>
            </div>
            <span class="tag" :class="config.server.websocket.enabled ? 'green' : ''">
              {{ config.server.websocket.enabled ? t('common.enabled') : t('common.disabled') }}
            </span>
          </li>
          <li>
            <div>
              <strong>{{ t('settings.dashboard') }}</strong>
              <span>:{{ config.server.http.port }} · WS :{{ config.server.http.webSocketPort }}</span>
            </div>
            <span class="tag green">{{ t('common.enabled') }}</span>
          </li>
        </ul>
      </section>

      <section v-if="config" class="panel">
        <header class="panel-header">
          <div>
            <h3>{{ t('settings.limits') }}</h3>
            <p>{{ t('broker') }}</p>
          </div>
          <Icon name="shield" :size="18" />
        </header>

        <dl class="metric-list">
          <div class="metric-row">
            <dt>{{ t('settings.maxPacket') }}</dt>
            <dd>{{ formatBytes(config.broker.maxPacketSize) }}</dd>
          </div>
          <div class="metric-row">
            <dt>{{ t('settings.maxInflight') }}</dt>
            <dd>{{ formatNumber(config.broker.maxInflightMessages) }}</dd>
          </div>
          <div class="metric-row">
            <dt>{{ t('settings.maxQueued') }}</dt>
            <dd>{{ formatNumber(config.broker.maxQueuedMessages) }}</dd>
          </div>
          <div class="metric-row">
            <dt>{{ t('settings.maxSubs') }}</dt>
            <dd>{{ formatNumber(config.broker.maxSubscriptionsPerSession) }}</dd>
          </div>
          <div class="metric-row">
            <dt>{{ t('settings.maxAlias') }}</dt>
            <dd>{{ formatNumber(config.broker.maxTopicAlias) }}</dd>
          </div>
        </dl>
      </section>

      <section class="panel">
        <header class="panel-header">
          <div>
            <h3>{{ t('settings.about') }}</h3>
            <p>{{ version?.runtime || '—' }}</p>
          </div>
          <Icon name="info" :size="18" />
        </header>

        <dl class="metric-list">
          <div class="metric-row">
            <dt>{{ t('common.version') }}</dt>
            <dd>{{ version?.version || '0.1.0' }}</dd>
          </div>
          <div class="metric-row">
            <dt>{{ t('common.status') }}</dt>
            <dd>{{ version?.environment || '—' }}</dd>
          </div>
          <div class="metric-row">
            <dt>{{ t('cluster.title') }}</dt>
            <dd>{{ config?.cluster.status || '—' }}</dd>
          </div>
        </dl>

        <p class="muted about-copy">{{ t('settings.independent') }}</p>

        <div class="link-row">
          <a class="button ghost" href="https://cn-cam.github.io/next-mqtt/" target="_blank" rel="noreferrer">
            <Icon name="external" :size="15" />
            {{ t('common.docs') }}
          </a>
          <a class="button ghost" href="https://github.com/cn-cam/next-mqtt" target="_blank" rel="noreferrer">
            <Icon name="github" :size="15" />
            {{ t('common.github') }}
          </a>
        </div>
      </section>

      <section class="panel">
        <header class="panel-header">
          <div>
            <h3>{{ t('common.theme') }} &amp; {{ t('common.language') }}</h3>
            <p>{{ t('settings.themeHint') }}</p>
          </div>
          <Icon name="settings" :size="18" />
        </header>

        <div class="preference-row">
          <div>
            <strong>{{ t('common.theme') }}</strong>
            <span>{{ t('settings.themeHint') }}</span>
          </div>
          <div class="segmented">
            <button type="button" :class="{ active: theme === 'dark' }" @click="setTheme('dark')">
              {{ t('common.dark') }}
            </button>
            <button type="button" :class="{ active: theme === 'light' }" @click="setTheme('light')">
              {{ t('common.light') }}
            </button>
          </div>
        </div>

        <div class="preference-row">
          <div>
            <strong>{{ t('common.language') }}</strong>
            <span>{{ t('settings.langHint') }}</span>
          </div>
          <div class="segmented">
            <button type="button" :class="{ active: locale === 'en' }" @click="setLocale('en')">English</button>
            <button type="button" :class="{ active: locale === 'zh' }" @click="setLocale('zh')">中文</button>
          </div>
        </div>
      </section>

      <section class="panel">
        <header class="panel-header">
          <div>
            <h3>{{ t('settings.dangerZone') }}</h3>
            <p>{{ t('settings.session', { user: auth.username || 'admin' }) }}</p>
          </div>
          <Icon name="key" :size="18" />
        </header>

        <div class="token-row">
          <div>
            <strong>{{ t('settings.apiToken') }}</strong>
            <span class="mono">{{ maskedToken }}</span>
          </div>
          <button class="icon-button" type="button" :title="t('common.copy')" @click="copyToken">
            <Icon name="copy" :size="16" />
          </button>
        </div>

        <p class="muted">{{ t('settings.tokenHint') }}</p>
        <p class="muted">{{ t('settings.logoutHint') }}</p>

        <button class="button danger" type="button" @click="logout">
          <Icon name="logout" :size="16" />
          {{ t('common.logout') }}
        </button>
      </section>
    </div>
  </div>
</template>

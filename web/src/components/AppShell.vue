<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { api } from '../api/client'
import { locale, t, toggleLocale } from '../i18n'
import { useAuthStore } from '../stores/auth'
import { theme, toggleTheme } from '../stores/theme'
import BrandMark from './BrandMark.vue'
import Icon from './Icon.vue'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const sidebarOpen = ref(false)
const version = ref('0.1.0')
const clusterStatus = ref<'standalone' | 'clustered'>('standalone')

const groups = computed(() => [
  {
    label: t('nav.operations'),
    items: [
      { name: 'overview', label: t('nav.overview'), icon: 'overview' },
      { name: 'clients', label: t('nav.clients'), icon: 'clients' },
      { name: 'subscriptions', label: t('nav.subscriptions'), icon: 'subscriptions' },
      { name: 'retained', label: t('nav.retained'), icon: 'retained' },
    ],
  },
  {
    label: t('nav.messaging'),
    items: [
      { name: 'publish', label: t('nav.publish'), icon: 'publish' },
      { name: 'debugger', label: t('nav.debugger'), icon: 'terminal' },
    ],
  },
  {
    label: t('nav.system'),
    items: [
      { name: 'cluster', label: t('nav.cluster'), icon: 'cluster' },
      { name: 'settings', label: t('nav.settings'), icon: 'settings' },
    ],
  },
])

const pageTitle = computed(() =>
  typeof route.meta.title === 'string' ? t(route.meta.title) : 'NextMQTT',
)

const initial = computed(() => (auth.username || 'A').slice(0, 1).toUpperCase())

watch(
  () => route.fullPath,
  () => {
    sidebarOpen.value = false
  },
)

onMounted(async () => {
  try {
    const info = await api.version()
    version.value = info.version
  } catch {
    // Version is cosmetic; ignore failures.
  }

  try {
    const cluster = await api.cluster()
    clusterStatus.value = cluster.status === 'clustered' ? 'clustered' : 'standalone'
  } catch {
    // Ignore; the cluster page reports errors.
  }
})

function signOut(): void {
  auth.clear()
  void router.push({ name: 'login' })
}
</script>

<template>
  <div class="shell">
    <aside class="sidebar" :class="{ 'is-open': sidebarOpen }">
      <div class="sidebar-brand">
        <BrandMark :size="38" />
        <div>
          <strong>NextMQTT</strong>
          <span>{{ t('brand.tagline') }}</span>
        </div>
      </div>

      <nav class="sidebar-nav">
        <section v-for="group in groups" :key="group.label" class="nav-group">
          <p class="nav-group-label">{{ group.label }}</p>
          <RouterLink
            v-for="item in group.items"
            :key="item.name"
            :to="{ name: item.name }"
            class="nav-link"
          >
            <Icon :name="item.icon" :size="18" />
            <span>{{ item.label }}</span>
          </RouterLink>
        </section>
      </nav>

      <div class="sidebar-footer">
        <div class="node-chip">
          <span class="status-dot" :class="clusterStatus"></span>
          <span>{{ clusterStatus === 'clustered' ? t('cluster.title') : t('overview.standalone') }}</span>
        </div>
        <div class="sidebar-meta">
          <span>v{{ version }}</span>
          <a href="https://github.com/cn-cam/next-mqtt" target="_blank" rel="noreferrer">
            <Icon name="external" :size="14" />
          </a>
        </div>
      </div>
    </aside>

    <div class="shell-content">
      <header class="topbar">
        <button class="icon-button mobile-only" type="button" :aria-label="t('common.menu')" @click="sidebarOpen = true">
          <Icon name="menu" :size="20" />
        </button>
        <div class="topbar-title">
          <span class="eyebrow">{{ t('common.live') }}</span>
          <h1>{{ pageTitle }}</h1>
        </div>

        <div class="topbar-actions">
          <button class="pill-button" type="button" :title="t('common.language')" @click="toggleLocale">
            <Icon name="globe" :size="15" />
            <span>{{ locale === 'en' ? 'EN' : '中文' }}</span>
          </button>
          <button class="icon-button" type="button" :title="t('common.theme')" @click="toggleTheme">
            <Icon :name="theme === 'dark' ? 'sun' : 'moon'" :size="18" />
          </button>
          <div class="user-chip">
            <span class="avatar">{{ initial }}</span>
            <span class="user-name">{{ auth.username || t('common.admin') }}</span>
          </div>
          <button class="icon-button" type="button" :title="t('common.logout')" @click="signOut">
            <Icon name="logout" :size="18" />
          </button>
        </div>
      </header>

      <main class="page">
        <RouterView />
      </main>
    </div>

    <button
      v-if="sidebarOpen"
      class="scrim"
      type="button"
      :aria-label="t('common.close')"
      @click="sidebarOpen = false"
    />
  </div>
</template>

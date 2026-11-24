<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ApiError } from '../api/client'
import { t, toggleLocale, locale } from '../i18n'
import { useAuthStore } from '../stores/auth'
import { theme, toggleTheme } from '../stores/theme'
import BrandMark from '../components/BrandMark.vue'
import Icon from '../components/Icon.vue'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()

const username = ref('admin')
const password = ref('')
const error = ref('')
const showPassword = ref(false)

const submitting = computed(() => auth.loggingIn)

async function submit(): Promise<void> {
  if (!username.value || !password.value || submitting.value) return

  error.value = ''

  try {
    await auth.login(username.value, password.value)
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    await router.replace(redirect)
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) {
      error.value = t('login.invalid')
    } else if (cause instanceof ApiError && cause.status === 0) {
      error.value = t('login.network')
    } else {
      error.value = cause instanceof Error ? cause.message : t('common.error')
    }
  }
}
</script>

<template>
  <div class="login-page">
    <div class="login-aurora" aria-hidden="true">
      <span class="orb orb-a"></span>
      <span class="orb orb-b"></span>
      <span class="grid-overlay"></span>
    </div>

    <div class="login-layout">
      <section class="login-brand">
        <div class="login-brand-top">
          <BrandMark :size="46" />
          <span class="login-brand-name">NextMQTT</span>
        </div>

        <div class="login-hero">
          <p class="eyebrow">{{ t('login.eyebrow') }}</p>
          <h1>{{ t('brand.tagline') }}</h1>
          <p class="login-hero-copy">{{ t('login.subtitle') }}</p>
        </div>

        <ul class="login-features">
          <li>
            <span class="feature-icon"><Icon name="activity" :size="18" /></span>
            <div>
              <strong>{{ t('login.feature1.title') }}</strong>
              <p>{{ t('login.feature1.text') }}</p>
            </div>
          </li>
          <li>
            <span class="feature-icon"><Icon name="cluster" :size="18" /></span>
            <div>
              <strong>{{ t('login.feature2.title') }}</strong>
              <p>{{ t('login.feature2.text') }}</p>
            </div>
          </li>
          <li>
            <span class="feature-icon"><Icon name="shield" :size="18" /></span>
            <div>
              <strong>{{ t('login.feature3.title') }}</strong>
              <p>{{ t('login.feature3.text') }}</p>
            </div>
          </li>
        </ul>

        <p class="login-footer">{{ t('login.footer') }}</p>
      </section>

      <section class="login-panel">
        <div class="login-panel-tools">
          <button class="pill-button" type="button" @click="toggleLocale">
            <Icon name="globe" :size="14" />
            <span>{{ locale === 'en' ? 'EN' : '中文' }}</span>
          </button>
          <button class="icon-button" type="button" @click="toggleTheme">
            <Icon :name="theme === 'dark' ? 'sun' : 'moon'" :size="17" />
          </button>
        </div>

        <div class="login-card">
          <h2>{{ t('login.title') }}</h2>
          <p class="login-subtitle">{{ t('login.subtitle') }}</p>

          <form class="login-form" @submit.prevent="submit">
            <label class="field">
              <span>{{ t('login.username') }}</span>
              <input v-model.trim="username" autocomplete="username" name="username" required />
            </label>

            <label class="field">
              <span>{{ t('login.password') }}</span>
              <div class="password-wrap">
                <input
                  v-model="password"
                  :type="showPassword ? 'text' : 'password'"
                  autocomplete="current-password"
                  name="password"
                  required
                />
                <button
                  class="password-toggle"
                  type="button"
                  :aria-label="showPassword ? 'Hide password' : 'Show password'"
                  @click="showPassword = !showPassword"
                >
                  <Icon name="key" :size="16" />
                </button>
              </div>
            </label>

            <p v-if="error" class="form-error">
              <Icon name="alert" :size="15" />
              {{ error }}
            </p>

            <button class="button primary login-submit" type="submit" :disabled="submitting">
              <span v-if="submitting">{{ t('login.submitting') }}</span>
              <span v-else>{{ t('login.submit') }}</span>
              <Icon name="arrowRight" :size="17" />
            </button>
          </form>

          <p class="login-hint">
            {{ t('login.footer') }}
          </p>
        </div>
      </section>
    </div>
  </div>
</template>

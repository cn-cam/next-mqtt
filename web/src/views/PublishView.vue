<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { api } from '../api/client'
import type { PublishResult } from '../api/types'
import { useToast } from '../composables/useToast'
import { t } from '../i18n'
import { formatDate } from '../utils/format'
import Icon from '../components/Icon.vue'

interface HistoryEntry extends PublishResult {
  timestamp: string
  retained: boolean
}

const toast = useToast()
const submitting = ref(false)
const topicError = ref('')
const payloadError = ref('')

const form = reactive({
  topic: '',
  payload: '{"command":"reboot"}',
  qos: 0 as 0 | 1 | 2,
  retain: false,
  encoding: 'plain' as 'plain' | 'base64',
})

const history = ref<HistoryEntry[]>([])

const payloadBytes = computed(() => {
  if (form.encoding === 'base64') {
    const value = form.payload.replace(/\s+/g, '').replace(/=+$/, '')
    if (!/^[A-Za-z0-9+/]*$/.test(value) || value.length % 4 === 1) return 0
    return Math.floor((value.length * 3) / 4)
  }

  return new TextEncoder().encode(form.payload).length
})

const suggestions = ['devices/demo/command', 'sensors/demo/telemetry', 'alerts/demo']

function validate(): boolean {
  topicError.value = ''
  payloadError.value = ''

  if (!form.topic.trim()) {
    topicError.value = t('publish.topicRequired')
    return false
  }

  if (/[#+]/.test(form.topic)) {
    topicError.value = t('common.error')
    return false
  }

  if (form.encoding === 'base64') {
    try {
      atob(form.payload.replace(/\s+/g, ''))
    } catch {
      payloadError.value = t('publish.invalidBase64')
      return false
    }
  }

  return true
}

async function submit(): Promise<void> {
  if (submitting.value || !validate()) return

  submitting.value = true

  try {
    const result = await api.publish({
      topic: form.topic.trim(),
      payload: form.payload,
      encoding: form.encoding,
      qos: form.qos,
      retain: form.retain,
    })

    toast.success(
      t('publish.success', {
        bytes: result.bytes,
        topic: result.topic,
      }),
    )

    history.value.unshift({
      ...result,
      timestamp: new Date().toISOString(),
      retained: form.retain,
    })

    if (history.value.length > 12) history.value.length = 12
  } catch (cause) {
    toast.error(cause instanceof Error ? cause.message : t('common.error'))
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="view">
    <header class="page-header">
      <div>
        <p class="eyebrow">{{ t('nav.messaging') }}</p>
        <h2>{{ t('publish.title') }}</h2>
        <p>{{ t('publish.subtitle') }}</p>
      </div>
    </header>

    <div class="content-grid wide-left">
      <section class="panel form-panel">
        <form class="publish-form" @submit.prevent="submit">
          <label class="field">
            <span>{{ t('publish.topic') }}</span>
            <input v-model="form.topic" :placeholder="t('publish.topicPlaceholder')" spellcheck="false" />
            <small v-if="topicError" class="field-error">{{ topicError }}</small>
          </label>

          <div class="suggestion-row">
            <button
              v-for="suggestion in suggestions"
              :key="suggestion"
              class="tag clickable-tag"
              type="button"
              @click="form.topic = suggestion"
            >
              {{ suggestion }}
            </button>
          </div>

          <label class="field">
            <span>{{ t('publish.payload') }}</span>
            <textarea
              v-model="form.payload"
              rows="10"
              spellcheck="false"
              :placeholder="t('publish.payloadPlaceholder')"
            ></textarea>
            <small v-if="payloadError" class="field-error">{{ payloadError }}</small>
            <span class="field-meta">{{ payloadBytes }} B</span>
          </label>

          <div class="form-row">
            <label class="field compact">
              <span>{{ t('publish.qos') }}</span>
              <select v-model.number="form.qos">
                <option :value="0">QoS 0</option>
                <option :value="1">QoS 1</option>
                <option :value="2">QoS 2</option>
              </select>
            </label>

            <div class="field compact">
              <span>{{ t('publish.encoding') }}</span>
              <div class="segmented">
                <button
                  type="button"
                  :class="{ active: form.encoding === 'plain' }"
                  @click="form.encoding = 'plain'"
                >
                  {{ t('publish.plain') }}
                </button>
                <button
                  type="button"
                  :class="{ active: form.encoding === 'base64' }"
                  @click="form.encoding = 'base64'"
                >
                  Base64
                </button>
              </div>
            </div>
          </div>

          <label class="switch-row">
            <input v-model="form.retain" type="checkbox" />
            <span class="switch"></span>
            <span class="switch-copy">
              <strong>{{ t('publish.retain') }}</strong>
              <small>{{ t('publish.retainHint') }}</small>
            </span>
          </label>

          <div class="form-actions">
            <p class="form-note">
              <Icon name="info" :size="15" />
              {{ t('publish.note') }}
            </p>
            <button class="button primary" type="submit" :disabled="submitting">
              <Icon name="publish" :size="17" />
              {{ submitting ? t('publish.submitting') : t('publish.submit') }}
            </button>
          </div>
        </form>
      </section>

      <section class="panel">
        <header class="panel-header">
          <div>
            <h3>{{ t('publish.history') }}</h3>
            <p>{{ history.length }} {{ t('common.total') }}</p>
          </div>
          <Icon name="clock" :size="18" />
        </header>

        <ol v-if="history.length" class="publish-history">
          <li v-for="entry in history" :key="`${entry.topic}-${entry.timestamp}`">
            <div class="history-main">
              <span class="mono strong">{{ entry.topic }}</span>
              <span class="history-meta">
                {{ formatDate(entry.timestamp) }} · {{ entry.bytes }} B
              </span>
            </div>
            <div class="history-tags">
              <span class="qos-chip">QoS {{ entry.qos }}</span>
              <span v-if="entry.retained" class="tag amber">{{ t('publish.retain') }}</span>
            </div>
          </li>
        </ol>
        <p v-else class="muted empty-copy">{{ t('publish.noHistory') }}</p>
      </section>
    </div>
  </div>
</template>

<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useDebuggerStore } from '../stores/debugger'
import { t } from '../i18n'
import Icon from '../components/Icon.vue'

const debuggerStore = useDebuggerStore()
const {
  status,
  logs,
  paused,
  filter,
  directionFilter,
  logEncoding,
  trace,
  messagesIn,
  messagesOut,
  packetsIn,
  packetsOut,
  selected,
  subscriptions,
  publishHistory,
  connected,
  canPublish,
  uptime,
  visibleLogs,
} = storeToRefs(debuggerStore)

const { connection, subscribeForm, publishForm } = debuggerStore
const {
  connect,
  disconnect,
  addSubscription,
  removeSubscription,
  publish,
  ping,
  displayPayload,
  clearLog,
  exportLog,
  copyLog,
  directionLabel,
} = debuggerStore</script>

<template>
  <div class="view">
    <header class="page-header">
      <div>
        <p class="eyebrow">{{ t('nav.messaging') }}</p>
        <h2>{{ t('debugger.title') }}</h2>
        <p>{{ t('debugger.subtitle') }}</p>
      </div>
      <div class="header-actions">
        <span class="debug-status" :data-state="status">
          <span class="status-dot" :class="status"></span>
          {{ t(`debugger.state.${status}`) }}
        </span>
        <button class="button ghost" type="button" :disabled="!connected" @click="ping">
          <Icon name="activity" :size="15" />
          PING
        </button>
      </div>
    </header>

    <div class="debugger-stats">
      <div><span>{{ t('debugger.messagesIn') }}</span><strong>{{ messagesIn }}</strong></div>
      <div><span>{{ t('debugger.messagesOut') }}</span><strong>{{ messagesOut }}</strong></div>
      <div><span>{{ t('debugger.packets') }}</span><strong>{{ packetsIn }} / {{ packetsOut }}</strong></div>
      <div><span>{{ t('debugger.uptime') }}</span><strong>{{ uptime }}</strong></div>
      <div><span>{{ t('debugger.subscriptions') }}</span><strong>{{ subscriptions.length }}</strong></div>
    </div>

    <div class="debugger-layout">
      <div class="debugger-side">
        <section class="panel">
          <header class="panel-header">
            <div>
              <h3>{{ t('debugger.connection') }}</h3>
              <p>{{ t('debugger.wsHint') }}</p>
            </div>
            <span class="debug-status" :data-state="status">{{ t(`debugger.state.${status}`) }}</span>
          </header>

          <form class="debug-form" @submit.prevent="connect">
            <label class="field">
              <span>{{ t('debugger.url') }}</span>
              <input v-model="connection.url" spellcheck="false" placeholder="ws://localhost:8080/mqtt" />
            </label>

            <div class="form-row compact-row">
              <label class="field">
                <span>{{ t('debugger.clientId') }}</span>
                <input v-model="connection.clientId" spellcheck="false" />
              </label>
              <label class="field">
                <span>{{ t('debugger.protocol') }}</span>
                <select v-model.number="connection.protocolVersion">
                  <option :value="5">MQTT 5.0</option>
                  <option :value="4">MQTT 3.1.1</option>
                </select>
              </label>
            </div>

            <div class="form-row compact-row">
              <label class="field">
                <span>{{ t('debugger.keepalive') }}</span>
                <input v-model.number="connection.keepalive" type="number" min="0" max="65535" />
              </label>
              <label class="checkbox-inline">
                <input v-model="connection.clean" type="checkbox" />
                <span>{{ t('debugger.cleanStart') }}</span>
              </label>
            </div>

            <div class="form-row compact-row">
              <label class="field">
                <span>{{ t('debugger.username') }}</span>
                <input v-model="connection.username" autocomplete="username" />
              </label>
              <label class="field">
                <span>{{ t('debugger.password') }}</span>
                <input v-model="connection.password" type="password" autocomplete="current-password" />
              </label>
            </div>

            <details class="debug-details">
              <summary>{{ t('debugger.advanced') }}</summary>
              <div class="details-body">
                <div class="form-row compact-row">
                  <label class="field">
                    <span>{{ t('debugger.sessionExpiry') }}</span>
                    <input v-model.number="connection.sessionExpiry" type="number" min="0" />
                  </label>
                  <label class="field">
                    <span>{{ t('debugger.receiveMaximum') }}</span>
                    <input v-model.number="connection.receiveMaximum" type="number" min="1" max="65535" />
                  </label>
                </div>
                <div class="form-row compact-row">
                  <label class="field">
                    <span>{{ t('debugger.maxPacketSize') }}</span>
                    <input v-model.number="connection.maximumPacketSize" type="number" min="1" />
                  </label>
                  <label class="field">
                    <span>{{ t('debugger.topicAliasMaximum') }}</span>
                    <input v-model.number="connection.topicAliasMaximum" type="number" min="0" max="65535" />
                  </label>
                </div>
                <label class="checkbox-inline">
                  <input v-model="connection.requestResponseInformation" type="checkbox" />
                  <span>{{ t('debugger.requestResponseInfo') }}</span>
                </label>
                <label class="checkbox-inline">
                  <input v-model="connection.requestProblemInformation" type="checkbox" />
                  <span>{{ t('debugger.requestProblemInfo') }}</span>
                </label>

                <p class="details-label">{{ t('debugger.will') }}</p>
                <label class="field">
                  <span>{{ t('debugger.willTopic') }}</span>
                  <input v-model="connection.willTopic" spellcheck="false" />
                </label>
                <div class="form-row compact-row">
                  <label class="field">
                    <span>{{ t('debugger.willPayload') }}</span>
                    <input v-model="connection.willPayload" />
                  </label>
                  <label class="field">
                    <span>{{ t('publish.qos') }}</span>
                    <select v-model.number="connection.willQos">
                      <option :value="0">QoS 0</option>
                      <option :value="1">QoS 1</option>
                      <option :value="2">QoS 2</option>
                    </select>
                  </label>
                </div>
                <div class="form-row compact-row">
                  <label class="field">
                    <span>{{ t('debugger.willDelay') }}</span>
                    <input v-model.number="connection.willDelay" type="number" min="0" />
                  </label>
                  <label class="checkbox-inline">
                    <input v-model="connection.willRetain" type="checkbox" />
                    <span>{{ t('publish.retain') }}</span>
                  </label>
                </div>
              </div>
            </details>

            <div class="debug-actions">
              <button v-if="!connected" class="button primary" type="submit">
                <Icon name="wifi" :size="16" />
                {{ t('debugger.connect') }}
              </button>
              <button v-else class="button danger" type="button" @click="disconnect">
                <Icon name="close" :size="16" />
                {{ t('debugger.disconnect') }}
              </button>
              <span class="debug-tip">{{ t('debugger.secureHint') }}</span>
            </div>
          </form>
        </section>

        <section class="panel">
          <header class="panel-header">
            <div>
              <h3>{{ t('debugger.subscriptions') }}</h3>
              <p>{{ t('debugger.subHint') }}</p>
            </div>
            <span class="tag">{{ subscriptions.length }}</span>
          </header>

          <div class="debug-form">
            <label class="field">
              <span>{{ t('debugger.filter') }}</span>
              <input v-model="subscribeForm.filter" spellcheck="false" placeholder="sensors/#" />
            </label>
            <div class="form-row compact-row">
              <label class="field">
                <span>{{ t('debugger.qos') }}</span>
                <select v-model.number="subscribeForm.qos">
                  <option :value="0">QoS 0</option>
                  <option :value="1">QoS 1</option>
                  <option :value="2">QoS 2</option>
                </select>
              </label>
              <label class="field">
                <span>{{ t('debugger.retainHandling') }}</span>
                <select v-model.number="subscribeForm.retainHandling">
                  <option :value="0">0</option>
                  <option :value="1">1</option>
                  <option :value="2">2</option>
                </select>
              </label>
            </div>
            <div class="form-row compact-row">
              <label class="field">
                <span>{{ t('debugger.subscriptionId') }}</span>
                <input v-model.number="subscribeForm.identifier" type="number" min="1" />
              </label>
              <label class="checkbox-inline">
                <input v-model="subscribeForm.noLocal" type="checkbox" />
                <span>noLocal</span>
              </label>
              <label class="checkbox-inline">
                <input v-model="subscribeForm.retainAsPublished" type="checkbox" />
                <span>RAP</span>
              </label>
            </div>
            <button class="button" type="button" :disabled="!connected" @click="addSubscription">
              <Icon name="plus" :size="16" />
              {{ t('debugger.subscribe') }}
            </button>

            <ul v-if="subscriptions.length" class="debug-subscriptions">
              <li v-for="subscription in subscriptions" :key="subscription.filter">
                <div>
                  <span class="mono">{{ subscription.filter }}</span>
                  <small>QoS {{ subscription.qos }}<span v-if="subscription.noLocal"> · noLocal</span><span v-if="subscription.retainAsPublished"> · RAP</span></small>
                </div>
                <button class="icon-button small danger-hover" type="button" @click="removeSubscription(subscription)">
                  <Icon name="trash" :size="14" />
                </button>
              </li>
            </ul>
            <p v-else class="muted debug-empty">{{ t('debugger.noSubscriptions') }}</p>
          </div>
        </section>
      </div>

      <div class="debugger-main">
        <section class="panel">
          <header class="panel-header">
            <div>
              <h3>{{ t('debugger.publish') }}</h3>
              <p>{{ t('debugger.publishHint') }}</p>
            </div>
            <button class="button primary small" type="button" :disabled="!canPublish" @click="publish">
              <Icon name="publish" :size="15" />
              {{ t('debugger.publishMessage') }}
            </button>
          </header>

          <div class="debug-form">
            <div class="form-row compact-row">
              <label class="field">
                <span>{{ t('debugger.topic') }}</span>
                <input v-model="publishForm.topic" spellcheck="false" list="publish-history" />
                <datalist id="publish-history">
                  <option v-for="topic in publishHistory" :key="topic" :value="topic" />
                </datalist>
              </label>
              <label class="field">
                <span>{{ t('debugger.encoding') }}</span>
                <select v-model="publishForm.encoding">
                  <option value="plain">{{ t('publish.plain') }}</option>
                  <option value="base64">Base64</option>
                  <option value="hex">Hex</option>
                </select>
              </label>
            </div>

            <label class="field">
              <span>{{ t('debugger.payload') }}</span>
              <textarea v-model="publishForm.payload" rows="7" spellcheck="false"></textarea>
            </label>

            <div class="form-row compact-row">
              <label class="field">
                <span>{{ t('debugger.qos') }}</span>
                <select v-model.number="publishForm.qos">
                  <option :value="0">QoS 0</option>
                  <option :value="1">QoS 1</option>
                  <option :value="2">QoS 2</option>
                </select>
              </label>
              <label class="checkbox-inline">
                <input v-model="publishForm.retain" type="checkbox" />
                <span>{{ t('publish.retain') }}</span>
              </label>
            </div>

            <details v-if="connection.protocolVersion === 5" class="debug-details" open>
              <summary>{{ t('debugger.properties') }}</summary>
              <div class="details-body">
                <div class="form-row compact-row">
                  <label class="field">
                    <span>{{ t('debugger.contentType') }}</span>
                    <input v-model="publishForm.contentType" placeholder="application/json" />
                  </label>
                  <label class="field">
                    <span>{{ t('debugger.responseTopic') }}</span>
                    <input v-model="publishForm.responseTopic" />
                  </label>
                </div>
                <div class="form-row compact-row">
                  <label class="field">
                    <span>{{ t('debugger.correlationData') }}</span>
                    <input v-model="publishForm.correlationData" />
                  </label>
                  <label class="field">
                    <span>{{ t('debugger.messageExpiry') }}</span>
                    <input v-model.number="publishForm.messageExpiry" type="number" min="1" />
                  </label>
                </div>
                <div class="form-row compact-row">
                  <label class="field">
                    <span>{{ t('debugger.userKey') }}</span>
                    <input v-model="publishForm.userKey" />
                  </label>
                  <label class="field">
                    <span>{{ t('debugger.userValue') }}</span>
                    <input v-model="publishForm.userValue" />
                  </label>
                </div>
              </div>
            </details>
          </div>
        </section>

        <section class="panel debug-log-panel">
          <header class="debug-log-header">
            <div>
              <h3>{{ t('debugger.log') }}</h3>
              <p>{{ visibleLogs.length }} / {{ logs.length }}</p>
            </div>
            <div class="debug-log-tools">
              <label class="checkbox-inline">
                <input v-model="trace" type="checkbox" />
                <span>{{ t('debugger.protocolTrace') }}</span>
              </label>
              <select v-model="logEncoding" class="mini-select">
                <option value="plain">{{ t('publish.plain') }}</option>
                <option value="base64">Base64</option>
                <option value="hex">Hex</option>
              </select>
              <button class="icon-button small" type="button" :title="t('debugger.copy')" @click="copyLog">
                <Icon name="copy" :size="14" />
              </button>
              <button class="icon-button small" type="button" :title="t('debugger.export')" @click="exportLog">
                <Icon name="arrowRight" :size="14" />
              </button>
              <button class="icon-button small" type="button" :title="t('debugger.clear')" @click="clearLog">
                <Icon name="trash" :size="14" />
              </button>
            </div>
          </header>

          <div class="debug-log-filter">
            <div class="search-field">
              <Icon name="search" :size="15" />
              <input v-model="filter" type="search" :placeholder="t('debugger.filterMessages')" />
            </div>
            <select v-model="directionFilter" class="mini-select">
              <option value="all">{{ t('common.all') }}</option>
              <option value="in">{{ t('debugger.inbound') }}</option>
              <option value="out">{{ t('debugger.outbound') }}</option>
              <option value="system">{{ t('debugger.system') }}</option>
            </select>
            <button class="pill-button" type="button" @click="paused = !paused">
              {{ paused ? t('debugger.resume') : t('debugger.pause') }}
            </button>
          </div>

          <div v-if="visibleLogs.length" class="debug-log-list">
            <template v-for="entry in visibleLogs" :key="entry.id">
              <button
                class="debug-log-row"
                :data-direction="entry.direction"
                type="button"
                @click="selected = selected?.id === entry.id ? null : entry"
              >
                <span class="log-time">{{ entry.at }}</span>
                <span class="log-direction">{{ directionLabel(entry.direction) }}</span>
                <span class="log-title">
                  <strong class="mono">{{ entry.title }}</strong>
                  <small v-if="entry.qos !== undefined">QoS {{ entry.qos }}<span v-if="entry.retain"> · retain</span></small>
                </span>
                <span class="log-size">{{ entry.payload ? `${entry.payload.length} B` : '' }}</span>
              </button>
              <div v-if="selected?.id === entry.id" class="debug-log-detail">
                <header>
                  <strong class="mono">{{ entry.title }}</strong>
                  <button class="icon-button small" type="button" @click="selected = null">
                    <Icon name="close" :size="14" />
                  </button>
                </header>
                <pre v-if="entry.payload">{{ displayPayload(entry) }}</pre>
                <pre v-if="entry.detail">{{ entry.detail }}</pre>
              </div>
            </template>
          </div>
          <p v-else class="debug-empty muted">{{ t('debugger.emptyLog') }}</p>
        </section>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, shallowRef } from 'vue'
import mqtt, { type IClientOptions, type MqttClient } from 'mqtt'
import { t } from '../i18n'
import { useToast } from '../composables/useToast'
import Icon from '../components/Icon.vue'

type Direction = 'in' | 'out' | 'system'
type PayloadEncoding = 'plain' | 'base64' | 'hex'

interface LogEntry {
  id: number
  at: string
  direction: Direction
  kind: 'packet' | 'message' | 'system'
  title: string
  qos?: number
  retain?: boolean
  payload?: Uint8Array
  detail?: string
}

interface SavedSubscription {
  filter: string
  qos: 0 | 1 | 2
  noLocal: boolean
  retainAsPublished: boolean
  retainHandling: 0 | 1 | 2
  identifier?: number
}

const toast = useToast()
const status = ref<'disconnected' | 'connecting' | 'connected' | 'reconnecting' | 'error'>('disconnected')
// A plain ref would deep-proxy the MQTT client and break its internal identity
// checks, so keep the instance raw.
const client = shallowRef<MqttClient | null>(null)
const logs = ref<LogEntry[]>([])
const paused = ref(false)
const filter = ref('')
const directionFilter = ref<'all' | 'in' | 'out' | 'system'>('all')
const logEncoding = ref<PayloadEncoding>('plain')
const trace = ref(true)
const connectedAt = ref<Date | null>(null)
const now = ref(Date.now())
const messagesIn = ref(0)
const messagesOut = ref(0)
const packetsIn = ref(0)
const packetsOut = ref(0)
const selected = ref<LogEntry | null>(null)
const subscriptions = ref<SavedSubscription[]>([])
const publishHistory = ref<string[]>([])
let logId = 1
let clock: number | undefined

const defaultUrl = (() => {
  const scheme = window.location.protocol === 'https:' ? 'wss' : 'ws'
  return `${scheme}://${window.location.host}/mqtt`
})()

const connection = reactive({
  url: defaultUrl,
  clientId: `nextmqtt-debug-${Math.random().toString(16).slice(2, 8)}`,
  protocolVersion: 5 as 4 | 5,
  clean: true,
  keepalive: 60,
  username: '',
  password: '',
  sessionExpiry: 0,
  receiveMaximum: 32,
  maximumPacketSize: 1048576,
  topicAliasMaximum: 32,
  requestResponseInformation: false,
  requestProblemInformation: true,
  willTopic: '',
  willPayload: '',
  willQos: 0 as 0 | 1 | 2,
  willRetain: false,
  willDelay: 0,
})

const subscribeForm = reactive({
  filter: '#',
  qos: 1 as 0 | 1 | 2,
  noLocal: false,
  retainAsPublished: false,
  retainHandling: 0 as 0 | 1 | 2,
  identifier: undefined as number | undefined,
})

const publishForm = reactive({
  topic: 'demo/hello',
  payload: '{"hello":"world"}',
  qos: 1 as 0 | 1 | 2,
  retain: false,
  encoding: 'plain' as PayloadEncoding,
  contentType: '',
  responseTopic: '',
  correlationData: '',
  messageExpiry: undefined as number | undefined,
  userKey: '',
  userValue: '',
})

const connected = computed(() => status.value === 'connected')
const canPublish = computed(() => connected.value && publishForm.topic.trim().length > 0)
const uptime = computed(() => {
  if (!connectedAt.value) return '—'
  const seconds = Math.max(0, Math.floor((now.value - connectedAt.value.getTime()) / 1000))
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = seconds % 60
  return h > 0 ? `${h}h ${m}m` : `${m}m ${s}s`
})

const visibleLogs = computed(() => {
  const needle = filter.value.trim().toLowerCase()
  return logs.value.filter(entry => {
    if (directionFilter.value !== 'all' && entry.direction !== directionFilter.value) return false
    if (!needle) return true
    return `${entry.title} ${entry.detail ?? ''}`.toLowerCase().includes(needle)
  })
})

function addLog(entry: Omit<LogEntry, 'id' | 'at'>): void {
  if (paused.value) return
  logs.value.unshift({
    ...entry,
    id: logId++,
    at: new Date().toLocaleTimeString(undefined, { hour12: false }),
  })
  if (logs.value.length > 600) logs.value.length = 600
}

function system(message: string): void {
  addLog({ direction: 'system', kind: 'system', title: message })
}

function packetLabel(packet: any): string {
  const cmd = String(packet?.cmd ?? 'packet').toUpperCase()
  const id = packet?.messageId != null ? ` #${packet.messageId}` : ''
  const reason = packet?.reasonCode != null ? ` reason=0x${Number(packet.reasonCode).toString(16)}` : ''
  return `${cmd}${id}${reason}`
}

function packetDetail(packet: any): string {
  try {
    const copy: Record<string, unknown> = {}
    for (const key of ['cmd', 'qos', 'retain', 'dup', 'messageId', 'reasonCode', 'topic', 'properties']) {
      if (packet?.[key] !== undefined) copy[key] = packet[key]
    }
    return JSON.stringify(copy)
  } catch {
    return ''
  }
}

function attach(clientInstance: MqttClient): void {
  clientInstance.on('connect', packet => {
    status.value = 'connected'
    connectedAt.value = new Date()
    system(t('debugger.connectedMessage', { clientId: connection.clientId }))
    if (packet) packetsIn.value++
    for (const subscription of subscriptions.value) {
      subscribeOnWire(subscription, true)
    }
  })
  clientInstance.on('reconnect', () => { status.value = 'reconnecting' })
  clientInstance.on('close', () => {
    if (status.value !== 'disconnected') status.value = 'disconnected'
    connectedAt.value = null
  })
  clientInstance.on('offline', () => { status.value = 'disconnected' })
  clientInstance.on('error', error => {
    status.value = 'error'
    system(error.message)
    toast.error(error.message)
  })
  clientInstance.on('message', (topic, payload, packet) => {
    messagesIn.value++
    addLog({
      direction: 'in',
      kind: 'message',
      title: topic,
      qos: packet.qos,
      retain: packet.retain,
      payload: Uint8Array.from(payload),
      detail: JSON.stringify({ qos: packet.qos, retain: packet.retain, properties: (packet as any).properties ?? {} }),
    })
  })
  clientInstance.on('packetsend', (packet: any) => {
    packetsOut.value++
    if (!trace.value || packet?.cmd === 'publish') return
    addLog({ direction: 'out', kind: 'packet', title: packetLabel(packet), detail: packetDetail(packet) })
  })
  clientInstance.on('packetreceive', (packet: any) => {
    packetsIn.value++
    if (!trace.value || packet?.cmd === 'publish') return
    addLog({ direction: 'in', kind: 'packet', title: packetLabel(packet), detail: packetDetail(packet) })
  })
}

function connect(): void {
  if (!connection.url.trim() || !connection.clientId.trim()) {
    toast.error(t('debugger.urlRequired'))
    return
  }
  disconnect()
  status.value = 'connecting'

  const properties: Record<string, unknown> = {}
  if (connection.protocolVersion === 5) {
    if (connection.sessionExpiry > 0) properties.sessionExpiryInterval = connection.sessionExpiry
    if (connection.receiveMaximum > 0) properties.receiveMaximum = connection.receiveMaximum
    if (connection.maximumPacketSize > 0) properties.maximumPacketSize = connection.maximumPacketSize
    if (connection.topicAliasMaximum > 0) properties.topicAliasMaximum = connection.topicAliasMaximum
    if (connection.requestResponseInformation) properties.requestResponseInformation = true
    properties.requestProblemInformation = connection.requestProblemInformation
  }

  const options: IClientOptions = {
    clientId: connection.clientId.trim(),
    protocolVersion: connection.protocolVersion,
    clean: connection.clean,
    keepalive: connection.keepalive,
    connectTimeout: 10000,
    reconnectPeriod: 0,
    username: connection.username || undefined,
    password: connection.password || undefined,
  }

  if (connection.protocolVersion === 5) {
    (options as any).properties = properties
  }

  if (connection.willTopic.trim()) {
    (options as any).will = {
      topic: connection.willTopic.trim(),
      payload: connection.willPayload,
      qos: connection.willQos,
      retain: connection.willRetain,
      properties: connection.protocolVersion === 5 && connection.willDelay > 0
        ? { willDelayInterval: connection.willDelay }
        : undefined,
    }
  }

  try {
    const instance = mqtt.connect(connection.url.trim(), options)
    client.value = instance
    attach(instance)
  } catch (error) {
    status.value = 'error'
    system(error instanceof Error ? error.message : String(error))
  }
}

function disconnect(): void {
  const instance = client.value
  client.value = null
  connectedAt.value = null
  status.value = 'disconnected'
  if (instance) {
    try { instance.end(false) } catch { /* already closed */ }
  }
}

function subscribeOnWire(subscription: SavedSubscription, quiet = false): void {
  const instance = client.value
  if (!instance) return
  const options: Record<string, unknown> = {
    qos: subscription.qos,
    nl: subscription.noLocal,
    rap: subscription.retainAsPublished,
    rh: subscription.retainHandling,
  }
  if (subscription.identifier !== undefined) {
    options.properties = { subscriptionIdentifier: subscription.identifier }
  }
  instance.subscribe(subscription.filter, options as any, (error, granted) => {
    if (error) {
      system(error.message)
      return
    }
    if (!quiet) {
      system(t('debugger.subscribed', { filter: subscription.filter, qos: granted?.[0]?.qos ?? subscription.qos }))
    }
  })
}

function addSubscription(): void {
  const value = subscribeForm.filter.trim()
  if (!value) return
  if (!connected.value) {
    toast.error(t('debugger.connectFirst'))
    return
  }
  if (subscriptions.value.some(item => item.filter === value)) {
    toast.info(t('debugger.alreadySubscribed'))
    return
  }
  const subscription: SavedSubscription = {
    filter: value,
    qos: subscribeForm.qos,
    noLocal: subscribeForm.noLocal,
    retainAsPublished: subscribeForm.retainAsPublished,
    retainHandling: subscribeForm.retainHandling,
    identifier: subscribeForm.identifier ? Number(subscribeForm.identifier) : undefined,
  }
  subscriptions.value.push(subscription)
  subscribeOnWire(subscription)
}

function removeSubscription(subscription: SavedSubscription): void {
  subscriptions.value = subscriptions.value.filter(item => item !== subscription)
  if (client.value) {
    client.value.unsubscribe(subscription.filter, () => system(t('debugger.unsubscribed', { filter: subscription.filter })))
  }
}

function payloadBytes(): Uint8Array {
  const value = publishForm.payload
  if (publishForm.encoding === 'base64') {
    const binary = atob(value)
    return Uint8Array.from(binary, character => character.charCodeAt(0))
  }
  if (publishForm.encoding === 'hex') {
    const cleaned = value.replace(/\s+/g, '')
    if (cleaned.length % 2 !== 0) throw new Error(t('debugger.invalidHex'))
    return Uint8Array.from(cleaned.match(/.{2}/g)?.map(byte => parseInt(byte, 16)) ?? [])
  }
  return new TextEncoder().encode(value)
}

function publish(): void {
  if (!client.value || !canPublish.value) return
  let payload: Uint8Array
  try {
    payload = payloadBytes()
  } catch (error) {
    toast.error(error instanceof Error ? error.message : t('debugger.invalidPayload'))
    return
  }

  const options: Record<string, unknown> = { qos: publishForm.qos, retain: publishForm.retain }
  if (connection.protocolVersion === 5) {
    const properties: Record<string, unknown> = {}
    if (publishForm.contentType) properties.contentType = publishForm.contentType
    if (publishForm.responseTopic) properties.responseTopic = publishForm.responseTopic
    if (publishForm.correlationData) properties.correlationData = publishForm.correlationData
    if (publishForm.messageExpiry) properties.messageExpiryInterval = publishForm.messageExpiry
    if (publishForm.userKey) {
      properties.userProperties = { [publishForm.userKey]: publishForm.userValue }
    }
    // MQTT.js expects a boolean for this property, not the wire value 0/1.
    if (publishForm.encoding === 'plain') properties.payloadFormatIndicator = true
    if (Object.keys(properties).length) options.properties = properties
  }

  client.value.publish(publishForm.topic.trim(), payload as any, options as any, error => {
    if (error) {
      toast.error(error.message)
      return
    }
    messagesOut.value++
    addLog({
      direction: 'out',
      kind: 'message',
      title: publishForm.topic.trim(),
      qos: publishForm.qos,
      retain: publishForm.retain,
      payload: Uint8Array.from(payload),
      detail: JSON.stringify({ qos: publishForm.qos, retain: publishForm.retain, properties: (options.properties as any) ?? {} }),
    })
    if (!publishHistory.value.includes(publishForm.topic.trim())) {
      publishHistory.value.unshift(publishForm.topic.trim())
      publishHistory.value.length = Math.min(publishHistory.value.length, 10)
    }
  })
}

function ping(): void {
  const instance = client.value
  if (!instance) return
  try {
    instance.sendPing()
    system('PING → broker')
  } catch (error) {
    system(error instanceof Error ? error.message : String(error))
  }
}

function displayPayload(entry: LogEntry): string {
  if (!entry.payload) return ''
  if (logEncoding.value === 'hex') {
    return Array.from(entry.payload, byte => byte.toString(16).padStart(2, '0')).join(' ')
  }
  if (logEncoding.value === 'base64') {
    let binary = ''
    entry.payload.forEach(byte => { binary += String.fromCharCode(byte) })
    return btoa(binary)
  }
  return new TextDecoder().decode(entry.payload)
}

function clearLog(): void {
  logs.value = []
  selected.value = null
}

function exportLog(): void {
  const data = logs.value.map(entry => ({
    at: entry.at,
    direction: entry.direction,
    kind: entry.kind,
    title: entry.title,
    qos: entry.qos,
    retain: entry.retain,
    payload: entry.payload ? displayPayload(entry) : undefined,
    detail: entry.detail,
  }))
  const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `nextmqtt-debug-${Date.now()}.json`
  anchor.click()
  URL.revokeObjectURL(url)
}

function copyLog(): void {
  void navigator.clipboard.writeText(JSON.stringify(logs.value.map(entry => ({
    at: entry.at,
    direction: entry.direction,
    title: entry.title,
    detail: entry.detail,
    payload: entry.payload ? displayPayload(entry) : undefined,
  })), null, 2)).then(() => toast.success(t('toast.copied')))
}

function directionLabel(direction: Direction): string {
  if (direction === 'in') return t('debugger.inbound')
  if (direction === 'out') return t('debugger.outbound')
  return t('debugger.system')
}

clock = window.setInterval(() => { now.value = Date.now() }, 1000)

onBeforeUnmount(() => {
  if (clock) window.clearInterval(clock)
  disconnect()
})
</script>

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

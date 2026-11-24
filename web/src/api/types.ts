export interface BrokerStats {
  connectionsOpened: number
  connectionsClosed: number
  connectionsRejected: number
  authFailures: number
  publishesReceived: number
  publishesSent: number
  bytesReceived: number
  bytesSent: number
  droppedMessages: number
  retransmissions: number
  subscriptionsCreated: number
  sessionsExpired: number
  connectedClients: number
  sessions: number
  subscriptions: number
  retainedMessages: number
  messagesPerSecond: number[]
  collectedAt: string
}

export interface ClientInfo {
  clientId: string
  username?: string
  protocolVersion: string
  connected: boolean
  remoteAddress?: string
  connectedAt: string
  lastSeen: string
  sessionExpiryInterval: number
  subscriptions: number
  inflightMessages: number
  queuedMessages: number
  isSuperuser: boolean
}

export interface SubscriptionInfo {
  clientId: string
  filter: string
  sharedGroup?: string
  qoS: string
  noLocal: boolean
  retainAsPublished: boolean
  retainHandling: string
  identifier?: number
}

export interface RetainedInfo {
  topic: string
  payloadBytes: number
  qoS: string
  storedAt: string
  expiresAt?: string
}

export interface ClusterNode {
  nodeId: string
  address: string
  status: string
  lastSeen: string
  isSelf: boolean
}

export interface ClusterInfo {
  nodeId: string
  status: string
  nodes: ClusterNode[]
}

export interface Paged<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface LoginResponse {
  token: string
  expiresAt: string
  username: string
}

export interface PublishResult {
  topic: string
  bytes: number
  qos: number
  retain: boolean
}

export interface ConfigInfo {
  server: {
    tcp: { enabled: boolean; host: string; port: number }
    tls: { enabled: boolean; port: number }
    websocket: { enabled: boolean; path: string }
    http: { port: number; webSocketPort: number }
  }
  broker: {
    maxPacketSize: number
    maxInflightMessages: number
    maxQueuedMessages: number
    maxSubscriptionsPerSession: number
    maxTopicAlias: number
  }
  cluster: ClusterInfo
}

export interface PublishRequest {
  topic: string
  payload: string
  encoding: 'plain' | 'base64'
  qos: 0 | 1 | 2
  retain: boolean
}

export interface VersionInfo {
  name: string
  version: string
  runtime: string
  environment: string
}

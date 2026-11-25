import type {
  BrokerStats,
  ClientInfo,
  ClusterInfo,
  ConfigInfo,
  LoginResponse,
  LogsInfo,
  Paged,
  PluginsInfo,
  PublishRequest,
  PublishResult,
  RetainedInfo,
  SubscriptionInfo,
  VersionInfo,
} from './types'

export const TOKEN_KEY = 'nextmqtt.token'
export const USER_KEY = 'nextmqtt.username'

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly code?: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

function token(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

async function request<T>(
  path: string,
  init: RequestInit = {},
  authenticated = true,
): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')

  if (init.body !== undefined && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  if (authenticated) {
    const value = token()
    if (value) headers.set('Authorization', `Bearer ${value}`)
  }

  let response: Response

  try {
    response = await fetch(path, { ...init, headers })
  } catch {
    throw new ApiError(0, 'Network request failed. Is the broker reachable?')
  }

  if (response.status === 401 && authenticated) {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(USER_KEY)
    window.dispatchEvent(new CustomEvent('nextmqtt:unauthorized'))
  }

  if (!response.ok) {
    let message = response.statusText || `HTTP ${response.status}`
    let code: string | undefined

    try {
      const body = (await response.json()) as { message?: string; code?: string }
      message = body.message ?? message
      code = body.code
    } catch {
      // Non-JSON error body.
    }

    throw new ApiError(response.status, message, code)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

function query(params: Record<string, string | number | undefined>): string {
  const search = new URLSearchParams()

  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== '') {
      search.set(key, String(value))
    }
  }

  const value = search.toString()
  return value ? `?${value}` : ''
}

export const api = {
  login(username: string, password: string): Promise<LoginResponse> {
    return request<LoginResponse>(
      '/api/v1/auth/login',
      {
        method: 'POST',
        body: JSON.stringify({ username, password }),
      },
      false,
    )
  },

  me(): Promise<{ username?: string; superuser: boolean }> {
    return request('/api/v1/auth/me')
  },

  stats(): Promise<BrokerStats> {
    return request('/api/v1/stats')
  },

  clients(params: { search?: string; page?: number; pageSize?: number } = {}): Promise<Paged<ClientInfo>> {
    return request(`/api/v1/clients${query(params)}`)
  },

  kickClient(clientId: string): Promise<void> {
    return request(`/api/v1/clients/${encodeURIComponent(clientId)}`, { method: 'DELETE' })
  },

  subscriptions(params: { search?: string; page?: number; pageSize?: number } = {}): Promise<Paged<SubscriptionInfo>> {
    return request(`/api/v1/subscriptions${query(params)}`)
  },

  retained(params: { search?: string; page?: number; pageSize?: number } = {}): Promise<Paged<RetainedInfo>> {
    return request(`/api/v1/retained${query(params)}`)
  },

  deleteRetained(topic: string): Promise<void> {
    return request(`/api/v1/retained${query({ topic })}`, { method: 'DELETE' })
  },

  publish(payload: PublishRequest): Promise<PublishResult> {
    return request('/api/v1/publish', {
      method: 'POST',
      body: JSON.stringify({
        topic: payload.topic,
        payload: payload.payload,
        encoding: payload.encoding,
        qos: payload.qos,
        retain: payload.retain,
      }),
    })
  },

  cluster(): Promise<ClusterInfo> {
    return request('/api/v1/cluster')
  },

  version(): Promise<VersionInfo> {
    return request('/api/v1/version', {}, false)
  },

  plugins(): Promise<PluginsInfo> {
    return request('/api/v1/plugins')
  },

  logs(params: { level?: string; search?: string; limit?: number } = {}): Promise<LogsInfo> {
    return request(`/api/v1/logs${query(params)}`)
  },

  config(): Promise<ConfigInfo> {
    return request('/api/v1/config')
  },
}

export type ApiMethod = 'GET' | 'POST' | 'DELETE'

export interface ApiEndpoint {
  readonly method: ApiMethod
  readonly path: string
  readonly description: string
  readonly auth: boolean
  readonly body?: string
}

/**
 * Every endpoint listed in docs/api.md. scripts/api-surface-check.mjs compares
 * this list with the documentation table, so a new endpoint must be added in
 * both places.
 */
export const apiEndpoints: ApiEndpoint[] = [
  {
    method: 'GET',
    path: '/api/v1/health',
    description: 'Version and liveness',
    auth: false,
  },
  {
    method: 'GET',
    path: '/api/v1/ready',
    description: 'Readiness and session count',
    auth: false,
  },
  {
    method: 'GET',
    path: '/api/v1/version',
    description: 'Product, runtime and environment',
    auth: false,
  },
  {
    method: 'POST',
    path: '/api/v1/auth/login',
    description: 'Issue a dashboard token',
    auth: false,
    body: '{\n  "username": "admin",\n  "password": "..."\n}',
  },
  {
    method: 'GET',
    path: '/api/v1/auth/me',
    description: 'Current principal',
    auth: true,
  },
  {
    method: 'GET',
    path: '/api/v1/stats',
    description: 'Counters and 60-second publish series',
    auth: true,
  },
  {
    method: 'GET',
    path: '/api/v1/clients',
    description: 'Paginated clients/sessions',
    auth: true,
  },
  {
    method: 'GET',
    path: '/api/v1/clients/{clientId}',
    description: 'One client',
    auth: true,
  },
  {
    method: 'DELETE',
    path: '/api/v1/clients/{clientId}',
    description: 'Disconnect a client',
    auth: true,
  },
  {
    method: 'GET',
    path: '/api/v1/subscriptions',
    description: 'Paginated subscriptions',
    auth: true,
  },
  {
    method: 'GET',
    path: '/api/v1/retained',
    description: 'Paginated retained messages',
    auth: true,
  },
  {
    method: 'DELETE',
    path: '/api/v1/retained?topic=...',
    description: 'Delete a retained message',
    auth: true,
  },
  {
    method: 'POST',
    path: '/api/v1/publish',
    description: 'Publish through the broker',
    auth: true,
    body: '{\n  "topic": "devices/demo/command",\n  "payload": "{\\"action\\":\\"reboot\\"}",\n  "encoding": "plain",\n  "qos": 1,\n  "retain": false\n}',
  },
  {
    method: 'GET',
    path: '/api/v1/cluster',
    description: 'Membership snapshot',
    auth: true,
  },
  {
    method: 'GET',
    path: '/api/v1/logs',
    description: 'Recent log entries, filtered by level and text',
    auth: true,
  },
  {
    method: 'GET',
    path: '/api/v1/plugins',
    description: 'Loaded plugins and which hooks are active',
    auth: true,
  },
  {
    method: 'POST',
    path: '/api/v1/plugins/reload',
    description: 'Rescan the plugin directory and swap the hooks',
    auth: true,
  },
  {
    method: 'GET',
    path: '/api/v1/config',
    description: 'Effective safe configuration',
    auth: true,
  },
  {
    method: 'GET',
    path: '/metrics',
    description: 'Prometheus exposition',
    auth: true,
  },
]

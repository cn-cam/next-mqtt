# REST API

관리 API는 `Server:Http:Port`(기본 8080)의 `/api/v1`에서 제공됩니다. 대시보드도
같은 엔드포인트를 사용합니다. **System → API console** 페이지에서 로그인한 세션으로
아래 모든 엔드포인트를 호출할 수 있습니다.

## 인증

공개 엔드포인트:

- `GET /healthz`, `GET /readyz`
- `GET /api/v1/health`, `GET /api/v1/ready`, `GET /api/v1/version`
- `POST /api/v1/auth/login`
- `Admin:MetricsRequireAuth=false`일 때 `GET /metrics`

나머지는 다음 중 하나가 필요합니다:

```http
Authorization: Bearer <token>
```

```http
X-API-Key: <api-key>
```

토큰은 로그인으로 발급되며 `Admin:TokenLifetimeMinutes`에 따라 만료됩니다. API
키는 SHA-256 다이제스트로 대조합니다.

### 로그인

```bash
TOKEN=$(curl -s http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"change-me-now"}' | jq -r .token)
```

```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "expiresAt": "2026-11-24T13:00:00Z",
  "username": "admin"
}
```

### 오류

오류 형식은 고정입니다:

```json
{ "code": "unauthorized", "message": "A valid bearer token or API key is required." }
```

| 상태 | 의미 |
| --- | --- |
| 400 | 요청 본문 또는 토픽이 잘못됨 |
| 401 | 토큰 또는 API 키가 없거나 유효하지 않음 |
| 404 | 리소스 없음 |
| 409 | 이미 처리된 클러스터 메시지 |
| 413 | 요청 본문이 너무 큼 |
| 500 | 처리되지 않은 서버 오류 |

## 엔드포인트

| 메서드 | 경로 | 설명 |
| --- | --- | --- |
| GET | `/api/v1/health` | 버전과 생존 상태 |
| GET | `/api/v1/ready` | 준비 상태와 세션 수 |
| GET | `/api/v1/version` | 제품, 런타임, 환경 |
| POST | `/api/v1/auth/login` | 대시보드 토큰 발급 |
| GET | `/api/v1/auth/me` | 현재 주체 |
| GET | `/api/v1/stats` | 카운터와 60초 발행 계열 |
| GET | `/api/v1/clients` | 페이지 단위 클라이언트/세션 |
| GET | `/api/v1/clients/{clientId}` | 단일 클라이언트 |
| DELETE | `/api/v1/clients/{clientId}` | 클라이언트 연결 끊기 |
| GET | `/api/v1/subscriptions` | 페이지 단위 구독 |
| GET | `/api/v1/retained` | 페이지 단위 retained 메시지 |
| DELETE | `/api/v1/retained?topic=...` | retained 메시지 삭제 |
| POST | `/api/v1/publish` | Broker를 통해 발행 |
| GET | `/api/v1/cluster` | 멤버십 스냅숏 |
| GET | `/api/v1/logs` | 최근 로그, 레벨과 문자열로 필터 |
| GET | `/api/v1/plugins` | 로드된 플러그인과 활성 훅 |
| POST | `/api/v1/plugins/reload` | 디렉터리를 다시 검색하고 훅 교체 |
| GET | `/api/v1/config` | 적용 중인 안전 설정 |
| GET | `/metrics` | Prometheus 형식 |

### 로그

```http
GET /api/v1/logs?level=Warning&search=cluster&limit=200
```

```json
{
  "entries": [
    {
      "timestamp": "2026-11-25T20:00:52.123+00:00",
      "level": "Warning",
      "category": "NextMqtt.Broker.Clustering.ClusterRouter",
      "message": "Cluster node node-b timed out and was removed",
      "exception": null
    }
  ],
  "total": 42,
  "capacity": 2000,
  "level": "Warning"
}
```

`level`은 `trace`, `debug`, `information`/`info`, `warning`/`warn`, `error`,
`critical`/`fatal`을 받습니다. 항목은 유계 메모리 링에 보관되므로 `total`이
`capacity`보다 클 수 있습니다.

### 플러그인

```http
GET /api/v1/plugins
```

```json
{
  "enabled": true,
  "directory": "plugins",
  "contractVersion": 1,
  "authenticator": "ProbeAuth.DenyAllAuthenticator",
  "authorizer": null,
  "authenticatorLoaded": true,
  "authorizerLoaded": false,
  "instances": 1,
  "reloads": 0,
  "plugins": [
    { "name": "probe-auth", "version": "1.0.0", "directory": "/app/plugins/probe-auth", "assembly": "ProbeAuth" }
  ]
}
```

`POST /api/v1/plugins/reload`는 구성의 `Plugins` 구역을 다시 읽고, 디렉터리를 다시
검색하며, 새 호스트를 로드해 인증기와 인가기를 교체한 뒤 이전 호스트를 폐기하고
언로드합니다. 디스크에서 사라진 플러그인은 다음 리로드에서 내장 구현으로 돌아가며,
처리 중인 요청은 이전 인스턴스에서 완료됩니다.

`IPluginLifecycle`을 구현한 플러그인은 생성 후 `Initialize(PluginContext)`를, 로드
컨텍스트 언로드 전에 `Dispose()`를 받으므로 파일, 스레드, 소켓을 확실히 해제할 수
있습니다. `Initialize`가 예외를 던진 플러그인은 건너뛰고 로그를 남기며 나머지는 계속
로드합니다.

### 클라이언트 목록

```http
GET /api/v1/clients?search=device&page=1&pageSize=50
```

```json
{
  "items": [
    {
      "clientId": "device-1",
      "username": "device-1",
      "protocolVersion": "5.0",
      "connected": true,
      "remoteAddress": "10.0.0.12:52133",
      "connectedAt": "2026-11-24T12:00:00Z",
      "lastSeen": "2026-11-24T12:00:30Z",
      "sessionExpiryInterval": 3600,
      "subscriptions": 4,
      "inflightMessages": 0,
      "queuedMessages": 0,
      "isSuperuser": false
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 50
}
```

검색은 클라이언트 ID, 사용자 이름, 원격 주소와 일치합니다. 페이지 크기는 1–200으로
제한됩니다.

### 클라이언트 연결 끊기

```bash
curl -X DELETE http://localhost:8080/api/v1/clients/device-1 \
  -H "Authorization: Bearer $TOKEN"
```

찾으면 `204`, 없으면 `404`를 반환합니다.

### 구독

```http
GET /api/v1/subscriptions?search=sensors&page=1&pageSize=100
```

각 항목은 `clientId`, `filter`, `sharedGroup`, `qos`, `noLocal`,
`retainAsPublished`, `retainHandling`, `identifier`를 포함합니다.

### retained 메시지

```http
GET /api/v1/retained?search=sensors&page=1&pageSize=100
```

```json
{
  "items": [
    { "topic": "sensors/demo", "payloadBytes": 4, "qoS": "AtLeastOnce", "storedAt": "..." }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 100
}
```

쿼리 매개변수로 삭제합니다(토픽에 `/`가 있으므로 인코딩 필요):

```bash
curl -X DELETE "http://localhost:8080/api/v1/retained?topic=sensors%2Fdemo" \
  -H "Authorization: Bearer $TOKEN"
```

### 발행

```bash
curl -X POST http://localhost:8080/api/v1/publish \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{
    "topic": "devices/demo/command",
    "payload": "{\"action\":\"reboot\"}",
    "encoding": "plain",
    "qos": 1,
    "retain": false
  }'
```

```json
{ "topic": "devices/demo/command", "bytes": 18, "qos": 1, "retain": false }
```

- `encoding`은 `plain`(UTF-8) 또는 `base64`입니다.
- `qos`는 0, 1, 2 중 하나여야 합니다.
- `retain`은 마지막 값으로 저장합니다.
- 관리용 발행이므로 클라이언트 ACL 규칙이 적용되지 않습니다.

### 클러스터

```json
{
  "nodeId": "node-a",
  "status": "clustered",
  "nodes": [
    {
      "nodeId": "node-a",
      "address": "http://node-a:8080",
      "status": "alive",
      "lastSeen": "2026-11-24T12:00:00Z",
      "isSelf": true
    }
  ]
}
```

### 통계

`stats`에는 단조 카운터, 게이지, `messagesPerSecond`(1초 버킷 60개의 배열)가
들어 있습니다. `collectedAt`은 서버 타임스탬프입니다.

## 클러스터 내부 API

아래는 노드 간 전용이며 `X-NextMQTT-Timestamp`와 `X-NextMQTT-Signature`
(`timestamp.body`에 대한 16진 HMAC-SHA256)로 서명됩니다:

- `POST /internal/cluster/heartbeat`
- `POST /internal/cluster/publish`
- `POST /internal/cluster/leave`
- `POST /internal/cluster/retained`
- `POST /internal/cluster/session/claim`

관리자 토큰 인증 대상이 아니므로 에지에서 차단해야 합니다.

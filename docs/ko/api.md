# REST API

`http://host:8080/api/v1`

## 인증

공개: `/healthz`, `/readyz`, `/api/v1/health`, `/api/v1/ready`,
`/api/v1/version`, `POST /api/v1/auth/login`.

```http
Authorization: Bearer <token>
```

```http
X-API-Key: <api-key>
```

```bash
TOKEN=$(curl -s http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"change-me-now"}' | jq -r .token)
```

## 엔드포인트

| 메서드 | 경로 | 설명 |
| --- | --- | --- |
| GET | `/api/v1/stats` | 통계와 60초 시계열 |
| GET | `/api/v1/clients` | 클라이언트 목록 |
| DELETE | `/api/v1/clients/{id}` | 연결 종료 |
| GET | `/api/v1/subscriptions` | 구독 목록 |
| GET | `/api/v1/retained` | retained 목록 |
| DELETE | `/api/v1/retained?topic=...` | retained 삭제 |
| POST | `/api/v1/publish` | publish |
| GET | `/api/v1/cluster` | 클러스터 |
| GET | `/api/v1/config` | 적용 설정 |
| GET | `/metrics` | Prometheus |

## 목록

```http
GET /api/v1/clients?search=device&page=1&pageSize=50
```

```json
{
  "items": [
    {
      "clientId": "device-1",
      "connected": true,
      "protocolVersion": "5.0",
      "subscriptions": 4,
      "inflightMessages": 0,
      "queuedMessages": 0
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 50
}
```

## 연결 종료

```bash
curl -X DELETE http://localhost:8080/api/v1/clients/device-1 \
  -H "Authorization: Bearer $TOKEN"
```

## Publish

```bash
curl -X POST http://localhost:8080/api/v1/publish \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{
    "topic": "devices/demo/command",
    "payload": "hello",
    "encoding": "plain",
    "qos": 1,
    "retain": false
  }'
```

`encoding`은 `plain` 또는 `base64`. 관리 publish는 클라이언트 ACL을 적용받지 않습니다.

## Retained 삭제

```bash
curl -X DELETE "http://localhost:8080/api/v1/retained?topic=sensors%2Fdemo" \
  -H "Authorization: Bearer $TOKEN"
```

## 내부 API

`/internal/cluster/heartbeat`, `/publish`, `/leave`는 노드 간 전용이며 HMAC 서명을
사용합니다. 외부에 노출하지 마세요.

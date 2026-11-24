# 설정

우선순위(낮음 → 높음):

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. 사용자 시크릿(개발)
4. 환경 변수
5. `NEXTMQTT_*` 환경 변수
6. 명령줄 인자

## 환경 변수

`NEXTMQTT_` 접두사, `__`로 섹션 구분:

```bash
NEXTMQTT_BROKER__MAXPACKETSIZE=2097152
NEXTMQTT_SERVER__HTTP__PORT=9090
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
```

| 변수 | 용도 |
| --- | --- |
| `NEXTMQTT_ADMIN_PASSWORD` | 관리 비밀번호(권장) |
| `NEXTMQTT_ADMIN_USERNAME` | 관리 사용자명 |
| `NEXTMQTT_ADMIN_TOKEN_SECRET` | 토큰 서명 키 |
| `NEXTMQTT_ALLOW_ANONYMOUS` | 익명 MQTT 허용 |

## 서버

| 설정 | 기본 |
| --- | --- |
| `Server:Tcp:Port` | 1883 |
| `Server:Tls:Port` | 8883 |
| `Server:Http:Port` | 8080 |
| `Server:Http:WebSocketPort` | 8083 |
| `Server:Http:TlsPort` | 8084 |
| `Server:Tls:CertificatePath` | — |

## Broker

| 설정 | 기본 |
| --- | --- |
| `MaxPacketSize` | 1 MiB |
| `MaxInflightMessages` | 32 |
| `MaxQueuedMessages` | 1000 |
| `MaxSubscriptionsPerSession` | 1000 |
| `MaxTopicAlias` | 32 |
| `ConnectTimeout` | 10초 |
| `RetransmitInterval` | 10초 |
| `SweepInterval` | 30초 |

## 속도 제한

기본은 비활성. 두 제한 모두 토큰 버킷이라 버스트를 허용한 뒤 지속 속도로 제한합니다.

| 설정 | 기본 | 설명 |
| --- | --- | --- |
| `ConnectPerSecondPerIp` | `0` | 원격 주소당 초당 CONNECT 허용 수. `0`이면 비활성 |
| `ConnectBurst` | `0` | 연결 버스트. `0`이면 초당 속도 사용 |
| `PublishPerSecondPerClient` | `0` | 클라이언트당 초당 PUBLISH 허용 수. `0`이면 비활성 |
| `PublishBurst` | `0` | 발행 버스트. `0`이면 초당 속도 사용 |

```bash
NEXTMQTT_RATELIMITS__CONNECTPERSECONDPERIP=20 \
NEXTMQTT_RATELIMITS__PUBLISHPERSECONDPERCLIENT=500 \
dotnet NextMqtt.Server.dll
```

한도를 넘으면:

- 연결 속도를 넘은 CONNECT는 MQTT 5에서 CONNACK `0x9F`, MQTT 3.1.1에서 `0x03`을
  받고 연결이 닫힙니다.
- 발행 속도를 넘은 PUBLISH는 **버리지만 확인 응답은 보냅니다**(PUBACK, MQTT 5
  QoS 2는 PUBREC `0x96`). 재전송 폭주를 막기 위해서입니다. 건수는
  `nextmqtt_publishes_rate_limited_total`과 `nextmqtt_messages_dropped_total`로
  확인합니다.
- 연결 거부는 `nextmqtt_connections_rate_limited_total`에 집계됩니다.

제한은 노드 단위입니다. 3노드 클러스터에서는 각 노드가 설정값까지 허용합니다.

## 인증

```json
{
  "Authentication": {
    "AllowAnonymous": false,
    "Users": [
      { "Username": "device-1", "PasswordHash": "pbkdf2-sha256$...", "Roles": ["devices"] }
    ],
    "Jwt": { "Secret": "32자 이상", "RequireExpiration": true }
  }
}
```

```bash
nextmqtt hash-password --password 'your-password'
```

## ACL

```json
{
  "Acl": {
    "DefaultPolicy": "Deny",
    "Rules": [
      { "Effect": "Allow", "Action": "Both", "Topic": "devices/${clientid}/#" }
    ]
  }
}
```

순서대로 평가하며 `${username}` / `${clientid}` 확장, `*`/`?` glob을 지원합니다.

## 관리 / 클러스터

| 설정 | 설명 |
| --- | --- |
| `Admin:TokenLifetimeMinutes` | 토큰 유효 기간 |
| `Admin:MetricsRequireAuth` | `/metrics` 보호 |
| `Admin:ApiKeySha256` | API 키 SHA-256 |
| `Cluster:AdvertiseUrl` | 다른 노드에서 접근 가능한 URL |
| `Cluster:Secret` | 16자 이상 공유 키 |
| `Cluster:Seeds` | 초기 접속 지점 |

## 로그

`Logging:LogLevel:NextMqtt=Debug`로 프로토콜 오류를 확인할 수 있지만 운영에서는
과도한 로그를 피하세요.

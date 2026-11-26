# 설정

NextMQTT는 ASP.NET Core 구성 시스템을 사용합니다. 설정은 `appsettings.json`,
환경 변수, 명령줄 인수 또는 시크릿 저장소에서 올 수 있습니다.

## 우선순위

뒤에 있는 것이 우선합니다:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. 사용자 시크릿(`dotnet user-secrets`, Development 전용)
4. 환경 변수
5. `NEXTMQTT_*` 환경 변수
6. 명령줄 인수

컨테이너 이미지는 `ASPNETCORE_ENVIRONMENT=Production`을 설정합니다. 기본
`appsettings.json`은 로컬 평가를 위해 의도적으로 느슨하므로, 운영 전에 인증과 ACL을
반드시 덮어쓰십시오.

## 환경 변수 형식

두 가지 형식을 지원합니다.

**접두사 구성 키**는 `NEXTMQTT_`를 쓰고 구역 구분은 `__`입니다:

```bash
NEXTMQTT_BROKER__MAXPACKETSIZE=2097152
NEXTMQTT_SERVER__HTTP__PORT=9090
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
```

**부트스트랩 변수**는 코드에서 직접 읽습니다:

| 변수 | 용도 |
| --- | --- |
| `NEXTMQTT_ADMIN_PASSWORD` | 평문 관리자 비밀번호, 시작 시 해시(권장) |
| `NEXTMQTT_ADMIN_USERNAME` | 관리자 사용자 이름 덮어쓰기 |
| `NEXTMQTT_ADMIN_TOKEN_SECRET` | 대시보드 토큰용 고정 HMAC 키 |
| `NEXTMQTT_ALLOW_ANONYMOUS` | `true` / `false`, 익명 MQTT 허용 여부 |

::: tip 관리자 토큰
`NEXTMQTT_ADMIN_TOKEN_SECRET`이 없으면 임시 키를 생성합니다. 재시작하면 모든 토큰이
무효가 되고 여러 노드의 대시보드가 토큰을 공유할 수 없습니다. 운영에서는 모든 노드에
같은 값을 설정하십시오.
:::

## 리스너

```json
{
  "Server": {
    "Tcp": { "Enabled": true, "Host": "0.0.0.0", "Port": 1883 },
    "Tls": {
      "Enabled": false,
      "Host": "0.0.0.0",
      "Port": 8883,
      "CertificatePath": "/certs/server.pem",
      "CertificateKeyPath": "/certs/server.key",
      "Domain": "mqtt.example.com",
      "CertificatePassword": null,
      "RequireClientCertificate": false
    },
    "WebSocket": { "Enabled": true, "Path": "/mqtt" },
    "Http": {
      "Port": 8080,
      "WebSocketPort": 8083,
      "TlsPort": 8084,
      "CertificatePath": null,
      "CertificateKeyPath": null,
      "Domain": null,
      "CertificatePassword": null,
      "AllowedOrigins": ["http://localhost:5173"]
    }
  }
}
```

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `Tcp:Enabled` | `true` | 원시 MQTT 리스너 |
| `Tcp:Host` | `0.0.0.0` | 바인딩할 주소 |
| `Tcp:Port` | `1883` | MQTT 포트 |
| `Tls:Enabled` | `false` | TLS MQTT 리스너 |
| `Tls:CertificatePath` | — | PFX/PKCS#12 또는 PEM 경로 |
| `Tls:CertificateKeyPath` | — | PEM 개인 키. 생략하면 같은 디렉터리의 같은 이름 `.key` |
| `Tls:Domain` | — | 시작 시 인증서가 이 이름을 포함하는지 확인, 아니면 경고만 |
| `Tls:CertificatePassword` | — | PFX 비밀번호 |
| `Tls:RequireClientCertificate` | `false` | 클라이언트 인증서 요구(검증은 플랫폼 기본값) |
| `WebSocket:Enabled` | `true` | WebSocket을 통한 MQTT |
| `WebSocket:Path` | `/mqtt` | HTTP 경로 |
| `Http:Port` | `8080` | API와 대시보드 포트 |
| `Http:WebSocketPort` | `8083` | 브라우저 MQTT용 추가 Kestrel 엔드포인트 |
| `Http:TlsPort` | `8084` | 인증서가 설정된 경우의 HTTPS 엔드포인트 |
| `Http:AllowedOrigins` | 로컬 개발 주소 | 대시보드 API의 CORS 허용 목록 |
| `DrainTimeoutSeconds` | `5` | 종료 시 inflight QoS 확인을 기다리는 시간 |

`Http:WebSocketPort`를 `Http:Port`와 같게 하면 추가 엔드포인트가 꺼집니다.

## Broker

```json
{
  "Broker": {
    "MaxPacketSize": 1048576,
    "MaxInflightMessages": 32,
    "MaxQueuedMessages": 1000,
    "MaxSubscriptionsPerSession": 1000,
    "MaxTopicAlias": 32,
    "ConnectTimeout": "00:00:10",
    "RetransmitInterval": "00:00:10",
    "SweepInterval": "00:00:30",
    "MaxClientIdLength": 256,
    "AllowZeroLengthClientId": true
  }
}
```

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `MaxPacketSize` | 1 MiB | 패킷당 상한 |
| `MaxInflightMessages` | 32 | 송신 QoS 1/2 윈도이자 Receive Maximum |
| `MaxQueuedMessages` | 1000 | 영속 세션당 오프라인 큐 상한 |
| `MaxSubscriptionsPerSession` | 1000 | 세션당 구독 수 |
| `MaxTopicAlias` | 32 | 인바운드 토픽 별칭 테이블 크기 |
| `MaxSubscriptionIdentifiersPerClient` | 0 | 식별자를 붙일 수 있는 구독 수, 0은 무제한 |
| `ConnectTimeout` | 10초 | CONNECT를 보낼 때까지의 시간 |
| `RetransmitInterval` | 10초 | QoS 재전송 타이머 |
| `SweepInterval` | 30초 | 만료 정리와 유지 관리 주기 |
| `MaxClientIdLength` | 256 | 초과하는 ID는 거부 |
| `AllowZeroLengthClientId` | `true` | ID가 없으면 자동 할당 |

## 속도 제한

기본적으로 꺼져 있습니다. 둘 다 토큰 버킷이라 버스트를 허용한 뒤 지속 속도를
적용합니다.

```json
{
  "RateLimits": {
    "ConnectPerSecondPerIp": 0,
    "ConnectBurst": 0,
    "PublishPerSecondPerClient": 0,
    "PublishBurst": 0
  }
}
```

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `ConnectPerSecondPerIp` | `0` | 원격 주소당 초당 CONNECT 수, `0`이면 끔 |
| `ConnectBurst` | `0` | 연결 버스트, `0`이면 초당 속도 사용 |
| `PublishPerSecondPerClient` | `0` | 클라이언트당 초당 PUBLISH 수, `0`이면 끔 |
| `PublishBurst` | `0` | 발행 버스트, `0`이면 초당 속도 사용 |
| `Topics[].Filter` | – | 공유 쿼터의 토픽 필터 |
| `Topics[].PublishPerSecond` | `0` | 모든 클라이언트 합계 초당 발행 수 |
| `Topics[].Burst` | `0` | 버스트, `0`이면 초당 속도 사용 |

```bash
NEXTMQTT_RATELIMITS__CONNECTPERSECONDPERIP=20 \
NEXTMQTT_RATELIMITS__PUBLISHPERSECONDPERCLIENT=500 \
nextmqtt-server
```

토픽 쿼터는 목록으로 설정하므로 전체를 제한하지 않고 일부 인기 토픽만 보호할 수
있습니다:

```json
{
  "RateLimits": {
    "Topics": [
      { "Filter": "telemetry/#", "PublishPerSecond": 2000, "Burst": 4000 },
      { "Filter": "commands/#", "PublishPerSecond": 50 }
    ]
  }
}
```

한도를 넘을 때의 동작:

- 주소별 속도를 넘은 **CONNECT**는 MQTT 5에서 CONNACK
  `0x9F Connection rate exceeded`, MQTT 3.1.1에서 `0x03 Server unavailable`을
  보낸 뒤 연결을 닫습니다.
- 클라이언트별 속도를 넘은 **PUBLISH**는 **버려지지만 확인은 반환**합니다
  (PUBACK, MQTT 5 QoS 2는 `0x96 Message rate too high` PUBREC). 재전송을 막기
  위해서입니다. 버린 수는 `nextmqtt_publishes_rate_limited_total`,
  `nextmqtt_messages_dropped_total`, `nextmqtt_publishes_received_total`에
  기록됩니다.
- 연결 거부는 `nextmqtt_connections_rate_limited_total`에 기록됩니다.

제한은 **클러스터 전체** 기준입니다. 각 노드는 설정된 속도와 버스트를 살아 있는 노드
수로 나눕니다(최소 토큰 1개). 노드 3개에 `PublishPerSecondPerClient = 500`이면
클라이언트당 합계 약 500건/초이며 1500건이 아닙니다. 멤버십은 순회마다 다시 계산되지만
전역 카운터가 없어 참여·이탈 직후에는 잠시 초과할 수 있습니다. 단일 노드는 설정값을
그대로 씁니다.

## 저장소

retained 메시지는 기본적으로 메모리에 있습니다. `File`로 두면 디스크에 기록되어
재시작 후에도 남습니다. 세션과 복제본은 아래에서 따로 설정합니다.

```json
{
  "Storage": {
    "Retained": {
      "Mode": "Memory",
      "Path": "data/retained.log",
      "CompactThresholdBytes": 4194304
    }
  }
}
```

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `Mode` | `Memory` | `Memory` 또는 `File` |
| `Path` | `data/retained.log` | 저널 파일, 상위 디렉터리는 자동 생성 |
| `CompactThresholdBytes` | 4 MiB | 이 크기를 넘으면 저널을 다시 씀 |

```bash
NEXTMQTT_STORAGE__RETAINED__MODE=file \
NEXTMQTT_STORAGE__RETAINED__PATH=/app/data/retained.log \
nextmqtt-server
```

저널은 JSON 줄로, 한 줄에 `set` 또는 `remove` 작업 하나입니다. 토픽, Base64
페이로드, QoS, 타임스탬프, MQTT 5 속성을 담습니다. 시작할 때 재생하고 만료된 항목은
건너뛰며 파일이 커지면 압축합니다. 마지막 줄이 깨져도 무시하고 시작은 실패하지
않습니다. 각 노드가 자기 저널을 가지므로 오프라인이었던 노드도 재시작 후 저장했던
내용을 복구합니다.

영속 세션은 별도 스냅숏입니다:

```json
{
  "Storage": {
    "Session": {
      "Mode": "Memory",
      "Path": "data/sessions.json",
      "SnapshotSeconds": 30
    }
  }
}
```

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `Mode` | `Memory` | `Memory` 또는 `File` |
| `Path` | `data/sessions.json` | 스냅숏, 원자적으로 교체 |
| `SnapshotSeconds` | 30 | 다시 쓰는 주기, 변화가 없으면 생략 |

스냅숏에는 모든 영속 세션의 ID, 만료, 구독, MQTT 5 속성이 있는 오프라인 큐, 그리고
PUBREL을 기다리는 인바운드 QoS 2 발행이 들어갑니다. 아웃바운드 `inflight` 확인 상태는
저장하지 않으므로 재시작 후 다음 발행 때 다시 전달되며, 윈도는
`MaxInflightMessages`로 제한됩니다. 정상 종료 시에도 한 번 씁니다.

### 세션 복제본

피어에서 받은 복제본은 기본적으로 메모리에 있습니다. 영속화하면 모든 노드가 동시에
재시작해도 클러스터가 세션을 복구할 수 있습니다:

```json
{
  "Storage": {
    "Replicas": {
      "Mode": "File",
      "Path": "data/session-replicas.json",
      "ShardCount": 16
    }
  }
}
```

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `Mode` | `Memory` | `Memory` 또는 `File` |
| `Path` | `data/session-replicas.json` | 기본 경로, 확장자 앞에 샤드 번호가 붙음 |
| `ShardCount` | 16 | 복제본을 나눌 파일 수, `1`이면 단일 파일 |

각 샤드는 `{ session, ownerNodeId, version, updatedAtUnix }`의 JSON 배열입니다.
복제본은 클라이언트 ID 해시로 배정되므로 변경이 있는 샤드만 다시 쓰고 나머지는
완전히 건너뜁니다. 쓰기는 변경이 있는 동안 최대 5초에 한 번, 종료 시 한 번입니다.
손상된 샤드는 빈 것으로 보고, 만료된 복제본은 읽을 때 버립니다. 이전 버전의 단일
파일도 시작 시 읽으며 첫 저장에서 샤드로 대체됩니다.

## 백업

`GET /api/v1/backup`은 retained 메시지와 영속 세션을 담은 이식 가능한 JSON을
반환하고, `POST /api/v1/backup`으로 가져옵니다. 가져오기는 retained 집합을 교체하고
현재 오프라인인 세션만 추가하며 온라인 세션은 건드리지 않습니다.

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H 'content-type: application/json' \
  -d '{"username":"admin","password":"..."}' | jq -r .token)

curl -s http://localhost:8080/api/v1/backup \
  -H "Authorization: Bearer $TOKEN" > nextmqtt-backup.json

curl -s -X POST http://localhost:8080/api/v1/backup \
  -H "Authorization: Bearer $TOKEN" \
  -H 'content-type: application/json' \
  --data @nextmqtt-backup.json
```

## 연동

아웃바운드 webhook은 일치하는 발행을 HTTP 엔드포인트로 보내며 MQTT 전달을 막지
않습니다. 규칙마다 유계 큐와 백그라운드 작업자가 있고, 큐가 가득 차면 이벤트를 버리고
셉니다.

```json
{
  "Integrations": {
    "Webhooks": [
      {
        "Name": "audit",
        "Filter": "audit/#",
        "Url": "https://ops.example.com/hooks/mqtt",
        "TimeoutMs": 3000,
        "MaxQueue": 1000,
        "Headers": { "Authorization": "Bearer <token>" },
        "IncludePayload": true
      }
    ]
  }
}
```

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `Filter` | `#` | 메시지를 고르는 토픽 필터 |
| `Url` | – | 절대 http/https 엔드포인트 |
| `TimeoutMs` | 5000 | 시도당 제한 시간 |
| `MaxQueue` | 1000 | 버리기 전까지 버퍼링할 이벤트 수 |
| `Headers` | – | 추가 요청 헤더 |
| `IncludePayload` | `true` | Base64 페이로드 전송 여부 |

규칙별·이벤트별로 최소 한 번 전달하며 최대 3회까지 백오프로 재시도합니다. 실패는
로그와 지표에 남습니다. 본문은 JSON
`{ name, topic, qos, retain, receivedAt, payload }`입니다. 처음 발행을 받은 노드만
보내므로 클러스터에서 중복되지 않습니다.

### 라우팅 규칙

```json
{
  "Integrations": {
    "Rules": [
      { "Filter": "debug/#", "Action": "drop" },
      { "Filter": "health/ping", "Action": "drop" },
      { "Filter": "legacy/#", "Action": "rewrite", "Target": "modern/" }
    ]
  }
}
```

일치한 메시지는 retained 저장, 전달, 클러스터 전달 전에 버려지고
`nextmqtt_messages_dropped_total`에 기록됩니다. `rewrite`는 필터의 리터럴 부분을
`Target`으로 바꾸고 나머지를 유지하므로 `legacy/a/b`는 `modern/a/b`가 됩니다.
와일드카드가 없는 필터는 토픽 전체를 바꿉니다. drop이 rewrite보다 우선하며, 바뀐
토픽이 유효하지 않으면 메시지는 버려집니다.

### 플러그인

플러그인은 프로세스 안의 회수 가능한 로드 컨텍스트에서 실행되며 `plugin.json`
매니페스트에서 발견됩니다. 계약 버전이 호스트와 같아야 합니다.

```json
{
  "Plugins": {
    "Enabled": true,
    "Directory": "plugins",
    "Authenticator": "MyCompany.Auth.MyAuthenticator"
  }
}
```

```text
plugins/audit/
  plugin.json          { "name": "audit", "version": "1.0.0", "contractVersion": 1, "entryAssembly": "MyCompany.Audit.dll" }
  MyCompany.Audit.dll
```

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `Enabled` | `false` | 시작 시 플러그인 로드 |
| `Directory` | `plugins` | `plugin.json`을 재귀 검색할 디렉터리 |
| `Authenticator` | – | 사용할 `IClientAuthenticator` 전체 형식 이름 |
| `Authorizer` | – | 사용할 `ITopicAuthorizer` 전체 형식 이름 |

매니페스트가 실제 어셈블리를 가리키고 계약 버전이 맞을 때만 로드하며, 아니면 로그를
남기고 건너뜁니다. 플러그인은 broker 권한으로 실행됩니다. 확장 수단이지 샌드박스가
아닙니다.

### NATS 브리지

```json
{
  "Integrations": {
    "Nats": {
      "Enabled": true,
      "Url": "nats://127.0.0.1:4222",
      "SubjectPrefix": "mqtt.",
      "Filter": "telemetry/#",
      "MaxQueue": 5000
    }
  }
}
```

필터에 맞는 발행은 NATS 텍스트 프로토콜의 `PUB` 프레임으로 전달됩니다. `/`는 `.`이
되고 안전하지 않은 문자는 `_`가 됩니다. 유계 큐가 버스트를 흡수하고 작업자는 2초마다
재연결하며, 처음 메시지를 받은 노드만 전달하므로 클러스터에서 중복되지 않습니다.

### Kafka 브리지

```json
{
  "Integrations": {
    "Kafka": {
      "Enabled": true,
      "BootstrapServers": "127.0.0.1:9092",
      "TopicPrefix": "mqtt.",
      "Filter": "telemetry/#",
      "Compression": "gzip"
    }
  }
}
```

`Confluent.Kafka`(Apache-2.0)는 유일한 런타임 NuGet 의존성이며 ADR 0007에서
승인되었습니다. 토픽 매핑은 NATS와 같고(`/` → `.`, 안전하지 않은 문자 → `_`) 유계
큐가 버스트를 흡수합니다. 브로커에 연결하지 못해도 시작 시 경고만 남기고 리스너 시작은
실패하지 않습니다.

### RabbitMQ 브리지

```json
{
  "Integrations": {
    "Rabbit": {
      "Enabled": true,
      "HostName": "127.0.0.1",
      "Port": 5672,
      "UserName": "…",
      "Password": "…",
      "Exchange": "nextmqtt",
      "RoutingKeyPrefix": "mqtt.",
      "Filter": "telemetry/#"
    }
  }
}
```

메시지는 publisher confirm이 켜진 durable `topic` 교환기로 발행되므로 `Forwarded`는
RabbitMQ가 받아들인 것만 셉니다. 라우팅 키 매핑은 NATS와 같습니다(`/` → `.`, 안전하지
않은 문자 → `_`). Kafka와 RabbitMQ는 동시에 사용할 수 있습니다.

## 인증

```json
{
  "Authentication": {
    "AllowAnonymous": false,
    "Superusers": ["ops"],
    "Users": [
      {
        "Username": "device-1",
        "PasswordHash": "pbkdf2-sha256$210000$...$...",
        "Roles": ["devices"]
      }
    ],
    "Jwt": {
      "Secret": "32자 이상의 무작위 문자열",
      "Issuer": null,
      "Audience": null,
      "UsernameClaim": "sub",
      "RoleClaim": "roles",
      "RequireExpiration": true
    }
  }
}
```

CLI로 비밀번호 해시를 만듭니다:

```bash
dotnet run --project src/NextMqtt.Cli -- hash-password --password 'correct horse battery staple'
```

해시 형식은 `pbkdf2-sha256$반복 횟수$salt-base64$hash-base64`이며 기본 반복은
210,000회입니다.

JWT 인증은 MQTT의 비밀번호 필드를 사용합니다. 유효한 HS256 서명, `exp`(기본 필수),
설정한 issuer와 audience를 만족하는 토큰을 받습니다. `sub` 클레임이 사용자 이름이
되고 `roles` 클레임이 슈퍼유저(`admin`) 여부를 정하며 이후 정책 모듈에서도 쓸 수
있습니다.

## ACL

```json
{
  "Acl": {
    "DefaultPolicy": "Deny",
    "Rules": [
      { "Effect": "Allow", "Action": "Subscribe", "Topic": "$SYS/#" },
      { "Effect": "Allow", "Action": "Both", "Topic": "devices/${clientid}/#" },
      { "Effect": "Allow", "Action": "Subscribe", "Topic": "public/#" },
      { "Effect": "Deny", "Action": "Publish", "Topic": "public/audit/#", "Username": "*" }
    ]
  }
}
```

규칙은 순서대로 평가되며 처음 일치한 규칙이 결정합니다. `Action`, `Username`,
`ClientId`의 glob이 모두 맞아야 합니다. `Topic`에서는 `${username}`과 `${clientid}`를
쓸 수 있습니다. `DefaultPolicy`는 `Deny` 또는 `Allow`입니다.

구독 규칙은 요청된 필터를 **포함**해야 하며 단순히 겹치는 것으로는 부족합니다.
예를 들어 `sensors/#`는 `sensors/+/temperature`를 포함하지만 `sensors/+`는
`sensors/#`를 포함하지 않습니다.

슈퍼유저는 모든 ACL 검사를 우회합니다. `Authentication:Superusers`에 있거나 `admin`
역할을 가진 사용자가 해당합니다.

## 관리

```json
{
  "Admin": {
    "Username": "admin",
    "TokenSecret": null,
    "TokenLifetimeMinutes": 480,
    "MetricsRequireAuth": false,
    "ApiKeySha256": []
  }
}
```

| 옵션 | 설명 |
| --- | --- |
| `Username` | 대시보드 사용자 이름 |
| `PasswordHash` | PBKDF2 해시, `NEXTMQTT_ADMIN_PASSWORD`가 더 간편 |
| `TokenSecret` | 대시보드 토큰의 HMAC 키 |
| `TokenLifetimeMinutes` | 토큰 수명, 5–10,080분으로 제한 |
| `MetricsRequireAuth` | `/metrics`를 관리자 토큰으로 보호 |
| `ApiKeySha256` | `X-API-Key`로 받는 SHA-256 16진수 다이제스트 |

API 키 다이제스트를 만듭니다:

```bash
printf '%s' 'my-api-key' | sha256sum
```

## 클러스터

```json
{
  "Cluster": {
    "Enabled": true,
    "NodeId": "node-a",
    "AdvertiseUrl": "http://node-a:8080",
    "Seeds": ["http://node-b:8080", "http://node-c:8080"],
    "Secret": "shared-secret-at-least-16-chars",
    "HeartbeatSeconds": 5,
    "NodeTimeoutSeconds": 20,
    "RequestTimeoutSeconds": 5,
    "MaxFiltersPerHeartbeat": 10000,
    "SessionReplicaSeconds": 5
  }
}
```

`SessionReplicaSeconds`는 세션 복제본을 피어로 보내는 주기로, 노드가 죽어도 소유한
세션을 잃지 않게 합니다. `0`이면 복제를 끕니다.

클러스터를 켜면 `AdvertiseUrl`은 피어에서 접근 가능해야 하고 `Secret`은 16자
이상이어야 합니다. 자세한 내용은 [클러스터](/ko/clustering)를 참고하십시오.

## 로그

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "NextMqtt": "Information"
    }
  }
}
```

로그는 컨테이너 친화적인 평문으로 stdout에 나갑니다. 프로토콜 수준의 세부 정보가
필요하면 `Logging:LogLevel:NextMqtt=Debug`를 설정하십시오.

## 리버스 프록시

`Http:CertificatePath`를 설정하지 않으면 Kestrel은 대시보드의 TLS를 종단하지 않습니다.
ingress나 로드 밸런서에서 종단한다면:

- `X-Forwarded-For`와 `X-Forwarded-Proto`를 전달합니다;
- `/mqtt`의 WebSocket 업그레이드를 켭니다;
- 읽기 제한 시간을 MQTT keep-alive보다 길게 둡니다;
- `/internal/cluster/*`를 노드 간 트래픽으로 제한합니다.

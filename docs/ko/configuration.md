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

제한은 **클러스터 전체** 기준입니다. 각 노드는 설정 속도와 버스트를 살아있는 노드 수로 나누어(최소 1토큰) 적용하므로, 3노드에서 `PublishPerSecondPerClient = 500`이면 합계 초당 약 500건입니다. 멤버십은 주기적으로 재계산되며, 방금 합류/이탈한 노드는 전역 카운터가 없어 일시적으로 초과할 수 있습니다. 단일 노드에서는 설정값이 그대로 적용됩니다.

## 스토리지

retained는 기본적으로 메모리에 있습니다. `File`로 설정하면 디스크에 기록해 재시작
후에도 유지합니다. 세션, 큐, 구독은 이 릴리스에서 여전히 메모리에 있습니다.

| 설정 | 기본 | 설명 |
| --- | --- | --- |
| `Mode` | `Memory` | `Memory` 또는 `File` |
| `Path` | `data/retained.log` | 저널 파일 (상위 디렉터리는 자동 생성) |
| `CompactThresholdBytes` | 4 MiB | 이 크기를 넘으면 파일을 다시 씁니다 |

```bash
NEXTMQTT_STORAGE__RETAINED__MODE=file \
NEXTMQTT_STORAGE__RETAINED__PATH=/app/data/retained.log \
dotnet NextMqtt.Server.dll
```

저널은 한 줄에 한 작업(`set` / `remove`)인 JSON 라인으로, 토픽, Base64 페이로드,
QoS, 시각, MQTT 5 속성을 담습니다. 시작 시 순서대로 재생하고 만료 항목을 건너뛰며,
파일이 커지면 compaction합니다. 마지막 줄이 깨져도 기동은 실패하지 않습니다. 각
노드가 자체 저널을 유지합니다.

영구 세션은 별도 스냅샷입니다. `Session:Mode=File`이면 `Session:Path`(기본
`data/sessions.json`)에 쓰고 `Session:SnapshotSeconds`(기본 30)마다 갱신합니다.
스냅샷에는 identity, 만료, 구독, 오프라인 큐가 들어가며 inflight QoS는 포함되지
않습니다. 정상 종료 시에도 기록합니다.

논리 백업은 `GET /api/v1/backup`으로 내려받고 `POST /api/v1/backup`으로
복원합니다(retained 교체, 온라인이 아닌 세션만 추가).

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

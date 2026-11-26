# 프로토콜 지원

NextMQTT는 OASIS 명세의 MQTT 3.1.1(프로토콜 레벨 4)과 MQTT 5.0(레벨 5)을
구현합니다.

## 전송

| 전송 | 기본 포트 | 설명 |
| --- | --- | --- |
| TCP | 1883 | 평문 MQTT |
| TLS | 8883 | 선택 사항, 인증서 필요 |
| WebSocket | 8083(`/mqtt`) | 요청 시 하위 프로토콜 `mqtt` 협상 |
| 보안 WebSocket | 8084(`/mqtt`) | 선택적 Kestrel HTTPS 엔드포인트 |

WebSocket 클라이언트는 `ws://host:8083/mqtt` 또는 `wss://host:8084/mqtt`에
접속합니다. 같은 HTTP 엔드포인트가 대시보드와 REST API를 제공합니다.

## 제어 패킷

| 패킷 | 3.1.1 | 5.0 | 설명 |
| --- | --- | --- | --- |
| CONNECT | ✅ | ✅ | 사용자 이름/비밀번호, 유언, clean start, 세션 만료 |
| CONNACK | ✅ | ✅ | Session present, 이유 코드, 서버 속성 |
| PUBLISH | ✅ | ✅ | QoS 0/1/2, retain, DUP, 토픽 별칭 |
| PUBACK | ✅ | ✅ | 5.0은 이유 코드와 속성 |
| PUBREC / PUBREL / PUBCOMP | ✅ | ✅ | QoS 2 핸드셰이크 전체 |
| SUBSCRIBE | ✅ | ✅ | 와일드카드, 공유 구독, 구독 식별자 |
| SUBACK | ✅ | ✅ | 필터별 이유 코드 |
| UNSUBSCRIBE / UNSUBACK | ✅ | ✅ | 5.0은 필터별 이유 코드 |
| PINGREQ / PINGRESP | ✅ | ✅ | keep-alive |
| DISCONNECT | ✅ | ✅ | 이유 코드, 세션 만료 갱신, 연결 종료 시 유언 |
| AUTH | — | ✅ | 교체 가능한 제공자와 챌린지/응답, 내장 `nextmqtt-jwt` |

## 기능

### 세션

- Clean start와 영속 세션.
- MQTT 5 세션 만료 interval(DISCONNECT에서 갱신 포함).
- 세션 인계: 이전 연결은 DISCONNECT `0x8E`를 받습니다.
- 다른 노드로 `CleanStart=false` 재연결하면 소유자에게서 세션을 인계하거나 복제본에서
  복구합니다. PUBREL을 기다리는 인바운드 QoS 2는 세션과 함께 이동합니다.
- 영속 세션의 오프라인 큐(`Broker:MaxQueuedMessages`로 유계).
- 길이 0 ID에는 브로커가 클라이언트 ID를 할당합니다(`AssignedClientIdentifier`로 반환).

### 구독

- `+`와 `#` 와일드카드, 선두 `$` 제외 규칙.
- 공유 구독: `$share/{group}/{filter}`. 각 메시지는 클러스터 전체 그룹의 멤버 하나에
  배정됩니다.
- MQTT 5 옵션: `noLocal`, `retainAsPublished`, retain handling 0/1/2.
- 일치한 전달에 구독 식별자를 붙입니다.
- 겹치는 구독은 클라이언트별로 집계되어 가장 높은 일치 QoS로 한 번 전달됩니다.

### 메시지

- retained 메시지, 빈 페이로드로 삭제 포함.
- retained와 라이브 메시지의 메시지 만료 interval.
- 유언 지연이 있는 유언. 정상 DISCONNECT에서 억제됩니다(이유 `0x04`로 명시 발행 가능).
- MQTT 5 사용자 속성, content type, response topic, correlation data는 종단 간
  보존됩니다.
- 인바운드와 아웃바운드 토픽 별칭. `Broker:MaxTopicAlias`와 클라이언트의
  Topic Alias Maximum으로 제한됩니다.
- `Broker:MaxPacketSize`로 리스너별 최대 패킷 적용.
- CONNACK에서 Receive Maximum을 광고하고 세션별로 인바운드 QoS 2 미확인 수를
  제한합니다.

### keep-alive

클라이언트는 keep-alive interval의 1.5배 안에 아무 제어 패킷이나 보내야 합니다.
초과하면 브로커는 이유 `0x8D`(MQTT 5)로 연결을 닫습니다. keep-alive `0`은 검사를
끕니다.

## 한도

| 한도 | 기본값 | 옵션 |
| --- | --- | --- |
| 패킷 크기 | 1 MiB | `Broker:MaxPacketSize` |
| 세션당 inflight | 32 | `Broker:MaxInflightMessages` |
| 오프라인 큐 | 1,000 메시지 | `Broker:MaxQueuedMessages` |
| 세션당 구독 수 | 1,000 | `Broker:MaxSubscriptionsPerSession` |
| 토픽 별칭 | 32 | `Broker:MaxTopicAlias` |
| 클라이언트 ID 길이 | 256 | `Broker:MaxClientIdLength` |
| CONNECT 제한 시간 | 10초 | `Broker:ConnectTimeout` |
| 재전송 간격 | 10초 | `Broker:RetransmitInterval` |

MQTT 자체의 상한도 적용됩니다: 남은 길이 268,435,455바이트, UTF-8 필드당 65,535바이트,
동시 패킷 ID 65,535개.

## 오류 처리

잘못된 패킷은 연결을 닫습니다. MQTT 5에서 브로커는 다음과 같은 구체적 이유로
DISCONNECT를 보내려고 시도합니다:

| 이유 | 원인 |
| --- | --- |
| `0x81` Malformed packet | 잘못된 플래그, 잘린 필드, 잘못된 UTF-8 |
| `0x82` Protocol error | 잘못된 상태 전이, 패킷 ID 0 |
| `0x8F` Topic filter invalid | SUBSCRIBE의 잘못된 필터 |
| `0x90` Topic name invalid | PUBLISH의 빈 토픽 또는 와일드카드 |
| `0x94` Topic alias invalid | 협상 범위를 벗어난 별칭 |
| `0x95` Packet too large | 설정 한도를 넘는 패킷 |
| `0x87` Not authorized | ACL의 발행/구독 거부 |
| `0x8D` Keep-alive timeout | keep-alive의 1.5배 안에 패킷 없음 |

MQTT 3.1.1 클라이언트는 연결 실패 시 가장 가까운 CONNACK 반환 코드를 받고
프로토콜 오류에서는 연결이 끊깁니다.

## 비목표(현재 릴리스)

- MQTT over QUIC.
- 노드 간 순서 보장이나 exactly-once. 클러스터 전달은 최선 노력입니다.
- 다른 브로커의 플러그인이나 확장 패키지와의 런타임 호환.
- 비대칭 JWT 서명. HS256만 받습니다.

계획은 [로드맵](/ko/roadmap)을 참고하십시오.

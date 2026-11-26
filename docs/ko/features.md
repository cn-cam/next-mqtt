# 기능 목록

이 페이지는 현재 릴리스의 구현 계약입니다. 완전히 구현된 것, 일부만 구현된 것,
계획 중인 것을 나눠 보여 주므로 도입 결정을 홍보 문구에 기대지 않게 합니다.

범례: ✅ 구현됨 · ⚠️ 일부 구현 또는 문서화된 제한 · ❌ 미구현

## 프로토콜과 전송

| 기능 | 상태 | 설명 |
| --- | --- | --- |
| MQTT 3.1.1(레벨 4) | ✅ | 코덱, 플래그, CONNACK 반환 코드 |
| MQTT 5.0(레벨 5) | ✅ | 핵심 패킷과 속성 |
| TCP 리스너 | ✅ | 기본 포트 1883 |
| TLS 리스너 | ✅ | 선택적 PFX/PEM 인증서, TLS 1.2/1.3 |
| WebSocket 리스너 | ✅ | `/mqtt`, 하위 프로토콜 `mqtt` |
| 보안 WebSocket | ✅ | Kestrel HTTPS 엔드포인트 + `/mqtt` |
| QoS 0/1/2 | ✅ | 양방향, 재전송, 중복 제거 |
| retained 메시지 | ✅ | 와일드카드 조회, 만료, 삭제 |
| 유언과 유언 지연 | ✅ | 정상 DISCONNECT 시 억제 |
| 세션 만료 | ✅ | MQTT 5 interval, DISCONNECT에서 갱신 |
| 세션 인계 | ✅ | 이전 연결은 `0x8E`를 받음 |
| 오프라인 큐 | ✅ | 영속 세션당 유계 |
| 공유 구독 | ✅ | `$share/{group}/{filter}`, 메시지마다 그룹 멤버 하나 |
| 구독 옵션 | ✅ | `noLocal`, `retainAsPublished`, retain handling |
| 토픽 별칭 | ⚠️ | 인바운드 지원, 아웃바운드는 계획 중 |
| 확장 인증(`AUTH`) | ✅ | 교체 가능한 제공자와 챌린지/응답, 내장 `nextmqtt-jwt` |
| 서버 측 receive-maximum 적용 | ⚠️ | 자체 inflight 윈도를 광고하고 제한 |
| 전달 시 클라이언트 최대 패킷 | ⚠️ | 인바운드는 제한, 아웃바운드 절단은 미구현 |
| MQTT over QUIC | ❌ | 연구 항목 |
| HTTP webhook 브리지 | ✅ | 규칙별 유계 큐, 재시도, 최초 수신 노드만 전송 |
| 플러그인 호스트 | ✅ | 매니페스트 + 회수 가능 로드 컨텍스트, 인증기/인가기 선택 |
| 규칙 엔진 동작 | ✅ | 저장 전 토픽 필터 `drop`과 접두사 `rewrite` |
| NATS 브리지 | ✅ | 텍스트 프로토콜 발행, 유계 큐, 자동 재연결 |
| Kafka 브리지 | ✅ | Confluent.Kafka 프로듀서, NATS와 같은 필터와 상한 |
| RabbitMQ 브리지 | ✅ | durable topic 교환기, publisher confirm, 자동 재연결 |

## 세션과 전달

| 기능 | 상태 | 설명 |
| --- | --- | --- |
| Clean Start / Session Present | ✅ | MQTT 3.1.1과 5.0 의미 |
| 영속 세션 | ✅ | 기본은 메모리, 선택적으로 디스크 스냅숏 |
| 세션 만료 정리 | ✅ | 백그라운드 유지 관리 루프 |
| QoS 1/2 inflight 윈도 | ✅ | 세션별 설정 가능 |
| 패킷 ID 할당 | ✅ | inflight 중인 ID를 건너뜀 |
| 오프라인 큐 상한 | ✅ | 가장 오래된 메시지를 버리고 계수 |
| 중복 구독 집계 | ✅ | 클라이언트당 최고 QoS로 한 번 전달 |
| 재시작 후 상태 유지 | ✅ | retained 저널과 세션/복제본 스냅숏, 미확인 메시지는 스냅숏에서 재생 |
| 세션 백업/복원 | ✅ | `GET`/`POST /api/v1/backup`으로 retained와 세션 입출력 |
| 정상 종료 드레인 | ✅ | `Server:DrainTimeoutSeconds`까지 inflight 확인 대기 |
| 노드 간 세션 이전 | ✅ | `CleanStart=false`면 소유자 또는 최신 복제본에서 인계 |

## 보안

| 기능 | 상태 | 설명 |
| --- | --- | --- |
| PBKDF2-SHA256 비밀번호 | ✅ | 버전 지정 형식, 210k 반복 |
| HS256 JWT | ✅ | `exp`, `nbf`, issuer, audience, roles |
| 익명 접근 | ✅ | 명시적으로 켠 경우만 |
| 토픽 ACL | ✅ | 순서 있는 규칙, ID 자리표시자, glob |
| retained 읽기 ACL | ✅ | 전달 시 다시 확인 |
| TLS | ✅ | MQTT, 대시보드/WSS |
| 대시보드 토큰 인증 | ✅ | HS256 Bearer |
| API 키 | ✅ | SHA-256 다이제스트 허용 목록 |
| 역할 기반 관리 권한 | ❌ | 현재는 관리자 역할만 |
| 연결/메시지 속도 제한 | ✅ | 원격 주소별, 클라이언트별 토큰 버킷 |
| 토픽 단위 발행 쿼터 | ✅ | 필터마다 버킷 하나를 클라이언트 간 공유 |
| 아웃바운드 토픽 별칭 | ✅ | 클라이언트의 Topic Alias Maximum 안에서 연결별 할당 |
| 구독 식별자 쿼터 | ✅ | `MaxSubscriptionIdentifiersPerClient`로 식별자 구독 제한 |
| mTLS 정책 | ⚠️ | 선택적 클라이언트 인증서 요구, 검증은 플랫폼 기본값 |

## 클러스터

| 기능 | 상태 | 설명 |
| --- | --- | --- |
| Gossip 멤버십 | ✅ | heartbeat, seed 발견, suspect/dead |
| 서명된 노드 통신 | ✅ | HMAC-SHA256, 5분 타임스탬프 창 |
| 노드 간 라우팅 | ✅ | 일치 필터를 광고한 노드로만 전달 |
| retained 복제 | ✅ | 원래 타임스탬프, last-write-wins, 삭제 표식 |
| 메시지 중복 제거 | ✅ | 유계 확인 캐시 |
| 루프 방지 | ✅ | 원격 발행은 다시 전달하지 않음 |
| 전역 공유 구독 | ✅ | 메시지마다 공유 그룹에 노드 하나를 선택 |
| 재연결 시 세션 인계 | ✅ | `CleanStart=false`면 이전 노드에서 오프라인 큐와 미완료 인바운드 QoS 2까지 가져옴 |
| 세션 복제본 영속화 | ✅ | 선택적 파일 저장으로 클러스터 전체 재시작도 견딤 |
| 노드 간 QoS 보장 | ❌ | 전달은 최선 노력 HTTP |
| 재시작 없는 클러스터 키 교체 | ❌ | 배포마다 공유 키 하나 |
| 대규모 필터 집합 라우팅 | ⚠️ | heartbeat마다 전량, 최대 10,000 |

## 운영과 관리

| 기능 | 상태 | 설명 |
| --- | --- | --- |
| REST 관리 API | ✅ | 클라이언트, 구독, retained, 발행, 클러스터, 로그, 플러그인, 설정 |
| Vue 3 대시보드 | ✅ | 영어/중국어, 다크/라이트, 라이브 폴링, 플러그인과 로그 화면 |
| MQTT 디버거 | ✅ | 브라우저 WebSocket: 3.1.1/5.0, QoS 0–2, MQTT 5 속성, 구독, 패킷 추적, 로그 내보내기 |
| 플러그인 핫 리로드 | ✅ | `POST /api/v1/plugins/reload`가 설정을 다시 읽고 디렉터리를 다시 검색해 인증과 인가를 교체 |
| 플러그인 수명 주기 | ✅ | `IPluginLifecycle`이 언로드 전에 `Initialize`와 `Dispose`를 받음 |
| Prometheus 지표 | ✅ | `/metrics`, 카운터와 게이지 |
| JSON 통계 | ✅ | 60초 발행 계열 |
| 상태/준비 확인 | ✅ | `/healthz`, `/readyz` |
| 구조화 로그 | ✅ | stdout, 레벨 설정 가능 |
| CLI | ✅ | `pub`, `sub`, `request`, `bench`, `hash-password` |
| Docker 이미지 | ✅ | 멀티 아키, 비루트, 헬스 체크 |
| Compose 단일/3노드 | ✅ | `docker-compose.yml`, `deploy/compose` |
| Kustomize 매니페스트 | ✅ | StatefulSet, PDB, HPA, Ingress, NetworkPolicy |
| Helm 차트 | ✅ | 프로브, 오토스케일, Ingress, 기존 시크릿 |
| 영속 저장소 | ✅ | retained 저널, 세션 스냅숏, 세션 복제본 |
| 분산 추적 | ❌ | 로드맵 |

## 검증

위 주장은 저장소의 테스트와 CI로 검증합니다:

| 확인 | 위치 |
| --- | --- |
| 패킷 왕복과 잘못된 입력 | `tests/NextMqtt.Core.Tests` |
| 토픽 일치와 retained 트라이 | `tests/NextMqtt.Core.Tests` |
| 세션, QoS, 큐, ACL, 인증, 클러스터 라우팅 | `tests/NextMqtt.Broker.Tests` |
| 실제 TCP CONNECT/SUBSCRIBE/PUBLISH/QoS | `tests/NextMqtt.Server.Tests` |
| 대시보드 타입 검사와 프로덕션 빌드 | CI `Dashboard` 작업 |
| 문서 빌드 | CI `Documentation` 작업 |
| Helm lint/render와 Kustomize render | CI `Kubernetes manifests and Helm chart` 작업 |
| 컨테이너 빌드 + 헬스/로그인 스모크 | CI `Container image` 작업 |
| CodeQL | `codeql.yml` |

현재 **217개 테스트**가 통과합니다(Core 63, Broker 111, Server 43).

## 확장성

NextMQTT는 프로세스 내 확장 인터페이스와 플러그인 호스트를 모두 제공합니다.
플러그인은 `plugin.json` 매니페스트에서 계약 버전 1의 회수 가능 컨텍스트로
로드됩니다. 없는 어셈블리나 다른 계약 버전을 가리키는 매니페스트는 건너뜁니다.

| 확장 지점 | 인터페이스 | 용도 |
| --- | --- | --- |
| 클라이언트 인증 | `IClientAuthenticator` | 사용자 이름/비밀번호와 API 키 검증 교체·확장 |
| 토픽 인가 | `ITopicAuthorizer` | 클라이언트별 pub/sub 허용·거부 |
| 클러스터 라우팅 | `IClusterRouter` | HTTP 전달 방식 교체 |
| 플러그인 수명 주기 | `IPluginLifecycle` | 로드 컨텍스트 언로드 전 리소스 해제 |

인터페이스는 DI에서 확인하므로 구현을 직접 컴파일해 넣을 수도 있습니다. 플러그인은
재빌드를 피하는 디스크 기반 수단입니다. `Plugins:Authenticator`와
`Plugins:Authorizer`가 활성화할 형식을 지정하고 `POST /api/v1/plugins/reload`가
재시작 없이 적용합니다. 프로세스 밖 연동은 버전이 붙은 REST API(`/api/v1`)와
클러스터 엔드포인트(`/internal/cluster`)를 사용합니다.

## 알려진 제한

- 아웃바운드 `inflight` 확인 상태는 노드 간에 이전되지 않습니다. 해당 메시지는 큐,
  복제본, 스냅숏에서 재생되어 두 번 도착할 수 있습니다. PUBREL을 기다리는 인바운드
  QoS 2 발행은 세션과 함께 이동합니다.
- 복제본의 최신성은 `Cluster:SessionReplicaSeconds`에 달려 있습니다.
  `Storage:Replicas:Mode=File`이 없으면 모든 노드 재시작 시 사라집니다.
- 클러스터 전달은 최선 노력입니다. 공유 구독은 노드 간에 조정합니다.
- 노드 간 순서 보장도 exactly-once도 없습니다.
- 프로세스 내 확장은 소스 변경과 재빌드가 필요합니다. 플러그인 호스트가 런타임
  대안입니다.
- 기본 설정은 개발 친화적입니다. 브로커를 공개하기 전에 반드시 강화하십시오.

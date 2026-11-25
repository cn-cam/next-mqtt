# 기능 목록

범례: ✅ 구현 · ⚠️ 부분/제한 · ❌ 미구현

## 프로토콜 / 전송

| 기능 | 상태 | 비고 |
| --- | --- | --- |
| MQTT 3.1.1 | ✅ | level 4 |
| MQTT 5.0 | ✅ | 핵심 패킷/속성 |
| TCP / TLS | ✅ | 1883 / 8883 |
| WebSocket / WSS | ✅ | `/mqtt`, `mqtt` 서브프로토콜 |
| QoS 0/1/2 | ✅ | 양방향, 재전송, 중복 제거 |
| Retained / will / will delay | ✅ | 만료/삭제 |
| 세션 만료 / 탈취 | ✅ | `0x8E` |
| 공유 구독 | ✅ | 노드 내 라운드로빈 |
| 구독 옵션 | ✅ | noLocal, RAP, retain handling |
| 토픽 별칭 | ⚠️ | 인바운드만 |
| 강화 인증 AUTH | ❌ | `0x8C` 반환 |
| 영구 저장 | ⚠️ | retained 저널 + 세션 스냅샷 (inflight 제외) |`n| 백업/복구 | ✅ | `GET`/`POST /api/v1/backup` |`n| 우아한 종료 | ✅ | inflight 확인 응답 대기 (`Server:DrainTimeoutSeconds`) |

## 보안

| 기능 | 상태 |
| --- | --- |
| PBKDF2-SHA256 / JWT HS256 | ✅ |
| 익명 접속(명시적) | ✅ |
| 순서형 토픽 ACL | ✅ |
| TLS / WSS | ✅ |
| 관리 토큰 / API 키 | ✅ |
| 역할 기반 관리 권한 | ❌ |
| 속도 제한 | ✅ |`n| 토픽 단위 발행 할당량 | ✅ | 필터 단위로 클라이언트 공유 |
| 아웃바운드 토픽 별칭 | ✅ | 클라이언트 상한 내에서 연결별 할당 |
| 구독 식별자 할당량 | ✅ | `MaxSubscriptionIdentifiersPerClient`로 제한 |

## 클러스터

| 기능 | 상태 | 비고 |
| --- | --- | --- |
| Gossip 멤버십 | ✅ | heartbeat / seed |
| 서명된 노드 통신 | ✅ | HMAC-SHA256 |
| 노드 간 라우팅 | ✅ | 구독 필터 광고 |
| Retained 복제 | ✅ | 최선 노력 |
| 메시지 중복 제거 | ✅ | 유계 캐시 |
| 전역 공유 구독 | ❌ | 노드 단위 |
| 노드 간 QoS 보장 | ❌ | HTTP 최선 노력 |

## 운영

| 기능 | 상태 |
| --- | --- |
| REST API / Vue 3 대시보드 | ✅ |
| MQTT 디버거 (WebSocket, 3.1.1 / 5.0, QoS 0–2, 패킷 추적) | ✅ |
| Prometheus / JSON 통계 | ✅ |
| 헬스 체크 | ✅ |
| CLI | ✅ |
| Docker/Compose/Kustomize/Helm | ✅ |
| 영구 저장 / 백업 | ❌ |
| 분산 추적 | ❌ |

## 검증

현재 **147개 테스트** 통과(Core 63, Broker 61, Server 23). CI에서 빌드, 테스트,
대시보드, 문서, Helm/Kustomize, 이미지, CodeQL을 검증합니다.

## 확장성

NextMQTT 0.1.x는 플러그인 ABI가 아니라 확장 **인터페이스**를 제공합니다. 다른
MQTT 브로커용으로 빌드된 플러그인 패키지는 로드되지 않으며 호환되지도, 지원되지도
않습니다.

| 확장 지점 | 인터페이스 | 용도 |
| --- | --- | --- |
| 클라이언트 인증 | `IClientAuthenticator` | 사용자 이름/비밀번호 및 API 키 검증 대체·확장 |
| 토픽 권한 | `ITopicAuthorizer` | 클라이언트별 publish/subscribe 허용·거부 |
| 클러스터 라우팅 | `IClusterRouter` | HTTP 전달 전략 대체 |

모두 의존성 주입으로 해석되므로 확장은 소스 수정 후 재빌드로 이루어집니다. 별도
런타임이나 자체 설정 스키마가 없습니다. 프로세스 외부 연동은 버전이 있는 REST
API(`/api/v1`)와 클러스터 엔드포인트(`/internal/cluster`)를 사용합니다. 자체 확장
호스트는 로드맵에 있으며 구현 전에 ADR이 필요합니다
([ADR 0005](/adr/0005-extension-interfaces.md)).

## 알려진 제한

- 세션, 큐, 구독은 메모리 내에 있어 재시작 시 사라집니다. retained는 파일 로그로 유지할 수 있습니다.
- 클러스터 전달은 최선 노력이며 공유 구독은 노드 단위입니다.
- 영구 저장, 강화 인증, 아웃바운드 별칭, 속도 제한, 브리지는 미구현입니다.
- 확장에는 소스 수정이 필요하며 플러그인 호스트는 아직 없습니다.

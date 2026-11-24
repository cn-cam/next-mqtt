# 프로토콜 지원

OASIS MQTT 3.1.1(level 4)과 5.0(level 5)을 구현합니다.

## 전송

| 방식 | 기본 포트 | 비고 |
| --- | --- | --- |
| TCP | 1883 | 평문 |
| TLS | 8883 | 인증서 필요 |
| WebSocket | 8083 `/mqtt` | `mqtt` 서브프로토콜 |
| WSS | 8084 `/mqtt` | HTTPS 인증서 |

## 패킷

| 패킷 | 3.1.1 | 5.0 |
| --- | --- | --- |
| CONNECT / CONNACK | ✅ | ✅ |
| PUBLISH | ✅ | ✅ |
| PUBACK/PUBREC/PUBREL/PUBCOMP | ✅ | ✅ |
| SUBSCRIBE / SUBACK | ✅ | ✅ |
| UNSUBSCRIBE / UNSUBACK | ✅ | ✅ |
| PINGREQ / PINGRESP | ✅ | ✅ |
| DISCONNECT | ✅ | ✅ |
| AUTH | — | ⚠️ `0x8C` |

## 기능

- clean start, 영구 세션, 세션 만료, 탈취
- 유계 오프라인 큐
- retained, 메시지 만료, will, will delay
- `+`/`#`, `$` 첫 레벨 규칙
- `$share/{group}/{filter}` (노드 내 라운드로빈)
- noLocal / retainAsPublished / retain handling
- 사용자 속성, content type, response topic, correlation data
- 인바운드 토픽 별칭
- keep-alive 1.5배

## 제한

| 제한 | 기본 |
| --- | --- |
| 최대 패킷 | 1 MiB |
| inflight | 32 |
| 오프라인 큐 | 1000 |
| 세션 구독 수 | 1000 |
| 토픽 별칭 | 32 |
| CONNECT 타임아웃 | 10초 |
| 재전송 간격 | 10초 |

## 오류

| 코드 | 원인 |
| --- | --- |
| `0x81` | 잘못된 패킷/UTF-8 |
| `0x82` | 프로토콜 위반 |
| `0x8F` | 잘못된 필터 |
| `0x90` | 잘못된 토픽 |
| `0x94` | 잘못된 별칭 |
| `0x95` | 패킷 초과 |
| `0x87` | ACL 거부 |
| `0x8D` | keep-alive 초과 |

## 비목표

강화 인증, 아웃바운드 별칭, QUIC, 전역 공유 구독, 영구 저장, 브리지, 규칙 엔진,
플러그인 SDK.

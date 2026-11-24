# 아키텍처

NextMQTT는 모듈형 모놀리스입니다. 데이터 플레인(MQTT)과 제어 플레인(API/UI)을
분리하고 단일 프로세스로 동작합니다.

## 원칙

1. **데이터 플레인 분리**: 관리 요청이 publish를 막지 않음
2. **모든 것에 유계**: inflight, 큐, 패킷 크기, 송신 채널
3. **세션 단일 소유**: 클라이언트 ID는 한 노드에 속함
4. **제어면 교체 가능**: API/UI는 동일 Broker 파사드 사용
5. **클린룸**: OASIS MQTT 사양 준수

## 구성

```text
MQTT/TCP/TLS ──► Server listeners ──► ClientConnection
Browser WS ────► Kestrel /mqtt ─────► ClientConnection
                                              │
              ┌───────────────────────────────▼─────────────┐
              │ Broker: sessions, routing, retained,        │
              │ auth/ACL, cluster, metrics                  │
              └───────────────────────────────┬─────────────┘
              ┌───────────────────────────────▼─────────────┐
              │ Core: codec, topic filters, topic trie      │
              └─────────────────────────────────────────────┘
```

- **Core**: MQTT 3.1.1/5.0 코덱, UTF-8/속성 검증, 토픽 Trie
- **Broker**: 연결 상태, 세션, QoS, retained, 인증, ACL, 클러스터
- **Server**: TCP/TLS, Kestrel, REST, UI, 내부 API
- **Cli**: pub/sub/bench/hash

## 연결 수명주기

1. TCP/TLS/WS 수신
2. `ConnectTimeout` 내 CONNECT 수신
3. 클라이언트 ID 검증/생성
4. 인증(비밀번호 → JWT → 익명)
5. 세션 연결(clean start/재개/탈취)
6. CONNACK 후 송신 루프와 재전송
7. DISCONNECT/keep-alive/오류 시 정리
8. 세션 만료/삭제, will 발행

## 데이터 구조

- **TopicTrie**: 구독 매칭과 retained 검색
- **SubscriptionIndex**: 일반/공유 구독, 클라이언트별 색인
- **BrokerSession**: inflight, 오프라인 큐, 인바운드 QoS 2 중복 제거
- **RetainedMessageStore**: 토픽별 Trie, 만료, 삭제

## publish 경로

```text
PUBLISH → topic/alias 검증 → ACL → retained 갱신
        → 구독 매칭 → 클라이언트 단위 집계
        → 세션/큐 → 연결 채널 → 클러스터 전달
```

전달 QoS는 `min(publish, subscription)`. RAP이 없으면 retain을 해제합니다.

## 클러스터

- heartbeat로 일반 구독 필터 광고
- gossip으로 멤버 전파
- retain은 모든 노드, 일반은 일치 필터만
- 메시지 ID로 중복 제거, 재전달 없음
- 공유 구독은 노드 단위

## 내구성

| 이벤트 | 동작 |
| --- | --- |
| 연결 종료 | 만료/clean start에 따라 세션 유지 |
| 재시작 | 세션/큐/retained 손실 |
| 노드 장애 | suspect → dead → 제거 |
| 네트워크 분할 | 각 측 계속 서비스, 전달 실패 |
| 느린 소비자 | 채널 포화 시 폐기/계측 |

## 비목표

- 타사 Broker 런타임/플러그인 호환
- 내장 규칙 엔진/브리지
- 타사 제품 인증이나 보증 주장

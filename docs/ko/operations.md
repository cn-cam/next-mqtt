# 운영

## 헬스 체크

| 엔드포인트 | 용도 |
| --- | --- |
| `/healthz` | 생존 확인 |
| `/readyz` | 준비 상태 |
| `/api/v1/health` | API 상태 |
| `/api/v1/ready` | API 준비/세션 수 |

## 메트릭

`GET /metrics`는 Prometheus 형식입니다. `Admin:MetricsRequireAuth=true`로 보호할 수
있습니다.

| 메트릭 | 유형 | 설명 |
| --- | --- | --- |
| `nextmqtt_connections_opened_total` | counter | 연결 수 |
| `nextmqtt_connections_closed_total` | counter | 종료 수 |
| `nextmqtt_authentication_failures_total` | counter | 인증 실패 |
| `nextmqtt_publishes_received_total` | counter | 수신 publish |
| `nextmqtt_publishes_sent_total` | counter | 전달 publish |
| `nextmqtt_bytes_received_total` | counter | 수신 바이트 |
| `nextmqtt_bytes_sent_total` | counter | 송신 바이트 |
| `nextmqtt_messages_dropped_total` | counter | 폐기 |
| `nextmqtt_retransmissions_total` | counter | 재전송 |
| `nextmqtt_clients_connected` | gauge | 연결 중 |
| `nextmqtt_sessions` | gauge | 세션 |
| `nextmqtt_subscriptions` | gauge | 구독 |
| `nextmqtt_retained_messages` | gauge | retained |
| `nextmqtt_cluster_nodes` | gauge | 노드 |

## 로그

stdout으로 출력합니다. 연결/종료, 클러스터 참여/타임아웃, 시작 비밀번호 경고 등이
포함됩니다. `Logging:LogLevel:NextMqtt=Debug`는 문제 분석에 유용하지만 운영에서는
주의하세요.

## 용량

| 워크로드 | CPU | 메모리 |
| --- | --- | --- |
| 1만 유휴 연결 | 0.5 코어 | 256–512 MiB |
| 1만 QoS 1 연결 | 1–2 코어 | 512 MiB–1 GiB |
| 대규모 retained/큐 | 1+ 코어 | 부하에 따라 |

## 세션

- 영구 세션은 만료까지 메모리에 유지
- `SweepInterval`(기본 30초)로 만료 정리
- `cleanStart=true`는 다음 연결 시 기존 세션 폐기
- 관리 UI 연결 종료는 `0x98` 전송

## 업그레이드

1. 노드를 하나씩 drain/축소
2. 클라이언트 재연결(상태는 메모리)
3. 이미지 태그 갱신
4. `/readyz`와 메트릭 확인
5. 클러스터는 2개 이상 노드 유지

롤링 재시작 시 큐와 retained가 사라집니다. 영구 저장은 로드맵을 참고하세요.

## 문제 해결

| 증상 | 원인 | 조치 |
| --- | --- | --- |
| UI 401 | 토큰 만료/임시 키 | 재로그인, Secret 고정 |
| MQTT 모두 거부 | 익명 비활성 + 사용자 없음 | 사용자/JWT 설정 |
| SUBACK 0x87 | ACL 거부 | 규칙 확인 |
| 유휴 연결 종료 | keep-alive 초과 | 클라이언트/네트워크 설정 |
| 오프라인 메시지 유실 | 비영구/큐 가득 | 세션 만료/큐 확대 |
| 노드 suspect/dead | 네트워크/URL/키/시계 | 8080, AdvertiseUrl, 비밀키, NTP |
| drop 증가 | 느린 소비자 | 처리량 확인, 제한 조정 |

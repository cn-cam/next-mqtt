# 운영

이 페이지는 일상 운영에 필요한 내용을 다룹니다: 상태 확인, 지표, 로그, 용량, 업그레이드,
문제 해결.

## 상태 엔드포인트

| 엔드포인트 | 용도 |
| --- | --- |
| `GET /healthz` | 프로세스 생존. liveness 프로브에 적합 |
| `GET /readyz` | 트래픽을 받을 준비 완료 |
| `GET /api/v1/health` | 버전이 포함된 API 상태 |
| `GET /api/v1/ready` | 세션 수가 포함된 API 준비 상태 |

모두 설계상 인증이 필요 없습니다. 상태 응답에 비밀을 넣지 마십시오.

## 지표

`GET /metrics`는 Prometheus 텍스트 형식을 노출합니다. 인증은 기본적으로 꺼져 있습니다.
Bearer 토큰이나 API 키를 요구하려면 `Admin:MetricsRequireAuth=true`를 설정하십시오.

| 지표 | 유형 | 의미 |
| --- | --- | --- |
| `nextmqtt_connections_opened_total` | counter | 수락한 CONNECT |
| `nextmqtt_connections_closed_total` | counter | 닫힌 연결 |
| `nextmqtt_connections_rejected_total` | counter | CONNECT 중 거부 |
| `nextmqtt_authentication_failures_total` | counter | 인증 실패 |
| `nextmqtt_publishes_received_total` | counter | 수락한 PUBLISH |
| `nextmqtt_publishes_sent_total` | counter | 전달한 PUBLISH |
| `nextmqtt_bytes_received_total` | counter | MQTT 소켓에서 읽은 바이트 |
| `nextmqtt_bytes_sent_total` | counter | MQTT 소켓에 쓴 바이트 |
| `nextmqtt_messages_dropped_total` | counter | 정책 또는 백프레셔로 버린 메시지 |
| `nextmqtt_retransmissions_total` | counter | QoS 재전송 |
| `nextmqtt_subscriptions_created_total` | counter | 성공한 구독 |
| `nextmqtt_sessions_expired_total` | counter | 만료로 제거된 세션 |
| `nextmqtt_connections_rate_limited_total` | counter | 연결 속도 제한으로 거부한 CONNECT |
| `nextmqtt_publishes_rate_limited_total` | counter | 클라이언트 속도 제한으로 버린 PUBLISH |
| `nextmqtt_clients_connected` | gauge | 온라인 연결 |
| `nextmqtt_sessions` | gauge | 레지스트리의 세션 |
| `nextmqtt_subscriptions` | gauge | 활성 구독 |
| `nextmqtt_retained_messages` | gauge | retained 메시지 |
| `nextmqtt_cluster_nodes` | gauge | 알려진 클러스터 노드 |
| `nextmqtt_build_info` | gauge | 버전 레이블 |

Prometheus 스크레이프 설정 예:

```yaml
scrape_configs:
  - job_name: nextmqtt
    metrics_path: /metrics
    static_configs:
      - targets: ["nextmqtt-http:8080"]
```

Kubernetes 매니페스트는 `prometheus.io/scrape` 애노테이션을 추가합니다.

## 대시보드

개요 페이지는 3초마다 API를 폴링하며 다음을 보여 줍니다:

- 연결된 클라이언트와 총 세션 수
- 구독과 retained 메시지
- 최근 1분 발행 처리량
- 연결, 인증, 드롭, 재전송 카운터
- 클러스터 멤버십

클라이언트, 구독, retained 표는 5초마다 갱신되고 서버 측에서 페이지를 나눕니다. 발행
콘솔은 REST API와 같은 ACL 없는 관리 경로를 사용합니다.

MQTT 디버거(Messaging → Debugger)는 브라우저에서 WebSocket으로 브로커에 직접
연결하므로 관리 API가 아니라 실제 프로토콜을 검증합니다. MQTT 3.1.1과 5.0, QoS 0–2,
retained, 유언, MQTT 5 세션/발행 속성, noLocal 토픽 필터, retain-as-published, retain
handling, PING, 패킷 추적, JSON 로그 내보내기를 지원합니다. 일반 MQTT 클라이언트와
같은 자격 증명을 쓰며 브로커의 인증과 ACL을 따릅니다.

## 로그

로그는 stdout에 기록됩니다. 유용한 메시지:

- 전송과 주소가 포함된 리스너 시작
- 클라이언트 ID와 session-present 플래그가 포함된 연결/해제
- 클러스터 참여/이탈/시간 초과
- 생성된 부트스트랩 비밀번호와 임시 토큰 시크릿 경고
- 디버그 수준의 프로토콜 오류

패킷 수준 진단에는 `Logging:LogLevel:NextMqtt=Debug`를 설정합니다. 페이로드 프라이버시가
중요한 운영에서는 디버그 로그를 피하십시오. 브로커는 페이로드를 로그에 남기지 않습니다.

## 용량 계획

출발점:

| 워크로드 | CPU | 메모리 |
| --- | --- | --- |
| 유휴 클라이언트 1만까지 | 0.5 코어 | 256–512 MiB |
| 적당한 QoS 1 트래픽의 1만 클라이언트 | 1–2 코어 | 512 MiB–1 GiB |
| 큰 retained 집합 / 오프라인 큐 | 1 코어 이상 | 페이로드와 큐 크기에 맞춤 |

메모리를 지배하는 것:

- 세션 객체와 구독 항목
- 오프라인 큐(영속 세션당 `MaxQueuedMessages`)
- retained 메시지 페이로드
- inflight 윈도

메모리가 빠듯하면 큐 상한이나 세션 만료를 줄이십시오. 현재 릴리스에는 retained 전체
쿼터가 없으므로 `nextmqtt_retained_messages`와 페이로드 크기를 모니터링하십시오.

### 측정된 참고 수치

| 시나리오 | 결과 |
| --- | --- |
| 동시 1,000 연결 | 1.09초에 1,000 수락(약 920 연결/초) |
| 동시 5,000 연결 | 3.16초에 5,000 수락(약 1,580 연결/초), 실패 0 |
| QoS 0, 50 클라이언트 × 2,000 메시지, 64 B | 약 194,000 msg/s |
| QoS 0, 100 클라이언트 × 2,000 메시지, 128 B | 약 186,000 msg/s |
| QoS 1, 20 클라이언트 × 500 메시지 | 약 19,800 msg/s |

하드웨어: Windows 11 한 대, Intel Core i7-11700F(8코어/16스레드), 32 GiB RAM.
브로커와 부하 생성기가 루프백으로 같은 CPU를 공유합니다. 이 수치는 의도적으로
보수적입니다. 별도 호스트의 생성기, Linux, 전용 호스트의 릴리스 빌드에서는 더 좋습니다.
`nextmqtt bench`와 `nextmqtt bench-connections`로 재현할 수 있습니다. TLS, retained가
많은 토픽, 큰 팬아웃은 수치를 낮춥니다.

Docker Desktop의 게시 포트에서 실행하면 호스트 포트 포워더가 이 머신에서 약 2,000 동시
연결에서 포화됩니다. 그 이상에서는 브로커에 도달하기 전에 연결이 실패합니다. 연결
용량을 측정하려면 컨테이너 네트워크 안이나 Linux 호스트에서 생성기를 실행하십시오.

## 세션 수명 주기

- 영속 세션은 만료까지 메모리에 있고, `Storage:Session:Mode`가 `file`이면 디스크
  스냅숏에도 있습니다
- 재연결 시 새 소유자가 인계하고, 소유자가 죽었으면 최신 복제본을 사용합니다
  (복제본도 파일 저장 가능)
- 유지 보수 루프가 `Broker:SweepInterval`(기본 30초)마다 만료를 확인합니다
- `cleanStart=true`로 연결을 끊은 클라이언트는 다음 CONNECT에서 이전 세션을 버립니다
- 대시보드의 "연결 끊기"는 MQTT 5 이유 `0x98`을 보내고 소켓을 닫습니다. 이후에는 정상
  만료 규칙을 따릅니다

## 업그레이드

1. 노드를 하나씩 드레인하거나 축소합니다.
2. 상태를 유지해야 하면 세션, 복제본, retained 파일을 마운트합니다. 아니면 클라이언트
   재연결과 재구독을 계획합니다.
3. Compose/Helm의 이미지 태그를 바꿉니다.
4. 롤아웃 중 `/readyz`와 `nextmqtt_clients_connected`를 봅니다.
5. 클러스터에서는 라우팅 범위를 유지하려고 항상 2개 이상 노드를 살립니다.

파일 저장소를 마운트하고 복제본이 최신이면 롤링 재시작 후에도 영속 세션이 남습니다.
없으면 큐와 retained가 사라집니다. retained는 마지막 값 저장소이지 영속 이벤트 로그가
아닙니다.

## 문제 해결

| 증상 | 가능한 원인 | 조치 |
| --- | --- | --- |
| 대시보드 401 | 토큰 만료 또는 임시 시크릿으로 재시작 | 다시 로그인, `NEXTMQTT_ADMIN_TOKEN_SECRET` 설정 |
| 모든 MQTT 클라이언트 거부 | 익명 비활성이고 사용자/JWT 없음 | 사용자 설정 또는 익명 활성화 |
| SUBACK `0x87` | ACL이 필터를 거부 | `Acl:Rules`와 기본 정책 확인 |
| PUBACK `0x87` | ACL이 토픽을 거부 | 발행 규칙 확인 |
| 유휴 후 연결 끊김 | keep-alive 초과 | 클라이언트 keep-alive와 네트워크 idle timeout 확인 |
| 오프라인 클라이언트 메시지 유실 | 세션이 비영속이거나 큐가 가득 참 | 세션 만료 / clean start false 사용, 큐 상한 상향 |
| 클러스터가 suspect/dead | heartbeat가 방화벽에 막힘, URL 오류 | `AdvertiseUrl`, 8080, 시크릿, 시계 오차 확인 |
| 드롭 카운터 증가 | 느린 소비자, 작은 큐/채널 | 클라이언트 처리량 확인 후 신중히 상한 상향 |
| `nextmqtt_connections_rejected_total` 증가 | 인증 또는 클라이언트 ID 실패 | 인증 로그와 클라이언트 ID 규칙 확인 |

## 백업

`Storage:Retained:Mode`가 `file`이면 `Storage:Retained:Path`의 저널이 모든 retained
메시지를 보관하며 브로커가 실행 중에도 복사할 수 있습니다(추가 전용이고 압축은 원자적
rename 사용). 영속 세션은 `Storage:Session:Mode`가 `file`이면
`Storage:Session:Path`에, 받은 복제본은 `Storage:Replicas:Mode`가 `file`이면
`Storage:Replicas:Path`에 스냅숏됩니다. 해당 파일은 노드를 멈춘 뒤 복사하거나 아래 API를
사용하십시오.

저장 모드와 무관한 논리 백업에는 API를 사용합니다:

```bash
curl -s http://localhost:8080/api/v1/backup -H "Authorization: Bearer $TOKEN" \
  > nextmqtt-backup.json
curl -s -X POST http://localhost:8080/api/v1/backup -H "Authorization: Bearer $TOKEN" \
  -H 'content-type: application/json' --data @nextmqtt-backup.json
```

가져오기는 retained를 교체하고 현재 온라인이 아닌 영속 세션을 추가합니다. 확인을
기다리는 아웃바운드 메시지는 큐 형태로 포함되지만 확인 상태 자체는 포함되지 않습니다.

설정은 환경 변수와 Kubernetes/Compose 파일에 있습니다. 이것도 백업하십시오. 관리자
토큰 시크릿과 클러스터 시크릿은 토큰 무효화나 노드 신뢰 붕괴를 막기 위해 보존해야
합니다.

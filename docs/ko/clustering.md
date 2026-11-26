# 클러스터

코디네이터 없는 메시. 각 노드가 세션을 소유하고 구독 필터에 따라 publish를
전달합니다.

## 설정

```json
{
  "Cluster": {
    "Enabled": true,
    "NodeId": "node-a",
    "AdvertiseUrl": "http://node-a:8080",
    "Seeds": ["http://node-b:8080", "http://node-c:8080"],
    "Secret": "16자 이상 공유 키",
    "HeartbeatSeconds": 5,
    "NodeTimeoutSeconds": 20
  }
}
```

```bash
NEXTMQTT_CLUSTER__ENABLED=true
NEXTMQTT_CLUSTER__NODEID=node-a
NEXTMQTT_CLUSTER__ADVERTISEURL=http://node-a:8080
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
NEXTMQTT_CLUSTER__SECRET=shared-secret-at-least-16-chars
```

요건: 8080 접근 가능, 시계 오차 5분 이내, NodeId 고유, Secret 일치.

## 멤버십

- heartbeat에 NodeId, URL, 일반 구독 필터 포함
- 수신 측은 멤버를 병합하고 자신의 멤버 표를 반환
- seed 하나로 gossip을 통해 전체 발견
- 타임아웃 시 suspect, 3배 후 제거

`GET /api/v1/cluster`와 UI에서 확인할 수 있습니다.

## 자가 치유

- **정지 노드는 제거됩니다**: 노드 타임아웃 후 suspect, 3배 시간이 지나면 멤버 표에서 제거되고 해당 필터로의 전달도 멈춥니다.
- **제한은 즉시 재계산**: 참여·이탈 시점에 연결·publish·토픽 할당을 다시 나눕니다.
- **실행 중 참여**: 재시작·추가 노드는 시드 하나면 충분하며 첫 heartbeat 응답에서 멤버 표를 학습합니다.
- **retained 따라잡기**: 최초 참여 시 피어에서 retained를 가져오고 원래 타임스탬프를 유지해 last-write-wins가 유지됩니다.
- **세션 인계**: `CleanStart=false`로 다른 노드에 재연결하면 새 노드가 이전 소유자에게서 세션을 인계받아 구독과 오프라인 큐를 복원합니다. `CleanStart=true`는 새 세션입니다. 이전 연결은 0x8E로 닫고 유언은 보내지 않습니다.
- **세션 복제본**: `SessionReplicaSeconds`마다 영속 세션 스냅샷을 피어로 보냅니다. 소유자가 죽어도 다른 노드에서 재연결하면 최신 복제본으로 복원합니다. 단, 복제본이 기록한 소유자가 아직 살아 있으면 사용하지 않습니다(실행 중인 세션을 되살리지 않기 위해).

## 가용성 모델

전제: 한 클라이언트는 한 번에 한 노드에만 연결되고, 끊기면 스스로 재연결합니다.

| 장애 | 영향 | 클러스터 동작 |
| --- | --- | --- |
| 클라이언트 TCP 끊김 | 세션이 오프라인 | 만료까지 큐가 계속 수신 |
| 노드 정지 | 해당 세션 접근 불가, 전달 중단 | `NodeTimeoutSeconds` 후 suspect, 3배 후 제거, 재연결 시 인계 |
| 다른 노드로 재연결 | 세션이 원래 노드에 있음 | 살아 있는 소유자에게서 인계, 없거나 "없음" 응답이면 최신 복제본 사용 |
| 소유자 디스크 손실 | 로컬 저장이 비어 있음 | 피어 복제본으로 복원(소유자가 살아 있어도 "없음"이라 답하면 허용) |
| 전체 재시작 | 메모리 비움 | 각 노드가 세션·복제본 파일을 다시 읽고 `(lastSeen, nodeId)`로 최신 선택 |
| 네트워크 분할 | 멤버십이 둘로 나뉨 | 실제 연결을 가진 쪽만 서비스, 소유자가 살아 있으면 복제본 사용 안 함 |

**RPO**: 복제본 최대 5초, 소유자 스냅샷 최대 30초 지연. retained는 쓰기마다 저장.
**RTO**: 재연결 + 내부 왕복 1회, 운영자 개입 불필요.
**제외**: 인바운드 QoS 2와 inflight 이전, 만료 0 세션, 순서 보장, exactly-once.
**분할 결정**: 살아 있는 소유자 우선, 없으면 최신 복제본, 동률은 `(lastSeen, nodeId)`로 결정적으로 해결.

## 라우팅

- 로컬 전달 후 retain은 모든 노드로
- 일반 publish는 일치 필터를 광고한 노드로만
- 수신 측은 HMAC 검증, 메시지 ID 중복 제거, 재전달 없음

## 전달 의미

- 노드 간 HTTP 최선 노력
- 세션은 `CleanStart=false` 재연결 시 인계(인바운드 QoS 2와 inflight 제외)
- 공유 구독은 클러스터 전체에서 한 노드만
- 필터 전파는 최대 1 heartbeat 지연

## 보안

`X-NextMQTT-Timestamp`와 `X-NextMQTT-Signature`(HMAC-SHA256, 5분 허용).

- 비밀 키는 전송하지 않음
- 외부 네트워크에서는 HTTPS
- `/internal/cluster/*` 차단
- 키 교체는 재시작 필요

대시보드 로그인은 클러스터 시크릿과 별개입니다. `NEXTMQTT_ADMIN_TOKEN_SECRET`이
없으면 각 노드가 자체 임시 키로 토큰에 서명하므로 한 노드에서 발급한 토큰은 다른
노드에서 거부됩니다. 한 번 로그인해 어느 노드의 대시보드/API든 사용하려면 모든
노드에 같은 값을 설정하세요.

## Kubernetes

```text
NEXTMQTT_CLUSTER__NODEID=$(POD_NAME)
NEXTMQTT_CLUSTER__ADVERTISEURL=http://$(POD_NAME).nextmqtt-headless.nextmqtt.svc.cluster.local:8080
```

처음 3개 Pod를 seed로 설정합니다. 추가 Pod는 gossip으로 발견됩니다.

## 검증

```bash
curl -s http://localhost:8080/api/v1/cluster -H "Authorization: Bearer $TOKEN" | jq '.nodes'

nextmqtt sub --port 1884 --topic cluster/# --count 1 &
nextmqtt pub --port 1883 --topic cluster/test --message hello --qos 1
```

구독 후 최소 1 heartbeat 기다린 뒤 publish하세요.

## 제한

- 세션 인계는 구독과 오프라인 큐만 대상(인바운드 QoS 2와 inflight 제외, 클라이언트가 재전송)
- 복제본은 기본적으로 메모리에 있음. `Storage:Replicas:Mode`를 `File`로 두면 전체 재시작에도 유지
- retained는 최선 노력 복제
- 필터 전체 전송, 최대 10,000

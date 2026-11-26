# 클러스터

NextMQTT 클러스터는 코디네이터 없는 메시입니다. 각 노드가 자기 세션을 소유하고,
일치하는 구독을 광고한 피어로 발행을 전달합니다.

## 요건

- 16자 이상의 공유 HMAC 시크릿.
- 각 노드에서 접근 가능한 `AdvertiseUrl`(피어가 HTTP 또는 HTTPS로 호출).
- 노드 간 TCP 8080 개방.
- 시계가 5분 이내로 동기화(서명된 요청 타임스탬프는 5분 창을 가집니다).
- 고유한 `NodeId`.

## 설정

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
    "MaxFiltersPerHeartbeat": 10000
  }
}
```

환경 변수 형식:

```bash
NEXTMQTT_CLUSTER__ENABLED=true
NEXTMQTT_CLUSTER__NODEID=node-a
NEXTMQTT_CLUSTER__ADVERTISEURL=http://node-a:8080
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
NEXTMQTT_CLUSTER__SEEDS__1=http://node-c:8080
NEXTMQTT_CLUSTER__SECRET=shared-secret-at-least-16-chars
```

도달 가능한 seed 하나면 충분합니다. 멤버십은 gossip으로 전파되므로 새 노드가 자동으로
메시 전체에 보입니다.

## 멤버십

- 각 노드는 시작 즉시 heartbeat 루프를 시작합니다.
- heartbeat에는 노드 ID, Advertise URL, 일반 구독 필터가 들어갑니다.
- 수신 측은 보낸 노드를 멤버 표에 합치고 자신의 멤버 표를 돌려줍니다.
- 받은 멤버도 합쳐지며 멤버십이 메시 전체로 전파됩니다.
- `NodeTimeoutSeconds`를 넘긴 노드는 `suspect`, 다음으로 `dead`가 되고 타임아웃 3배
  후 제거됩니다.

대시보드 **Cluster** 페이지와 `GET /api/v1/cluster`에서 현재 뷰를 볼 수 있습니다.

## 자가 복구

- **정지 노드는 제거됩니다.** `NodeTimeoutSeconds` 동안 조용한 피어는 `suspect`, 다음
  `dead`가 되고 세 번의 타임아웃 후 표에서 빠집니다. 광고했던 필터로의 전달도 즉시
  멈추므로 크래시한 노드가 계속 트래픽을 끌어들이지 않습니다.
- **한도는 즉시 재계산됩니다.** 노드가 참여하거나 떠나면 연결, 발행, 토픽 쿼터의 노드별
  몫을 다음 순회를 기다리지 않고 다시 계산합니다. 클러스터 전체 값은 설정대로 유지됩니다.
- **실행 중 참여는 정상입니다.** 재시작하거나 새로 추가된 노드는 도달 가능한 seed 하나만
  필요합니다. 첫 heartbeat 응답에서 멤버 표를 합치고, 다른 노드도 같은 방식으로 그를
  알게 됩니다.
- **retained가 따라잡습니다.** 처음 참여할 때 서명된 내부 API로 한 피어에서 retained
  집합을 가져오며 원래 타임스탬프를 유지하므로 last-write-wins가 그대로 적용됩니다.
  오프라인이었던 동안 발행된 메시지도 잃지 않습니다.
- **세션 복제본은 크래시를 견딥니다.** `SessionReplicaSeconds`마다 영속 세션 스냅숏을
  피어로 보냅니다. 소유자가 죽고 클라이언트가 다른 노드로 재연결하면 새 노드는 도달
  가능한 소유자에게서 세션을 인계하고, 아니면 최신 복제본으로 대체합니다. 복제본은
  소유자가 살아 있지 않거나, 인계 시 도달할 수 없거나, 살아 있는 소유자가 "보유하지
  않음"이라고 답할 때(자체 파일 유실 등)만 사용합니다. 따라서 오래된 사본이 다른 곳에서
  실행 중인 세션을 되살릴 수 없습니다.

세션은 클라이언트를 따라갑니다. `CleanStart=false`로 다른 노드에 재연결하면 이전
소유자에게서 세션을 인계받아 오프라인 큐와 구독이 노드 변경을 넘어 유지됩니다.
`CleanStart=true`는 기존처럼 새 세션입니다.

## 가용성 모델

전제: 한 클라이언트는 한 번에 한 노드에만 연결되고, 끊기면 스스로 재연결합니다.
이 전제 아래에서:

| 장애 | 영향 | 클러스터 동작 |
| --- | --- | --- |
| 클라이언트 TCP 끊김 | 세션이 오프라인 | 만료까지 큐가 계속 수신 |
| 노드 정지 | 해당 세션 접근 불가, 전달 중단 | `NodeTimeoutSeconds` 후 dead. 구독자용 메시지는 클라이언트가 재연결해 다른 노드에서 인계할 때까지 유실 |
| 다른 노드로 재연결 | 세션이 죽은 노드에 있음 | 새 노드가 인계. 소유자가 없거나, 도달할 수 없거나, "없음"이라고 답하면 최신 복제본 사용 |
| 소유자 디스크 손실, 재시작 | 세션 저장소가 비어 있음 | 피어 복제본(설정 시 파일 저장)으로 복원. "없음"이라고 답한 피어가 대체를 막지 않음 |
| 전체 재시작 | 메모리 비움 | 각 노드가 세션과 복제본 파일을 다시 읽고 `(lastSeen, nodeId)`로 최신 선택 |
| 네트워크 분할 | 멤버십이 둘로 나뉨 | 실제 연결을 가진 쪽만 서비스. 기록된 소유자가 로컬 뷰에서 살아 있으면 복제본 사용 안 함 |
| 복제본 전송 지연 | 복제본이 최대 한 배치 라운드(`SessionReplicaSeconds`, 세션 수가 `SessionReplicaBatchSize`를 넘으면 더 오래)만큼 오래됨 | 복구한 세션이 최근 큐를 놓칠 수 있음 |

**RPO**: 복제본 쪽 세션은 보통 최대 `SessionReplicaSeconds`(기본 5초) 지연됩니다.
라운드마다 `SessionReplicaBatchSize`(기본 200) 세션을 보내므로, 영속 세션이 그보다 많은
노드는 한 바퀴에 `ceil(세션 수 / 배치 크기) × SessionReplicaSeconds`가 걸리고 가장 오래된
복제본은 그만큼 지연될 수 있습니다. 소유자 자체 파일은 최대
`SessionSnapshotSeconds`(30초) 지연. retained는 쓰기마다 저장. 전달되었지만 확인되지 않은
메시지는 큐에서 재생되며 중복될 수 있지만 MQTT QoS 1/2는 이를 허용합니다.

**RTO**: 재연결과 내부 왕복 1회. 새 노드가 살아 있는 피어에 묻고, 그다음 자기 복제본
파일을 읽습니다. 운영자 개입은 필요 없습니다.

**의도적 제한**: 아웃바운드 `inflight` 확인 상태는 이전되지 않습니다. 메시지는 큐에서
재생되어 두 번 도착할 수 있습니다(QoS 1/2 허용). PUBREL을 기다리는 인바운드 QoS 2
발행은 세션과 함께 이동하므로 클라이언트는 페이로드를 다시 보내지 않고 핸드셰이크를
마칠 수 있습니다. 만료 0 세션은 복제하지 않습니다(정의상 연결보다 오래 살 수 없음).
노드 간 순서 보장도 exactly-once도 없습니다.

**분할 결정**: 세션 소유권은 합의가 아니라 증거로 정합니다. 살아 있는 소유자가 항상
이기고, 소유자가 "세션 없음"이라고 답하면 최신 복제본을 씁니다. 복원된 두 사본의 동률은
`(lastSeen, nodeId)`로 깨므로 각 노드가 조정 없이 같은 결론에 도달합니다. 트레이드오프는
분할된 소유자와 복구 사본이 짧은 동안 같은 클라이언트 ID를 모두 받아들일 수 있다는
점입니다. 클라이언트는 한 곳에만 있을 수 있으므로 MQTT 의미론에서 허용됩니다.

## 라우팅

1. 발행은 먼저 로컬로 전달됩니다.
2. 클러스터가 켜져 있으면 라우터가 살아 있는 피어를 고릅니다:
   - retained 발행은 모든 살아 있는 피어로 가서 retained 상태가 수렴합니다;
   - 그 외 발행은 일치 필터를 광고한 피어로만 갑니다.
3. 피어는 HMAC 서명을 확인하고 메시지 ID로 중복을 제거한 뒤 다시 전달하지 않고 로컬
   라우팅 경로를 호출합니다.

```text
publisher ──► node-a ──► 로컬 구독자
                  │
                  ├── 서명된 POST /internal/cluster/publish ──► node-b
                  └── 서명된 POST /internal/cluster/publish ──► node-c
```

느리거나 도달 불가능한 피어가 로컬 전달을 막지 않습니다. 각 전달에는 자체 요청 제한
시간이 있고 실패는 디버그 수준으로 기록됩니다.

## retained 메시지

retained 발행은 모든 살아 있는 노드로 복제됩니다. 어느 노드의 새 구독자든 SUBACK 후에
retained 값을 받습니다. retained 메시지 삭제(빈 페이로드)도 복제됩니다.

## 전달 의미

- **경로별 at-most-once.** 클러스터 전달은 최선 노력 HTTP입니다. 피어가 죽어 있으면 그쪽
  으로 간 메시지는 유실됩니다. MQTT QoS는 노드를 넘지 않습니다.
- **중복 제거.** 유계 확인 메시지 캐시가 같은 클러스터 메시지의 재전달을 거부합니다.
- **세션 친화성과 인계.** 세션은 보통 연결을 받은 노드에 있습니다. 클라이언트가
  `CleanStart=false`로 다른 노드에 재연결하면 그 노드가 이전 소유자에게서 세션을
  인계받아 구독과 오프라인 큐를 복원하고 `session present = 1`을 반환합니다. 이전
  연결은 0x8E로 닫히고 유언은 발행되지 않습니다. PUBREL을 기다리는 인바운드 QoS 2는
  세션과 함께 이동하므로 클라이언트는 새 노드에서 PUBREL을 보내고 그곳에서 메시지가
  라우팅됩니다.
- **공유 구독은 클러스터에 걸칩니다.** 노드는 heartbeat에서 `$share` 그룹을 광고하고,
  각 메시지는 그룹을 호스팅하는 정확히 한 노드에 배정됩니다. 메시지 ID와 그룹 이름의
  해시로 선택하므로 다른 노드의 그룹 멤버가 중복 사본을 받지 않습니다.
- **필터 전파는 결과적입니다.** 새 필터는 다음 heartbeat에서 광고됩니다. 구독 직후의
  발행은 다음 heartbeat(기본 5초)까지 원격 구독자에게 도달하지 않을 수 있습니다.

## 보안

내부 API는 `timestamp.body`에 대한 HMAC-SHA256을 쓰며 5분의 시계 오차를 허용합니다.
타임스탬프와 서명은 `X-NextMQTT-Timestamp`와 `X-NextMQTT-Signature`로 전송됩니다.

- 시크릿은 네트워크로 전송되지 않습니다.
- 노드 트래픽이 신뢰할 수 없는 네트워크를 지나면 `AdvertiseUrl`에 HTTPS를 쓰십시오.
- ingress 또는 NetworkPolicy로 `/internal/cluster/*`를 제한하십시오.
- 시크릿 교체는 새 값으로 노드를 재시작합니다. 단일 시크릿이라 롤링 교체는 아직
  지원되지 않습니다.

대시보드 로그인은 클러스터 시크릿과 별개입니다. `NEXTMQTT_ADMIN_TOKEN_SECRET`이 없으면
각 노드가 자체 키로 Bearer 토큰에 서명하므로 한 노드가 발급한 토큰이 다른 노드에서
거부됩니다. 운영자가 한 번 로그인해 어느 노드의 대시보드나 API든 쓰려면 모든 노드에
같은 값을 설정하십시오.

## Kubernetes 세부

StatefulSet은 headless 서비스와 Pod DNS를 사용합니다:

```text
NEXTMQTT_CLUSTER__NODEID=$(POD_NAME)
NEXTMQTT_CLUSTER__ADVERTISEURL=http://$(POD_NAME).nextmqtt-headless.nextmqtt.svc.cluster.local:8080
```

seed는 처음 세 Pod 이름을 참조합니다. 초기 레플리카 수를 넘겨 확장해도 새 Pod가 seed에
접속하고 gossip으로 발견됩니다.

## 클러스터 확인

```bash
# 멤버십
curl -s http://localhost:8080/api/v1/cluster -H "Authorization: Bearer $TOKEN" | jq '.nodes'

# 노드 간 전달
dotnet run --project src/NextMqtt.Cli -- sub --port 1884 --topic cluster/# --count 1 &
dotnet run --project src/NextMqtt.Cli -- pub --port 1883 --topic cluster/test --message hello --qos 1
```

구독 후 발행 전에 최소 1 heartbeat 기다리십시오.

## 문제 해결

| 증상 | 확인 |
| --- | --- |
| 목록에 노드가 하나뿐 | 피어에서 `AdvertiseUrl` 접근 가능 여부, seed 오타, 8080 방화벽 |
| 피어 로그의 `401` | 시크릿 불일치 또는 5분을 넘는 시계 오차 |
| 노드가 alive/dead 반복 | heartbeat 간격과 타임아웃, CPU 기아 |
| 피어에 retained 누락 | 동기화 도입 전에 참여한 피어. 재시작하거나 다시 발행 |
| 원격 구독자가 메시지 놓침 | 필터 미전파. 1 heartbeat 대기 |
| 원격 전달 중복 | 메시지 ID 캐시 조기 정리. 노드 시계와 부하 확인 |
| 재시작 후 한도가 낮아 보임 | 죽은 피어를 `NodeTimeoutSeconds`의 3배까지 세고 있음 |

## 제한

- 세션 인계는 구독, 오프라인 큐, PUBREL 대기 인바운드 QoS 2를 포함합니다. 아웃바운드
  `inflight` 확인 상태는 이전되지 않으며 큐에서 재생되어 클라이언트가 중복을 볼 수
  있습니다.
- 복제본은 보통 최대 `SessionReplicaSeconds`만큼 오래됩니다. 세션 수가
  `SessionReplicaBatchSize`를 넘으면 가장 오래된 복제본은 한 바퀴만큼 지연됩니다. 파일
  저장소가 없으면 메모리에만 있어 모든 노드 재시작 시 복제본이 사라집니다.
  `Storage:Replicas:Mode`를 `File`로 두면 재시작을 넘어 유지됩니다.
- 인계와 복제 외의 노드 간 세션 영속화는 없습니다. 소유자와 모든 복제본 보유자, 그
  디스크가 함께 사라지면 세션도 사라집니다.
- 필터 광고는 전량 스냅숏이며 기본적으로 heartbeat당 10,000 필터가 상한입니다.
- 클러스터 라우팅은 링크별 at-least-once이고 중복 제거 캐시를 사용합니다. 트랜잭션
  보장이 아닙니다.
- retained 동기화는 참여 시 한 번뿐입니다. 메시에서 분할된 노드는 재연결까지 자기
  오래된 retained 값을 계속 제공합니다.

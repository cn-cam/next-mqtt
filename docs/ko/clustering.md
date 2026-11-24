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

## 라우팅

- 로컬 전달 후 retain은 모든 노드로
- 일반 publish는 일치 필터를 광고한 노드로만
- 수신 측은 HMAC 검증, 메시지 ID 중복 제거, 재전달 없음

## 전달 의미

- 노드 간 HTTP 최선 노력
- 세션은 노드 고정(다른 노드 재연결 시 새 세션)
- 공유 구독은 노드 단위
- 필터 전파는 최대 1 heartbeat 지연

## 보안

`X-NextMQTT-Timestamp`와 `X-NextMQTT-Signature`(HMAC-SHA256, 5분 허용).

- 비밀 키는 전송하지 않음
- 외부 네트워크에서는 HTTPS
- `/internal/cluster/*` 차단
- 키 교체는 재시작 필요

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

- 세션 이전/영구 저장 없음
- 전역 공유 구독 없음
- retained는 최선 노력 복제
- 필터 전체 전송, 최대 10,000

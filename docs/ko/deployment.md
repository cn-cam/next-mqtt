# 배포

NextMQTT는 멀티 아키텍처 컨테이너 이미지, Kustomize 매니페스트, Helm 차트로
제공됩니다. 같은 이미지가 단일 노드도 클러스터 멤버도 될 수 있으며, 클러스터링은
설정만으로 켜집니다.

::: warning 기본은 메모리
저장소를 설정하지 않으면 retained, 세션, 큐가 메모리에 있습니다. 상태를 유지해야 하는
롤아웃 전에 retained 저널, 세션 스냅숏, 복제본 파일 저장소를 켜십시오. inflight QoS
핸드셰이크는 항상 클라이언트가 재전송합니다.
:::

## 컨테이너 이미지

```bash
docker pull ghcr.io/cn-cam/next-mqtt:latest
```

사용 가능한 태그:

- `latest` — `main`의 최신 안정 빌드
- `0.23.1`, `0.23` — 릴리스 태그
- `sha-<commit>` — 변경되지 않는 커밋 빌드

플랫폼: `linux/amd64`, `linux/arm64`.

### 로컬 빌드

```bash
docker build -f deploy/docker/Dockerfile -t nextmqtt:local .
```

빌드는 Vue 대시보드를 컴파일하고 .NET 서버를 게시하며 번들을 `wwwroot`에 복사하고
UID 10001로 실행합니다.

### 실행

```bash
docker run -d --name nextmqtt \
  -p 1883:1883 \
  -p 8080:8080 \
  -p 8083:8083 \
  -e NEXTMQTT_ADMIN_PASSWORD='change-me-now' \
  -e NEXTMQTT_ALLOW_ANONYMOUS=false \
  ghcr.io/cn-cam/next-mqtt:latest
```

이미지에는 `/healthz`를 호출하는 `HEALTHCHECK`가 포함됩니다.

| 포트 | 용도 |
| --- | --- |
| 1883 | MQTT TCP |
| 8883 | MQTT TLS(`Server:Tls`로 활성화) |
| 8080 | API, 대시보드, 지표 |
| 8083 | MQTT WebSocket |
| 8084 | HTTPS/WSS(`Server:Http:CertificatePath`로 활성화) |

## Docker Compose

### 단일 노드

```bash
cp .env.example .env
# NEXTMQTT_ADMIN_PASSWORD와 NEXTMQTT_ADMIN_TOKEN_SECRET 편집
docker compose up -d
docker compose logs -f nextmqtt
```

### 3노드 클러스터

```bash
export NEXTMQTT_CLUSTER_SECRET='a-long-shared-secret'
docker compose -f deploy/compose/docker-compose.cluster.yml up -d
```

클러스터용 Compose는 대시보드를 **8080–8082**, HTTPS를 **8086–8088**에 게시합니다
(컨테이너 안에서는 8084 그대로). node-a는 `https://<인증서 이름>:8086/`으로
여십시오. 8084는 컨테이너 네트워크 안에서만 접근할 수 있습니다. 전체 호스트 포트
표는 `deploy/compose/README.md`를 참고하십시오.

대시보드와 REST API는 **노드 로컬**입니다. 각 노드는 자신이 보유한 세션만 보여 주며,
`NEXTMQTT_ADMIN_TOKEN_SECRET`이 모든 노드에서 같을 때만 토큰이 클러스터 전체에서
허용됩니다. 단일 공개 진입점이 필요하면 dashboard override를 추가합니다:

```bash
docker compose -f deploy/compose/docker-compose.cluster.yml \
               -f deploy/compose/docker-compose.dashboard.yml up -d
```

**8443**에서 TLS를 한 번 종단하고 `ip_hash`로 node-a/b/c에 전달하므로 브라우저가 한
노드에 고정됩니다. 운영에서는 스티키 세션이 있는 ingress나 로드 밸런서를 사용하고
8086–8088은 비공개로 두십시오.

멤버십을 확인합니다:

```bash
curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"change-me-now"}' > /tmp/login.json

TOKEN=$(jq -r .token /tmp/login.json)
curl -s http://localhost:8080/api/v1/cluster -H "Authorization: Bearer $TOKEN" | jq .
```

`localhost:1883`에서 발행하고 `localhost:1884`에서 구독하면 라우팅을 확인할 수
있습니다.

## Kubernetes(Kustomize)

매니페스트는 StatefulSet 레플리카 3개, 피어 발견용 headless 서비스, MQTT와
대시보드 서비스, Ingress, PodDisruptionBudget, HPA, NetworkPolicy를 만듭니다.

```bash
# 1. 네임스페이스와 시크릿은 직접 만든다(번들 Secret은 적용하지 않는다)
kubectl create namespace nextmqtt

kubectl -n nextmqtt create secret generic nextmqtt-secrets \
  --from-literal=admin-password='change-me-now' \
  --from-literal=token-secret="$(openssl rand -base64 32)" \
  --from-literal=cluster-secret="$(openssl rand -base64 32)"

# 2. 번들 secret.yaml을 지우거나 무시한 뒤 적용
kubectl apply -k deploy/kubernetes
```

번들 `secret.yaml`을 그대로 적용한다면 먼저 값을 바꾸십시오. 템플릿이며 사용 가능한
자격 증명이 아닙니다.

```bash
kubectl -n nextmqtt get pods,svc
kubectl -n nextmqtt port-forward svc/nextmqtt-http 8080:8080
# http://localhost:8080 열기
```

기본 Ingress 호스트는 `nextmqtt.example.com`입니다.
`deploy/kubernetes/ingress.yaml`을 편집하거나 Helm 값을 사용하십시오.

### 만들어지는 것

| 리소스 | 용도 |
| --- | --- |
| `StatefulSet/nextmqtt` | 안정적인 DNS 이름을 가진 브로커 Pod 3개 |
| `Service/nextmqtt-headless` | 피어 발견과 클러스터 주소 |
| `Service/nextmqtt-mqtt` | MQTT TCP/TLS/WS, LoadBalancer |
| `Service/nextmqtt-http` | Ingress 뒤의 대시보드/API |
| `ConfigMap/nextmqtt-config` | 비밀 아닌 설정 |
| `Secret/nextmqtt-secrets` | 관리자, 토큰, 클러스터 시크릿 |
| `Ingress` | 대시보드와 WebSocket 라우팅 |
| `PodDisruptionBudget` | 중단 시 최소 2노드 유지 |
| `HorizontalPodAutoscaler` | CPU 70%에서 3–9 Pod |
| `NetworkPolicy` | MQTT/HTTP 포트 허용 |
| `ServiceAccount` | API 토큰을 마운트하지 않음 |

클러스터 멤버 주소는 Pod DNS에서 파생됩니다:

```text
http://nextmqtt-0.nextmqtt-headless.nextmqtt.svc.cluster.local:8080
http://nextmqtt-1.nextmqtt-headless.nextmqtt.svc.cluster.local:8080
http://nextmqtt-2.nextmqtt-headless.nextmqtt.svc.cluster.local:8080
```

## Kubernetes(Helm)

```bash
helm install nextmqtt deploy/helm/next-mqtt \
  --namespace nextmqtt --create-namespace \
  --set admin.password='change-me-now' \
  --set admin.tokenSecret="$(openssl rand -base64 32)" \
  --set cluster.secret="$(openssl rand -base64 32)" \
  --set ingress.enabled=true \
  --set ingress.host=mqtt.example.com
```

자주 쓰는 재정의:

| 값 | 기본 | 설명 |
| --- | --- | --- |
| `replicaCount` | `3` | 초기 클러스터 크기 |
| `image.tag` | Chart appVersion | 이미지 태그 |
| `auth.allowAnonymous` | `true` | 운영에서는 끄기 |
| `acl.defaultPolicy` | `Allow` | 운영에서는 `Deny` |
| `cluster.enabled` | `true` | 클러스터링 켜기/끄기 |
| `mqtt.type` | `LoadBalancer` | MQTT 서비스 유형 |
| `ingress.enabled` | `false` | 대시보드 노출 |
| `autoscaling.enabled` | `true` | HPA 활성화 |
| `podDisruptionBudget.enabled` | `true` | 2노드 유지 |
| `existingSecret` | `""` | 세 키를 가진 자체 시크릿 사용 |

`existingSecret`을 설정하지 않으면 Helm이 `admin-password`, `token-secret`,
`cluster-secret` 키를 가진 시크릿을 만듭니다.

### 업그레이드

```bash
helm upgrade nextmqtt deploy/helm/next-mqtt -n nextmqtt --reuse-values \
  --set image.tag=0.23.1
```

파일 저장소를 마운트하고 복제본이 최신이면 롤링 재시작 후에도 영속 세션이 남습니다.
없으면 메모리 세션이 사라집니다. 노드를 하나씩 드레인하고 클라이언트가 재연결하는지
확인하십시오.

## 운영 체크리스트

- [ ] `NEXTMQTT_ALLOW_ANONYMOUS=false`
- [ ] `Acl:DefaultPolicy=Deny`와 명시적 규칙
- [ ] 강한 `NEXTMQTT_ADMIN_PASSWORD`
- [ ] 모든 노드가 공유하는 고정 `NEXTMQTT_ADMIN_TOKEN_SECRET`
- [ ] 16자 이상의 고유한 `Cluster:Secret`
- [ ] MQTT TLS와 대시보드 HTTPS
- [ ] Ingress를 신뢰 네트워크 또는 SSO 뒤로 제한
- [ ] NetworkPolicy로 `/internal/cluster/*`를 클라이언트에서 차단
- [ ] `Storage:Retained:Mode=File`, `Storage:Session:Mode=File`,
      `Storage:Replicas:Mode=File`을 영속 볼륨에 배치
- [ ] 트래픽에 맞춘 리소스와 HPA
- [ ] 모니터링이 `/metrics`를 수집하고 드롭과 인증 실패에 알림
- [ ] 설정과 시크릿 백업

## 검증

```bash
# API
curl -fsS http://localhost:8080/healthz

# MQTT 왕복
dotnet run --project src/NextMqtt.Cli -- sub --topic smoke/# --count 1 &
dotnet run --project src/NextMqtt.Cli -- pub --topic smoke/test --message ok

# 클러스터
curl -fsS http://localhost:8080/api/v1/cluster -H "Authorization: Bearer $TOKEN"
```

## 제거

```bash
helm uninstall nextmqtt -n nextmqtt
# 또는
kubectl delete -k deploy/kubernetes
kubectl delete namespace nextmqtt
```

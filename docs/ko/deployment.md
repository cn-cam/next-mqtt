# 배포

::: warning 상태는 메모리
세션, 큐, retained는 재시작 시 사라집니다. 영구 저장은 로드맵을 참고하세요.
:::

## 컨테이너

```bash
docker pull ghcr.io/cn-cam/next-mqtt:latest
# 또는 Docker Hub
docker pull ooooooooocam/next-mqtt:latest
```

```bash
docker run -d --name nextmqtt \
  -p 1883:1883 -p 8080:8080 -p 8083:8083 \
  -e NEXTMQTT_ADMIN_PASSWORD='change-me-now' \
  -e NEXTMQTT_ALLOW_ANONYMOUS=false \
  ghcr.io/cn-cam/next-mqtt:latest
```

| 포트 | 용도 |
| --- | --- |
| 1883 | MQTT |
| 8883 | MQTT TLS |
| 8080 | API / UI |
| 8083 | WebSocket |
| 8084 | HTTPS / WSS |

## Compose

```bash
cp .env.example .env
docker compose up -d
```

3노드:

```bash
export NEXTMQTT_CLUSTER_SECRET='a-long-shared-secret'
docker compose -f deploy/compose/docker-compose.cluster.yml up -d
```

## Kubernetes (Kustomize)

```bash
kubectl create namespace nextmqtt

kubectl -n nextmqtt create secret generic nextmqtt-secrets \
  --from-literal=admin-password='change-me-now' \
  --from-literal=token-secret="$(openssl rand -base64 32)" \
  --from-literal=cluster-secret="$(openssl rand -base64 32)"

kubectl apply -k deploy/kubernetes
kubectl -n nextmqtt port-forward svc/nextmqtt-http 8080:8080
```

저장소의 `secret.yaml`은 템플릿입니다. 값을 반드시 변경하세요.

## Helm

```bash
helm install nextmqtt deploy/helm/next-mqtt \
  --namespace nextmqtt --create-namespace \
  --set admin.password='change-me-now' \
  --set admin.tokenSecret="$(openssl rand -base64 32)" \
  --set cluster.secret="$(openssl rand -base64 32)" \
  --set ingress.enabled=true \
  --set ingress.host=mqtt.example.com
```

주요 값:

| 값 | 기본 |
| --- | --- |
| `replicaCount` | 3 |
| `auth.allowAnonymous` | true |
| `acl.defaultPolicy` | Allow |
| `cluster.enabled` | true |
| `mqtt.type` | LoadBalancer |
| `ingress.enabled` | false |
| `autoscaling.enabled` | true |

## 운영 체크리스트

- 익명 비활성화, ACL 기본 거부
- 강한 관리 비밀번호, 고정 토큰 키
- 클러스터 키 16자 이상
- MQTT TLS, UI HTTPS
- Ingress/IP 제한
- `/internal/cluster/*` 차단
- 리소스 제한, HPA, 모니터링
- 설정/비밀 정보 백업

## 검증

```bash
curl -fsS http://localhost:8080/healthz

nextmqtt sub --topic smoke/# --count 1 &
nextmqtt pub --topic smoke/test --message ok

curl -fsS http://localhost:8080/api/v1/cluster \
  -H "Authorization: Bearer $TOKEN"
```

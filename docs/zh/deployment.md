# 部署

NextMQTT 提供多架构容器镜像、Kustomize 清单与 Helm Chart。同一个镜像既可单机
运行，也可作为集群成员；集群完全由配置开启。

::: warning 默认在内存中
未配置存储时，会话、离线队列与保留消息都在内存中。需要在滚动升级中保留状态时，
请先启用保留日志、会话快照与副本文件存储。inflight QoS 握手始终由客户端重传。
:::

## 容器镜像

```bash
docker pull ghcr.io/cn-cam/next-mqtt:latest
```

标签：

- `latest`：`main` 分支最新稳定构建
- `0.21.0` / `0.21`：发布标签
- `sha-<commit>`：不可变提交构建

平台：`linux/amd64`、`linux/arm64`。

本地构建：

```bash
docker build -f deploy/docker/Dockerfile -t nextmqtt:local .
```

构建阶段会编译 Vue 控制台、发布 .NET 服务，并把前端 bundle 复制到 `wwwroot`；
运行时以 UID 10001 非 root 用户启动。

## Docker 运行

```bash
docker run -d --name nextmqtt \
  -p 1883:1883 \
  -p 8080:8080 \
  -p 8083:8083 \
  -e NEXTMQTT_ADMIN_PASSWORD='change-me-now' \
  -e NEXTMQTT_ALLOW_ANONYMOUS=false \
  ghcr.io/cn-cam/next-mqtt:latest
```

镜像内置基于 `/healthz` 的 `HEALTHCHECK`。

| 端口 | 用途 |
| --- | --- |
| 1883 | MQTT TCP |
| 8883 | MQTT TLS（需启用） |
| 8080 | API、控制台、指标 |
| 8083 | MQTT WebSocket |
| 8084 | HTTPS/WSS（需证书） |

## Docker Compose

单节点：

```bash
cp .env.example .env
# 修改管理密码与令牌密钥
docker compose up -d
```

三节点集群：

```bash
export NEXTMQTT_CLUSTER_SECRET='a-long-shared-secret'
docker compose -f deploy/compose/docker-compose.cluster.yml up -d
```

验证成员：

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"change-me-now"}' | jq -r .token)

curl -s http://localhost:8080/api/v1/cluster \
  -H "Authorization: Bearer $TOKEN" | jq .
```

## Kubernetes（Kustomize）

清单包含 3 副本 StatefulSet、headless Service、MQTT/HTTP Service、Ingress、PDB、
HPA 与 NetworkPolicy。

```bash
kubectl create namespace nextmqtt

# 不要直接使用仓库里的占位 Secret
kubectl -n nextmqtt create secret generic nextmqtt-secrets \
  --from-literal=admin-password='change-me-now' \
  --from-literal=token-secret="$(openssl rand -base64 32)" \
  --from-literal=cluster-secret="$(openssl rand -base64 32)"

kubectl apply -k deploy/kubernetes

kubectl -n nextmqtt get pods,svc
kubectl -n nextmqtt port-forward svc/nextmqtt-http 8080:8080
```

默认 Ingress 域名是 `nextmqtt.example.com`，请修改
`deploy/kubernetes/ingress.yaml`。

集群成员地址由 Pod DNS 推导：

```text
http://nextmqtt-0.nextmqtt-headless.nextmqtt.svc.cluster.local:8080
http://nextmqtt-1.nextmqtt-headless.nextmqtt.svc.cluster.local:8080
http://nextmqtt-2.nextmqtt-headless.nextmqtt.svc.cluster.local:8080
```

## Kubernetes（Helm）

```bash
helm install nextmqtt deploy/helm/next-mqtt \
  --namespace nextmqtt --create-namespace \
  --set admin.password='change-me-now' \
  --set admin.tokenSecret="$(openssl rand -base64 32)" \
  --set cluster.secret="$(openssl rand -base64 32)" \
  --set ingress.enabled=true \
  --set ingress.host=mqtt.example.com
```

未设置 `existingSecret` 时，Chart 会创建包含 `admin-password`、`token-secret`、
`cluster-secret` 三个键的 Secret。

常用覆盖项：

| 值 | 默认 | 说明 |
| --- | --- | --- |
| `replicaCount` | `3` | 初始集群规模 |
| `image.tag` | Chart appVersion | 镜像标签 |
| `auth.allowAnonymous` | `true` | 生产请关闭 |
| `acl.defaultPolicy` | `Allow` | 生产建议改为 `Deny` |
| `cluster.enabled` | `true` | 是否启用集群 |
| `mqtt.type` | `LoadBalancer` | MQTT Service 类型 |
| `ingress.enabled` | `false` | 是否暴露控制台 |
| `autoscaling.enabled` | `true` | 是否启用 HPA |
| `existingSecret` | `""` | 使用自建 Secret |

## 生产检查清单

- [ ] 关闭匿名访问（`NEXTMQTT_ALLOW_ANONYMOUS=false`）
- [ ] ACL 默认拒绝，并配置明确规则
- [ ] 设置强管理密码
- [ ] 所有节点使用相同的 `NEXTMQTT_ADMIN_TOKEN_SECRET`
- [ ] 集群密钥至少 16 字符且各节点一致
- [ ] MQTT 启用 TLS，控制台使用 HTTPS
- [ ] Ingress 限制来源或接入 SSO
- [ ] NetworkPolicy 阻止客户端访问 `/internal/cluster/*`
- [ ] 配置资源限制、HPA 与监控告警
- [ ] 备份配置与密钥

## 验证

```bash
curl -fsS http://localhost:8080/healthz

dotnet run --project src/NextMqtt.Cli -- sub --topic smoke/# --count 1 &
dotnet run --project src/NextMqtt.Cli -- pub --topic smoke/test --message ok

curl -fsS http://localhost:8080/api/v1/cluster \
  -H "Authorization: Bearer $TOKEN"
```

## 卸载

```bash
helm uninstall nextmqtt -n nextmqtt
# 或
kubectl delete -k deploy/kubernetes
kubectl delete namespace nextmqtt
```

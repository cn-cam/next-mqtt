# TLS

## 证书格式

- **PFX/PKCS#12**：设置 `CertificatePath` 与 `CertificatePassword`
- **PEM**：`CertificatePath` 指向证书，`CertificateKeyPath` 指向私钥；私钥路径留空时自动加载同目录同名 `.key`

PEM 可以包含完整证书链（叶子 + 中间证书），中间证书会自动下发给客户端，公网 CA（如 Let's Encrypt）通常要求如此。

`Domain` 可选：设置后启动时校验证书是否覆盖该域名，不覆盖只告警，不会拒绝连接。

```text
/certs/server.pfx
/certs/server.pem        叶子 + 中间证书
/certs/server.key        私钥
```

## MQTT over TLS

```json
{
  "Server": {
    "Tls": {
      "Enabled": true,
      "Host": "0.0.0.0",
      "Port": 8883,
      "CertificatePath": "/certs/server.pem",
      "CertificateKeyPath": "/certs/server.key",
      "Domain": "mqtt.example.com"
    }
  }
}
```

客户端连接 `mqtts://mqtt.example.com:8883`。支持 TLS 1.2 / 1.3。

## 控制台 HTTPS 与 WSS

```json
{
  "Server": {
    "Http": {
      "Port": 8080,
      "WebSocketPort": 8083,
      "TlsPort": 8084,
      "CertificatePath": "/certs/server.pem",
      "CertificateKeyPath": "/certs/server.key",
      "Domain": "mqtt.example.com"
    }
  }
}
```

- 控制台：`https://host:8084/`
- WSS：`wss://host:8084/mqtt`

## 自签名证书（仅测试）

```bash
openssl req -x509 -newkey rsa:2048 -sha256 -days 365 -nodes \
  -keyout server.key -out server.crt \
  -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"

openssl pkcs12 -export -out server.pfx -inkey server.key -in server.crt \
  -passout pass:change-me
```

## 反向代理

- 转发 `X-Forwarded-For`、`X-Forwarded-Proto`
- `/mqtt` 必须允许 WebSocket Upgrade
- 读超时高于 MQTT keep-alive
- `/internal/cluster/*` 只允许节点间访问

## 容器

```bash
docker run -d --name nextmqtt \
  -p 1883:1883 -p 8883:8883 -p 8080:8080 \
  -v "$PWD/certs:/certs:ro" \
  -e NEXTMQTT_SERVER__TLS__ENABLED=true \
  -e NEXTMQTT_SERVER__TLS__CERTIFICATEPATH=/certs/server.pfx \
  -e NEXTMQTT_SERVER__TLS__CERTIFICATEPASSWORD=change-me \
  -e NEXTMQTT_ADMIN_PASSWORD='change-me-now' \
  ghcr.io/cn-cam/next-mqtt:latest
```

镜像以 UID 10001 运行，确保证书可读。

## Kubernetes

```bash
kubectl create secret tls nextmqtt-mqtt-tls \
  --cert=server.crt --key=server.key -n nextmqtt
```

挂载到 `/certs`，并设置：

```text
NEXTMQTT_SERVER__TLS__ENABLED=true
NEXTMQTT_SERVER__TLS__CERTIFICATEPATH=/certs/tls.crt
```

PEM 加载器会查找同目录的 `tls.key`。

## 排错

| 现象 | 检查 |
| --- | --- |
| 启动报未配置证书 | 设置路径或关闭 TLS |
| 文件不存在 | 挂载路径与 UID 10001 权限 |
| PFX 密码错误 | 核对 `CertificatePassword` |
| 主机名校验失败 | 证书 SAN 是否包含域名/IP |
| WebSocket 升级失败 | 代理是否转发 Upgrade |

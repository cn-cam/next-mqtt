# TLS

NextMQTT 可以自己为 MQTT 和控制台加密，也可以放在负责终止 TLS 的反向代理之后。

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

- 控制台：`https://mqtt.example.com:8084/`
- WSS：`wss://mqtt.example.com:8084/mqtt`

除非关闭明文 HTTP 端口，Kestrel 仍会在这些端口监听；用防火墙或反向代理限制它们。

### 自签名证书（仅测试）

```bash
openssl req -x509 -newkey rsa:2048 -sha256 -days 365 -nodes \
  -keyout server.key -out server.crt \
  -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"

openssl pkcs12 -export -out server.pfx -inkey server.key -in server.crt \
  -passout pass:change-me
```

不要把自签名证书用于生产。请使用自有 PKI 或 ACME 客户端签发的证书。

## 反向代理

在 ingress 终止 TLS 往往更简单：

```nginx
server {
    listen 443 ssl http2;
    server_name mqtt.example.com;

    ssl_certificate     /etc/ssl/certs/server.crt;
    ssl_certificate_key /etc/ssl/private/server.key;

    location / {
        proxy_pass http://nextmqtt-http:8080;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }

    location /mqtt {
        proxy_pass http://nextmqtt-http:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 3600s;
    }
}
```

原始 MQTT/TLS 需要把 TCP 8883 转发到 MQTT 服务；HTTP 反向代理无法承载原始 MQTT。

- 转发 `X-Forwarded-For`、`X-Forwarded-Proto`
- `/mqtt` 必须允许 WebSocket Upgrade
- 读超时高于 MQTT keep-alive
- `/internal/cluster/*` 只允许节点间访问

## 容器

以只读方式挂载证书：

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

```yaml
spec:
  template:
    spec:
      containers:
        - name: broker
          env:
            - name: NEXTMQTT_SERVER__TLS__ENABLED
              value: "true"
            - name: NEXTMQTT_SERVER__TLS__CERTIFICATEPATH
              value: /certs/tls.crt
          volumeMounts:
            - name: mqtt-tls
              mountPath: /certs
              readOnly: true
      volumes:
        - name: mqtt-tls
          secret:
            secretName: nextmqtt-mqtt-tls
```

PEM 加载器会查找 `tls.crt` 同目录的 `tls.key`，与 `kubectl create secret tls`
生成的文件布局一致。

## 排错

| 现象 | 检查 |
| --- | --- |
| `TLS listener is enabled but CertificatePath is not configured` | 设置路径或关闭 TLS |
| 启动时 `FileNotFoundException` | 挂载路径与权限（UID 10001） |
| `Certificate ... has no private key` | 设置 `CertificateKeyPath`，私钥不在证书旁边 |
| `The private key does not match any certificate in the file` | PEM 与私钥文件不是同一对 |
| 证书密码错误 | PFX 密码是否与 `CertificatePassword` 一致 |
| 客户端主机名校验失败 | 证书 SAN 是否包含 DNS 名或 IP |
| `HTTPS certificate does not cover ...` 警告 | SAN 未包含 `Domain`，客户端会拒绝 |
| `platform does not support ephemeral keys` | 仅旧版本；现在会重新导入 PEM 私钥 |
| WebSocket 升级失败 | 代理是否转发 `Upgrade` 与 `Connection` |
| 控制台登录反复跳转 | 重启后令牌密钥变了，设置固定的令牌密钥 |

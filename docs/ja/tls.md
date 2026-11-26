# TLS

NextMQTT は MQTT とダッシュボードを直接暗号化することも、TLS を終端するリバース
プロキシの背後に置くこともできます。

## 証明書

- **PFX/PKCS#12**：`CertificatePath` と `CertificatePassword`
- **PEM**：`CertificatePath` に証明書、`CertificateKeyPath` に秘密鍵。鍵パスを省略すると同ディレクトリの同名 `.key` を使用
- PEM に中間証明書を含めると、そのままクライアントへ送信されます
- `Domain` を設定すると起動時に証明書がその名前をカバーするか確認し、不足していれば警告します（接続は拒否しません）

```text
/certs/server.pfx
/certs/server.pem        リーフ + 中間証明書
/certs/server.key        秘密鍵
```

## MQTT TLS

```json
{
  "Server": {
    "Tls": {
      "Enabled": true,
      "Port": 8883,
      "CertificatePath": "/certs/server.pem",
      "CertificateKeyPath": "/certs/server.key",
      "Domain": "mqtt.example.com"
    }
  }
}
```

`mqtts://mqtt.example.com:8883` で接続。TLS 1.2/1.3 対応。

## 管理 UI / WSS

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

- UI: `https://mqtt.example.com:8084/`
- WSS: `wss://mqtt.example.com:8084/mqtt`

平文の HTTP ポートを無効にしない限り Kestrel はそちらでも待ち受けます。ファイアウォール
かリバースプロキシで制限してください。

## 自己署名（テストのみ）

```bash
openssl req -x509 -newkey rsa:2048 -sha256 -days 365 -nodes \
  -keyout server.key -out server.crt \
  -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"

openssl pkcs12 -export -out server.pfx -inkey server.key -in server.crt \
  -passout pass:change-me
```

自己署名証明書を本番へ出さないでください。PKI か ACME クライアントの証明書を使います。

## リバースプロキシ

ingress で TLS を終端すると運用が簡単なことがあります：

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

生の MQTT/TLS は TCP 8883 を MQTT サービスへ転送します。HTTP リバースプロキシは
生の MQTT を運べません。`X-Forwarded-For` / `X-Forwarded-Proto` の転送、WebSocket
Upgrade の許可、keep-alive より長い read timeout、`/internal/cluster/*` の非公開化も
忘れないでください。

## コンテナ

証明書は読み取り専用でマウントします：

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

UID 10001 で動作するため、証明書の読み取り権限を確認してください。

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

PEM ローダーは `tls.crt` の隣の `tls.key` を探します。これは
`kubectl create secret tls` が作るファイル構成と同じです。

## トラブルシューティング

| 症状 | 確認 |
| --- | --- |
| `TLS listener is enabled but CertificatePath is not configured` | パスを設定するか TLS を無効化 |
| 起動時の `FileNotFoundException` | マウントパスと権限（UID 10001） |
| `Certificate ... has no private key` | `CertificateKeyPath` を設定。鍵が証明書の隣にない |
| `The private key does not match any certificate in the file` | PEM と鍵ファイルが別の鍵ペア |
| 証明書パスワードエラー | PFX のパスワードが `CertificatePassword` と一致するか |
| クライアントのホスト名検証失敗 | 証明書 SAN に DNS 名または IP が含まれるか |
| `HTTPS certificate does not cover ...` 警告 | SAN に `Domain` がない。クライアントは拒否する |
| `platform does not support ephemeral keys` | 古いビルドのみ。現在は PEM 鍵を再インポートする |
| WebSocket 昇格失敗 | プロキシが `Upgrade` と `Connection` を転送しているか |
| ダッシュボードのログインがループする | 再起動でトークン鍵が変わる。固定の秘密鍵を設定 |

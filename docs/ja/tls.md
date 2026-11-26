# TLS

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

- UI: `https://host:8084/`
- WSS: `wss://host:8084/mqtt`

## 自己署名（テストのみ）

```bash
openssl req -x509 -newkey rsa:2048 -sha256 -days 365 -nodes \
  -keyout server.key -out server.crt \
  -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"

openssl pkcs12 -export -out server.pfx -inkey server.key -in server.crt \
  -passout pass:change-me
```

## リバースプロキシ

- `X-Forwarded-For` / `X-Forwarded-Proto` を転送
- `/mqtt` の WebSocket Upgrade を許可
- read timeout を keep-alive より長く
- `/internal/cluster/*` を外部公開しない

## コンテナ

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

`/certs` にマウントし、`NEXTMQTT_SERVER__TLS__CERTIFICATEPATH=/certs/tls.crt` を設定。
PEM ローダーは同ディレクトリの `tls.key` を使用します。

## トラブルシューティング

| 症状 | 確認 |
| --- | --- |
| 起動時に証明書未設定 | パス設定または TLS 無効化 |
| ファイルが見つからない | マウントと UID 10001 権限 |
| PFX パスワードエラー | `CertificatePassword` |
| ホスト名検証失敗 | 証明書 SAN |
| WebSocket 昇格失敗 | Upgrade ヘッダー |

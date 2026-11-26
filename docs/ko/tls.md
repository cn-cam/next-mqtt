# TLS

NextMQTT는 MQTT와 대시보드를 직접 암호화하거나, TLS를 종단하는 리버스 프록시 뒤에 둘
수 있습니다.

## 인증서

- **PFX/PKCS#12**: `CertificatePath` + `CertificatePassword`
- **PEM**: `CertificatePath`에 인증서, `CertificateKeyPath`에 개인 키. 키 경로를 생략하면 같은 디렉터리의 같은 이름 `.key`를 사용
- PEM에 중간 인증서를 함께 넣으면 클라이언트로 그대로 전송됩니다
- `Domain`을 설정하면 시작 시 인증서가 그 이름을 포함하는지 확인하고, 아니면 경고합니다(연결을 거부하지는 않음)

```text
/certs/server.pfx
/certs/server.pem        리프 + 중간 인증서
/certs/server.key        개인 키
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

`mqtts://mqtt.example.com:8883`으로 접속합니다. TLS 1.2/1.3 지원.

## 관리 UI / WSS

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

평문 HTTP 포트를 끄지 않으면 Kestrel은 그 포트에서도 계속 수신합니다. 방화벽이나
리버스 프록시로 제한하십시오.

### 자체 서명(테스트 전용)

```bash
openssl req -x509 -newkey rsa:2048 -sha256 -days 365 -nodes \
  -keyout server.key -out server.crt \
  -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"

openssl pkcs12 -export -out server.pfx -inkey server.key -in server.crt \
  -passout pass:change-me
```

자체 서명 인증서를 운영에 사용하지 마십시오. PKI 또는 ACME 클라이언트의 인증서를
사용하십시오.

## 리버스 프록시

ingress에서 TLS를 종단하면 운영이 더 단순할 때가 많습니다:

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

원시 MQTT/TLS는 TCP 8883을 MQTT 서비스로 전달합니다. HTTP 리버스 프록시는 원시
MQTT를 운반할 수 없습니다. `X-Forwarded-For`/`X-Forwarded-Proto` 전달, WebSocket
Upgrade 허용, keep-alive보다 긴 read timeout, `/internal/cluster/*` 차단도
잊지 마십시오.

## 컨테이너

인증서는 읽기 전용으로 마운트합니다:

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

UID 10001로 실행되므로 인증서 읽기 권한을 확인하세요.

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

PEM 로더는 `tls.crt` 옆의 `tls.key`를 찾습니다. `kubectl create secret tls`가
만드는 파일 구성과 같습니다.

## 문제 해결

| 증상 | 확인 |
| --- | --- |
| `TLS listener is enabled but CertificatePath is not configured` | 경로를 설정하거나 TLS 비활성화 |
| 시작 시 `FileNotFoundException` | 마운트 경로와 권한(UID 10001) |
| `Certificate ... has no private key` | `CertificateKeyPath` 설정. 키가 인증서 옆에 없음 |
| `The private key does not match any certificate in the file` | PEM과 키 파일이 다른 키 쌍 |
| 인증서 비밀번호 오류 | PFX 비밀번호가 `CertificatePassword`와 일치하는지 |
| 클라이언트 호스트 이름 검증 실패 | 인증서 SAN에 DNS 이름 또는 IP 포함 여부 |
| `HTTPS certificate does not cover ...` 경고 | SAN에 `Domain`이 없음. 클라이언트가 거부 |
| `platform does not support ephemeral keys` | 이전 빌드 전용. 지금은 PEM 키를 다시 가져옴 |
| WebSocket 업그레이드 실패 | 프록시가 `Upgrade`와 `Connection`을 전달하는지 |
| 대시보드 로그인이 반복됨 | 재시작 시 토큰 키가 바뀜. 고정 시크릿 설정 |

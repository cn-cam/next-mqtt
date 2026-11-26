# TLS

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

- UI: `https://host:8084/`
- WSS: `wss://host:8084/mqtt`

## 자체 서명(테스트 전용)

```bash
openssl req -x509 -newkey rsa:2048 -sha256 -days 365 -nodes \
  -keyout server.key -out server.crt \
  -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"

openssl pkcs12 -export -out server.pfx -inkey server.key -in server.crt \
  -passout pass:change-me
```

## 리버스 프록시

- `X-Forwarded-For`, `X-Forwarded-Proto` 전달
- `/mqtt` WebSocket Upgrade 허용
- read timeout을 keep-alive보다 길게
- `/internal/cluster/*` 외부 차단

## 컨테이너

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

`/certs`에 마운트하고 `NEXTMQTT_SERVER__TLS__CERTIFICATEPATH=/certs/tls.crt`를
설정합니다. PEM 로더는 같은 디렉터리의 `tls.key`를 사용합니다.

## 문제 해결

| 증상 | 확인 |
| --- | --- |
| 시작 시 인증서 미설정 | 경로 설정 또는 TLS 비활성화 |
| 파일 없음 | 마운트 경로와 UID 10001 권한 |
| PFX 비밀번호 오류 | `CertificatePassword` |
| 호스트 이름 검증 실패 | 인증서 SAN |
| WebSocket 업그레이드 실패 | Upgrade 헤더 |

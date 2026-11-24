# TLS

NextMQTT can encrypt MQTT and the dashboard directly, or run behind a reverse
proxy that terminates TLS.

## Certificate formats

The same loader is used for MQTT/TLS and Kestrel HTTPS:

- **PFX / PKCS#12** — set `CertificatePath` to the `.pfx`/`.p12` file and
  `CertificatePassword` if needed.
- **PEM** — set `CertificatePath` to the certificate file. If a matching `.key`
  file exists next to it, it is used automatically.

```text
/certs/server.pfx
/certs/server.pem
/certs/server.key
```

## MQTT over TLS

```json
{
  "Server": {
    "Tls": {
      "Enabled": true,
      "Host": "0.0.0.0",
      "Port": 8883,
      "CertificatePath": "/certs/server.pfx",
      "CertificatePassword": "change-me"
    }
  }
}
```

Clients connect to `mqtts://host:8883` or use TLS on port 8883. TLS 1.2 and 1.3
are enabled.

### Self-signed certificate for testing

```bash
openssl req -x509 -newkey rsa:2048 -sha256 -days 365 -nodes \
  -keyout server.key -out server.crt \
  -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"

openssl pkcs12 -export -out server.pfx -inkey server.key -in server.crt \
  -passout pass:change-me
```

Never ship a self-signed certificate to production. Use certificates from your
PKI or an ACME client.

## Dashboard HTTPS and WSS

Set a certificate under `Server:Http` to enable the HTTPS endpoint (default port
8084):

```json
{
  "Server": {
    "Http": {
      "Port": 8080,
      "WebSocketPort": 8083,
      "TlsPort": 8084,
      "CertificatePath": "/certs/server.pfx",
      "CertificatePassword": "change-me"
    }
  }
}
```

- Dashboard: `https://host:8084/`
- Secure WebSocket MQTT: `wss://host:8084/mqtt`

Kestrel still listens on the plain HTTP ports unless you disable them; restrict
them with firewall rules or a reverse proxy.

## Reverse proxy

Terminating TLS at an ingress is often simpler:

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

For raw MQTT/TLS, forward TCP port 8883 to the MQTT service; HTTP reverse proxies
cannot carry raw MQTT.

## Containers

Mount certificates read-only:

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

The image runs as UID/GID 10001; make sure the certificate files are readable.

## Kubernetes

Create a TLS secret and mount it:

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
            - name: NEXTMQTT_SERVER__TLS__CERTIFICATEKEYPATH
              value: /certs/tls.key
          volumeMounts:
            - name: mqtt-tls
              mountPath: /certs
              readOnly: true
      volumes:
        - name: mqtt-tls
          secret:
            secretName: nextmqtt-mqtt-tls
```

`CertificateKeyPath` is reserved for future use; PEM loading looks for a `.key`
file next to the certificate. For a Kubernetes secret named `tls.crt`/`tls.key`,
mount both and point `CertificatePath` at `/certs/tls.crt` after renaming the
key or use a PFX.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| `TLS listener is enabled but CertificatePath is not configured` | Set the path or disable TLS |
| `FileNotFoundException` on startup | Mount path and file permissions (UID 10001) |
| Certificate password error | PFX password matches `CertificatePassword` |
| Clients fail hostname validation | Certificate SAN includes the DNS name or IP |
| WebSocket upgrade fails | Proxy forwards `Upgrade` and `Connection` headers |
| Dashboard loops on login | Token secret changes on restart; set a stable secret |

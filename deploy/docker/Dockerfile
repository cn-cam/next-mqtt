# syntax=docker/dockerfile:1.7

# ---------------------------------------------------------------------------
# Dashboard bundle
# ---------------------------------------------------------------------------
FROM node:24-alpine AS web

WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY web/ ./
RUN npm run build

# ---------------------------------------------------------------------------
# Broker publish
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props NextMqtt.slnx ./
COPY src/NextMqtt.Core/NextMqtt.Core.csproj src/NextMqtt.Core/
COPY src/NextMqtt.Broker/NextMqtt.Broker.csproj src/NextMqtt.Broker/
COPY src/NextMqtt.Server/NextMqtt.Server.csproj src/NextMqtt.Server/

RUN dotnet restore src/NextMqtt.Server/NextMqtt.Server.csproj

COPY src/ src/
RUN dotnet publish src/NextMqtt.Server/NextMqtt.Server.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

COPY --from=web /src/web/dist /app/publish/wwwroot

# ---------------------------------------------------------------------------
# Runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

LABEL org.opencontainers.image.title="NextMQTT" \
      org.opencontainers.image.description="Cloud-native MQTT 3.1.1/5.0 broker with management API and dashboard" \
      org.opencontainers.image.source="https://github.com/cn-cam/next-mqtt" \
      org.opencontainers.image.url="https://github.com/cn-cam/next-mqtt" \
      org.opencontainers.image.licenses="Apache-2.0" \
      org.opencontainers.image.vendor="NextMQTT contributors"

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 nextmqtt \
    && useradd --uid 10001 --gid nextmqtt --create-home --shell /usr/sbin/nologin nextmqtt \
    && mkdir -p /app/data \
    && chown -R nextmqtt:nextmqtt /app

WORKDIR /app
COPY --from=build --chown=nextmqtt:nextmqtt /app/publish ./

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    NEXTMQTT_SERVER__TCP__HOST=0.0.0.0 \
    NEXTMQTT_SERVER__TCP__PORT=1883 \
    NEXTMQTT_SERVER__HTTP__PORT=8080 \
    NEXTMQTT_SERVER__HTTP__WEBSOCKETPORT=8083

EXPOSE 1883 8883 8080 8083 8084

USER nextmqtt

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "NextMqtt.Server.dll"]

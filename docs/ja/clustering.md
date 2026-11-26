# クラスタ

コーディネータ不要のメッシュ。各ノードがセッションを所有し、購読フィルタに基づいて
publish を転送します。

## 設定

```json
{
  "Cluster": {
    "Enabled": true,
    "NodeId": "node-a",
    "AdvertiseUrl": "http://node-a:8080",
    "Seeds": ["http://node-b:8080", "http://node-c:8080"],
    "Secret": "16 文字以上の共有キー",
    "HeartbeatSeconds": 5,
    "NodeTimeoutSeconds": 20
  }
}
```

```bash
NEXTMQTT_CLUSTER__ENABLED=true
NEXTMQTT_CLUSTER__NODEID=node-a
NEXTMQTT_CLUSTER__ADVERTISEURL=http://node-a:8080
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
NEXTMQTT_CLUSTER__SECRET=shared-secret-at-least-16-chars
```

要件：8080 到達可能、時刻差 5 分以内、NodeId 一意、Secret 一致。

## メンバーシップ

- heartbeat に NodeId、URL、通常購読フィルタを含める
- 受信側は送信者をマージし、自身のメンバー表を返す
- 1 つの seed から gossip で全体を発見
- タイムアウトで suspect、3 倍で削除

`GET /api/v1/cluster` と UI で確認できます。

## 自己修復

- **停止ノードは削除される**：ノードタイムアウト後は suspect、3 倍の時間でメンバー表から削除され、そのフィルタへの転送も止まります。
- **制限は即座に再計算**：参加・離脱の時点で接続・publish・トピックの割当を再配分します。
- **実行中の参加**：再起動・追加ノードは 1 つのシードだけでよく、最初の heartbeat 応答からメンバー表を学びます。
- **retained の追いつき**：初回参加時にピアから retained を取得し、元のタイムスタンプを保つため last-write-wins が維持されます。

セッションはクライアントに追従します。`CleanStart=false` で別ノードへ再接続すると、
そのノードが前の所有者からセッションを引き継ぎ、サブスクリプションとオフラインキューを復元します。
`CleanStart=true` は従来どおり新規セッションです。旧接続は 0x8E で閉じ、遺言は発行しません。

## ルーティング

- ローカル配信後、retain は全ノードへ
- 通常 publish は一致フィルタを広告したノードのみ
- 受信側は HMAC 検証、メッセージ ID で重複排除、再転送しない

## 配信セマンティクス

- ノード間は HTTP ベストエフォート
- セッションは `CleanStart=false` の再接続時に引き継がれる（入站 QoS 2 と inflight は対象外）
- 共有サブスクリプションはクラスタ全体で 1 ノードのみ
- フィルタ伝播は最大 1 heartbeat 遅延

## セキュリティ

`X-NextMQTT-Timestamp` と `X-NextMQTT-Signature`（HMAC-SHA256、5 分許容）。

- 秘密鍵は送信しない
- 外部ネットワークでは HTTPS
- `/internal/cluster/*` をブロック
- 鍵ローテーションは再起動が必要

ダッシュボードのログインはクラスタ秘密鍵とは別です。`NEXTMQTT_ADMIN_TOKEN_SECRET`
が未設定の場合、各ノードは独自の一時鍵でトークンに署名するため、あるノードが
発行したトークンは別ノードでは拒否されます。1 回のログインで任意のノードの
ダッシュボードや API を使うには、全ノードに同じ値を設定してください。

## Kubernetes

```text
NEXTMQTT_CLUSTER__NODEID=$(POD_NAME)
NEXTMQTT_CLUSTER__ADVERTISEURL=http://$(POD_NAME).nextmqtt-headless.nextmqtt.svc.cluster.local:8080
```

先頭 3 Pod を seed に設定。追加 Pod は gossip で発見されます。

## 検証

```bash
curl -s http://localhost:8080/api/v1/cluster -H "Authorization: Bearer $TOKEN" | jq '.nodes'

nextmqtt sub --port 1884 --topic cluster/# --count 1 &
nextmqtt pub --port 1883 --topic cluster/test --message hello --qos 1
```

購読後、1 heartbeat 待ってから publish してください。

## 制限

- セッション移行/永続化なし
- グローバル共有サブスクリプションなし
- retained はベストエフォート
- フィルタは全量・上限 10,000

# 運用

## ヘルスチェック

| エンドポイント | 用途 |
| --- | --- |
| `/healthz` | 生存確認 |
| `/readyz` | 準備完了 |
| `/api/v1/health` | API ヘルス |
| `/api/v1/ready` | API レディネス |

## メトリクス

`GET /metrics` は Prometheus 形式。`Admin:MetricsRequireAuth=true` で保護できます。

| メトリクス | 型 | 説明 |
| --- | --- | --- |
| `nextmqtt_connections_opened_total` | counter | 接続数 |
| `nextmqtt_connections_closed_total` | counter | 切断数 |
| `nextmqtt_authentication_failures_total` | counter | 認証失敗 |
| `nextmqtt_publishes_received_total` | counter | 受信 publish |
| `nextmqtt_publishes_sent_total` | counter | 配信 publish |
| `nextmqtt_bytes_received_total` | counter | 受信バイト |
| `nextmqtt_bytes_sent_total` | counter | 送信バイト |
| `nextmqtt_messages_dropped_total` | counter | 破棄 |
| `nextmqtt_retransmissions_total` | counter | 再送 |
| `nextmqtt_clients_connected` | gauge | 接続中 |
| `nextmqtt_sessions` | gauge | セッション |
| `nextmqtt_subscriptions` | gauge | 購読 |
| `nextmqtt_retained_messages` | gauge | retained |
| `nextmqtt_cluster_nodes` | gauge | ノード |

## ログ

stdout に出力。接続/切断、クラスタ参加/タイムアウト、起動パスワード警告などを
含みます。`Logging:LogLevel:NextMqtt=Debug` で詳細化できますが、本番では控えめに。

## 容量

| ワークロード | CPU | メモリ |
| --- | --- | --- |
| 1 万アイドル接続 | 0.5 コア | 256–512 MiB |
| 1 万 QoS 1 接続 | 1–2 コア | 512 MiB–1 GiB |
| 大規模 retained / キュー | 1+ コア | 負荷依存 |

### 実測値

| シナリオ | 結果 |
| --- | --- |
| 同時接続 1,000 | 1.09 秒で 1,000 接続（約 920 接続/秒） |
| 同時接続 5,000 | 3.16 秒で 5,000 接続（約 1,580 接続/秒）、失敗 0 |
| QoS 0、50 クライアント × 2,000 メッセージ、64 B | 約 194,000 msg/s |
| QoS 0、100 クライアント × 2,000 メッセージ、128 B | 約 186,000 msg/s |
| QoS 1、20 クライアント × 500 メッセージ | 約 19,800 msg/s |

測定環境: 1 台の Windows 11、Intel Core i7-11700F（8 コア / 16 スレッド）、
32 GiB RAM。ブローカーと負荷生成を同じホストで loopback 経由で実行している
ため、数値は控えめです。`nextmqtt bench` と `nextmqtt bench-connections` で
再現できます。TLS、retained が多い構成、大きなファンアウトでは低下します。

Docker Desktop でポート公開して動かす場合、このマシンではホスト側のポート転送が
約 2,000 同時接続で飽和し、それを超えるとブローカーに届く前に接続が失敗します。
実際の接続容量を測るには、生成側をコンテナネットワーク内に置くか Linux ホストを
使ってください。

## セッション

- 永続セッションは期限までメモリに保持。`Storage:Session:Mode=File` ならディスクにも
  スナップショット
- 再接続時は新しいノードが引き継ぎ、所有者が停止していれば最新レプリカを使う
  （レプリカもファイル保存できる）
- `SweepInterval`（既定 30 秒）で期限切れを削除
- `cleanStart=true` で次回接続時に破棄
- 管理 UI の切断は `0x98` を送信

## アップグレード

1. ノードを 1 台ずつ drain / 縮小
2. 状態を残すならセッション、レプリカ、retained のファイルをマウント。なければ
   クライアントの再接続と再購読を計画
3. イメージタグ更新
4. `/readyz` とメトリクス確認
5. クラスタは 2 ノード以上維持

ファイルストアをマウントしレプリカが新しければ、ローリング再起動でも永続セッションは
残ります。なければキューと retained は失われます。retained は最後の値の保存であり、
永続イベントログではありません。

## トラブルシューティング

| 症状 | 原因 | 対処 |
| --- | --- | --- |
| UI 401 | トークン期限切れ/一時キー | 再ログイン、固定 Secret |
| MQTT 全拒否 | 匿名無効でユーザーなし | ユーザー/JWT 設定 |
| SUBACK 0x87 | ACL 拒否 | ルール確認 |
| アイドル切断 | keep-alive 超過 | クライアント/ネットワーク設定 |
| オフライン消息消失 | 非永続/キュー満杯 | セッション期限・キュー拡大 |
| ノード suspect/dead | ネットワーク/URL/キー/時計 | 8080、AdvertiseUrl、秘密鍵、NTP |
| drop 増加 | 遅い消費者 | スループット確認、制限調整 |

# 運用

このページは日々の運用で必要な内容を扱います：ヘルス、メトリクス、ログ、容量、
アップグレード、トラブルシューティングです。

## ヘルスエンドポイント

| エンドポイント | 用途 |
| --- | --- |
| `GET /healthz` | プロセスが生きている。liveness プローブ向け |
| `GET /readyz` | トラフィックを受けられる状態 |
| `GET /api/v1/health` | バージョン付きの API ヘルス |
| `GET /api/v1/ready` | セッション数付きの API レディネス |

いずれも設計上認証不要です。ヘルス応答に秘密を入れないでください。

## メトリクス

`GET /metrics` は Prometheus テキスト形式を公開します。認証は既定で無効です。
Bearer トークンか API キーを要求するには `Admin:MetricsRequireAuth=true` を設定します。

| メトリクス | 型 | 意味 |
| --- | --- | --- |
| `nextmqtt_connections_opened_total` | counter | 受け付けた CONNECT |
| `nextmqtt_connections_closed_total` | counter | 閉じた接続 |
| `nextmqtt_connections_rejected_total` | counter | CONNECT 中に拒否 |
| `nextmqtt_authentication_failures_total` | counter | 認証失敗 |
| `nextmqtt_publishes_received_total` | counter | 受け付けた PUBLISH |
| `nextmqtt_publishes_sent_total` | counter | 配信した PUBLISH |
| `nextmqtt_bytes_received_total` | counter | MQTT ソケットから読んだバイト |
| `nextmqtt_bytes_sent_total` | counter | MQTT ソケットへ書いたバイト |
| `nextmqtt_messages_dropped_total` | counter | ポリシーまたはバックプレッシャーで破棄 |
| `nextmqtt_retransmissions_total` | counter | QoS 再送 |
| `nextmqtt_subscriptions_created_total` | counter | 成功した購読 |
| `nextmqtt_sessions_expired_total` | counter | 期限切れで削除したセッション |
| `nextmqtt_connections_rate_limited_total` | counter | 接続レート制限で拒否した CONNECT |
| `nextmqtt_publishes_rate_limited_total` | counter | クライアント別レート制限で破棄した PUBLISH |
| `nextmqtt_clients_connected` | gauge | オンライン接続 |
| `nextmqtt_sessions` | gauge | レジストリ内のセッション |
| `nextmqtt_subscriptions` | gauge | 有効な購読 |
| `nextmqtt_retained_messages` | gauge | retained メッセージ |
| `nextmqtt_cluster_nodes` | gauge | 既知のクラスタノード |
| `nextmqtt_build_info` | gauge | バージョンラベル |

Prometheus のスクレイプ設定例：

```yaml
scrape_configs:
  - job_name: nextmqtt
    metrics_path: /metrics
    static_configs:
      - targets: ["nextmqtt-http:8080"]
```

Kubernetes マニフェストは `prometheus.io/scrape` アノテーションを追加します。

## ダッシュボード

概要ページは 3 秒ごとに API をポーリングし、次を表示します：

- 接続中のクライアントと総セッション数
- 購読と retained メッセージ
- 直近 1 分の公開スループット
- 接続、認証、破棄、再送のカウンター
- クラスタメンバーシップ

クライアント、購読、retained の表は 5 秒ごとに更新され、サーバー側でページングします。
公開コンソールは REST API と同じ ACL なしの管理経路を使います。

MQTT デバッガー（Messaging → Debugger）はブラウザーから WebSocket でブローカーへ直接
接続するため、管理 API ではなく実際のプロトコルを検証できます。MQTT 3.1.1 と 5.0、
QoS 0–2、retained、遺言、MQTT 5 のセッション/公開プロパティ、noLocal 付きトピック
フィルタ、retain-as-published、retain handling、PING、パケットトレース、JSON ログ
出力に対応します。通常の MQTT クライアントと同じ資格情報を使い、ブローカーの認証と
ACL の対象になります。

## ログ

ログは stdout に書かれます。主なメッセージ：

- トランスポートとアドレスを含むリスナー起動
- クライアント ID と session-present を含む接続/切断
- クラスタの参加/離脱/タイムアウト
- 生成されたブートストラップパスワードと一時トークンシークレットの警告
- デバッグレベルのプロトコルエラー

パケットレベルの診断には `Logging:LogLevel:NextMqtt=Debug` を設定します。ペイロードの
プライバシーが重要な本番ではデバッグログを避けてください。ブローカーはペイロードを
ログに残しません。

## 容量計画

まずは次を目安にします：

| ワークロード | CPU | メモリ |
| --- | --- | --- |
| アイドル 1 万クライアントまで | 0.5 コア | 256–512 MiB |
| 中程度の QoS 1 トラフィックで 1 万クライアント | 1–2 コア | 512 MiB–1 GiB |
| 大きな retained 集合 / オフラインキュー | 1 コア以上 | ペイロードとキューに合わせて |

メモリを支配するもの：

- セッションオブジェクトと購読エントリ
- オフラインキュー（永続セッションごとの `MaxQueuedMessages`）
- retained メッセージのペイロード
- inflight ウィンドウ

メモリが厳しいときはキューの上限かセッション期限を下げます。現行リリースに retained
の全体クォータはないため、`nextmqtt_retained_messages` とペイロードサイズを監視して
ください。

### 実測参考値

| シナリオ | 結果 |
| --- | --- |
| 同時 1,000 接続 | 1.09 秒で 1,000 受理（約 920 接続/秒） |
| 同時 5,000 接続 | 3.16 秒で 5,000 受理（約 1,580 接続/秒）、失敗 0 |
| QoS 0、50 クライアント × 2,000 メッセージ、64 B | 約 194,000 msg/s |
| QoS 0、100 クライアント × 2,000 メッセージ、128 B | 約 186,000 msg/s |
| QoS 1、20 クライアント × 500 メッセージ | 約 19,800 msg/s |

ハードウェア：1 台の Windows 11、Intel Core i7-11700F（8 コア / 16 スレッド）、
32 GiB RAM。ブローカーと負荷生成器がループバック経由で同じ CPU を共有しています。
この数値は意図的に控えめです。別ホストの生成器、Linux、専用ホストのリリースビルドでは
さらに良くなります。`nextmqtt bench` と `nextmqtt bench-connections` で再現できます。
TLS、retained の多いトピック、大きなファンアウトは数値を下げます。

Docker Desktop の公開ポートで動かす場合、ホストのポートフォワーダーがこのマシンでは
約 2,000 同時接続で飽和します。それ以上ではブローカーに届く前に接続が失敗します。
接続容量を測るには、コンテナネットワーク内か Linux ホストで生成器を動かしてください。

## セッションのライフサイクル

- 永続セッションは期限までメモリに保持され、`Storage:Session:Mode` が `file` なら
  ディスクのスナップショットにも入ります
- 再接続時は新しい所有者が引き継ぎ、所有者が停止していれば最新レプリカが使われます
  （レプリカもファイル保存できます）
- 保守ループは `Broker:SweepInterval`（既定 30 秒）ごとに期限を確認します
- `cleanStart=true` で切断したクライアントは、次の CONNECT で以前のセッションを破棄
  します
- ダッシュボードの「切断」は MQTT 5 の理由 `0x98` を送ってソケットを閉じます。その後は
  通常の期限規則に従います

## アップグレード

1. ノードを 1 台ずつドレインまたは縮小します。
2. 状態を残す必要がある場合はセッション、レプリカ、retained のファイルをマウント
   します。そうでなければクライアントの再接続と再購読を計画します。
3. Compose/Helm のイメージタグを更新します。
4. ロールアウト中は `/readyz` と `nextmqtt_clients_connected` を監視します。
5. クラスタではルーティング範囲を保つため、常に 2 ノード以上を生かします。

ファイルストアをマウントしレプリカが新しければ、ローリング再起動でも永続セッションは
残ります。なければキューと retained は失われます。retained は最後の値の保存であり、
永続イベントログではありません。

## トラブルシューティング

| 症状 | 考えられる原因 | 対処 |
| --- | --- | --- |
| ダッシュボードが 401 | トークン期限切れ、または一時シークレットでの再起動 | 再ログイン；`NEXTMQTT_ADMIN_TOKEN_SECRET` を設定 |
| 全 MQTT クライアントが拒否 | 匿名無効でユーザー/JWT なし | ユーザー設定か匿名有効化 |
| SUBACK `0x87` | ACL がフィルタを拒否 | `Acl:Rules` と既定ポリシーを確認 |
| PUBACK `0x87` | ACL がトピックを拒否 | 公開ルールを確認 |
| アイドル後に切断 | keep-alive 超過 | クライアントの keep-alive とネットワークの idle timeout を確認 |
| オフラインクライアントのメッセージが消える | セッションが非永続、またはキュー満杯 | セッション期限 / clean start false を使い、キュー上限を上げる |
| クラスタが suspect/dead | heartbeat がファイアウォールで遮断、URL 誤り | `AdvertiseUrl`、8080、シークレット、時計ずれを確認 |
| 破棄カウンターが高い | 遅い消費者、小さいキュー/チャネル | クライアントのスループットを確認し、慎重に上限を上げる |
| `nextmqtt_connections_rejected_total` が増える | 認証またはクライアント ID の失敗 | 認証ログとクライアント ID 規則を確認 |

## バックアップ

`Storage:Retained:Mode` が `file` のとき、`Storage:Retained:Path` のジャーナルが全
retained メッセージを保持し、ブローカー稼働中でもコピーできます（追記専用で、圧縮は
アトミックな rename を使います）。永続セッションは `Storage:Session:Mode` が `file`
なら `Storage:Session:Path` に、受信したレプリカは `Storage:Replicas:Mode` が `file`
なら `Storage:Replicas:Path` にスナップショットされます。これらのファイルは該当ノード
を停止してからコピーするか、下の API を使ってください。

ストレージモードに依存しない論理バックアップには API を使います：

```bash
curl -s http://localhost:8080/api/v1/backup -H "Authorization: Bearer $TOKEN" \
  > nextmqtt-backup.json
curl -s -X POST http://localhost:8080/api/v1/backup -H "Authorization: Bearer $TOKEN" \
  -H 'content-type: application/json' --data @nextmqtt-backup.json
```

インポートは retained を置き換え、現在オンラインでない永続セッションを追加します。
送信 inflight の確認状態はバックアップに含まれませんが、PUBREL 待ちの受信 QoS 2 は
含まれます。

設定は環境変数と Kubernetes/Compose ファイルにあります。これらもバックアップして
ください。管理者トークンシークレットとクラスタシークレットは、トークン無効化やノード
信頼の破壊を避けるため保持する必要があります。

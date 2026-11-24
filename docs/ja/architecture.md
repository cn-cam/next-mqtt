# アーキテクチャ

NextMQTT はモジュラーモノリスです。データプレーン（MQTT）とコントロールプレーン
（API/UI）を分離し、単一プロセスで動作します。

## 原則

1. **データプレーン分離**：管理リクエストが publish をブロックしない
2. **すべて有界**：inflight、キュー、パケットサイズ、送信チャネル
3. **セッション単一所有**：クライアント ID は 1 ノードに属する
4. **制御系の交換可能性**：API と UI は同じ Broker ファサードを使用
5. **クリーンルーム**：OASIS MQTT 仕様に従う

## コンポーネント

```text
MQTT/TCP/TLS ──► Server listeners ──► ClientConnection
Browser WS ────► Kestrel /mqtt ─────► ClientConnection
                                              │
                    ┌─────────────────────────▼────────────────┐
                    │ Broker: sessions, routing, retained,     │
                    │ auth/ACL, cluster, metrics               │
                    └─────────────────────────┬────────────────┘
                    ┌─────────────────────────▼────────────────┐
                    │ Core: codec, topic filters, topic trie   │
                    └──────────────────────────────────────────┘
```

- **Core**：MQTT 3.1.1/5.0 編解码、UTF-8/プロパティ検証、トピック Trie
- **Broker**：接続状態、セッション、QoS、retained、認証、ACL、クラスタ
- **Server**：TCP/TLS、Kestrel、REST、UI、クラスタ内部 API
- **Cli**：pub/sub/bench/hash

## 接続ライフサイクル

1. TCP/TLS/WS 受信
2. CONNECT を `ConnectTimeout` 内で受信
3. クライアント ID 検証/生成
4. 認証（パスワード → JWT → 匿名）
5. セッション接続（clean start / 再開 / 乗っ取り）
6. CONNACK 後に送信ループと再送
7. DISCONNECT/keep-alive/エラーでクリーンアップ
8. セッション期限または削除、will 発行

## データ構造

- **TopicTrie**：購読マッチと retained 検索を双方向で提供
- **SubscriptionIndex**：通常/共有サブスクリプション、クライアント別索引
- **BrokerSession**：inflight、オフラインキュー、受信 QoS 2 重複排除
- **RetainedMessageStore**：トピック別 Trie、期限、削除

## publish 経路

```text
PUBLISH → topic/alias 検証 → ACL → retained 更新
        → 購読マッチ → クライアント単位で集約
        → セッション/キュー → 接続チャネル → クラスタ転送
```

配信 QoS は `min(publish, subscription)`。RAP が無ければ retain をクリア。

## クラスタ

- heartbeat で通常購読フィルタを広告
- gossip でメンバー伝播
- retain は全ノードへ、通常は一致フィルタのみ
- メッセージ ID で重複排除、再転送なし
- 共有サブスクリプションはノード単位

## 耐久性

| イベント | 動作 |
| --- | --- |
| 切断 | 期限/clean start に応じて保持 |
| 再起動 | セッション/キュー/retained は失われる |
| ノード障害 | suspect → dead → 削除 |
| ネットワーク分断 | 各側で継続、転送は失敗 |
| 遅い消費者 | チャネル飽和で破棄・計測 |

## 非目標

- 他社 Broker のランタイム/プラグイン互換
- ルールエンジン/ブリッジ内蔵
- 他社製品との認証互換や推奨表明

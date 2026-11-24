# ロードマップ

方向性であり約束ではありません。

## 0.1 — 基盤（完了）

- MQTT 3.1.1/5.0、TCP/TLS/WS/WSS
- QoS 0/1/2、retained、will、session expiry
- 共有サブスクリプション、MQTT 5 購読オプション
- パスワード/JWT/匿名認証、ACL
- gossip クラスタ、署名付き転送
- REST API、Vue 3 UI
- CLI、Docker、Compose、Kubernetes、Helm
- Prometheus、ヘルスチェック

## 0.2 — 永続化と運用

- [x] 追記ログによる retained ストア（0.4.0）
- [x] 起動時と肥大時の compaction（0.4.0）
- [x] セッションスナップショット永続化（0.5.0）
- [ ] SQLite / PostgreSQL
- [x] retained とセッションのバックアップ/リストア（0.5.0）
- [x] 優雅な drain（0.5.0）
- [x] 接続レート制限（IP 単位、0.3.0）
- [x] クライアント単位のメッセージレートクォータ（0.3.0）
- [x] トピック単位の公開クォータ（0.5.0）
- [ ] OpenTelemetry

## 0.3 — プロトコル深化

- [ ] 拡張認証 AUTH
- [ ] 送信トピックエイリアス
- [ ] request/response ヘルパー
- [ ] MQTT over QUIC
- [ ] グローバル共有サブスクリプション
- [ ] retained の競合解決

## 0.4 — 統合

- [ ] Webhook bridge
- [ ] Kafka / NATS bridge
- [ ] ルールエンジン
- [ ] .NET プラグイン SDK
- [ ] スキーマ検証
- [ ] マルチテナント

## 非目標

- 他社 Broker のプライベートプラグイン API 互換
- 他社設定/ダッシュボードの再利用
- 他実装のソース/テスト/ドキュメント複製
- ベンダー認証・推奨の主張

セキュリティ修正は機能より優先されます。

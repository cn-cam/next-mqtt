---
layout: home
title: 次世代 MQTT プラットフォーム

hero:
  name: NextMQTT
  text: 次世代 MQTT プラットフォーム
  tagline: クラウド、エッジ、リアルタイムデータ向けの .NET ネイティブ MQTT プラットフォーム。クラスタ、管理 API、ダッシュボードを標準搭載。
  image:
    src: /logo.svg
    alt: NextMQTT
  actions:
    - theme: brand
      text: はじめに
      link: /ja/getting-started
    - theme: alt
      text: アーキテクチャ
      link: /ja/architecture
    - theme: alt
      text: GitHub
      link: https://github.com/cn-cam/next-mqtt

features:
  - icon: 📡
    title: MQTT 3.1.1 / 5.0
    details: TCP、TLS、WebSocket、WSS。QoS 0/1/2、retained、will、共有サブスクリプション、トピックエイリアス、セッション期限。
  - icon: 🧩
    title: 統合とプラグイン
    details: Webhook、NATS、Kafka、RabbitMQ ブリッジ（有界キュー）と、認証・認可を差し替えるファーストパーティのプラグインホスト。
  - icon: 🔐
    title: 強化できるセキュリティ
    details: PBKDF2、HS256 JWT、MQTT 5 拡張認証、トピック ACL、管理トークン、API キー、鍵パスを明示できる TLS。
  - icon: 🔁
    title: 自己修復クラスタ
    details: 署名付き gossip、ノード間ルーティング、クラスタ全体の共有サブスクリプション、参加時の retained 同期、停止ノードの自動排除。
  - icon: 🧭
    title: 運用ファースト
    details: クライアント、サブスクリプション、retained、publish コンソール、プラグインとログの画面、クラスタトポロジー、Prometheus メトリクス。
  - icon: 📦
    title: 単純なデプロイ
    details: マルチアーキテクチャイメージ、Compose、Kustomize、Helm。プローブとオートスケール対応。
  - icon: 🧼
    title: 独立実装
    details: クリーンルーム、Apache-2.0。他社 Broker のソース、設定形式、ロゴ、商標を含みません。
---

## 最近の追加

- **TLS を明確に**: 証明書、秘密鍵のパス、想定ドメインを個別に設定。中間証明書を配信し、ドメインをカバーしない証明書は起動時に警告します。
- **クラスタの自己修復**: 参加ノードは retained をピアから取得し、メンバー変化で制限を即再計算し、無応答のノードは実際に削除されます。
- **統合はルーティング規則の後**: `drop` で捨てられたメッセージは Webhook やブリッジに届かず、`rewrite` 後のトピックが下流に渡ります。
- **ダッシュボードにプラグインとログ**: プラグインホストと有界ログバッファを画面から確認できます。
- **ドキュメントゲート**: `scripts/docs-coverage.ps1` がコメントのないメンバーでビルドを失敗させます。


## MQTT の新しい基盤

NextMQTT は .NET に標準化するチーム向けに、単一プロセス、明示的なリソース制限、
運用ツールを一体で提供します。

- **.NET 10 の単一プロセス**。外部データベースやメッセージミドルウェア不要。
- **全層の可観測性**。接続、セッション、inflight、サブスクリプション、retained、クラスタ。
- **予測可能な非同期パイプライン**。`System.IO.Pipelines` と有界チャネル。
- **CI で検証されるデプロイ資産**。

[アーキテクチャ](/ja/architecture)と[はじめに](/ja/getting-started)をご覧ください。

## 独立性

NextMQTT は商用 MQTT Broker と提携・推奨・派生の関係にありません。第三者 Broker の
ソース、ドキュメント、設定形式、ロゴ、商標を含みません。

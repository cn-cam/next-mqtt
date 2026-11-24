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
    details: TCP、TLS、WebSocket、WSS。QoS 0/1/2、retained、will、共有サブスクリプション、セッション期限。
  - icon: 🧭
    title: 運用ファースト
    details: クライアント、サブスクリプション、retained、publish コンソール、クラスタトポロジー、Prometheus メトリクス。
  - icon: 🧩
    title: コーディネータ不要のクラスタ
    details: 署名付き gossip とノード間ルーティング。1 ノードから開始し、クライアント設定を変えずに拡張。
  - icon: 📦
    title: 単純なデプロイ
    details: マルチアーキテクチャイメージ、Compose、Kustomize、Helm。プローブとオートスケール対応。
  - icon: 🔐
    title: 強化できるセキュリティ
    details: PBKDF2、HS256 JWT、トピック ACL、管理トークン、API キー、TLS。
  - icon: 🧼
    title: 独立実装
    details: クリーンルーム、Apache-2.0。他社 Broker のソース、設定形式、ロゴ、商標を含みません。
---

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

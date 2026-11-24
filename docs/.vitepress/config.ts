import { defineConfig } from 'vitepress'

const base = process.env.DOCS_BASE ?? '/'

interface Labels {
  guide: string
  architecture: string
  features: string
  protocol: string
  deploy: string
  api: string
  more: string
  security: string
  development: string
  roadmap: string
  faq: string
  licences: string
  gettingStarted: string
  configuration: string
  authentication: string
  tls: string
  operations: string
  clustering: string
  concepts: string
  reference: string
  cli: string
  adr: string
  prev: string
  next: string
  edit: string
  footer: string
  copyright: string
}

const en: Labels = {
  guide: 'Guide',
  architecture: 'Architecture',
  features: 'Features',
  protocol: 'Protocol',
  deploy: 'Deploy',
  api: 'API',
  more: 'More',
  security: 'Security',
  development: 'Development',
  roadmap: 'Roadmap',
  faq: 'FAQ',
  licences: 'Third-party licences',
  gettingStarted: 'Getting started',
  configuration: 'Configuration',
  authentication: 'Authentication & ACL',
  tls: 'TLS',
  operations: 'Operations',
  clustering: 'Clustering',
  concepts: 'Concepts',
  reference: 'Reference',
  cli: 'CLI',
  adr: 'Architecture decisions',
  prev: 'Previous',
  next: 'Next',
  edit: 'Edit this page',
  footer: 'Apache-2.0 · Independent clean-room implementation',
  copyright: 'Copyright © 2026 NextMQTT contributors',
}

const zh: Labels = {
  guide: '指南',
  architecture: '架构',
  features: '功能',
  protocol: '协议',
  deploy: '部署',
  api: 'API',
  more: '更多',
  security: '安全',
  development: '开发',
  roadmap: '路线图',
  faq: '常见问题',
  licences: '第三方许可',
  gettingStarted: '快速开始',
  configuration: '配置',
  authentication: '认证与 ACL',
  tls: 'TLS',
  operations: '运维',
  clustering: '集群',
  concepts: '核心概念',
  reference: '参考',
  cli: '命令行',
  adr: '架构决策',
  prev: '上一页',
  next: '下一页',
  edit: '编辑此页',
  footer: 'Apache-2.0 · 独立净室实现',
  copyright: 'Copyright © 2026 NextMQTT contributors',
}

const ja: Labels = {
  guide: 'ガイド',
  architecture: 'アーキテクチャ',
  features: '機能',
  protocol: 'プロトコル',
  deploy: 'デプロイ',
  api: 'API',
  more: 'その他',
  security: 'セキュリティ',
  development: '開発',
  roadmap: 'ロードマップ',
  faq: 'FAQ',
  licences: 'サードパーティライセンス',
  gettingStarted: 'はじめに',
  configuration: '設定',
  authentication: '認証と ACL',
  tls: 'TLS',
  operations: '運用',
  clustering: 'クラスタ',
  concepts: 'コンセプト',
  reference: 'リファレンス',
  cli: 'CLI',
  adr: '設計判断',
  prev: '前へ',
  next: '次へ',
  edit: 'このページを編集',
  footer: 'Apache-2.0 · 独立したクリーンルーム実装',
  copyright: 'Copyright © 2026 NextMQTT contributors',
}

const ko: Labels = {
  guide: '가이드',
  architecture: '아키텍처',
  features: '기능',
  protocol: '프로토콜',
  deploy: '배포',
  api: 'API',
  more: '더보기',
  security: '보안',
  development: '개발',
  roadmap: '로드맵',
  faq: 'FAQ',
  licences: '서드파티 라이선스',
  gettingStarted: '시작하기',
  configuration: '설정',
  authentication: '인증 및 ACL',
  tls: 'TLS',
  operations: '운영',
  clustering: '클러스터',
  concepts: '개념',
  reference: '레퍼런스',
  cli: 'CLI',
  adr: '아키텍처 결정',
  prev: '이전',
  next: '다음',
  edit: '이 페이지 편집',
  footer: 'Apache-2.0 · 독립적인 클린룸 구현',
  copyright: 'Copyright © 2026 NextMQTT contributors',
}

function nav(prefix: string, t: Labels) {
  return [
    {
      text: t.guide,
      link: `${prefix}/getting-started`,
      activeMatch: '/(getting-started|configuration|authentication|tls|operations|features)$',
    },
    { text: t.architecture, link: `${prefix}/architecture` },
    { text: t.protocol, link: `${prefix}/protocol` },
    { text: t.deploy, link: `${prefix}/deployment`, activeMatch: '/(deployment|clustering)$' },
    { text: t.api, link: `${prefix}/api` },
    {
      text: t.more,
      items: [
        { text: t.security, link: `${prefix}/security` },
        { text: t.development, link: `${prefix}/development` },
        { text: t.roadmap, link: `${prefix}/roadmap` },
        { text: t.faq, link: `${prefix}/faq` },
        { text: t.licences, link: `${prefix}/legal/third-party-licenses` },
      ],
    },
  ]
}

function sidebar(prefix: string, t: Labels) {
  return [
    {
      text: t.guide,
      items: [
        { text: t.gettingStarted, link: `${prefix}/getting-started` },
        { text: t.features, link: `${prefix}/features` },
        { text: t.configuration, link: `${prefix}/configuration` },
        { text: t.authentication, link: `${prefix}/authentication` },
        { text: t.tls, link: `${prefix}/tls` },
        { text: t.operations, link: `${prefix}/operations` },
      ],
    },
    {
      text: t.concepts,
      items: [
        { text: t.architecture, link: `${prefix}/architecture` },
        { text: t.protocol, link: `${prefix}/protocol` },
        { text: t.clustering, link: `${prefix}/clustering` },
        { text: t.security, link: `${prefix}/security` },
      ],
    },
    {
      text: t.reference,
      items: [
        { text: t.api, link: `${prefix}/api` },
        { text: t.cli, link: `${prefix}/cli` },
        { text: t.deploy, link: `${prefix}/deployment` },
        { text: t.development, link: `${prefix}/development` },
        { text: t.adr, link: `${prefix}/adr/` },
        { text: t.roadmap, link: `${prefix}/roadmap` },
        { text: t.faq, link: `${prefix}/faq` },
      ],
    },
  ]
}

function localeTheme(prefix: string, t: Labels) {
  return {
    nav: nav(prefix, t),
    sidebar: sidebar(prefix, t),
    footer: { message: t.footer, copyright: t.copyright },
    editLink: {
      pattern: 'https://github.com/cn-cam/next-mqtt/edit/main/docs/:path',
      text: t.edit,
    },
    docFooter: { prev: t.prev, next: t.next },
    search: { provider: 'local' },
    outline: { level: [2, 3] },
  }
}

export default defineConfig({
  base,
  lang: 'en',
  title: 'NextMQTT',
  titleTemplate: ':title · NextMQTT',
  description: 'Next-generation .NET-native MQTT platform for cloud, edge and real-time data.',
  cleanUrls: false,
  lastUpdated: true,
  ignoreDeadLinks: [/^http:\/\/localhost/],
  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: `${base}logo.svg` }],
    ['meta', { name: 'theme-color', content: '#0891b2' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:title', content: 'NextMQTT' }],
    ['meta', { property: 'og:description', content: 'Next-generation .NET-native MQTT platform for cloud, edge and real-time data.' }],
  ],
  locales: {
    root: { label: 'English', lang: 'en' },
    zh: { label: '简体中文', lang: 'zh-CN', link: '/zh/', themeConfig: localeTheme('/zh', zh) },
    ja: { label: '日本語', lang: 'ja', link: '/ja/', themeConfig: localeTheme('/ja', ja) },
    ko: { label: '한국어', lang: 'ko', link: '/ko/', themeConfig: localeTheme('/ko', ko) },
  },
  themeConfig: {
    logo: '/logo.svg',
    siteTitle: 'NextMQTT',
    nav: nav('', en),
    sidebar: sidebar('', en),
    socialLinks: [{ icon: 'github', link: 'https://github.com/cn-cam/next-mqtt' }],
    footer: { message: en.footer, copyright: en.copyright },
    editLink: {
      pattern: 'https://github.com/cn-cam/next-mqtt/edit/main/docs/:path',
      text: en.edit,
    },
    search: { provider: 'local' },
    outline: { level: [2, 3] },
    docFooter: { prev: en.prev, next: en.next },
  },
})

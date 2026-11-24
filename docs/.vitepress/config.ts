import { defineConfig } from 'vitepress'

const base = process.env.DOCS_BASE ?? '/'

export default defineConfig({
  base,
  lang: 'en',
  title: 'NextMQTT',
  titleTemplate: ':title · NextMQTT',
  description: 'Cloud-native MQTT 3.1.1 and 5.0 broker built on .NET 10 and Vue 3.',
  cleanUrls: false,
  lastUpdated: true,
  ignoreDeadLinks: [/^http:\/\/localhost/],
  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: `${base}logo.svg` }],
    ['meta', { name: 'theme-color', content: '#0891b2' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:title', content: 'NextMQTT' }],
    ['meta', { property: 'og:description', content: 'Cloud-native MQTT broker, management API and dashboard.' }],
  ],
  themeConfig: {
    logo: '/logo.svg',
    siteTitle: 'NextMQTT',
    nav: [
      { text: 'Guide', link: '/getting-started', activeMatch: '/(getting-started|configuration|authentication|tls|operations)' },
      { text: 'Architecture', link: '/architecture' },
      { text: 'Features', link: '/features' },
      { text: 'Protocol', link: '/protocol' },
      { text: 'Deploy', link: '/deployment', activeMatch: '/(deployment|clustering)' },
      { text: 'API', link: '/api' },
      {
        text: 'More',
        items: [
          { text: 'Security', link: '/security' },
          { text: 'Development', link: '/development' },
          { text: 'Roadmap', link: '/roadmap' },
          { text: 'FAQ', link: '/faq' },
          { text: 'Third-party licences', link: '/legal/third-party-licenses' },
          { text: '简体中文', link: '/zh/' },
        ],
      },
    ],
    sidebar: [
      {
        text: 'Guide',
        items: [
          { text: 'Getting started', link: '/getting-started' },
          { text: 'Feature matrix', link: '/features' },
          { text: 'Configuration', link: '/configuration' },
          { text: 'Authentication & ACL', link: '/authentication' },
          { text: 'TLS', link: '/tls' },
          { text: 'Operations', link: '/operations' },
        ],
      },
      {
        text: 'Concepts',
        items: [
          { text: 'Architecture', link: '/architecture' },
          { text: 'Protocol support', link: '/protocol' },
          { text: 'Clustering', link: '/clustering' },
          { text: 'Security model', link: '/security' },
        ],
      },
      {
        text: 'Reference',
        items: [
          { text: 'REST API', link: '/api' },
          { text: 'CLI', link: '/cli' },
          { text: 'Deployment', link: '/deployment' },
          { text: 'Development', link: '/development' },
          { text: 'Architecture decisions', link: '/adr/' },
          { text: 'Roadmap', link: '/roadmap' },
          { text: 'FAQ', link: '/faq' },
        ],
      },
    ],
    socialLinks: [{ icon: 'github', link: 'https://github.com/cn-cam/next-mqtt' }],
    footer: {
      message: 'Apache-2.0 · Independent clean-room implementation',
      copyright: 'Copyright © 2026 NextMQTT contributors',
    },
    editLink: {
      pattern: 'https://github.com/cn-cam/next-mqtt/edit/main/docs/:path',
      text: 'Edit this page',
    },
    search: {
      provider: 'local',
    },
    outline: { level: [2, 3] },
    docFooter: { prev: 'Previous', next: 'Next' },
  },
})

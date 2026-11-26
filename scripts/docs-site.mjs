#!/usr/bin/env node
// Generates pages that must never go stale, for every docs locale:
//
//   {locale}/changelog.md        from the repository CHANGELOG.md
//   {locale}/development-log.md  from `git log`, grouped by day
//
// Run before `vitepress build` (the docs package script does this) so the
// published site always reflects the current commit history. These files are
// ignored by git; they are build outputs.

import { execFileSync } from 'node:child_process'
import { readFileSync, writeFileSync, existsSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const root = join(here, '..')
const docs = join(root, 'docs')

function frontMatter(title) {
  return `---\ntitle: ${title}\n---\n\n`
}

const TYPES = {
  feat: 'Feature',
  fix: 'Fix',
  docs: 'Docs',
  test: 'Tests',
  build: 'Build',
  ci: 'CI',
  refactor: 'Refactor',
  perf: 'Performance',
  chore: 'Chore',
}

const RELEASES = 'https://github.com/cn-cam/next-mqtt/releases'

const LOCALES = {
  '': {
    changelogTitle: 'Changelog',
    changelogNote: `See the [GitHub releases](${RELEASES}) for downloadable artefacts.`,
    developmentTitle: 'Development log',
    developmentIntro: [
      'Generated from the commit history on every docs build. Newest first;',
      'merge commits and version bumps are included so the page matches the repository.',
    ],
    types: TYPES,
    other: 'Other',
  },
  zh: {
    changelogTitle: '更新日志',
    changelogNote: `可下载的发布包见 [GitHub releases](${RELEASES})。条目保留仓库中的原文。`,
    developmentTitle: '开发日志',
    developmentIntro: [
      '每次文档构建时从提交历史生成。最新的在前；',
      '合并提交与版本号提交也会包含，因此页面与仓库一致。',
    ],
    types: {
      feat: '功能',
      fix: '修复',
      docs: '文档',
      test: '测试',
      build: '构建',
      ci: 'CI',
      refactor: '重构',
      perf: '性能',
      chore: '杂项',
    },
    other: '其他',
  },
  ja: {
    changelogTitle: '変更履歴',
    changelogNote: `ダウンロード可能な成果物は [GitHub releases](${RELEASES}) を参照してください。項目はリポジトリの原文のままです。`,
    developmentTitle: '開発ログ',
    developmentIntro: [
      'ドキュメントビルドのたびにコミット履歴から生成されます。新しい順です。',
      'マージコミットとバージョン更新も含まれるため、ページはリポジトリと一致します。',
    ],
    types: {
      feat: '機能',
      fix: '修正',
      docs: 'ドキュメント',
      test: 'テスト',
      build: 'ビルド',
      ci: 'CI',
      refactor: 'リファクタリング',
      perf: '性能',
      chore: '雑務',
    },
    other: 'その他',
  },
  ko: {
    changelogTitle: '변경 기록',
    changelogNote: `내려받을 수 있는 배포본은 [GitHub releases](${RELEASES})를 참고하십시오. 항목은 저장소의 원문을 유지합니다.`,
    developmentTitle: '개발 로그',
    developmentIntro: [
      '문서를 빌드할 때마다 커밋 기록에서 생성됩니다. 최신 항목이 먼저 나옵니다.',
      '병합 커밋과 버전 갱신도 포함되므로 페이지가 저장소와 일치합니다.',
    ],
    types: {
      feat: '기능',
      fix: '수정',
      docs: '문서',
      test: '테스트',
      build: '빌드',
      ci: 'CI',
      refactor: '리팩터링',
      perf: '성능',
      chore: '잡무',
    },
    other: '기타',
  },
}

function localeDirectory(locale) {
  return locale ? join(docs, locale) : docs
}

function writeChangelog() {
  const source = join(root, 'CHANGELOG.md')

  if (!existsSync(source)) {
    console.warn('changelog: CHANGELOG.md not found, skipping')
    return
  }

  const markdown = readFileSync(source, 'utf8')
    .replace(/^# Changelog\s*\n/, '')
    .trimStart()

  for (const [locale, text] of Object.entries(LOCALES)) {
    writeFileSync(
      join(localeDirectory(locale), 'changelog.md'),
      frontMatter(text.changelogTitle) +
        `# ${text.changelogTitle}\n\n` +
        markdown +
        `\n\n${text.changelogNote}\n`,
    )
  }

  console.log('changelog: generated for every locale')
}

function readCommits() {
  const raw = execFileSync(
    'git',
    ['log', '--date=short', '--pretty=format:%h%x1f%ad%x1f%s', '-n', '400'],
    { cwd: root, encoding: 'utf8' },
  )

  return raw
    .split('\n')
    .filter((line) => line.trim() !== '')
    .map((line) => {
      const [hash, date, subject] = line.split('\x1f')
      const match = /^(\w+)(?:\(([^)]+)\))?!?:\s*(.+)$/.exec(subject)

      return {
        hash,
        date,
        subject,
        type: match ? match[1].toLowerCase() : 'other',
        scope: match ? match[2] ?? '' : '',
        title: match ? match[3] : subject,
      }
    })
}

function writeDevelopmentLog() {
  let commits

  try {
    commits = readCommits()
  } catch (error) {
    console.warn(`development log: git log failed (${error.message}), skipping`)
    return
  }

  const byDate = new Map()

  for (const commit of commits) {
    if (!byDate.has(commit.date)) byDate.set(commit.date, [])
    byDate.get(commit.date).push(commit)
  }

  for (const [locale, text] of Object.entries(LOCALES)) {
    const lines = [
      frontMatter(text.developmentTitle),
      `# ${text.developmentTitle}\n`,
      ...text.developmentIntro,
      '',
    ]

    for (const [date, entries] of byDate) {
      lines.push(`## ${date}\n`)

      for (const entry of entries) {
        const label = text.types[entry.type] ?? text.other
        const scope = entry.scope ? ` **${entry.scope}** —` : ''
        lines.push(`- \`${entry.hash}\` _${label}_${scope} ${entry.title}`)
      }

      lines.push('')
    }

    writeFileSync(join(localeDirectory(locale), 'development-log.md'), lines.join('\n'))
  }

  console.log(`development log: ${commits.length} commit(s) for every locale`)
}

writeChangelog()
writeDevelopmentLog()

#!/usr/bin/env node
// Generates two VitePress pages that must never go stale:
//
//   docs/changelog.md        from the repository CHANGELOG.md
//   docs/development-log.md  from `git log`, grouped by day
//
// Run before `vitepress build` (the docs package script does this) so the
// published site always reflects the current commit history. Both files are
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

function writeChangelog() {
  const source = join(root, 'CHANGELOG.md')

  if (!existsSync(source)) {
    console.warn('changelog: CHANGELOG.md not found, skipping')
    return
  }

  const markdown = readFileSync(source, 'utf8')
    .replace(/^# Changelog\s*\n/, '')
    .trimStart()

  writeFileSync(
    join(docs, 'changelog.md'),
    frontMatter('Changelog') +
      '# Changelog\n\n' +
      markdown +
      '\n\nSee the [GitHub releases](https://github.com/cn-cam/next-mqtt/releases) for downloadable artefacts.\n',
  )

  console.log('changelog: generated from CHANGELOG.md')
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

  const lines = [
    frontMatter('Development log'),
    '# Development log\n',
    'Generated from the commit history on every docs build. Newest first;',
    'merge commits and version bumps are included so the page matches the repository.\n',
  ]

  for (const [date, entries] of byDate) {
    lines.push(`## ${date}\n`)

    for (const entry of entries) {
      const label = TYPES[entry.type] ?? 'Other'
      const scope = entry.scope ? ` **${entry.scope}** —` : ''
      lines.push(`- \`${entry.hash}\` _${label}_${scope} ${entry.title}`)
    }

    lines.push('')
  }

  writeFileSync(join(docs, 'development-log.md'), lines.join('\n'))
  console.log(`development log: ${commits.length} commit(s)`)
}

writeChangelog()
writeDevelopmentLog()

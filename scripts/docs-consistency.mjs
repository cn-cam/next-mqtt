#!/usr/bin/env node
// Fails when a translated page drifts structurally from its English source.
//
// The i18n baseline only guards line-count coverage, which still allowed a
// page to lose a section, a checklist item or a table row. This check compares
// headings, task-list items and table rows for every page in zh, ja and ko.
//
// Usage: node scripts/docs-consistency.mjs

import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const docs = join(root, 'docs')
const locales = ['zh', 'ja', 'ko']
const ignored = new Set(['changelog.md', 'development-log.md'])

function stats(path) {
  const lines = readFileSync(path, 'utf8').split(/\r?\n/)

  return {
    h2: lines.filter((line) => /^## /.test(line)).length,
    h3: lines.filter((line) => /^### /.test(line)).length,
    tasks: lines.filter((line) => /^- \[[ x]\]/.test(line)).length,
    rows: lines.filter((line) => /^\|/.test(line)).length,
  }
}

const pages = readdirSync(docs)
  .filter((name) => name.endsWith('.md') && !ignored.has(name))
  .sort()

const labels = {
  h2: 'level-2 headings',
  h3: 'level-3 headings',
  tasks: 'task-list items',
  rows: 'table rows',
}

const violations = []

for (const page of pages) {
  const english = stats(join(docs, page))

  for (const locale of locales) {
    let translated

    try {
      translated = stats(join(docs, locale, page))
    } catch {
      violations.push(`${locale}/${page}: missing translation`)
      continue
    }

    const differences = Object.keys(labels)
      .filter((key) => english[key] !== translated[key])
      .map((key) => `${labels[key]} ${translated[key]}/${english[key]}`)

    if (differences.length > 0) {
      violations.push(`${locale}/${page}: ${differences.join(', ')}`)
    }
  }
}

if (violations.length > 0) {
  console.log('Documentation consistency: violations found')
  for (const violation of violations) {
    console.log(`  ${violation}`)
  }
  console.log('')
  console.log('Keep the heading, task-list and table structure aligned across locales.')
  process.exit(1)
}

console.log(`Documentation consistency: OK (${pages.length} page(s) × ${locales.length} locale(s))`)

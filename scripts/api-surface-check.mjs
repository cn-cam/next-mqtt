#!/usr/bin/env node
// Keeps the dashboard API console and docs/api.md in lockstep.
//
// Every endpoint in the documentation table must exist in
// web/src/api/endpoints.ts, and the console may not advertise an endpoint the
// documentation does not list. Both are updated in the same commit.

import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const docs = readFileSync(join(root, 'docs', 'api.md'), 'utf8')
const endpoints = readFileSync(join(root, 'web', 'src', 'api', 'endpoints.ts'), 'utf8')

const documented = new Set()
const implemented = new Set()

for (const line of docs.split(/\r?\n/)) {
  const match = /^\| (GET|POST|DELETE) \| `([^`]+)` \|/.exec(line)
  if (match) documented.add(`${match[1]} ${match[2]}`)
}

for (const match of endpoints.matchAll(/method:\s*'(GET|POST|DELETE)',\s*\r?\n\s*path:\s*'([^']+)'/g)) {
  implemented.add(`${match[1]} ${match[2]}`)
}

const missingFromConsole = [...documented].filter((item) => !implemented.has(item)).sort()
const undocumented = [...implemented].filter((item) => !documented.has(item)).sort()

if (missingFromConsole.length > 0 || undocumented.length > 0) {
  console.log('API surface: dashboard and docs/api.md differ')

  for (const item of missingFromConsole) {
    console.log(`  add to the API console: ${item}`)
  }

  for (const item of undocumented) {
    console.log(`  document or remove: ${item}`)
  }

  process.exit(1)
}

console.log(`API surface: OK (${documented.size} endpoint(s))`)

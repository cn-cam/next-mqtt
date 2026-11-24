#!/usr/bin/env bash
# Repository compliance scanner.
#
# The forbidden brand token is assembled at runtime from character codes so
# that this scanner never stores it. See AGENTS.md section 1.
set -euo pipefail

cd "$(dirname "$0")/.."

token="$(printf '%c%c%c%c' 101 109 113 120)"
prefix="$(printf '%c%c%c' 101 109 113)"
status=0

report() {
  printf '  \033[31mFAIL\033[0m %s\n' "$1"
  status=1
}

printf 'Compliance scan: forbidden brand tokens\n'

# 1. Tracked file names (case-insensitive).
while IFS= read -r file; do
  [ -n "$file" ] || continue
  lower="$(printf '%s' "$file" | tr '[:upper:]' '[:lower:]')"
  case "$lower" in
    *"$prefix"*) report "file name contains a forbidden token: $file" ;;
  esac
done < <(git ls-files)

# 2. Tracked and untracked (non-ignored) file contents.
if git grep -n -i -I -e "$token" -- . >/tmp/nextmqtt-compliance.$$ 2>/dev/null; then
  while IFS= read -r line; do report "content match: $line"; done < /tmp/nextmqtt-compliance.$$
fi
rm -f /tmp/nextmqtt-compliance.$$

while IFS= read -r file; do
  [ -n "$file" ] && [ -f "$file" ] || continue
  if grep -I -i -q -- "$token" "$file" 2>/dev/null; then
    report "content match (untracked): $file"
  fi
done < <(git ls-files --others --exclude-standard)

# 3. Full commit history messages.
if git log --format=%B 2>/dev/null | grep -i -q -- "$token"; then
  report "commit message history contains a forbidden token"
fi

# 4. Branch and tag names.
if git for-each-ref --format='%(refname)' 2>/dev/null | grep -i -q -- "$token"; then
  report "a branch or tag name contains a forbidden token"
fi

if [ "$status" -ne 0 ]; then
  printf '\nCompliance scan failed. Rename or rephrase the offending content.\n'
  exit 1
fi

printf '  \033[32mOK\033[0m no forbidden brand tokens found\n'

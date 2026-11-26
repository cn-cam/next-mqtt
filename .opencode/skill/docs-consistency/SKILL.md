---
name: docs-consistency
description: Keep every GitHub Pages page identical in structure across English, Chinese, Japanese and Korean. Use before committing any change under docs/ or scripts/docs-site.mjs.
---

# Documentation consistency gate

GitHub Pages publishes four locales: English (`docs/`), Chinese (`docs/zh/`),
Japanese (`docs/ja/`) and Korean (`docs/ko/`). A page that exists in only one
locale, or that silently loses a section in a translation, is a release blocker.

## Mandatory workflow

1. Change the English page first.
2. Update `docs/zh/`, `docs/ja/` and `docs/ko/` in the same commit. Never leave a
   translation for a later commit.
3. Preserve the structure of the English page:
   - the same number of level-2 and level-3 headings;
   - the same number of task-list items (`- [ ]` / `- [x]`);
   - the same number of table rows;
   - the same code fences, links and anchors.
4. Translated pages keep the original language for code, identifiers, commit
   titles and command output. Translate prose, headings and UI labels.
5. Run every gate locally before committing:

```bash
node scripts/docs-consistency.mjs
pwsh -NoProfile -File scripts/docs-coverage.ps1
pwsh -NoProfile -File scripts/docs-i18n-check.ps1
cd docs && npm run build
```

`scripts/docs-consistency.mjs` compares headings, task lists and table rows for
all 17 pages across the three translations; CI runs it in the compliance job.
The i18n baseline is only a coverage guard and does not replace this check.

## Generated pages

`changelog.md` and `development-log.md` are build outputs generated for every
locale by `scripts/docs-site.mjs`. They are git-ignored. Never hand-edit or
translate them; if a locale is missing, fix the generator, not the output.

## Version and feature claims

When a release or feature changes, update `docs/features.md`,
`docs/roadmap.md`, `docs/protocol.md` and `CHANGELOG.md` together. A capability
must not be marked partial in one table and complete in another page.

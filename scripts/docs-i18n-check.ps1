#!/usr/bin/env pwsh
#
# Guards against documentation translations drifting further behind English.
#
# Every English page under docs/ must have a zh, ja and ko counterpart. The
# line-count ratio of each translation is recorded in docs/i18n-baseline.json;
# the check fails when a page falls more than a few points below its recorded
# ratio, so new English content has to be translated (or the baseline has to be
# lowered deliberately).
#
# Usage:
#   pwsh -File scripts/docs-i18n-check.ps1           # check
#   pwsh -File scripts/docs-i18n-check.ps1 -Update   # rewrite the baseline
#
# Exit: 0 clean, 1 regression or missing page.

[CmdletBinding()]
param(
    [string]$Root,
    [switch]$Update
)

$ErrorActionPreference = 'Stop'

if (-not $Root) {
    $Root = Split-Path -Parent $PSScriptRoot
}

$docs = Join-Path $Root 'docs'
$baselinePath = Join-Path $docs 'i18n-baseline.json'
$locales = @('zh', 'ja', 'ko')
$tolerance = 0.03

$pages = Get-ChildItem -Path $docs -Filter *.md -File |
    Where-Object { $_.Name -notin @('changelog.md', 'development-log.md') } |
    Sort-Object Name

if ($pages.Count -eq 0) {
    Write-Error "No English pages found under $docs"
    exit 1
}

function Get-Ratio([string]$englishPath, [string]$translatedPath) {
    $english = (Get-Content $englishPath).Count

    if ($english -eq 0) {
        return 1.0
    }

    $translated = (Get-Content $translatedPath).Count
    return [math]::Round($translated / $english, 3)
}

$baseline = @{}
if (Test-Path $baselinePath) {
    $loaded = Get-Content $baselinePath -Raw | ConvertFrom-Json
    foreach ($property in $loaded.PSObject.Properties) {
        $baseline[$property.Name] = $property.Value
    }
}

$violations = [System.Collections.Generic.List[string]]::new()
$current = [ordered]@{}

foreach ($page in $pages) {
    $entry = [ordered]@{}
    $missing = $false

    foreach ($locale in $locales) {
        $translated = Join-Path (Join-Path $docs $locale) $page.Name

        if (-not (Test-Path $translated)) {
            $violations.Add("$($page.Name): missing $locale translation")
            $entry[$locale] = 0.0
            $missing = $true
            continue
        }

        $ratio = Get-Ratio $page.FullName $translated
        $entry[$locale] = $ratio

        if (-not $Update -and -not $missing -and $baseline.ContainsKey($page.Name)) {
            $recorded = $baseline[$page.Name].$locale

            if ($null -ne $recorded -and $ratio -lt ($recorded - $tolerance)) {
                $violations.Add(("{0}: {1} coverage fell from {2:P0} to {3:P0}" -f $page.Name, $locale, $recorded, $ratio))
            }
        }
    }

    $current[$page.Name] = $entry
}

if ($Update) {
    $json = $current | ConvertTo-Json -Depth 4
    Set-Content -Path $baselinePath -Value $json -NoNewline
    Write-Host "Documentation i18n: baseline updated for $($pages.Count) page(s)."
    exit 0
}

if ($violations.Count -gt 0) {
    Write-Host 'Documentation i18n: violations found'
    foreach ($violation in $violations) {
        Write-Host "  $violation"
    }

    Write-Host ''
    Write-Host 'Translate the new sections, or run scripts/docs-i18n-check.ps1 -Update when the drop is intentional.'
    exit 1
}

Write-Host 'Documentation i18n: OK'
exit 0

#!/usr/bin/env pwsh
#
# C# documentation coverage gate.
#
# Enforces AGENTS.md section 5.1:
#   * every type and member, private included, has an XML <summary>;
#   * members of publicly visible types also have <remarks>;
#   * no member carries a stacked <summary> or <remarks> block.
#
# The scan is syntax-aware enough to ignore braces and keywords inside strings
# and comments, and it understands brace-on-the-next-line type declarations and
# enum members. It does not type-check: a member that is public but nested in a
# private type is treated as internal API.
#
# Usage:  pwsh -File scripts/docs-coverage.ps1 [-Root <repository root>]
# Exit:   0 clean, 1 violations found.
#
# Limitation by design: the check only looks at src/**.cs. Test projects are
# exempt because their method names already read as assertions.

[CmdletBinding()]
param(
    [string]$Root
)

$ErrorActionPreference = 'Stop'

if (-not $Root) {
    $Root = Split-Path -Parent $PSScriptRoot
}

$sourceRoot = Join-Path $Root 'src'
if (-not (Test-Path $sourceRoot)) {
    Write-Error "Source directory not found: $sourceRoot"
    exit 1
}

$violations = [System.Collections.Generic.List[string]]::new()

function Get-MaskedText {
    param([string]$Text)

    $chars = $Text.ToCharArray()
    $length = $chars.Length
    $masked = New-Object char[] $length
    $i = 0

    function SetBlank([char[]]$target, [int]$index, [char]$source) {
        $target[$index] = if ($source -eq "`n" -or $source -eq "`r") { $source } else { ' ' }
    }

    while ($i -lt $length) {
        $current = $chars[$i]
        $next = if ($i + 1 -lt $length) { $chars[$i + 1] } else { [char]0 }

        # Line comment: keep documentation, blank the rest.
        if ($current -eq '/' -and $next -eq '/') {
            $isDoc = $i + 2 -lt $length -and $chars[$i + 2] -eq '/'
            while ($i -lt $length -and $chars[$i] -ne "`n") {
                $masked[$i] = if ($isDoc) { $chars[$i] } else { ' ' }
                $i++
            }
            continue
        }

        # Block comment.
        if ($current -eq '/' -and $next -eq '*') {
            SetBlank $masked $i $current
            SetBlank $masked ($i + 1) $next
            $i += 2
            while ($i -lt $length) {
                if ($chars[$i] -eq '*' -and $i + 1 -lt $length -and $chars[$i + 1] -eq '/') {
                    SetBlank $masked $i $chars[$i]
                    SetBlank $masked ($i + 1) $chars[$i + 1]
                    $i += 2
                    break
                }
                SetBlank $masked $i $chars[$i]
                $i++
            }
            continue
        }

        # Raw string literal: """ ... """.
        if ($current -eq '"' -and $i + 2 -lt $length -and $chars[$i + 1] -eq '"' -and $chars[$i + 2] -eq '"') {
            for ($k = 0; $k -lt 3; $k++) { $masked[$i + $k] = '"' }
            $i += 3
            while ($i -lt $length) {
                if ($chars[$i] -eq '"' -and $i + 2 -lt $length -and $chars[$i + 1] -eq '"' -and $chars[$i + 2] -eq '"') {
                    for ($k = 0; $k -lt 3; $k++) { $masked[$i + $k] = '"' }
                    $i += 3
                    break
                }
                SetBlank $masked $i $chars[$i]
                $i++
            }
            continue
        }

        # Verbatim string: @"...".
        if ($current -eq '@' -and $next -eq '"') {
            $masked[$i] = ' '
            $masked[$i + 1] = '"'
            $i += 2
            while ($i -lt $length) {
                if ($chars[$i] -eq '"') {
                    if ($i + 1 -lt $length -and $chars[$i + 1] -eq '"') {
                        $masked[$i] = ' '
                        $masked[$i + 1] = ' '
                        $i += 2
                        continue
                    }
                    $masked[$i] = '"'
                    $i++
                    break
                }
                SetBlank $masked $i $chars[$i]
                $i++
            }
            continue
        }

        # Regular string literal.
        if ($current -eq '"') {
            $masked[$i] = '"'
            $i++
            while ($i -lt $length) {
                if ($chars[$i] -eq '\') {
                    SetBlank $masked $i $chars[$i]
                    if ($i + 1 -lt $length) { SetBlank $masked ($i + 1) $chars[$i + 1] }
                    $i += 2
                    continue
                }
                if ($chars[$i] -eq '"') {
                    $masked[$i] = '"'
                    $i++
                    break
                }
                SetBlank $masked $i $chars[$i]
                $i++
            }
            continue
        }

        # Character literal.
        if ($current -eq "'") {
            $masked[$i] = "'"
            $i++
            while ($i -lt $length) {
                if ($chars[$i] -eq '\') {
                    SetBlank $masked $i $chars[$i]
                    if ($i + 1 -lt $length) { SetBlank $masked ($i + 1) $chars[$i + 1] }
                    $i += 2
                    continue
                }
                if ($chars[$i] -eq "'") {
                    $masked[$i] = "'"
                    $i++
                    break
                }
                $masked[$i] = ' '
                $i++
            }
            continue
        }

        $masked[$i] = $current
        $i++
    }

    return -join $masked
}

function Read-DocBlock {
    param([string[]]$Lines, [int]$Index)

    $summaries = 0
    $remarks = 0
    $seen = $false
    $j = $Index - 1

    while ($j -ge 0) {
        $trimmed = $Lines[$j].Trim()

        if ($trimmed -eq '') { $j--; continue }
        if ($trimmed.StartsWith('[')) { $j--; continue }
        break
    }

    while ($j -ge 0 -and $Lines[$j].TrimStart().StartsWith('///')) {
        $seen = $true
        if ($Lines[$j] -match '<summary>') { $summaries++ }
        if ($Lines[$j] -match '<remarks>') { $remarks++ }
        $j--
    }

    return [pscustomobject]@{
        Present   = $seen
        Summaries = $summaries
        Remarks   = $remarks
    }
}

$files = Get-ChildItem -Path $sourceRoot -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }

foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $masked = Get-MaskedText -Text $text
    $lines = $masked -split "`r?`n"
    $rawLines = $text -split "`r?`n"
    $relative = $file.FullName.Substring($Root.Length).TrimStart('\', '/')

    $depth = 0
    $types = [System.Collections.Generic.List[object]]::new()
    $pendingType = $null

    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        $trimmed = $line.Trim()

        if ($trimmed -eq '') { continue }

        $isType = $trimmed -match '^(?:[A-Za-z]+\s+)*(?:class|struct|interface|enum|record)\s+[A-Za-z_]' -and
                  $trimmed -notmatch '^\s*(?:new|return|var|await)\b'
        $isMember = $trimmed -match '^(?:public|protected|internal|private)\b'
        $inEnum = $types.Count -gt 0 -and $types[$types.Count - 1].Enum -and $depth -eq $types[$types.Count - 1].BodyDepth

        if ($isMember -or ($inEnum -and $trimmed -match '^[A-Za-z_][A-Za-z0-9_]*\s*(?:=[^,]+)?,?$')) {
            $doc = Read-DocBlock -Lines $lines -Index $i
            $display = $rawLines[$i].Trim()

            if ($doc.Summaries -eq 0) {
                $violations.Add(('{0}:{1}  missing <summary>: {2}' -f $relative, ($i + 1), $display))
            }
            elseif ($doc.Summaries -gt 1) {
                $violations.Add(('{0}:{1}  stacked <summary> blocks: {2}' -f $relative, ($i + 1), $display))
            }

            if ($doc.Remarks -gt 1) {
                $violations.Add(('{0}:{1}  stacked <remarks> blocks: {2}' -f $relative, ($i + 1), $display))
            }

            $publicApi = $false

            if ($trimmed -match '^(public|protected)\b') {
                $publicApi = $true

                if ($types.Count -gt 0 -and -not $types[$types.Count - 1].Public) {
                    $publicApi = $false
                }
            }

            if ($publicApi -and $doc.Summaries -eq 1 -and $doc.Remarks -eq 0) {
                $violations.Add(('{0}:{1}  missing <remarks> on public member: {2}' -f $relative, ($i + 1), $display))
            }
        }

        $opens = ([regex]::Matches($line, '\{')).Count
        $closes = ([regex]::Matches($line, '\}')).Count

        if ($isType) {
            $isPublic = $trimmed -match '^(?:[A-Za-z]+\s+)*public\b'

            if ($opens -gt 0) {
                $types.Add([pscustomobject]@{
                        BodyDepth = $depth + 1
                        Public    = $isPublic
                        Enum      = $trimmed -match '\benum\s+[A-Za-z_]'
                    })
            }
            else {
                $pendingType = [pscustomobject]@{
                    Public = $isPublic
                    Enum   = $trimmed -match '\benum\s+[A-Za-z_]'
                }
            }
        }

        if ($opens -gt 0 -and $pendingType) {
            $types.Add([pscustomobject]@{
                    BodyDepth = $depth + 1
                    Public    = $pendingType.Public
                    Enum      = $pendingType.Enum
                })
            $pendingType = $null
        }

        $depth += $opens - $closes

        while ($types.Count -gt 0 -and $types[$types.Count - 1].BodyDepth -gt $depth) {
            $types.RemoveAt($types.Count - 1)
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host 'Documentation coverage: violations found'
    foreach ($violation in $violations) {
        Write-Host "  $violation"
    }

    Write-Host ''
    Write-Host ("{0} violation(s). See AGENTS.md section 5.1." -f $violations.Count)
    exit 1
}

Write-Host 'Documentation coverage: OK'
exit 0

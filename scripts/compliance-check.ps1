# Repository compliance scanner (PowerShell).
#
# The forbidden brand token is assembled at runtime from character codes so
# that this scanner never stores it. See AGENTS.md section 1.
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$token = -join ([char[]](101, 109, 113, 120))
$prefix = -join ([char[]](101, 109, 113))
$failed = $false

function Report-Failure([string]$Message) {
    Write-Host "  FAIL $Message" -ForegroundColor Red
    $script:failed = $true
}

Write-Host "Compliance scan: forbidden brand tokens"

# 1. Tracked file names.
$files = @(git ls-files)
foreach ($file in $files) {
    if ($file.ToLowerInvariant().Contains($prefix)) {
        Report-Failure "file name contains a forbidden token: $file"
    }
}

# 2. Tracked file contents.
$matches = @(git grep -n -i -I -e $token -- . 2>$null)
if ($LASTEXITCODE -eq 0) {
    foreach ($match in $matches) { Report-Failure "content match: $match" }
}

# 3. Untracked, non-ignored files.
$untracked = @(git ls-files --others --exclude-standard)
foreach ($file in $untracked) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
    $found = Select-String -LiteralPath $file -Pattern $token -SimpleMatch -CaseSensitive:$false -ErrorAction SilentlyContinue
    if ($found) { Report-Failure "content match (untracked): $file" }
}

# 4. Full commit history messages.
$history = git log --format=%B 2>$null | Out-String
if ($history.ToLowerInvariant().Contains($token)) {
    Report-Failure "commit message history contains a forbidden token"
}

# 5. Branch and tag names.
$refs = git for-each-ref --format="%(refname)" 2>$null | Out-String
if ($refs.ToLowerInvariant().Contains($token)) {
    Report-Failure "a branch or tag name contains a forbidden token"
}

if ($failed) {
    Write-Host ""
    Write-Host "Compliance scan failed. Rename or rephrase the offending content." -ForegroundColor Red
    exit 1
}

Write-Host "  OK no forbidden brand tokens found" -ForegroundColor Green

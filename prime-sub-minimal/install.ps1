$ErrorActionPreference = 'Stop'

$kiloRoot = Join-Path $env:USERPROFILE '.config\kilo'
$agentDir = Join-Path $kiloRoot 'agent'
$backupRoot = Join-Path $kiloRoot 'prime-sub-backups'
$sourcePrime = Join-Path $PSScriptRoot 'agents\prime.md'
$sourceSub = Join-Path $PSScriptRoot 'agents\sub.md'
$targetPrime = Join-Path $agentDir 'prime.md'
$targetSub = Join-Path $agentDir 'sub.md'

if (-not (Get-Command kilo -ErrorAction SilentlyContinue)) {
    throw 'Kilo CLI is required but was not found in PATH. Install native Kilo first; this setup does not install or replace Kilo.'
}
if (-not (Test-Path -LiteralPath $sourcePrime) -or -not (Test-Path -LiteralPath $sourceSub)) {
    throw 'Package is incomplete: agents\prime.md or agents\sub.md is missing.'
}

New-Item -ItemType Directory -Path $agentDir -Force | Out-Null

$existing = @($targetPrime, $targetSub) | Where-Object { Test-Path -LiteralPath $_ }
if ($existing.Count -gt 0) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backupDir = Join-Path $backupRoot ("$stamp-" + (Get-Random -Minimum 1000 -Maximum 9999))
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    foreach ($path in $existing) {
        Copy-Item -LiteralPath $path -Destination (Join-Path $backupDir (Split-Path $path -Leaf)) -Force
    }
    Write-Host "Backed up existing Prime/Sub files to: $backupDir"
}

Copy-Item -LiteralPath $sourcePrime -Destination $targetPrime -Force
Copy-Item -LiteralPath $sourceSub -Destination $targetSub -Force

$projectRoot = (Get-Location).Path
if (Get-Command git -ErrorAction SilentlyContinue) {
    $gitRoot = (& git rev-parse --show-toplevel 2>$null | Select-Object -First 1)
    if ($LASTEXITCODE -eq 0 -and $gitRoot) { $projectRoot = $gitRoot.Trim() }
}
$stateDir = Join-Path $projectRoot '.prime'
$statePath = Join-Path $stateDir 'state.json'
if (-not (Test-Path -LiteralPath $statePath)) {
    New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
    '{"objective":{"revision":0,"text":""},"tasks":{},"active":null,"recovery":null,"next":"capture objective","git":null}' | Set-Content -LiteralPath $statePath -Encoding UTF8
    Write-Host "Bootstrapped project state: $statePath"
} else {
    Write-Host "Preserved existing project state: $statePath"
}

Write-Host "Installed Prime: $targetPrime"
Write-Host "Installed Sub:   $targetSub"
Write-Host 'No kilo.json/kilo.jsonc, built-in agent, provider, plugin, session, database, cache, or native Kilo installation was modified.'
Write-Host ''
Write-Host 'Checking effective agent definitions in this project...'

$primeJson = (& kilo debug agent prime | Out-String)
if ($LASTEXITCODE -ne 0) { throw 'kilo debug agent prime failed.' }
$subJson = (& kilo debug agent sub | Out-String)
if ($LASTEXITCODE -ne 0) { throw 'kilo debug agent sub failed. Ensure provider/model 9router/sub is configured.' }
$prime = $primeJson | ConvertFrom-Json
$sub = $subJson | ConvertFrom-Json

if ($prime.mode -ne 'primary') { throw "Prime effective mode is '$($prime.mode)', expected 'primary'. A project override may be taking precedence." }
if ($prime.tools.task -ne $true) { throw 'Prime effective Task tool is disabled; expected access to sub only.' }
if ($sub.mode -ne 'subagent') { throw "Sub effective mode is '$($sub.mode)', expected 'subagent'. A project override may be taking precedence." }
if ($sub.model.providerID -ne '9router' -or $sub.model.modelID -ne 'sub') {
    throw "Sub effective model is '$($sub.model.providerID)/$($sub.model.modelID)', expected exactly '9router/sub'."
}
if ($sub.tools.task -ne $false) { throw 'Sub effective Task tool is enabled; expected disabled.' }

Write-Host 'Agent verification: PASS (Prime primary; Sub subagent; Sub model 9router/sub; Sub cannot spawn).'
Write-Host ''
Write-Host 'This installer intentionally does not edit Kilo config. Follow CONFIG.md and then run its resolved-config checks.'

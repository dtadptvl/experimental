param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'

function Resolve-ProjectRoot {
    param([string]$Requested)
    if ($Requested) {
        return [System.IO.Path]::GetFullPath($Requested)
    }

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($git) {
        $candidate = (& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null | Select-Object -First 1)
        if ($LASTEXITCODE -eq 0 -and $candidate) {
            return [System.IO.Path]::GetFullPath($candidate.Trim())
        }
    }
    return [System.IO.Path]::GetFullPath($PSScriptRoot)
}

function Resolve-KiloConfigRoot {
    $text = (& kilo debug paths | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw "kilo debug paths failed with exit code $LASTEXITCODE."
    }
    $line = @($text -split "`r?`n") | Where-Object { $_ -match '^\s*config\s+' } | Select-Object -First 1
    if (-not $line -or $line -notmatch '^\s*config\s+(.+?)\s*$') {
        throw 'Could not resolve Kilo global config directory from kilo debug paths.'
    }
    return [System.IO.Path]::GetFullPath($Matches[1])
}

function Invoke-KiloJson {
    param([string[]]$Arguments, [string]$Label)
    $text = (& kilo @Arguments | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
    try {
        return $text | ConvertFrom-Json
    } catch {
        throw "$Label did not return valid JSON."
    }
}

function Permission-Action {
    param($Rules, [string]$Permission, [string]$Pattern)
    $action = 'ask'
    foreach ($rule in @($Rules)) {
        $permissionMatch = ($rule.permission -eq '*') -or ($rule.permission -eq $Permission)
        $patternMatch = $Pattern -like $rule.pattern
        if ($permissionMatch -and $patternMatch) {
            $action = $rule.action
        }
    }
    return $action
}

$project = Resolve-ProjectRoot $ProjectRoot
if (-not (Test-Path -LiteralPath $project -PathType Container)) {
    throw "Project root does not exist: $project"
}

$kilo = Get-Command kilo -ErrorAction SilentlyContinue
if (-not $kilo) {
    throw 'Native Kilo is required but was not found in PATH. Install/configure Kilo first, then run setup.cmd again.'
}

$kiloRoot = Resolve-KiloConfigRoot
$agentDir = Join-Path $kiloRoot 'agent'
$backupRoot = Join-Path $kiloRoot 'prime-sub-backups'
$sourcePrime = Join-Path $PSScriptRoot 'agents\prime.md'
$sourceSub = Join-Path $PSScriptRoot 'agents\sub.md'
$stateTemplate = Join-Path $PSScriptRoot 'templates\state.json'
$targetPrime = Join-Path $agentDir 'prime.md'
$targetSub = Join-Path $agentDir 'sub.md'

foreach ($required in @($sourcePrime, $sourceSub, $stateTemplate)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Package is incomplete: missing $required"
    }
}

New-Item -ItemType Directory -Path $agentDir -Force | Out-Null

$backupDir = $null
if ((Test-Path -LiteralPath $targetPrime) -or (Test-Path -LiteralPath $targetSub)) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backupDir = Join-Path $backupRoot ("$stamp-" + (Get-Random -Minimum 1000 -Maximum 9999))
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    if (Test-Path -LiteralPath $targetPrime) { Copy-Item -LiteralPath $targetPrime -Destination (Join-Path $backupDir 'prime.md') -Force }
    if (Test-Path -LiteralPath $targetSub) { Copy-Item -LiteralPath $targetSub -Destination (Join-Path $backupDir 'sub.md') -Force }
}

Copy-Item -LiteralPath $sourcePrime -Destination $targetPrime -Force
Copy-Item -LiteralPath $sourceSub -Destination $targetSub -Force

$stateDir = Join-Path $project '.prime'
$statePath = Join-Path $stateDir 'state.json'
if (-not (Test-Path -LiteralPath $statePath)) {
    New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
    Copy-Item -LiteralPath $stateTemplate -Destination $statePath
    $stateAction = 'created'
} else {
    $stateAction = 'preserved'
}

Push-Location $project
try {
    $config = Invoke-KiloJson -Arguments @('debug', 'config') -Label 'kilo debug config'
    $prime = Invoke-KiloJson -Arguments @('debug', 'agent', 'prime') -Label 'kilo debug agent prime'
    $sub = Invoke-KiloJson -Arguments @('debug', 'agent', 'sub') -Label 'kilo debug agent sub'
} finally {
    Pop-Location
}

$errors = New-Object System.Collections.Generic.List[string]
if ($config.experimental.task_model_selection -ne $true) {
    $errors.Add('experimental.task_model_selection must resolve to true so Prime can override stale per-agent model state on every Task call.')
}
if ($prime.mode -ne 'primary') { $errors.Add("Prime mode resolved to '$($prime.mode)', expected 'primary'.") }
if ((Permission-Action $prime.permission 'task' 'sub') -ne 'allow') { $errors.Add('Prime is not allowed to delegate to sub.') }
if ((Permission-Action $prime.permission 'task' 'general') -ne 'deny') { $errors.Add('Prime Task permission does not deny non-Sub agents.') }
if ($sub.mode -ne 'subagent') { $errors.Add("Sub mode resolved to '$($sub.mode)', expected 'subagent'.") }
if ($sub.model.providerID -ne '9router' -or $sub.model.modelID -ne 'sub') {
    $errors.Add("Effective Sub model resolved to '$($sub.model.providerID)/$($sub.model.modelID)', expected '9router/sub'.")
}
if ($sub.tools.task -ne $false) { $errors.Add('Sub Task tool is enabled; Sub must not spawn agents.') }

Write-Host ''
Write-Host "Installed Prime: $targetPrime"
Write-Host "Installed Sub:   $targetSub"
Write-Host "Project state:   $statePath ($stateAction)"
if ($backupDir) { Write-Host "Previous Prime/Sub files backed up to: $backupDir" }

if ($errors.Count -gt 0) {
    Write-Host ''
    Write-Host 'Installation completed, but effective Kilo configuration FAILED verification:' -ForegroundColor Red
    foreach ($item in $errors) { Write-Host " - $item" -ForegroundColor Red }
    Write-Host "Apply the minimal Human-owned edits in: $(Join-Path $PSScriptRoot 'CONFIG-GUIDE.md')"
    Write-Host 'Then run setup.cmd again. Setup does not edit Kilo configuration or built-in agents.'
    exit 1
}

Write-Host ''
Write-Host 'PASS: resolved Prime/Sub topology is valid and Sub resolves to exactly 9router/sub.' -ForegroundColor Green
Write-Host 'Setup did not edit Kilo configuration or built-in agents.'
exit 0

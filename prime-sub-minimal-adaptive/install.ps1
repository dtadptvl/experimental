$ErrorActionPreference = 'Stop'

function Resolve-KiloConfigRoot {
    $text = (& kilo debug paths | Out-String)
    if ($LASTEXITCODE -ne 0) { throw "kilo debug paths failed with exit code $LASTEXITCODE." }
    $line = @($text -split "`r?`n") | Where-Object { $_ -match '^\s*config\s+' } | Select-Object -First 1
    if (-not $line -or $line -notmatch '^\s*config\s+(.+?)\s*$') {
        throw 'Could not resolve Kilo global config directory from kilo debug paths.'
    }
    return [System.IO.Path]::GetFullPath($Matches[1])
}

function Invoke-KiloJson {
    param([string[]]$Arguments, [string]$Label)
    $text = (& kilo @Arguments | Out-String)
    if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE." }
    try { return $text | ConvertFrom-Json } catch { throw "$Label did not return valid JSON." }
}

function Permission-Action {
    param($Rules, [string]$Permission, [string]$Pattern)
    $action = 'ask'
    foreach ($rule in @($Rules)) {
        $permissionMatch = ($rule.permission -eq '*') -or ($rule.permission -eq $Permission)
        $patternMatch = $Pattern -like $rule.pattern
        if ($permissionMatch -and $patternMatch) { $action = $rule.action }
    }
    return $action
}

function Positive-FiniteNumber {
    param($Value)
    if ($null -eq $Value) { return $false }
    $number = 0.0
    if (-not [double]::TryParse([string]$Value, [ref]$number)) { return $false }
    return ($number -gt 0 -and -not [double]::IsInfinity($number) -and -not [double]::IsNaN($number))
}

$kilo = Get-Command kilo -ErrorAction SilentlyContinue
if (-not $kilo) {
    throw 'Native Kilo is required but was not found in PATH. Install/configure Kilo first, then run setup.cmd again.'
}

$kiloRoot = Resolve-KiloConfigRoot
$agentDir = Join-Path $kiloRoot 'agents'
$backupRoot = Join-Path $kiloRoot 'prime-sub-backups'
$sourcePrime = Join-Path $PSScriptRoot 'agents\prime.md'
$sourceSub = Join-Path $PSScriptRoot 'agents\sub.md'
$targetPrime = Join-Path $agentDir 'prime.md'
$targetSub = Join-Path $agentDir 'sub.md'

foreach ($required in @($sourcePrime, $sourceSub)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Package is incomplete: missing $required" }
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

$config = Invoke-KiloJson -Arguments @('debug', 'config') -Label 'kilo debug config'
$prime = Invoke-KiloJson -Arguments @('debug', 'agent', 'prime') -Label 'kilo debug agent prime'
$sub = Invoke-KiloJson -Arguments @('debug', 'agent', 'sub') -Label 'kilo debug agent sub'

$errors = New-Object System.Collections.Generic.List[string]
if ($config.experimental.task_model_selection -ne $true) {
    $errors.Add('experimental.task_model_selection must resolve to true so Prime can force 9router/sub despite stale per-agent CLI model state.')
}
$routerOptions = $config.provider.'9router'.options
if (-not (Positive-FiniteNumber $routerOptions.timeout)) {
    $errors.Add('provider.9router.options.timeout must be an explicit positive finite millisecond value.')
}
if (-not (Positive-FiniteNumber $routerOptions.chunkTimeout)) {
    $errors.Add('provider.9router.options.chunkTimeout must be an explicit positive finite millisecond value.')
}
if ($prime.mode -ne 'primary') { $errors.Add("Prime mode resolved to '$($prime.mode)', expected 'primary'.") }
if ((Permission-Action $prime.permission 'task' 'sub') -ne 'allow') { $errors.Add('Prime is not allowed to delegate to sub.') }
if ((Permission-Action $prime.permission 'task' 'general') -ne 'deny') { $errors.Add('Prime Task permission does not deny non-Sub agents.') }
if ($sub.mode -ne 'subagent') { $errors.Add("Sub mode resolved to '$($sub.mode)', expected 'subagent'.") }
if ($sub.model.providerID -ne '9router' -or $sub.model.modelID -ne 'sub') {
    $errors.Add("Effective Sub model resolved to '$($sub.model.providerID)/$($sub.model.modelID)', expected '9router/sub'.")
}
if (-not (Positive-FiniteNumber $sub.steps)) { $errors.Add('Sub steps is not a positive finite native loop fuse.') }
if ($sub.tools.task -ne $false) { $errors.Add('Sub Task tool is enabled; Sub must not spawn agents.') }
if ((Permission-Action $sub.permission 'doom_loop' '*') -ne 'deny') { $errors.Add('Sub doom_loop permission must resolve to deny under --auto.') }
if ((Permission-Action $sub.permission 'bash' 'git commit -m x') -ne 'deny') { $errors.Add('Sub can run Git mutation commands; expected git commit to be denied.') }
if ((Permission-Action $sub.permission 'bash' 'git status --short') -ne 'allow') { $errors.Add('Sub cannot perform read-only git status inspection.') }

Write-Host ''
Write-Host "Installed Prime: $targetPrime"
Write-Host "Installed Sub:   $targetSub"
Write-Host 'Project state:   created lazily by Prime as .prime\state.json on first run in each project'
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
Write-Host 'PASS: resolved Prime/Sub topology, Sub model, and native hang/loop guards are valid.' -ForegroundColor Green
Write-Host 'Run Kilo inside any target project with --auto --agent prime; Prime creates .prime\state.json there if absent.'
Write-Host 'Setup did not edit Kilo configuration or built-in agents.'
exit 0

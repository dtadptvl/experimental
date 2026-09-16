$ErrorActionPreference = 'Stop'

$kiloRoot = Join-Path $env:USERPROFILE '.config\kilo'
$agentDir = Join-Path $kiloRoot 'agent'
$backupRoot = Join-Path $kiloRoot 'prime-sub-backups'
$sourcePrime = Join-Path $PSScriptRoot 'agents\prime.md'
$sourceSub = Join-Path $PSScriptRoot 'agents\sub.md'
$targetPrime = Join-Path $agentDir 'prime.md'
$targetSub = Join-Path $agentDir 'sub.md'
$stagePrime = Join-Path $agentDir 'prime.md.primesub-new'
$stageSub = Join-Path $agentDir 'sub.md.primesub-new'
$backupDir = $null
$installedKilo = $false
$hadPrime = Test-Path -LiteralPath $targetPrime
$hadSub = Test-Path -LiteralPath $targetSub

function Restore-Agents {
    param([string]$BackupDir)

    if ($hadPrime) {
        if (-not $BackupDir) { throw 'Prime rollback backup is unavailable.' }
        Copy-Item -LiteralPath (Join-Path $BackupDir 'prime.md') -Destination $targetPrime -Force
    } elseif (Test-Path -LiteralPath $targetPrime) {
        Remove-Item -LiteralPath $targetPrime -Force
    }

    if ($hadSub) {
        if (-not $BackupDir) { throw 'Sub rollback backup is unavailable.' }
        Copy-Item -LiteralPath (Join-Path $BackupDir 'sub.md') -Destination $targetSub -Force
    } elseif (Test-Path -LiteralPath $targetSub) {
        Remove-Item -LiteralPath $targetSub -Force
    }
}

try {
    if (-not (Test-Path -LiteralPath $sourcePrime) -or -not (Test-Path -LiteralPath $sourceSub)) {
        throw 'Package is incomplete: agents\prime.md or agents\sub.md is missing.'
    }

    # Preserve an existing Kilo installation exactly as-is. Install only when the
    # kilo executable is not available at all.
    if (-not (Get-Command kilo -ErrorAction SilentlyContinue)) {
        if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
            throw 'Kilo is not installed and npm was not found in PATH.'
        }

        Write-Host 'Kilo CLI was not found. Installing @kilocode/cli@latest...'
        & npm install -g '@kilocode/cli@latest'
        if ($LASTEXITCODE -ne 0) {
            throw "Kilo installation failed with exit code $LASTEXITCODE."
        }
        $installedKilo = $true
    }

    New-Item -ItemType Directory -Path $agentDir -Force | Out-Null

    if ($hadPrime -or $hadSub) {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $backupDir = Join-Path $backupRoot ("$stamp-" + (Get-Random -Minimum 1000 -Maximum 9999))
        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        if ($hadPrime) { Copy-Item -LiteralPath $targetPrime -Destination (Join-Path $backupDir 'prime.md') -Force }
        if ($hadSub) { Copy-Item -LiteralPath $targetSub -Destination (Join-Path $backupDir 'sub.md') -Force }
    }

    # Stage both files before replacing either target. If replacement fails,
    # restore both targets from the backup (or remove newly-created targets).
    Copy-Item -LiteralPath $sourcePrime -Destination $stagePrime -Force
    Copy-Item -LiteralPath $sourceSub -Destination $stageSub -Force

    try {
        Move-Item -LiteralPath $stagePrime -Destination $targetPrime -Force
        Move-Item -LiteralPath $stageSub -Destination $targetSub -Force
    } catch {
        Restore-Agents -BackupDir $backupDir
        throw
    } finally {
        Remove-Item -LiteralPath $stagePrime -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $stageSub -Force -ErrorAction SilentlyContinue
    }

    Write-Host ''
    if ($installedKilo) {
        Write-Host 'Installed Kilo because no existing kilo executable was found.'
    } else {
        Write-Host 'Existing Kilo installation was preserved unchanged.'
    }
    Write-Host "Installed Prime: $targetPrime"
    Write-Host "Installed Sub:   $targetSub"
    if ($backupDir) { Write-Host "Previous Prime/Sub agents were backed up to: $backupDir" }
    Write-Host ''
    Write-Host 'Preserved: kilo.json/jsonc, plugins, auth, sessions, state, cache, provider data,'
    Write-Host 'built-in/custom agents other than prime/sub, environment overrides, and running Kilo processes.'
    Write-Host 'Running sessions may keep their already-loaded prompt; start a new Kilo session to use the new definitions.'
    Write-Host 'Review CONFIG.md only if your existing config does not already select Prime and expose 9router/sub.'
    exit 0
} catch {
    Remove-Item -LiteralPath $stagePrime -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stageSub -Force -ErrorAction SilentlyContinue
    Write-Error $_.Exception.Message
    if ($backupDir) { Write-Host "Backup remains at: $backupDir" }
    Write-Host 'No Kilo config, plugin, auth, session, state, cache, provider, or environment data was deleted.'
    exit 1
}

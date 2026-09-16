param(
    [Parameter(Mandatory = $true)]
    [string]$HostPath
)

$ErrorActionPreference = 'Stop'

function Read-Line-With-Timeout($reader, [string]$stage, [int]$milliseconds = 10000) {
    $task = $reader.ReadLineAsync()
    if (-not $task.Wait($milliseconds)) {
        throw "[$stage] Timed out waiting for SessionHost pipe message."
    }
    if ($null -eq $task.Result) {
        throw "[$stage] SessionHost pipe closed unexpectedly."
    }
    return $task.Result
}

function Connect-HostPipe([string]$pipeName, [string]$stage) {
    Write-Host "[$stage] Connecting to $pipeName"
    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
    $pipe.Connect(10000)
    $reader = [System.IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 4096, $true)
    $writer = [System.IO.StreamWriter]::new($pipe, [Text.Encoding]::UTF8, 4096, $true)
    $writer.AutoFlush = $true
    Write-Host "[$stage] Connected"
    return @($pipe, $reader, $writer)
}

function Read-Initial-State($reader, [string]$stage) {
    $hello = (Read-Line-With-Timeout $reader "$stage/hello" | ConvertFrom-Json)
    $snapshot = (Read-Line-With-Timeout $reader "$stage/snapshot" | ConvertFrom-Json)
    $status = (Read-Line-With-Timeout $reader "$stage/status" | ConvertFrom-Json)
    return @($hello, $snapshot, $status)
}

$instance = [guid]::NewGuid().ToString('N')
$pipeName = "kilo-manager-smoke-$instance"
$configPath = Join-Path $env:RUNNER_TEMP "kilo-host-$instance.json"
$stdoutPath = Join-Path $env:RUNNER_TEMP "kilo-host-$instance.stdout.log"
$stderrPath = Join-Path $env:RUNNER_TEMP "kilo-host-$instance.stderr.log"
$config = @{
    projectId = 'smoke-project'
    instanceId = $instance
    pipeName = $pipeName
    workingDirectory = $env:RUNNER_TEMP
    executable = $env:COMSPEC
    arguments = '/d /q'
    ringBufferBytes = 1048576
} | ConvertTo-Json
Set-Content -Path $configPath -Value $config -Encoding UTF8

Write-Host '[start] Launching SessionHost'
$hostProcess = Start-Process -FilePath $HostPath -ArgumentList @('--config', $configPath) -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

try {
    $conn = Connect-HostPipe $pipeName 'attach-1'
    $pipe = $conn[0]; $reader = $conn[1]; $writer = $conn[2]
    $initial = Read-Initial-State $reader 'attach-1'
    $hello = $initial[0]; $snapshot = $initial[1]; $status = $initial[2]

    if ($hello.type -ne 'hello' -or $hello.projectId -ne 'smoke-project' -or $hello.instanceId -ne $instance) {
        throw "[attach-1] Invalid handshake: $($hello | ConvertTo-Json -Compress)"
    }
    if ($snapshot.type -ne 'snapshot' -or $status.type -ne 'status' -or -not $status.running) {
        throw '[attach-1] Invalid snapshot/status sequence.'
    }
    Write-Host '[attach-1] Handshake OK'

    $input = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("echo KM_SMOKE_FIRST`r`n"))
    $writer.WriteLine((@{ type = 'input'; data = $input } | ConvertTo-Json -Compress))
    Write-Host '[io] Sent terminal input'

    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $found = $false
    while (-not $found -and [DateTime]::UtcNow -lt $deadline) {
        $msg = (Read-Line-With-Timeout $reader 'io/output' 5000 | ConvertFrom-Json)
        if ($msg.type -eq 'output' -and $msg.data) {
            $text = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($msg.data))
            if ($text -match 'KM_SMOKE_FIRST') { $found = $true }
        }
    }
    if (-not $found) { throw '[io] ConPTY output did not contain the smoke token.' }
    Write-Host '[io] ConPTY input/output OK'

    $writer.Dispose(); $reader.Dispose(); $pipe.Dispose()
    Write-Host '[detach] First client disconnected; host must remain alive'
    Start-Sleep -Milliseconds 300
    if ($hostProcess.HasExited) { throw '[detach] SessionHost exited when client disconnected.' }

    $conn2 = Connect-HostPipe $pipeName 'attach-2'
    $pipe2 = $conn2[0]; $reader2 = $conn2[1]; $writer2 = $conn2[2]
    $initial2 = Read-Initial-State $reader2 'attach-2'
    $snapshot2 = $initial2[1]
    $snapshotText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($snapshot2.data))
    if ($snapshotText -notmatch 'KM_SMOKE_FIRST') {
        throw '[attach-2] Reconnect snapshot did not preserve detached terminal output.'
    }
    Write-Host '[attach-2] Reconnect and ring-buffer snapshot OK'

    $writer2.WriteLine((@{ type = 'terminate' } | ConvertTo-Json -Compress))
    $writer2.Dispose(); $reader2.Dispose(); $pipe2.Dispose()
    Write-Host '[terminate] Terminate requested'

    if (-not $hostProcess.WaitForExit(15000)) {
        throw '[terminate] SessionHost did not exit within 15 seconds.'
    }
    if ($hostProcess.ExitCode -ne 0) {
        throw "[terminate] SessionHost exited with code $($hostProcess.ExitCode)."
    }

    Write-Host 'SessionHost smoke test passed: ConPTY I/O, detach survival, reconnect snapshot, and terminate.'
}
catch {
    Write-Host "SMOKE FAILURE: $($_.Exception.Message)"
    if (Test-Path $stdoutPath) {
        Write-Host '--- SessionHost stdout ---'
        Get-Content $stdoutPath -ErrorAction SilentlyContinue
    }
    if (Test-Path $stderrPath) {
        Write-Host '--- SessionHost stderr ---'
        Get-Content $stderrPath -ErrorAction SilentlyContinue
    }
    throw
}
finally {
    if (-not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $configPath, $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
}

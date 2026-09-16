param(
    [Parameter(Mandatory = $true)]
    [string]$HostPath
)

$ErrorActionPreference = 'Stop'

function Read-Line-With-Timeout($reader, [int]$milliseconds = 5000) {
    $task = $reader.ReadLineAsync()
    if (-not $task.Wait($milliseconds)) {
        throw "Timed out waiting for SessionHost pipe message."
    }
    if ($null -eq $task.Result) {
        throw "SessionHost pipe closed unexpectedly."
    }
    return $task.Result
}

function Connect-HostPipe([string]$pipeName) {
    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
    $pipe.Connect(5000)
    $reader = [System.IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 4096, $true)
    $writer = [System.IO.StreamWriter]::new($pipe, [Text.Encoding]::UTF8, 4096, $true)
    $writer.AutoFlush = $true
    return @($pipe, $reader, $writer)
}

$instance = [guid]::NewGuid().ToString('N')
$pipeName = "kilo-manager-smoke-$instance"
$configPath = Join-Path $env:RUNNER_TEMP "kilo-host-$instance.json"
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

$hostProcess = Start-Process -FilePath $HostPath -ArgumentList @('--config', $configPath) -PassThru -WindowStyle Hidden

try {
    $conn = Connect-HostPipe $pipeName
    $pipe = $conn[0]; $reader = $conn[1]; $writer = $conn[2]

    $hello = (Read-Line-With-Timeout $reader | ConvertFrom-Json)
    if ($hello.type -ne 'hello' -or $hello.projectId -ne 'smoke-project' -or $hello.instanceId -ne $instance) {
        throw "Invalid SessionHost handshake: $($hello | ConvertTo-Json -Compress)"
    }

    $snapshot = (Read-Line-With-Timeout $reader | ConvertFrom-Json)
    $status = (Read-Line-With-Timeout $reader | ConvertFrom-Json)
    if ($snapshot.type -ne 'snapshot' -or $status.type -ne 'status' -or -not $status.running) {
        throw 'Invalid initial snapshot/status sequence.'
    }

    $input = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("echo KM_SMOKE_FIRST`r`n"))
    $writer.WriteLine((@{ type = 'input'; data = $input } | ConvertTo-Json -Compress))

    $found = $false
    for ($i = 0; $i -lt 20 -and -not $found; $i++) {
        $msg = (Read-Line-With-Timeout $reader | ConvertFrom-Json)
        if ($msg.type -eq 'output' -and $msg.data) {
            $text = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($msg.data))
            if ($text -match 'KM_SMOKE_FIRST') { $found = $true }
        }
    }
    if (-not $found) { throw 'ConPTY output did not contain the smoke token.' }

    $writer.Dispose(); $reader.Dispose(); $pipe.Dispose()

    $conn2 = Connect-HostPipe $pipeName
    $pipe2 = $conn2[0]; $reader2 = $conn2[1]; $writer2 = $conn2[2]
    $null = Read-Line-With-Timeout $reader2 | ConvertFrom-Json
    $snapshot2 = (Read-Line-With-Timeout $reader2 | ConvertFrom-Json)
    $null = Read-Line-With-Timeout $reader2 | ConvertFrom-Json
    $snapshotText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($snapshot2.data))
    if ($snapshotText -notmatch 'KM_SMOKE_FIRST') {
        throw 'Reconnect snapshot did not preserve detached terminal output.'
    }

    $writer2.WriteLine((@{ type = 'terminate' } | ConvertTo-Json -Compress))
    $writer2.Dispose(); $reader2.Dispose(); $pipe2.Dispose()

    if (-not $hostProcess.WaitForExit(10000)) {
        throw 'SessionHost did not exit after terminate.'
    }
    if ($hostProcess.ExitCode -ne 0) {
        throw "SessionHost exited with code $($hostProcess.ExitCode)."
    }

    Write-Host 'SessionHost smoke test passed: ConPTY, detach/reconnect snapshot, and terminate.'
}
finally {
    if (-not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $configPath -Force -ErrorAction SilentlyContinue
}

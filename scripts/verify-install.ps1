param([Parameter(Mandatory)][string]$Msi, [string]$Exe)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This destructive install/recovery test is restricted to disposable GitHub-hosted CI runners.' }
function Install([string[]]$Arguments) {
    $process = Start-Process msiexec.exe -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -notin 0,3010) { throw "Windows Installer failed: $($process.ExitCode)" }
}
function Rpc([string]$Method = 'snapshot') {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'MkRgb.Hardware.v1', [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(10000)
        $bytes = [Text.Encoding]::UTF8.GetBytes((@{ Method=$Method; Payload=''; Version=1 } | ConvertTo-Json -Compress))
        $pipe.Write([BitConverter]::GetBytes($bytes.Length)); $pipe.Write($bytes); $pipe.Flush()
        $reader = [IO.BinaryReader]::new($pipe)
        $count = $reader.ReadInt32()
        if($count -lt 1 -or $count -gt 4000000) { throw 'Invalid snapshot length' }
        $result = [Text.Encoding]::UTF8.GetString($reader.ReadBytes($count)) | ConvertFrom-Json
        if(-not $result.Success) { throw $result.Error }
        return ($result.Payload | ConvertFrom-Json)
    } finally { $pipe.Dispose() }
}
function Snapshot { if (-not (Rpc).At) { throw 'Snapshot is missing its timestamp' } }
function RgbReady {
    # Regression: a clean install used to report success although --config did not exist.
    if (-not (Rpc 'startRgb')) { throw 'RGB backend failed to start' }
    if (-not (Test-Path (Join-Path $env:ProgramData 'MK RGB/OpenRGB'))) { throw 'RGB configuration directory was not created' }
    $client = [Net.Sockets.TcpClient]::new()
    try { $client.Connect('127.0.0.1', 6743) } finally { $client.Dispose() }
}
$full = (Resolve-Path -LiteralPath $Msi).Path
$logs = Join-Path $PWD 'artifacts/install-logs'
New-Item -ItemType Directory -Force $logs | Out-Null
try {
    Install @('/i', ('"'+$full+'"'), '/qn', '/norestart', '/L*v', ('"'+$logs+'\install.log"'))
    (Get-Service MkRgbHardware).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    Snapshot
    RgbReady
    Install @('/fa', ('"'+$full+'"'), '/qn', '/norestart', '/L*v', ('"'+$logs+'\repair.log"'))
    (Get-Service MkRgbHardware).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    Snapshot
    $service = Get-CimInstance Win32_Service -Filter "Name='MkRgbHardware'"
    # No cooling policies are enabled. Abruptly terminate only this newly installed service.
    Stop-Process -Id $service.ProcessId -Force
    $deadline = (Get-Date).AddSeconds(40)
    do {
        Start-Sleep -Seconds 2
        $recovered = Get-CimInstance Win32_Service -Filter "Name='MkRgbHardware'"
    } while (($recovered.State -ne 'Running' -or $recovered.ProcessId -eq $service.ProcessId) -and (Get-Date) -lt $deadline)
    if($recovered.State -ne 'Running' -or $recovered.ProcessId -eq $service.ProcessId) { throw 'Service failed to recover after termination' }
    Snapshot
} finally {
    Install @('/x', ('"'+$full+'"'), '/qn', '/norestart', '/L*v', ('"'+$logs+'\uninstall.log"'))
}
if(Get-Service MkRgbHardware -ErrorAction SilentlyContinue) { throw 'Service remained after uninstall' }
if(Test-Path (Join-Path $env:ProgramFiles 'MK RGB/MK RGB.exe')) { throw 'Application remained after uninstall' }
Write-Output 'Install, pipe snapshot, repair, crash recovery, and uninstall passed. No hardware control commands were issued.'
if ($Exe) {
    $bundle = (Resolve-Path -LiteralPath $Exe).Path
    function RunBundle([string]$Action) {
        $process = Start-Process -FilePath $bundle -ArgumentList @($Action, '/quiet', '/norestart', '/log', ('"'+$logs+'\bundle-'+$Action.TrimStart('/')+'.log"')) -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -notin 0,3010) { throw "EXE installer $Action failed: $($process.ExitCode)" }
    }
    try {
        RunBundle '/install'
        (Get-Service MkRgbHardware).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        Snapshot
        RgbReady
        RunBundle '/repair'
        Snapshot
    } finally { RunBundle '/uninstall' }
    if (Get-Service MkRgbHardware -ErrorAction SilentlyContinue) { throw 'Service remained after EXE uninstall' }
    Write-Output 'EXE install, service snapshot, repair, and uninstall passed.'
}

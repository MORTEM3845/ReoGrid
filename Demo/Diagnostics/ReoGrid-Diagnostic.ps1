[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('start', 'status', 'state', 'screenshot', 'request', 'stop')]
    [string]$Action,
    [string]$Command,
    [string]$Address,
    [string]$Value,
    [string]$Range,
    [string]$SessionDirectory
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$exe = Join-Path $repo 'Demo\bin\Debug\net8.0-windows7.0\ReoGridDemo.exe'
$source = Join-Path $repo 'Demo\Demo.csproj'
$supervisorProject = Join-Path $PSScriptRoot 'Supervisor\ReoGridDiagnosticSupervisor.csproj'
$supervisor = Join-Path $PSScriptRoot 'Supervisor\bin\Debug\net8.0-windows7.0\ReoGridDiagnosticSupervisor.exe'
$checkout = (Get-FileHash -Algorithm SHA256 -InputStream ([IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes($repo.ToLowerInvariant())))).Hash.Substring(0, 12)
if (-not $SessionDirectory) {
    $SessionDirectory = Join-Path $env:LOCALAPPDATA "ReoGrid\Diagnostic\$checkout"
}
$descriptorPath = Join-Path $SessionDirectory 'session.json'

function Read-Session {
    if (-not (Test-Path -LiteralPath $descriptorPath)) { return $null }
    try { return Get-Content -LiteralPath $descriptorPath -Raw | ConvertFrom-Json }
    catch { throw "Invalid diagnostic session descriptor: $descriptorPath. $($_.Exception.Message)" }
}

function Invoke-Pipe([object]$Session, [hashtable]$Payload) {
    $client = [IO.Pipes.NamedPipeClientStream]::new('.', [string]$Session.pipe, [IO.Pipes.PipeDirection]::InOut)
    try {
        $client.Connect(3000)
        $writer = [IO.StreamWriter]::new($client, [Text.Encoding]::UTF8, 1024, $true)
        $reader = [IO.StreamReader]::new($client, [Text.Encoding]::UTF8, $false, 1024, $true)
        $writer.AutoFlush = $true
        $writer.WriteLine(($Payload | ConvertTo-Json -Compress -Depth 8))
        $response = $reader.ReadLine() | ConvertFrom-Json
        if ($response.status -ne 'completed') { throw "Diagnostic request failed: $($response.error)" }
        if ($Payload.command -eq 'health' -and ($response.result.pid -ne $Session.pid -or $response.result.build -ne $Session.build)) {
            throw 'The named pipe answered from a different process or build than the session descriptor.'
        }
        return $response.result
    }
    finally { $client.Dispose() }
}

function Test-SessionProcess([object]$Session) {
    $process = Get-CimInstance Win32_Process -Filter ("ProcessId = {0}" -f [int]$Session.pid) -ErrorAction SilentlyContinue
    if (-not $process -or -not $process.ExecutablePath) { return $false }
    $expectedPath = [IO.Path]::GetFullPath([string]$Session.executable)
    $actualPath = [IO.Path]::GetFullPath([string]$process.ExecutablePath)
    $commandLine = [string]$process.CommandLine
    return $actualPath.Equals($expectedPath, [StringComparison]::OrdinalIgnoreCase) -and
        $commandLine.Contains('--diagnostic') -and $commandLine.Contains([string]$Session.profile)
}

switch ($Action) {
    'start' {
        $session = Read-Session
        if ($session) {
            try {
                $health = Invoke-Pipe $session @{ command = 'health' }
                Write-Output ("Already running: PID {0}, {1}" -f $session.pid, $session.executable)
                $health | ConvertTo-Json -Depth 8
                exit 0
            }
            catch {
                if (Test-SessionProcess $session) { throw "Diagnostic process PID $($session.pid) is alive but its channel did not answer. Inspect $SessionDirectory. No replacement was started." }
                Remove-Item -LiteralPath $descriptorPath -Force
            }
        }
        dotnet build $source --configuration Debug -p:Platform=AnyCPU -clp:ErrorsOnly
        if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
        dotnet build $supervisorProject --configuration Debug -p:Platform=AnyCPU --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw "Desktop supervisor build failed with exit code $LASTEXITCODE" }
        New-Item -ItemType Directory -Path $SessionDirectory -Force | Out-Null
        $process = Start-Process -FilePath $supervisor -ArgumentList @(('"{0}"' -f $exe), ('"{0}"' -f $SessionDirectory)) -PassThru -WindowStyle Hidden
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 250
            $session = Read-Session
            if ($session) {
                try {
                    $health = Invoke-Pipe $session @{ command = 'health' }
                    Write-Output ("Ready: PID {0}; profile {1}" -f $session.pid, $SessionDirectory)
                    $health | ConvertTo-Json -Depth 8
                    exit 0
                }
                catch { }
            }
            if ($process.HasExited) {
                $logs = @('supervisor.log', 'diagnostic-error.log') | ForEach-Object {
                    $path = Join-Path $SessionDirectory $_
                    if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw }
                }
                throw "Diagnostic supervisor exited with code $($process.ExitCode). $($logs -join [Environment]::NewLine) Inspect $SessionDirectory."
            }
        } while ([DateTime]::UtcNow -lt $deadline)
        throw "Diagnostic host did not become ready. Process PID $($process.Id); inspect $SessionDirectory."
    }
    'status' {
        $session = Read-Session
        if (-not $session) { Write-Output 'stopped'; exit 1 }
        $health = Invoke-Pipe $session @{ command = 'health' }
        [pscustomobject]@{ status = 'ready'; pid = $session.pid; executable = $session.executable; profile = $session.profile; build = $session.build; health = $health } | ConvertTo-Json -Depth 8
    }
    'state' { $session = Read-Session; if (-not $session) { throw 'No diagnostic session is running.' }; Invoke-Pipe $session @{ command = 'state' } | ConvertTo-Json -Depth 12 }
    'screenshot' { $session = Read-Session; if (-not $session) { throw 'No diagnostic session is running.' }; Invoke-Pipe $session @{ command = 'screenshot' } | ConvertTo-Json -Depth 8 }
    'request' {
        if (-not $Command) { throw 'request requires -Command setCell or select.' }
        $payload = @{ command = $Command }
        if ($Command -eq 'setCell') { if (-not $Address) { throw 'setCell requires -Address.' }; $payload.address = $Address; $payload.value = $Value }
        elseif ($Command -eq 'select') { if (-not $Range) { throw 'select requires -Range.' }; $payload.range = $Range }
        else { throw "Unsupported request command: $Command" }
        $session = Read-Session
        if (-not $session) { throw 'No diagnostic session is running.' }
        Invoke-Pipe $session $payload | ConvertTo-Json -Depth 8
    }
    'stop' {
        $session = Read-Session
        if (-not $session) { Write-Output 'stopped'; exit 0 }
        Invoke-Pipe $session @{ command = 'stop' } | ConvertTo-Json -Depth 8
        $process = Get-Process -Id ([int]$session.pid) -ErrorAction SilentlyContinue
        if ($process) { $process.WaitForExit(10000) | Out-Null }
        if (Test-Path -LiteralPath $descriptorPath) { throw "Process did not remove its descriptor. Inspect PID $($session.pid) and $SessionDirectory." }
        Write-Output 'stopped'
    }
}

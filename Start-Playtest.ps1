[CmdletBinding()]
param(
    [switch]$NoUnity,
    [string]$UnityEditorPath
)

$ErrorActionPreference = 'Stop'
$composePath = Join-Path $PSScriptRoot 'DockerServer/docker-compose.yml'
$projectPath = Join-Path $PSScriptRoot 'ScaspUnityGame'

function Test-DockerReady {
    # Docker reports startup failures on stderr; keep polling until it is ready.
    $ErrorActionPreference = 'Continue'
    & docker info --format '{{.ServerVersion}}' *> $null
    return $LASTEXITCODE -eq 0
}

function Test-ServerPort {
    $connection = New-Object System.Net.Sockets.TcpClient
    try {
        $pendingConnection = $connection.ConnectAsync('localhost', 6767)
        return $pendingConnection.Wait(500) -and $connection.Connected
    }
    catch { return $false }
    finally { $connection.Dispose() }
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker was not found. Install Docker Desktop, then reopen PowerShell.'
}

if (-not (Test-DockerReady)) {
    $desktopPath = Join-Path $env:ProgramFiles 'Docker/Docker/Docker Desktop.exe'
    if (-not (Test-Path -LiteralPath $desktopPath)) {
        throw 'Docker is not running. Start your Docker engine, then run this script again.'
    }
    Write-Host 'Starting Docker Desktop...'
    Start-Process -FilePath $desktopPath -WindowStyle Hidden
    $dockerDeadline = (Get-Date).AddMinutes(3)
    while (-not (Test-DockerReady)) {
        if ((Get-Date) -gt $dockerDeadline) {
            throw 'Docker did not become ready within 3 minutes. Check Docker Desktop.'
        }
        Start-Sleep -Seconds 2
    }
}

Write-Host 'Building and starting the Python + s(CASP) server (first build can take several minutes)...'
& docker compose -f $composePath up --build -d gamescasp
if ($LASTEXITCODE -ne 0) {
    throw 'Server startup failed. See the Docker output above. Check for another container using port 6767.'
}

$serverDeadline = (Get-Date).AddSeconds(60)
while (-not (Test-ServerPort)) {
    if ((Get-Date) -gt $serverDeadline) {
        & docker compose -f $composePath logs --tail 30 gamescasp
        throw 'The server did not open port 6767 within 60 seconds. See the logs above.'
    }
    Start-Sleep -Seconds 1
}
Write-Host 'Server is listening at ws://localhost:6767.'

if (-not $NoUnity) {
    $unityLockPath = Join-Path $projectPath 'Temp/UnityLockfile'
    if (Test-Path -LiteralPath $unityLockPath) {
        Write-Host 'Unity project lock found; leaving the existing Editor session in place.'
    }
    else {
        if (-not $UnityEditorPath) {
            $versionLine = Get-Content (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt') |
                Where-Object { $_ -match '^m_EditorVersion: ' } | Select-Object -First 1
            $editorVersion = ($versionLine -replace '^m_EditorVersion: ', '').Trim()
            $UnityEditorPath = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$editorVersion/Editor/Unity.exe"
        }
        if (-not (Test-Path -LiteralPath $UnityEditorPath)) {
            throw 'Server is running, but Unity was not found. Rerun with -UnityEditorPath pointing to Unity.exe, or open the project in Unity Hub.'
        }
        Start-Process -FilePath $UnityEditorPath -ArgumentList "-projectPath `"$projectPath`"" -WindowStyle Hidden
        Write-Host 'Opening Unity. Open the Main scene, enter Play Mode, then click Start Game.'
    }
}

Write-Host 'If Play Mode was already running before the server started, stop and restart Play Mode to reconnect.'
Write-Host 'Server logs: docker compose -f DockerServer/docker-compose.yml logs -f gamescasp'
Write-Host 'Stop server: docker compose -f DockerServer/docker-compose.yml stop gamescasp'

param([switch]$Online)
$ErrorActionPreference = 'Stop'
$pulseProject = Split-Path -Parent $PSScriptRoot
$pulseExe = Join-Path $pulseProject 'Builds\Windows\PulseArena.exe'
$pulseQA = Join-Path $pulseProject $(if ($Online) { 'QA\Online' } else { 'QA\Local' })
New-Item -ItemType Directory -Force -Path $pulseQA | Out-Null
if ($Online -and (Test-Path -LiteralPath (Join-Path $pulseQA 'room.txt'))) { Remove-Item -LiteralPath (Join-Path $pulseQA 'room.txt') }
$pulseHostMode = if ($Online) { 'online-host' } else { 'host' }
$pulseClientMode = if ($Online) { 'online-client' } else { 'client' }
$pulseHostArgs = '-screen-width 1600 -screen-height 900 -screen-fullscreen 0 -pulse-test ' + $pulseHostMode + ' -profile pulsehost -test-output "' + $pulseQA + '" -logFile "' + (Join-Path $pulseQA 'host.log') + '"'
$pulseHost = Start-Process -FilePath $pulseExe -ArgumentList $pulseHostArgs -WindowStyle Hidden -PassThru
$pulseRoom = ''
if ($Online) {
    $pulseRoomFile = Join-Path $pulseQA 'room.txt'
    $pulseDeadline = (Get-Date).AddSeconds(45)
    while ((!(Test-Path -LiteralPath $pulseRoomFile) -or (Get-Item -LiteralPath $pulseRoomFile).Length -eq 0) -and (Get-Date) -lt $pulseDeadline -and !$pulseHost.HasExited) { Start-Sleep -Milliseconds 500 }
    if (!(Test-Path -LiteralPath $pulseRoomFile)) { throw 'Online room was not created. See QA/Online/host.log.' }
    $pulseRoom = (Get-Content -LiteralPath $pulseRoomFile -Raw).Trim()
} else { Start-Sleep -Seconds 3 }
$pulseClientArgs = '-screen-width 1600 -screen-height 900 -screen-fullscreen 0 -pulse-test ' + $pulseClientMode + ' -profile pulseclient -test-output "' + $pulseQA + '" -room ' + $pulseRoom + ' -logFile "' + (Join-Path $pulseQA 'client.log') + '"'
$pulseClient = Start-Process -FilePath $pulseExe -ArgumentList $pulseClientArgs -WindowStyle Hidden -PassThru
Write-Output ('Started test players: host PID ' + $pulseHost.Id + ', client PID ' + $pulseClient.Id + '. Results: ' + $pulseQA)

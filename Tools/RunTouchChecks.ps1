$ErrorActionPreference = 'Stop'
$pulseProject = Split-Path -Parent $PSScriptRoot
foreach ($pulseSize in @(@(1600,900),@(1920,864))) {
    $pulseLabel = '{0}x{1}' -f $pulseSize[0],$pulseSize[1]
    $pulseOutput = Join-Path $pulseProject ('QA\StartupFix\Touch-' + $pulseLabel + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Force -Path $pulseOutput | Out-Null
    $pulseArgs = '-screen-fullscreen 0 -screen-width ' + $pulseSize[0] + ' -screen-height ' + $pulseSize[1] + ' -pulse-test touch -test-output "' + $pulseOutput + '" -logFile "' + (Join-Path $pulseOutput 'player.log') + '"'
    $pulseProcess = Start-Process -FilePath (Join-Path $pulseProject 'Builds\Windows\PulseArena.exe') -ArgumentList $pulseArgs -WindowStyle Hidden -PassThru
    if (!$pulseProcess.WaitForExit(45000)) {
        Stop-Process -Id $pulseProcess.Id
        throw ('Touch test timed out: ' + $pulseOutput)
    }
    $pulseResult = Get-Content -LiteralPath (Join-Path $pulseOutput 'touch.txt') -Raw
    if ($pulseProcess.ExitCode -ne 0 -or !$pulseResult.StartsWith('PASS')) { throw $pulseResult }
    if (Select-String -LiteralPath (Join-Path $pulseOutput 'player.log') -Pattern 'Exception:|PULSE_TOUCH_TEST FAIL' -Quiet) { throw ('Runtime error: ' + $pulseOutput) }
    Write-Output $pulseResult
}

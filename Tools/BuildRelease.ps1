$ErrorActionPreference = 'Stop'
$pulseProject = Split-Path -Parent $PSScriptRoot
$pulseSigning = Join-Path $pulseProject 'Signing'
$pulseSecure = (Get-Content -LiteralPath (Join-Path $pulseSigning 'password.dpapi') -Raw).Trim() | ConvertTo-SecureString
$pulseCredential = New-Object System.Management.Automation.PSCredential('pulsearena-upload', $pulseSecure)
$env:PULSE_KEYSTORE = Join-Path $pulseSigning 'pulsearena-upload.p12'
$env:PULSE_KEY_ALIAS = 'pulsearena-upload'
$env:PULSE_KEYSTORE_PASSWORD = $pulseCredential.GetNetworkCredential().Password
$env:PULSE_KEY_PASSWORD = $env:PULSE_KEYSTORE_PASSWORD
$env:PULSE_APPLICATION_ID = 'com.kagidhakappal.pulsearena'
try {
    $pulseArgs = '-batchmode -quit -projectPath "' + $pulseProject + '" -buildTarget Android -executeMethod PulseArena.Editor.PulseBuilder.AndroidRelease -logFile "' + (Join-Path $pulseProject 'Logs\Release.log') + '"'
    $pulseBuild = Start-Process -FilePath 'D:\UNITY\unity editor\6000.3.10f1\Editor\Unity.exe' -ArgumentList $pulseArgs -WindowStyle Hidden -PassThru -Wait
    if ($pulseBuild.ExitCode -ne 0) { throw 'Release build failed. See Logs/Release.log.' }
    & (Join-Path $PSScriptRoot 'TestPrimitivePreservation.ps1')
    Write-Output (Join-Path $pulseProject 'Builds\Android\PulseArena-release.aab')
} finally {
    'PULSE_KEYSTORE','PULSE_KEY_ALIAS','PULSE_KEYSTORE_PASSWORD','PULSE_KEY_PASSWORD','PULSE_APPLICATION_ID' | ForEach-Object { Remove-Item -LiteralPath ('Env:' + $_) -ErrorAction SilentlyContinue }
}


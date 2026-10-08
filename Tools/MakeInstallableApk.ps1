$ErrorActionPreference = 'Stop'
$pulseProject = Split-Path -Parent $PSScriptRoot
$pulseAndroid = Join-Path $pulseProject 'Builds\Android'
$pulseSigning = Join-Path $pulseProject 'Signing'
$pulsePasswordFile = Join-Path $pulseSigning 'temporary-build-password.txt'
$pulseAndroidTools = 'D:\UNITY\unity editor\6000.3.10f1\Editor\Data\PlaybackEngines\AndroidPlayer'
$pulseBundle = Join-Path $pulseAndroid 'PulseArena-release.aab'
$pulseApks = Join-Path $pulseAndroid 'PulseArena.apks'
$pulseKey = Join-Path $pulseSigning 'pulsearena-upload.p12'
$pulseSecure = (Get-Content -LiteralPath (Join-Path $pulseSigning 'password.dpapi') -Raw).Trim() | ConvertTo-SecureString
$pulseCredential = New-Object System.Management.Automation.PSCredential('pulsearena-upload', $pulseSecure)
try {
    [System.IO.File]::WriteAllText($pulsePasswordFile, $pulseCredential.GetNetworkCredential().Password)
    & (Join-Path $pulseAndroidTools 'OpenJDK\bin\java.exe') -jar (Join-Path $pulseAndroidTools 'Tools\bundletool-all-1.17.2.jar') validate "--bundle=$pulseBundle"
    if ($LASTEXITCODE -ne 0) { throw 'Bundle validation failed.' }
    & (Join-Path $pulseAndroidTools 'OpenJDK\bin\java.exe') -jar (Join-Path $pulseAndroidTools 'Tools\bundletool-all-1.17.2.jar') build-apks "--bundle=$pulseBundle" "--output=$pulseApks" --mode=universal --overwrite "--ks=$pulseKey" --ks-key-alias=pulsearena-upload "--ks-pass=file:$pulsePasswordFile" "--key-pass=file:$pulsePasswordFile"
    if ($LASTEXITCODE -ne 0) { throw 'APK generation failed.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $pulseZip = [IO.Compression.ZipFile]::OpenRead((Join-Path $pulseAndroid 'PulseArena.apks'))
    try { [IO.Compression.ZipFileExtensions]::ExtractToFile($pulseZip.GetEntry('universal.apk'), (Join-Path $pulseAndroid 'PulseArena.apk'), $true) } finally { $pulseZip.Dispose() }
    Write-Output (Join-Path $pulseAndroid 'PulseArena.apk')
} finally { Remove-Item -LiteralPath $pulsePasswordFile -ErrorAction SilentlyContinue }


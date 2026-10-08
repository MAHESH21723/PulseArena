$ErrorActionPreference = 'Stop'
$pulseProject = Split-Path -Parent $PSScriptRoot
$pulseBundle = Join-Path $pulseProject 'Builds\Android\PulseArena-release.aab'
$pulseApk = Join-Path $pulseProject 'Builds\Android\PulseArena.apk'
$pulseQA = Join-Path $pulseProject 'QA\Android'
$pulseTools = 'D:\UNITY\unity editor\6000.3.10f1\Editor\Data\PlaybackEngines\AndroidPlayer'
New-Item -ItemType Directory -Force -Path $pulseQA | Out-Null
& (Join-Path $pulseTools 'OpenJDK\bin\java.exe') -jar (Join-Path $pulseTools 'Tools\bundletool-all-1.17.2.jar') dump manifest "--bundle=$pulseBundle" | Set-Content -LiteralPath (Join-Path $pulseQA 'manifest.xml')
if ($LASTEXITCODE -ne 0) { throw 'Manifest extraction failed.' }
[xml]$pulseManifest = Get-Content -LiteralPath (Join-Path $pulseQA 'manifest.xml') -Raw
$pulseAndroidNs = 'http://schemas.android.com/apk/res/android'
$pulsePackage = $pulseManifest.manifest.GetAttribute('package')
$pulseTarget = $pulseManifest.manifest.'uses-sdk'.GetAttribute('targetSdkVersion', $pulseAndroidNs)
$pulseDebuggable = $pulseManifest.manifest.application.GetAttribute('debuggable', $pulseAndroidNs)
$pulseVersion = $pulseManifest.manifest.GetAttribute('versionName', $pulseAndroidNs)
$pulseVersionCode = $pulseManifest.manifest.GetAttribute('versionCode', $pulseAndroidNs)
if ($pulseVersion -ne '0.1.1' -or $pulseVersionCode -ne '2') { throw 'Expected fixed release 0.1.1 (version code 2).' }
& (Join-Path $PSScriptRoot 'TestPrimitivePreservation.ps1') | Tee-Object -FilePath (Join-Path $pulseQA 'primitive-preservation.txt')
if ($pulsePackage -ne 'com.kagidhakappal.pulsearena' -or $pulseTarget -ne '36' -or $pulseDebuggable -eq 'true') { throw 'Unexpected release manifest settings.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$pulseZip = [IO.Compression.ZipFile]::OpenRead($pulseBundle)
$pulseNative = @()
try {
    foreach ($pulseEntry in $pulseZip.Entries | Where-Object FullName -Match '^base/lib/arm64-v8a/.+\.so$') {
        $pulseMemory = New-Object IO.MemoryStream
        $pulseStream = $pulseEntry.Open()
        try { $pulseStream.CopyTo($pulseMemory); $pulseBytes = $pulseMemory.ToArray() } finally { $pulseStream.Dispose(); $pulseMemory.Dispose() }
        if ($pulseBytes[4] -ne 2 -or $pulseBytes[5] -ne 1) { throw 'Expected little-endian ELF64.' }
        $pulseHeaderStart = [BitConverter]::ToUInt64($pulseBytes,32)
        $pulseHeaderSize = [BitConverter]::ToUInt16($pulseBytes,54)
        $pulseHeaderCount = [BitConverter]::ToUInt16($pulseBytes,56)
        $pulseAlignments = @()
        for ($pulseI=0; $pulseI -lt $pulseHeaderCount; $pulseI++) {
            $pulseOffset = [int]($pulseHeaderStart + $pulseHeaderSize*$pulseI)
            if ([BitConverter]::ToUInt32($pulseBytes,$pulseOffset) -eq 1) { $pulseAlignments += [BitConverter]::ToUInt64($pulseBytes,$pulseOffset+48) }
        }
        $pulsePass = $pulseAlignments.Count -gt 0 -and @($pulseAlignments | Where-Object { $_ -lt 16384 }).Count -eq 0
        $pulseNative += [pscustomobject]@{library=$pulseEntry.FullName;loadSegmentAlignments=$pulseAlignments;aligned16KB=$pulsePass}
        if (!$pulsePass) { throw ('16 KB native alignment failed: ' + $pulseEntry.FullName) }
    }
} finally { $pulseZip.Dispose() }
if ($pulseNative.Count -eq 0) { throw 'No ARM64 native libraries found.' }
$pulseNative | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $pulseQA 'native-alignment.json')
& (Join-Path $pulseTools 'OpenJDK\bin\jarsigner.exe') -verify $pulseBundle | Set-Content -LiteralPath (Join-Path $pulseQA 'aab-signature.txt')
if ($LASTEXITCODE -ne 0) { throw 'AAB signature verification failed.' }
& (Join-Path $pulseTools 'OpenJDK\bin\java.exe') -jar (Join-Path $pulseTools 'SDK\build-tools\36.0.0\lib\apksigner.jar') verify --verbose --print-certs $pulseApk | Set-Content -LiteralPath (Join-Path $pulseQA 'apk-signature.txt')
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
& (Join-Path $pulseTools 'SDK\build-tools\36.0.0\zipalign.exe') -c -P 16 4 $pulseApk
if ($LASTEXITCODE -ne 0) { throw 'APK 16 KB ZIP alignment failed.' }
[pscustomobject]@{package=$pulsePackage;version=$pulseVersion;versionCode=$pulseVersionCode;targetAPI=$pulseTarget;debuggable=$pulseDebuggable;arm64Libraries=$pulseNative.Count;runtimePrimitives='PASS';native16KB='PASS';aabSignature='PASS';apkSignature='PASS';apkZip16KB='PASS'} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $pulseQA 'validation.json')

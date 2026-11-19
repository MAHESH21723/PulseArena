$ErrorActionPreference = 'Stop'
$pulseProject = Split-Path -Parent $PSScriptRoot
$pulseValidation = Get-Content -LiteralPath (Join-Path $pulseProject 'QA\Android\validation.json') -Raw | ConvertFrom-Json
if ($pulseValidation.version -ne '0.1.1' -or $pulseValidation.runtimePrimitives -ne 'PASS' -or $pulseValidation.apkSignature -ne 'PASS') { throw 'Validate the fixed Android release before packaging.' }
$pulseVersion = $pulseValidation.version
$pulseBuilds = Join-Path $pulseProject 'Builds'
Copy-Item -LiteralPath (Join-Path $pulseBuilds 'Android\PulseArena.apk') -Destination (Join-Path $pulseBuilds ('Android\PulseArena-' + $pulseVersion + '.apk'))
Copy-Item -LiteralPath (Join-Path $pulseBuilds 'Android\PulseArena-release.aab') -Destination (Join-Path $pulseBuilds ('Android\PulseArena-' + $pulseVersion + '.aab'))

Add-Type -AssemblyName System.IO.Compression.FileSystem
$pulseZipPath = Join-Path $pulseBuilds 'PulseArena-UnitySource.zip'
$pulseStream = [IO.File]::Open($pulseZipPath, [IO.FileMode]::Create)
$pulseArchive = New-Object IO.Compression.ZipArchive($pulseStream, [IO.Compression.ZipArchiveMode]::Create)
try {
    $pulseFiles = @()
    foreach ($pulseFolder in @('Assets','Packages','ProjectSettings','Tools')) { $pulseFiles += Get-ChildItem -LiteralPath (Join-Path $pulseProject $pulseFolder) -Recurse -File }
    $pulseFiles += Get-ChildItem -LiteralPath $pulseProject -File -Filter '*.md'
    $pulseFiles += Get-Item -LiteralPath (Join-Path $pulseProject '.gitignore') -Force
    foreach ($pulseFile in $pulseFiles) {
        if ($pulseFile.Extension -in @('.p12','.keystore','.jks','.dpapi')) { throw ('Signing file in source inputs: ' + $pulseFile.Name) }
        $pulseEntry = $pulseFile.FullName.Substring($pulseProject.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($pulseArchive,$pulseFile.FullName,$pulseEntry,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $pulseArchive.Dispose(); $pulseStream.Dispose() }
$pulseFilesToHash = @('Android\PulseArena.apk','Android\PulseArena-release.aab',('Android\PulseArena-' + $pulseVersion + '.apk'),('Android\PulseArena-' + $pulseVersion + '.aab'),'PulseArena-UnitySource.zip')
$pulseHashes = foreach ($pulseFile in $pulseFilesToHash) {
    $pulseFull = Join-Path $pulseBuilds $pulseFile
    [pscustomobject]@{file=$pulseFile;version=$pulseVersion;bytes=(Get-Item -LiteralPath $pulseFull).Length;sha256=(Get-FileHash -LiteralPath $pulseFull -Algorithm SHA256).Hash}
}
$pulseHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $pulseBuilds 'SHA256.json')
$pulseHashes | Select-Object file,version,bytes

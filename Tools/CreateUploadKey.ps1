$ErrorActionPreference = 'Stop'
$pulseProject = Split-Path -Parent $PSScriptRoot
$pulseSigning = Join-Path $pulseProject 'Signing'
$pulseKey = Join-Path $pulseSigning 'pulsearena-upload.p12'
if (Test-Path -LiteralPath $pulseKey) { throw 'An upload key already exists. Keep the original key; this script will not replace it.' }
New-Item -ItemType Directory -Force -Path $pulseSigning | Out-Null
$pulseBytes = New-Object byte[] 32
$pulseRandom = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$pulseRandom.GetBytes($pulseBytes)
$pulsePassword = [Convert]::ToBase64String($pulseBytes)
$pulseSecure = ConvertTo-SecureString -String $pulsePassword -AsPlainText -Force
$pulseSecure | ConvertFrom-SecureString | Set-Content -LiteralPath (Join-Path $pulseSigning 'password.dpapi')
$env:PULSE_KEYSTORE_PASSWORD = $pulsePassword
try {
    & 'D:\UNITY\unity editor\6000.3.10f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe' -genkeypair -keystore $pulseKey -storetype PKCS12 -alias pulsearena-upload -keyalg RSA -keysize 4096 -validity 10000 -dname 'CN=Pulse Arena Upload,O=kagidhakappal' -storepass:env PULSE_KEYSTORE_PASSWORD -keypass:env PULSE_KEYSTORE_PASSWORD
    if ($LASTEXITCODE -ne 0) { throw 'Key generation failed.' }
    & 'D:\UNITY\unity editor\6000.3.10f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe' -exportcert -rfc -keystore $pulseKey -alias pulsearena-upload -storepass:env PULSE_KEYSTORE_PASSWORD -file (Join-Path $pulseSigning 'upload-certificate.pem')
    Write-Output 'Upload key created. Password saved encrypted for this Windows user. No password printed.'
} finally {
    Remove-Item Env:PULSE_KEYSTORE_PASSWORD -ErrorAction SilentlyContinue
    $pulsePassword = $null
    $pulseRandom.Dispose()
}

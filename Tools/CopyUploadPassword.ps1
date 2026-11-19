# Run this yourself when saving the upload password to your password manager.
# It does not print the password to the terminal or chat.
$ErrorActionPreference = 'Stop'
$pulseProject = Split-Path -Parent $PSScriptRoot
$pulseSecure = (Get-Content -LiteralPath (Join-Path $pulseProject 'Signing\password.dpapi') -Raw).Trim() | ConvertTo-SecureString
$pulseCredential = New-Object System.Management.Automation.PSCredential('pulsearena-upload', $pulseSecure)
Set-Clipboard -Value $pulseCredential.GetNetworkCredential().Password
Write-Output 'Upload password copied. Paste it into your password manager, then clear the clipboard. Back up the .p12 file separately.'

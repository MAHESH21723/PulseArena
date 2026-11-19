PULSE ARENA UPLOAD SIGNING KEY

Owner / studio: kagidhakappal
Application: com.kagidhakappal.pulsearena
Key alias: pulsearena-upload
Key store: pulsearena-upload.p12 (PKCS12, RSA 4096)
Public certificate: upload-certificate.pem

password.dpapi contains the password encrypted for this Windows user.
Tools/BuildRelease.ps1 decrypts it only into the build process environment.
No password has been printed in chat or added to source control.

KEEP AND BACK UP THE KEY. Do not run CreateUploadKey.ps1 again for app updates.
Do not upload this folder or its password to source control or to testers.
The public PEM certificate is safe to supply to Google if requested.

Windows DPAPI files are bound to this Windows user/profile. For a portable
backup, save the keystore password in your own password manager while you
still have access to this profile. A copy of password.dpapi alone is not a
portable password backup. Preserve the .p12 and the password separately.
Run Tools/CopyUploadPassword.ps1 yourself to copy the password to your
clipboard without printing it, then paste it into your password manager.

To rebuild: close the PulseArena editor, then run Tools/BuildRelease.ps1
as this Windows user. It reads the encrypted password without printing it.

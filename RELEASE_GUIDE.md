# Pulse Arena — Android release

This is the first playable version, not a completed public launch. Do the physical-device and real-network tests below before distributing it to players.

## Open and play

Open this folder in Unity 6000.3.10f1, then open `Assets/PulseArena/Scenes/PulseArena.unity`. Press Play. Use **Practice with bots** for an offline match. For internet play, create a room and enter its code on another device. The host starts the match after another pilot connects. Up to four players can join. The host leaving ends the match for everyone. No host migration, public matchmaking, ranked mode, chat, ads or purchases are included.

Desktop: WASD, mouse aim, left-click fire, R reload, Shift sprint, Escape pause. Phone: left pad moves, drag the right side to look, hold Fire / Sprint, tap Reload. Sprinting prevents shooting. A player needs four hits to lose all shields. Respawn takes three seconds, followed by 1.5 seconds of protection.

## Android files and signing

Current release: **0.1.1, version code 2**. It fixes the missing menu and blocked camera view reported in the 0.1.0 Android APK. Install `PulseArena.apk` as an update to the existing installation; the package and upload key are unchanged. The version is displayed at the bottom of the main menu. Open **Practice with bots** to check the left movement pad, right-side aiming, Fire and Reload before trying an internet room.

`Assets/PulseArena/Runtime/link.xml` must stay in the project. The arena uses `GameObject.CreatePrimitive`; the Android engine stripper previously removed `CapsuleCollider` and `SphereCollider`, preventing startup from reaching the UI. See [Unity's primitive creation requirements](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/GameObject.CreatePrimitive.html). `Tools/BuildRelease.ps1` now checks the Android linker's retained components after building, and `Tools/ValidateAndroid.ps1` repeats that check alongside signing and alignment checks.

`Pulse Arena > Build Android Test APK` produces a development APK with a debug signature, for installation directly on test phones. It is not the Play Store upload artifact.

The delivered `Builds/Android/PulseArena.apk` is generated from the signed release AAB with `Tools/MakeInstallableApk.ps1`. It uses the same upload key and contains the release game, without development test behavior. Use it for phone testing; upload the `.aab` to Google Play.

`Pulse Arena > Build Play Store AAB` produces an ARM64 IL2CPP Android App Bundle, targeting API 36 and Android 8.0+ (API 26). Before starting Unity, set these environment variables in your local build environment:

- `PULSE_APPLICATION_ID`: your final unique package name, such as `com.yourstudio.pulsearena`.
- `PULSE_KEYSTORE`: absolute path to your upload keystore.
- `PULSE_KEY_ALIAS`: the upload key alias.
- `PULSE_KEYSTORE_PASSWORD` and `PULSE_KEY_PASSWORD`: the local signing passwords.

Do not send passwords in chat or commit them. Back up the upload key and passwords securely. The release command refuses to silently use a test key. Update the developer name in `PulseBuilder.Configure`, and increment the Android version code for each subsequent upload. The owner chose studio name **kagidhakappal**; the package name is **com.kagidhakappal.pulsearena**. A new RSA-4096 upload key is in the excluded `Signing` folder. Its password is encrypted with Windows DPAPI. Run `Tools/BuildRelease.ps1` as the same Windows user to build without exposing passwords. Read `Signing/README.txt` and make a secure, portable backup before changing computers.

The same builds can be invoked headlessly with the installed Unity Editor, `-batchmode -quit -projectPath <this-folder> -executeMethod PulseArena.Editor.PulseBuilder.AndroidTest` or `.AndroidRelease`. Close any editor using this project first. Build logs must report `PULSE_BUILD_OK`.

## Online configuration

The project is linked to the `PulseArena` Unity Cloud project in the owner's existing organization. Code uses anonymous authentication and Unity Multiplayer Services session APIs with brokered internet connectivity. A hosting player runs the authoritative movement, hit detection, ammunition, damage, scoring and timer simulation. This casual host architecture is not a dedicated competitive server or a comprehensive anti-cheat system. Owner movement has network latency; prediction and lag compensation are future improvements.

If hosting fails, check the Unity Dashboard project's Multiplayer service configuration and quotas, then retry. Do not enable paid plans or increase spending automatically. Server usage can incur charges depending on the account plan and traffic. SDK failures appear in the menu and player log; practice remains available.

## Release checks

1. Install on at least two physical Android phones. Test 16:9 and tall aspect ratios, safe areas, simultaneous move/look/fire, keyboard input on name/code fields, audio and settings persistence.
2. Host and join from different internet connections. Test four players, incorrect codes, full rooms, disconnects, host exit, app backgrounding, rematches and repeated leave/rejoin. Verify movement, cover collision, damage, reload, invulnerability and standings across devices.
3. Run a full match, check heat, battery usage, frame rate and memory on a lower-end phone. Inspect crash logs and Play pre-launch reports.
4. Validate the release AAB's native library 16 KB page-size compatibility and manifest with Android tooling / Play Console. An ARM64 setting alone does not prove compatibility.
5. Supply the final developer identity, app identifier and upload signing key. Review the store draft, screenshots, icon, privacy policy, support email, audience and content rating before upload.
6. Complete Data safety accurately for the installed Unity SDKs. Anonymous authentication still uses an identifier and networking data; do not claim that the online game collects no data. No advertising or purchase SDK has been added by this game.
7. Upload to an internal testing track, resolve findings, then satisfy your account's testing and production-access requirements before publication.

Requirements checked on 7 October 2026:

- [Google Play target API requirements](https://developer.android.com/google/play/requirements/target-sdk): new phone apps and updates must target Android 16 / API 36 under the current schedule.
- [16 KB page-size support](https://developer.android.com/guide/practices/page-sizes): validate all native dependencies, including the engine.
- [New personal account testing requirements](https://support.google.com/googleplay/android-developer/answer/14151465): affected accounts need 12 testers opted in continuously for at least 14 days before applying for production access.

## Art and audio

The arena, robots, carbine, icon and motion are generated by project code. Sound effects and the reactor ambience are synthesized by `PulseAudio`. UI uses Unity's bundled Liberation Sans TMP resources with their included license. No external game characters, music, weapons or branding have been copied.

## Store copy draft

**Title:** Pulse Arena

**Short description:** Four pilots. One arena. Fast sci-fi firefights with friends.

**Full description:** Charge into Reactor 07 for short first-person arena battles. Create a private internet room, share its code, and face up to three friends in a race to 15 eliminations. Move between reactor cover, manage your pulse carbine's charge, and get back into the action with quick respawns. Learn the arena in offline practice against three bots. Includes touch controls, adjustable sensitivity, sound settings and two performance modes. No ads or in-app purchases. Internet play requires a connection and an active host. The room ends when its host leaves.

Use only screenshots from a verified running build. Store copy remains a draft until the advertised online behavior passes validation.


# Pulse Arena — Android release

Release **v0.3.0** is published as an APK on https://github.com/MAHESH21723/PulseArena/releases/latest (player-facing text: `docs/RELEASE_NOTES_v0.3.0.md`). It is a portfolio release, not a Google Play launch. Do the physical-device and real-network tests below before submitting to a store.

## Open and play

Open this folder in Unity 6000.6.2f1, then open `Assets/PulseArena/Scenes/PulseArena.unity`. Press Play. Use **Practice with bots** for an offline match. For multiplayer, choose Local Wi-Fi: one phone hosts and others on the same network join from the nearby list or with the room code (see `ONLINE_SETUP.md`). Up to four players. The host leaving ends the match for everyone. Internet rooms are "coming soon". No host migration, public matchmaking, ranked mode, chat, ads or purchases are included.

Desktop: WASD, mouse aim, left-click fire, R reload, Q swap (or 1 / 2), Shift sprint, Escape pause. Phone: left pad moves, drag the right side to look, hold Fire / Sprint, tap Reload. Sprinting prevents shooting. Respawn takes three seconds.

## Android files and signing

Version numbers live in `PulseBuilder.Configure` (`bundleVersion`, `bundleVersionCode`); bump both before each release build. Minimum Android is 8.0 (API 26), target API 36, ARM64 only. `PlayerSettings` in the repo may lag behind the release tag, so check them before building. The version is displayed at the bottom of the main menu. Open **Practice with bots** to check the left movement pad, right-side aiming, Fire and Reload before testing Wi-Fi rooms.

`Assets/PulseArena/Runtime/link.xml` must stay in the project. The arena uses `GameObject.CreatePrimitive`; the Android engine stripper previously removed `CapsuleCollider` and `SphereCollider`, preventing startup from reaching the UI. See [Unity's primitive creation requirements](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/GameObject.CreatePrimitive.html). `Tools/BuildRelease.ps1` now checks the Android linker's retained components after building, and `Tools/ValidateAndroid.ps1` repeats that check alongside signing and alignment checks.

`Pulse Arena > Build Android Test APK` produces a development APK with a debug signature, for installation directly on test phones. It is not the Play Store upload artifact.

The delivered `Builds/Android/PulseArena.apk` is generated from the signed release AAB with `Tools/MakeInstallableApk.ps1`. It uses the same upload key and contains the release game, without development test behavior. Use it for phone testing; upload the `.aab` to Google Play.

`Pulse Arena > Build Play Store AAB` produces an ARM64 IL2CPP Android App Bundle, targeting API 36 and Android 8.0+ (API 26). Before starting Unity, set these environment variables in your local build environment:

- `PULSE_APPLICATION_ID`: your final unique package name, such as `com.yourstudio.pulsearena`.
- `PULSE_KEYSTORE`: absolute path to your upload keystore.
- `PULSE_KEY_ALIAS`: the upload key alias.
- `PULSE_KEYSTORE_PASSWORD` and `PULSE_KEY_PASSWORD`: the local signing passwords.

Put these in a local `.env` (git-ignored; see `.env.example` for the names) or the environment. Do not send passwords in chat or commit them. Back up the upload key and passwords securely. The release command refuses to silently use a test key. Increment the Android version code for each subsequent upload. The current package name is **com.kagidhakappal.pulsearena** (`PulseBuilder.Configure`). The upload key lives in the excluded `Signing` folder and its password is encrypted with Windows DPAPI. Run `Tools/BuildRelease.ps1` as the same Windows user to build without exposing passwords. Read `Signing/README.txt` and make a secure, portable backup before changing computers.

The same builds can be invoked headlessly with the installed Unity Editor, `-batchmode -quit -projectPath <this-folder> -executeMethod PulseArena.Editor.PulseBuilder.AndroidTest` or `.AndroidRelease`. Close any editor using this project first. Build logs must report `PULSE_BUILD_OK`.

## Online configuration

Unity Relay / Multiplayer Services were removed, so there is no Unity-billed service. Same-Wi-Fi rooms use direct UnityTransport connections with UDP-broadcast room discovery. Internet rooms will use free Epic Online Services (behind the `PULSE_EOS` define) and are not available yet; see `ONLINE_SETUP.md`. A hosting player runs the authoritative movement, hit detection, ammunition, damage, scoring and timer simulation. This casual host architecture is not a dedicated competitive server or a comprehensive anti-cheat system. Non-host movement has network latency; prediction and lag compensation are future improvements.

If a Wi-Fi room does not appear, the router may isolate clients; try a phone hotspot. Errors appear in the menu and player log; practice remains available.

## Release checks

1. Install on at least two physical Android phones. Test 16:9 and tall aspect ratios, safe areas, simultaneous move/look/fire, keyboard input on name/code fields, audio and settings persistence.
2. Host and join over the same Wi-Fi (and over a hotspot). Test four players, incorrect codes, full rooms, disconnects, host exit, app backgrounding, rematches and repeated leave/rejoin. Verify movement, cover collision, damage, reload, pickups and standings across devices. Repeat across the internet once EOS support exists.
3. Run a full match, check heat, battery usage, frame rate and memory on a lower-end phone. Inspect crash logs and Play pre-launch reports.
4. Validate the release AAB's native library 16 KB page-size compatibility and manifest with Android tooling / Play Console. An ARM64 setting alone does not prove compatibility.
5. Supply the final developer identity, app identifier and upload signing key. Review the store draft, screenshots, icon, privacy policy, support email, audience and content rating before upload.
6. Complete Data safety accurately for the installed SDKs. Local networking exposes the device's local IP to other players on the same network; internet play (when added) will use Epic identifiers. No advertising or purchase SDK has been added by this game.
7. Upload to an internal testing track, resolve findings, then satisfy your account's testing and production-access requirements before publication.

Requirements checked on 7 October 2026:

- [Google Play target API requirements](https://developer.android.com/google/play/requirements/target-sdk): new phone apps and updates must target Android 16 / API 36 under the current schedule.
- [16 KB page-size support](https://developer.android.com/guide/practices/page-sizes): validate all native dependencies, including the engine.
- [New personal account testing requirements](https://support.google.com/googleplay/android-developer/answer/14151465): affected accounts need 12 testers opted in continuously for at least 14 days before applying for production access.

## Art and audio

The arena uses CC0 models from Kenney and Quaternius plus generated materials and effects. Sounds are mastered from CC0 libraries and synthesized material. The UI font is Play (SIL OFL). Everything is listed in `CREDITS.md`; keep licence files with the assets. No external game characters, music, weapons or branding have been copied.

## Store copy draft

**Title:** Pulse Arena

**Short description:** Four pilots. One bright arena. Fast firefights on your phone.

**Full description:** Jump into short, colourful first-person arena battles. Race to 15 eliminations in three-minute rounds, grab shield and ammo pickups, and switch between a precise pulse carbine and a close-range scatter cannon. Practise offline against Easy, Normal or Hard bots, or host a room and play with friends on the same Wi-Fi. Includes touch controls with aim assist, adjustable sensitivity, sound and haptics settings, and three quality presets. No ads or in-app purchases. Wi-Fi play needs an active host; the room ends when its host leaves. Internet play is coming soon.

Use only screenshots from a verified running build.


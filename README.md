# Pulse Arena

A small 3D sci-fi first-person arena shooter for Android, made for **kagidhakappal**.

**Current version: 0.1.1 (Android version code 2).** This fixes the Android startup failure in 0.1.0 that left the camera inside the reactor without a menu. Install the updated APK over the existing game; it uses the same package and signing key.

**Open:** Unity 6000.3.10f1 → `Assets/PulseArena/Scenes/PulseArena.unity` → Play.

**Play:** create a private internet room and share its code with friends, or choose offline practice with three bots. The host starts a free-for-all: first to 15 eliminations, or highest score after three minutes. Maximum four players. The room ends if its host leaves.

Included: landscape touch controls, desktop controls, title menu, room creation/joining, roster, HUD, health/ammo feedback, reload, respawn, settings, pause/leave, results and rematches. Original generated arena, robot characters, pulse carbine, icon, procedural movement/recoil/reload animations, synthesized sound effects and ambient audio. No ads or purchases.

| Output | Use |
| --- | --- |
| `Builds/Android/PulseArena-release.aab` | Signed Google Play upload artifact |
| `Builds/Android/PulseArena.apk` | Install and test on an ARM64 Android phone |
| `Builds/Windows/PulseArena.exe` | Desktop test player; keep its neighboring files |
| `RELEASE_GUIDE.md` | Build, signing, cloud setup and publication instructions |
| `VALIDATION.md` | Recorded checks and remaining device tests |
| `Signing/README.txt` | Upload key ownership and backup instructions; keep private |

The Android outputs are available only after their build succeeds. The current release is an initial playable version for testing, with one arena and one weapon. It uses a player-hosted simulation with server-controlled movement, damage and match state. Dedicated servers, ranked play, host migration, lag compensation, controller support and additional content are not included.

## Source

- `Assets/PulseArena/Runtime/PulseApp.cs`: startup, camera, room lifecycle and settings.
- `ArenaPlayer.cs` / `ArenaMatch.cs`: networked combat and round state.
- `PulseUI.cs` / `PulseInput.cs`: UI construction and mobile / desktop input.
- `ArenaArt.cs` / `PulseAudio.cs`: generated art and audio.
- `PulseSmoke.cs`: development-only integration checks and offscreen visual capture.
- `PulseTouchCheck.cs`: Input System touch-event checks for menus, movement, simultaneous firing, aiming, reload and pause.
- `Assets/PulseArena/Runtime/link.xml`: keeps the primitive components required by the generated arena and pilots in stripped release builds.
- `Assets/PulseArena/Editor/PulseBuilder.cs`: reproducible scene/prefab setup and builds.
- `Tools/BuildRelease.ps1`: release build using the locally encrypted upload password.
- `Tools/TestPrimitivePreservation.ps1`: rejects an Android linker report missing runtime primitive components.
- `Tools/MakeInstallableApk.ps1`: validate the AAB and generate a signed APK with bundletool.
- `Tools/RunNetworkChecks.ps1 [-Online]`: two-process movement/combat/state replication checks.

Each `.cs` and asset stays with its `.meta` file. `Library`, `Temp`, build outputs, logs and signing credentials are excluded from source control. The project is not published to a remote source repository.

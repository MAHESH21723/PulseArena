# Validation — 7 October 2026

## 0.1.1 Android startup correction

The owner reported that the 0.1.0 APK opened with the camera inside geometry and no menu. Its saved Android linker report contains neither `CapsuleCollider` nor `SphereCollider`. `ArenaArt.Shape` creates cylinders and then accesses their collider while building the reactor, before `PulseApp` creates the weapon and UI. Stripping the capsule collider therefore interrupts that startup path and leaves the scene camera at its default origin. This is consistent with the supplied phone screenshot; no phone log was available.

The fix preserves the exact primitive component types in `Assets/PulseArena/Runtime/link.xml`. Unity documents these runtime dependencies under [GameObject.CreatePrimitive](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/GameObject.CreatePrimitive.html). Version is now 0.1.1 / Android version code 2, using the existing package and signing key.

Checks for this correction:

- `TestPrimitivePreservation.ps1` rejects the saved 0.1.0 linker report, identifying both missing collider types, and accepts the newly generated Android report with all five required components retained.
- Windows development player rebuilt successfully. Synthetic touchscreen events pass through the actual Input System and UI raycaster at 1600×900 and 1920×864: menu/settings, practice start, HUD, joystick movement with simultaneous firing, touch release, aiming, reload, pause/resume and leaving back to the menu all passed. Bots are held still during the control assertions.
- Updated wide-screen menu and practice screenshots were inspected. Menu camera is above the arena, the version label reads 0.1.1, and the gameplay camera, weapon, joystick and action controls are visible.
- The hidden-window test initially suppressed its synthetic touchscreen due to desktop focus policy. The test harness now ignores focus during these development-only checks; production focus behavior is unchanged. Subsequent touch runs passed without runtime exceptions.

Evidence: `QA/StartupFix/v0.1.0-linker.json`, `old-release-regression.txt`, the timestamped `Touch-*` folders, and `QA/StartupFix/Visual`. Run `Tools/RunTouchChecks.ps1` to repeat both aspect-ratio tests. These desktop touch-event checks do not substitute for running the updated APK on the owner's phone.

## Earlier gameplay checks (Windows)

- Unity 6000.3.10f1 script compilation with Netcode for GameObjects 2.13.3 and Multiplayer Services 2.3.3.
- Windows development player build.
- Two separate local player processes: connection, remote movement, server raycast damage, elimination / death scores, ammunition reload, respawn and synchronized timer / results.
- Two separate players using real internet room-code creation and joining through the linked Unity Cloud project: the same combat and replication checks passed on host and client.
- Offline practice creates one human player and three bots and starts a match.
- Rendered visual inspection of title / room entry, settings, controls, arena and combat HUD. The first pass found an input-field initialization issue; it was fixed and subsequent player logs were clear of runtime exceptions. Weapon scale and minimum surface lighting were adjusted following visual review.

Evidence: `QA/Local/host.txt`, `QA/Local/client.txt`, `QA/Online/online-host.txt`, `QA/Online/online-client.txt`, and `QA/Practice/practice.txt`. Screenshots are under `QA/Visual` and `QA/Practice`. The screenshots use an explicit offscreen camera target because hidden Windows player windows do not present their swap chain; their UI is rendered through that camera for capture.

## Android artifact checks

Repeated and passed on the corrected 0.1.1 files (version code 2):

- ARM64 IL2CPP release AAB build completed successfully.
- Android bundletool structural validation.
- Package `com.kagidhakappal.pulsearena`; target API 36; no debuggable flag enabled.
- AAB signature verified with jarsigner; APK signature verified with apksigner.
- All six ARM64 native libraries have ELF LOAD-segment alignment of at least 16 KB.
- APK passed `zipalign -c -P 16 4`.
- The APK was generated from the signed release AAB with the same upload key.
- Both previously stripped collider types are retained in the Android release. The APK signer certificate SHA-256 matches the original upload key: `0bfa5378ba6e3701e6f54b901a079c7f50e22d83451e0c579eb1fb2085f6c4be`.
- All source assets have their Unity `.meta` files. The source archive excludes signing secrets and generated build / cache folders.

Evidence: `QA/Android/validation.json`, `primitive-preservation.txt`, `native-alignment.json`, `manifest.xml`, `aab-signature.txt`, and `apk-signature.txt`. These are artifact checks, not proof of successful play on a 16 KB Android device. Physical-device testing remains outstanding.

## Not yet verified

- Successful play of the corrected 0.1.1 APK on a physical Android device: no device was connected to ADB during this fix. The owner's 0.1.0 device test failed at startup as described above.
- Actual simultaneous multi-touch behavior and mobile keyboard / safe-area interaction on hardware.
- Thermal behavior, battery impact, sustained mobile frame rate and low-memory recovery.
- Four-device matches, different physical networks, high latency / packet loss, repeated room lifecycle stress and app suspend/resume on Android.
- Google Play pre-launch reports, closed testing and production approval.

The internet test used two independent clients on this computer through the internet services. It does not substitute for testing two phones on separate networks. No claim of production-scale load testing or comprehensive anti-cheat validation is made.

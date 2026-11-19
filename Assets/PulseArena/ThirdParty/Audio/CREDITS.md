# PulseArena audio credits

All sounds in `Mixed/` are derived (layered, EQ'd, saturated, loudness-mastered, 48 kHz mono 16-bit WAV) from the CC0 / public-domain sources below, plus sounds synthesised from scratch with a Python/numpy script (sine sweeps, bells, filtered noise; no third-party material, released CC0 by the project). All sources are CC0 (public domain dedication): free for commercial use, no attribution required. Credit is given anyway as courtesy.

## Sources

| Pack | Author | URL | Licence | Used for |
|---|---|---|---|---|
| The Free Firearm Sound Library ("Prepared SFX Library") | bart (OpenGameArt) | https://opengameart.org/content/the-free-firearm-sound-library | CC0 1.0 (page lists CC0) | Transient + body layer of all gunshots. Carbine: AR-15 `D_32P`, Norinco SKS `U_14P`, Carl Gustav M45 `G_31P`, AK-47 `C_28P` (near-distance single shots). Scatter: Charles Daly `H_21P`, Winchester Model 12 `K_22P`, Benelli Nova `O_21P`, Mossberg 190 `N_30P`. |
| Gun reload sounds (`assaultriflereload1_0.wav`, `gunreload1.wav`, `shotguncock_0.wav`) | SpringySpringo (OpenGameArt) | https://opengameart.org/content/gun-reload-sounds | CC0 1.0 (page lists CC0; credit appreciated) | `reload_1`, `reload_2`, part of `reload_3` |
| Gun Reload Sound Effects (`clipload1.wav`, `clipload2.wav`, `singlebullet1.wav`) | BMacZero (OpenGameArt) | https://opengameart.org/content/gun-reload-sound-effects | CC0 1.0 (page lists CC0) | `reload_3`, swap clacks, ammo pickup |
| Kenney Sci-Fi Sounds 1.0 | Kenney (kenney.nl) | https://kenney.nl/assets/sci-fi-sounds | CC0 1.0 (`../Kenney_SciFiSounds/License.txt`) | Pulse layer under shots, impacts, explosions, force field / shield |
| Kenney Impact Sounds 1.0 | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 | Footsteps, wall impacts, hurt |
| Kenney Interface Sounds 1.0 | Kenney | https://kenney.nl/assets/interface-sounds | CC0 1.0 | UI sounds, weapon swap |
| Kenney Digital Audio 1.0 | Kenney | https://kenney.nl/assets/digital-audio | CC0 1.0 | Respawn, pickups, match end |

The 194 MB firearm archive and the raw reload files are NOT stored in the project (kept only in a temp folder during processing). Only the mastered results in `Mixed/` (about 4 MB total) ship.

## Design notes (research summary)

Gun sounds follow the usual layered approach (transient / body / sub / mechanical / tail; see Splice "How to design a weapon sound for video games", Violet Recording "How to Design Weapon Sounds", Lior Ortega "FPS gun sound design"):
- transient: 1.5 ms filtered noise click + 2-5 kHz presence boost (reads on phone speakers)
- body: real recorded shot, soft-saturated for density
- sub: short sine sweep (170 to 55 Hz carbine, 120 to 38 Hz scatter) for weight
- identity: Kenney laser (carbine) / low-frequency explosion (scatter) at low level
- tail: short synthetic reverb (0.28 s carbine, 0.45 s scatter), the rest is left to runtime distance filtering
- 4 variations per shot, plus +/-3% pitch and a small volume random per play at runtime
- measured max momentary loudness: carbine about -14 LUFS, scatter about -12.7 LUFS, kill confirm about -13 LUFS; hit confirm / UI / footsteps are peak-normalised well below that

## Files in `Mixed/` (key -> files)

| Bank key | Files | Notes |
|---|---|---|
| `shot_carbine` | `shot_carbine_1..4` | |
| `shot_secondary` | `shot_scatter_1..4` | scatter / shotgun |
| `reload` | `reload_1..3` | |
| `swap` | `swap_1..3` | |
| `empty` (new) | `empty_1..3` | dry-fire click |
| `low_ammo` (new) | `low_ammo_1` | 3 quick beeps |
| `impact_wall` | `impact_wall_1..4` | |
| `impact_shield` | `impact_shield_1..3` | pilot hit (other players) |
| `hit_confirm` (new) | `hit_confirm_1..3` | tick when you hit an enemy |
| `hit_crit` (new) | `hit_crit_1..2` | headshot / crit |
| `kill_confirm`, `kill` | `kill_confirm_1..2` | `kill` kept as alias |
| `hit_taken`, `hurt` (new) | `hurt_1..3` | player hurt |
| `shield_break` (new) | `shield_break_1..2` | |
| `death` (new) | `death_1..2` | your own elimination |
| `explosion` | `explosion_1..2` | other pilots' elimination |
| `respawn` | `respawn_1..2` | |
| `footstep` | `footstep_1..6` | |
| `pickup_shield`, `pickup_ammo` (new), `pickup` | `pickup_shield_1..2`, `pickup_ammo_1..2` | `pickup` = both sets |
| `countdown`, `countdown_go` | `countdown_1..2`, `countdown_go` | |
| `match_win`, `match_lose` (new), `match_end` | `match_win`, `match_lose`, `match_end_1..2` | |
| `low_shield` | `low_shield_loop` | 0.85 s heartbeat+beep loop |
| `ui_click`, `ui_confirm`, `ui_back` | `ui_click_1..3`, `ui_confirm_1..2`, `ui_back_1..2` | |
| `ui_hover`, `ui_error`, `ui_select` (new) | `ui_hover_1..2`, `ui_error`, `ui_select` | |

Rebuild after changing files: `Unity -batchmode -quit -projectPath . -executeMethod PulseArena.PulseAssetsBuilder.BuildSoundBank` (or menu Pulse Arena / Build Sound Bank).

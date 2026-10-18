# Reflective Room Acoustics for 2D Games

**Tyler Crump** · Student ID 47730757 · COMP3180 Final Project 

Commercial acoustic simulation tools such as Steam Audio need 3D meshes, so 2D tilemap games are left with hand-placed reverb zones. This project builds a **listener-centred raycast system** in Unity and FMOD. Rays cast from the listener measure the space around the player, and the measurements drive reverb and directional early reflections in real time. The raycast system is then compared against authored reverb zones and against Steam Audio running on generated proxy geometry.

**Primary research question:** *To what extent can a listener-centred raycast acoustic approximation match geometry-based acoustic simulation for perceived spatial quality in a 2D game, at lower implementation and runtime cost?*

---

## Deliverables

| # | Deliverable | Where |
|---|---|---|
| 1 | **Unity project**: all three acoustic conditions plus the test scene | [`UnityProject/`](UnityProject/) |
| 2 | **FMOD Studio project**: buses, returns, global parameters, automation (source audio excluded, see below) | [`FMODProject/`](FMODProject/) |
| 3 | **Portfolio video** (Echo360) | [ECHO360 LINK] |
| 4 | **Development diary**: weekly progress, roadblocks, breakthroughs | [`Docs/DevelopmentDiary.md`](Docs/DevelopmentDiary.md) |
| 5 | **Evaluation materials and raw data**: protocol, instruments, results | [`Docs/Evaluation/`](Docs/Evaluation/) |
| 6 | **Implementation guide** for 2D audio designers | [`Docs/Guide/`](Docs/Guide/) |
| 7 | **Final Project Report** | Submitted on iLearn |

The portfolio video and other large media are deliberately **not** stored in this repo, because of the unit's storage limits.

---

## The three conditions

| Condition | What drives the room sound | Key components |
|---|---|---|
| **A: Zone Baseline** | Hand-placed trigger zones cross-fade FMOD blending snapshots | `AcousticZone2D`, `bus:/Reverb Zone` |
| **B: Raycast Dynamic** | Rays from the listener measure the room size and how enclosed it is, and find the nearest wall left, right and above | `AcousticProbe2D`, `EarlyReflections2D`, `bus:/Reverb Dynamic`, `bus:/Reflections` |
| **C: Steam Audio** | Steam Audio simulates the room from a 3D mesh extruded from the tilemap collider | `TilemapAcousticProxy`, Steam Audio Geometry/Listener/Source, `bus:/Steam Audio Reverb` |

`ConditionManager` enables exactly one condition at a time. It mutes the other conditions' return buses and disables their components, so no leftover reverb contaminates a comparison.

---

## Requirements

- **Unity 6.3 LTS** (6000.3.10f1) with the **Input System** package
- **FMOD Studio 2.03.x** and **FMOD for Unity 2.03**. The Studio and integration versions must match.
- **Steam Audio** Unity package plus its FMOD Studio plugin (Condition C only) `[VERSION]`

## Opening the project

1. Clone the repo.
2. Open `UnityProject/` in Unity Hub. The built FMOD banks are already included at `UnityProject/Assets/FMOD/Desktop/`, so the project plays sound without FMOD Studio.
3. Open `Assets/Scenes/[TEST SCENE NAME].unity` and press **Play**.

**The FMOD Studio project** (`FMODProject/ACIT_Audio.fspro`) opens in FMOD Studio 2.03 and shows the full mixer, routing, global parameters, automation, snapshots and event structure. The source audio files (`.wav` and `.flac`) are deliberately left out because of the unit's storage limits, so FMOD Studio reports them as missing. They are kept in my private audio repository and are available on request.

**Switching conditions:** set **Starting Condition** on the `ConditionManager` component before pressing Play. `[If the hotkeys are fixed for the Input System: 7 = A, 8 = B, 9 = C, Right Arrow = cycle]`

**FMOD Live Update** uses port **9265** (`localhost:9265`), changed from the default 9264, which clashed with another process.

---

## How Condition B works

```
                ┌─ AcousticProbe2D ── 32 rays ─► RoomSize, Enclosure (global params)
AudioListener ──┤                                      │
(child of Player)└─ EarlyReflections2D ── L/R/Up rays ─► ReflectDelay*, ReflectGain*
                                                       │
Player/SFX/Environment/Music groups ── sends ──► Reverb Dynamic return
                                          └────► Reflections group
                                                  ├─ Reflect L   (delay, panned −90°)
                                                  ├─ Reflect R   (delay, panned +90°)
                                                  └─ Reflect UP  (delay, centred)
```

| Global parameter | Range | Written by | Drives |
|---|---|---|---|
| `RoomSize` | 0–1 | `AcousticProbe2D` | Reverb Time (0.4 s → 4 s) on `Reverb Dynamic` |
| `Enclosure` | 0–1 | `AcousticProbe2D` | `Reverb Dynamic` volume (−40 dB → 0 dB) |
| `Brightness` | 0–1 | `AcousticProbe2D` (optional) | Unused |
| `ReflectDelayL/R/Up` | 0–250 ms | `EarlyReflections2D` | Delay time on each Reflect return |
| `ReflectGainL/R/Up` | 0–1 | `EarlyReflections2D` | Wet level on each Reflect return |

The delay comes from the wall distance: `delay (ms) = 2 × distance (m) / 343 × 1000`. A tap fades to silence when its ray hits nothing within 30 units, or when the round trip is shorter than **Min Useful Delay** (set to 6 ms).

### Scripts (`ReflectiveAudio2D` namespace)

| Script | Purpose |
|---|---|
| `Listener2D` | Keeps the FMOD listener at z = 0 with locked rotation, following the player |
| `AcousticProbe2D` | Ring of rays → `RoomSize`, `Enclosure`, optional `Brightness` |
| `EarlyReflections2D` | Directional taps → delay time and gain per side |
| `AcousticZone2D` | Condition A trigger zones that fade blending snapshots |
| `ConditionManager` | One condition at a time; mutes the other buses |
| `TilemapAcousticProxy` | Builds Steam Audio geometry from a `CompositeCollider2D` (right-click → *Build Acoustic Mesh*) |
| `OccludedEmitter2D` | Per-emitter occlusion through walls |
| `RoomEstimationTask`, `AudioBenchmarkLogger` | Study tools: room-size estimates and performance logging to CSV |
| `SurfaceType` | Surface label (footsteps) and reflectivity (probe brightness) |

---

## Repository layout

```
├── UnityProject/        Unity project, including built banks in Assets/FMOD/Desktop
├── FMODProject/         FMOD Studio project (no source audio; see above)
└── Docs/
    ├── DevelopmentDiary.md
    ├── DiaryImages/
    ├── Evaluation/      protocol, instruments, raw data, analysis
    └── Guide/           implementation guide
```

## Scope, history and credits

This repo was split out of my team's game project (Group Flux, COMP3150) so that only my audio work is shown.

- **Commit history** is kept only for my audio files (the acoustic scripts, the audio layer, the audio UI and the FMOD settings), extracted from the team repository with `git filter-repo`, and for the FMOD Studio project.
- **Supporting files** that Unity needs to run the showcase (project settings, packages, the player and its sprites, the beam, the Greybox tiles and the built banks) were added as a single snapshot commit, because teammates also worked on them. That commit message says so.
- Teammates' other code and art, and third-party store assets, are not included.

Middleware: FMOD Studio (Firelight Technologies), Steam Audio (Valve), Unity.

Generative AI was used during this project. Its use is cited and evidenced in the *Statement on Generative AI use* in the Final Project Report.

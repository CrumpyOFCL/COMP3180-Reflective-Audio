# Reflective Room Acoustics for 2D Games

**Tyler Crump** · Student ID 47730757 · COMP3180 Final Project · Session 2, 2026

Commercial acoustic simulation tools such as Steam Audio need 3D meshes, so 2D tilemap games are left with hand-placed reverb zones. This project builds a **listener-centred raycast system** in Unity and FMOD. Rays cast from the listener measure the space around the player, and the measurements drive reverb and directional early reflections in real time. The raycast system is then compared against authored reverb zones and against Steam Audio running on generated proxy geometry.

**Result in brief:** in a playtest with twelve people, the raycast system (B) was rated as convincing as Steam Audio (C) and far above authored zones (A), and FMOD's CPU use stayed under 1% of one core in every condition. B's advantage is implementation cost: no proxy mesh and no third-party plugin. Details, limitations and raw data are in the report and [`Docs/Evaluation/`](Docs/Evaluation/).

**Primary research question:** *To what extent can a listener-centred raycast acoustic approximation match geometry-based acoustic simulation for perceived spatial quality in a 2D game, at lower implementation and runtime cost?*

---

## Deliverables

| # | Deliverable | Where |
|---|---|---|
| 1 | **Unity project**: all three acoustic conditions plus the test scene | [`UnityProject/`](UnityProject/) |
| 2 | **FMOD Studio project**: buses, returns, global parameters, automation (source audio excluded, see below) | [`FMODProject/`](FMODProject/) |
| 3 | **Portfolio video** | [`Link`](https://echo360.net.au/media/0d4af3e3-887a-454f-94cd-07fa26657b4b/public) |
| 4 | **Development diary**: weekly progress, roadblocks, breakthroughs | [`Docs/DevelopmentDiary.md`](Docs/DevelopmentDiary.md) |
| 5 | **Evaluation materials and raw data**: protocol, instruments, results | [`Docs/Evaluation/`](Docs/Evaluation/) |
| 6 | **Implementation guide** for 2D audio designers | [`Docs/Guide/`](Docs/Guide/) |
| 7 | **Reusable package**: `ReflectiveAudio2D.unitypackage` (scripts and a self-building demo scene) and a template FMOD project | [Release: ReflectiveAudioDemo V1.2](https://github.com/CrumpyOFCL/COMP3180-Reflective-Audio/releases/tag/ReflectiveAudioDemo) |
| 8 | **Final Project Report** | Submitted on iLearn |

The portfolio video and other large media are deliberately **not** stored in this repo, because of the unit's storage limits.

---

## The three conditions

| Condition | What drives the room sound | Key components |
|---|---|---|
| **A: Zone Baseline** | Hand-placed trigger zones cross-fade FMOD blending snapshots | `AcousticZone2D`, `bus:/Reverb Zone` |
| **B: Raycast Dynamic** | Rays from the listener measure the room size and how enclosed it is, and find the nearest wall left, right and above. Sounds made away from the player, such as a beam hitting a wall, also get image-source echoes | `AcousticProbe2D`, `EarlyReflections2D`, `ImageSourceEchoes2D`, `bus:/Reverb Dynamic`, `bus:/Reflections` |
| **C: Steam Audio** | Steam Audio simulates the room from a 3D mesh extruded from the tilemap collider | `TilemapAcousticProxy`, Steam Audio Geometry/Listener/Source, `bus:/Steam Audio Reverb` |

`ConditionManager` enables exactly one condition at a time. It mutes the other conditions' return buses and disables their components, so no leftover reverb contaminates a comparison.

---

## Requirements

- **Unity 6.3 LTS** (6000.3.10f1) with the **Input System** package
- **FMOD Studio 2.03.x** and **FMOD for Unity 2.03**. The Studio and integration versions must match.
- **Steam Audio 4.8.1** Unity package plus its FMOD Studio plugin (Condition C only)

## Opening the project

1. Clone the repo.
2. Open `UnityProject/` in Unity Hub. The built FMOD banks are already included at `UnityProject/Assets/FMOD/Desktop/`, so the project plays sound without FMOD Studio.
3. Open `Assets/Scenes/AcousticsGallery.unity` and press **Play**.

**The FMOD Studio project** (`FMODProject/ACIT_Audio.fspro`) opens in FMOD Studio 2.03 and shows the full mixer, routing, global parameters, automation, snapshots and event structure. The source audio files (`.wav` and `.flac`, about 1.3 GB) are deliberately left out because of the unit's storage limits, so FMOD Studio reports them as missing. They are kept in my private audio repository and are available on request.

**Switching conditions:** set **Starting Condition** on the `ConditionManager` component before pressing Play, or press **7** (A), **8** (B), **9** (C) or **Enter** (cycle) while playing.

**FMOD Live Update** uses port **9265** (`localhost:9265`), changed from the default 9264, which clashed with another process.

### The test scene

`AcousticsGallery` is a row of closed rooms of known size, joined by doorways and ending in an open area. It is built by `AcousticsGalleryBuilder` (right-click the component → *Build Rooms*), so the sizes are exact. The same geometry feeds every condition: the zone triggers for A, the rays for B and the proxy mesh for C.

| Room | Interior (units) | Enclosure | Purpose |
|---|---|---|---|
| A Small | 4 × 4 | 1 | Short, dense reverb; very early reflections |
| B Corridor | 20 × 3 | 1 | Near walls above, far walls to the sides: tests the L/R/Up taps |
| C Medium | 10 × 8 | 1 | Mid-size reference room |
| D Hall | 25 × 15 | 1 | Long tail; reflections clearly separate from the direct sound |
| E Outdoors | 30 × open | 0 | Floor only: the open-air contrast |

1 unit = 1 m (`EarlyReflections2D` Units Per Metre = 1). Walls are 2 units thick and doorways 3 units high.

**Study keys** (operator only): **F12** starts the room-estimation task, **P** plays the probe sound during it, **F10** toggles performance logging and **F11** adds a marker. Results are written as CSV to Unity's `persistentDataPath`, which is printed in the Console on Play.

---

## How Condition B works

```
                ┌─ AcousticProbe2D ── 32 rays ─► RoomSize, Enclosure (global params)
AudioListener ──┤                                      │
(child of Player)└─ EarlyReflections2D ── L/R/Up rays ─► ReflectDelay*, ReflectGain*
                                                       │
Player/SFX/Environment/Music groups ── sends ──► Reverb Dynamic return
                                          └────► Reflections group
                                                  ├─ Reflect L   (delay, panned −90°, 40° spread)
                                                  ├─ Reflect R   (delay, panned +90°, 40° spread)
                                                  └─ Reflect UP  (delay, centred, 60° spread)
```

| Global parameter | Range | Written by | Drives |
|---|---|---|---|
| `RoomSize` | 0–1 | `AcousticProbe2D` | Reverb Time (0.4 s → 4 s) on `Reverb Dynamic` |
| `Enclosure` | 0–1 | `AcousticProbe2D` | `Reverb Dynamic` volume (−40 dB → 0 dB) |
| `Brightness` | 0–1 | `AcousticProbe2D` (optional) | Unused |
| `ReflectDelayL/R/Up` | 0–250 ms | `EarlyReflections2D` | Delay time on each Reflect return |
| `ReflectGainL/R/Up` | 0–1 | `EarlyReflections2D` | Wet level on each Reflect return |

The delay comes from the wall distance: `delay (ms) = 2 × distance (m) / 343 × 1000`. A tap fades to silence when its ray hits nothing within 30 units, or when the round trip is shorter than **Min Useful Delay** (set to 6 ms).

The taps assume the sound starts at the listener, which suits footsteps and jumps. For a sound made elsewhere, `ImageSourceEchoes2D` mirrors the source in each nearby wall (the image-source method; Allen & Berkley, 1979) and plays each echo after the extra path length divided by the speed of sound, quieter in proportion to the longer path. Bigger rooms therefore give later, quieter echoes, and a bounce point that falls in a doorway is dropped.

### Scripts (`ReflectiveAudio2D` namespace)

| Script | Purpose |
|---|---|
| `Listener2D` | Keeps the FMOD listener at z = 0 with locked rotation, following the player |
| `AcousticProbe2D` | Ring of rays → `RoomSize`, `Enclosure`, optional `Brightness` |
| `EarlyReflections2D` | Directional taps → delay time and gain per side |
| `ImageSourceEchoes2D` | First-order echoes for one-shots made away from the listener |
| `AcousticZone2D` | Condition A trigger zones that fade blending snapshots |
| `ConditionManager` | One condition at a time; mutes the other buses |
| `TilemapAcousticProxy` | Builds Steam Audio geometry from a `CompositeCollider2D` (right-click → *Build Acoustic Mesh*) |
| `SteamAudioFmodPluginLoader` | Loads Steam Audio's FMOD plugin from `Assets/`, which FMOD for Unity's plugin list can't reach when installed as a package |
| `OccludedEmitter2D` | Per-emitter occlusion through walls |
| `RoomEstimationTask`, `AudioBenchmarkLogger` | Study tools: room-size estimates and performance logging to CSV |
| `AcousticsGalleryBuilder` | Builds the test rooms, Condition A zones, listening spots and the room-estimation ground truth |
| `KeyInput` | Lets the study tools' hotkeys work with the Input System package |
| `SurfaceType` | Surface label (footsteps) and reflectivity (probe brightness) |

---

## Repository layout

```
├── UnityProject/        Unity project, including built banks in Assets/FMOD/Desktop
│   └── Assets/ReflectiveAudio2D/   the system itself: Runtime scripts, Demo, Editor tools
├── FMODProject/         FMOD Studio project (no source audio; see above)
└── Docs/
    ├── DevelopmentDiary.md
    ├── DiaryImages/
    ├── Evaluation/      raw playtest data, analysis scripts, figures, benchmark logs
    └── Guide/           implementation guide
```

## Scope, history and credits

This repo was split out of my team's game project (Group Flux, COMP3150) so that only my audio work is shown.

- **Commit history** is kept only for my audio files (the acoustic scripts, the audio layer, the audio UI and the FMOD settings), extracted from the team repository with `git filter-repo`, and for the FMOD Studio project.
- **Supporting files** that Unity needs to run the showcase (project settings, packages, the player and its sprites, the beam, the Greybox tiles and the built banks) were added as a single snapshot commit, because teammates also worked on them. That commit message says so.
- Teammates' other code and art, and third-party store assets, are not included.
- **Showcase trims.** `Player`, `PlayerAimAndShoot` and `PlayerMovement` are trimmed versions of the game's scripts (walk, jump and shoot only), `ShowcaseBeam` replaces the game's `BeamBehaviour` on the `TimeBeam` prefab, and `ShowcaseCamera` is a simple follow camera for the test scene. The removed systems (skills, saving, respawn, time switching, climbing, box pushing, puzzles) are not part of this repo.
- **`Assets/GameIntegration~/`** holds audio scripts that only make sense inside the full game (area music, puzzle-solved music, sound callables). Unity ignores folders ending in `~`, so they are kept here for reading, with their history, but are not compiled in the showcase.

Middleware: FMOD Studio (Firelight Technologies), Steam Audio (Valve), Unity.

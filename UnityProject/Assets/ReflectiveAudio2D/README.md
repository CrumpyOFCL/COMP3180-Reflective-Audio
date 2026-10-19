# Reflective Audio 2D

Room acoustics for 2D tilemap games in Unity and FMOD. It has three interchangeable approaches, and one component switches between them:

| Condition | How the room sound is made |
|---|---|
| **A: Zone baseline** | Hand-placed trigger zones fade FMOD snapshots in and out |
| **B: Raycast dynamic** | Rays from the listener measure the room and drive a reverb, three directional early reflections, and image-source echoes for sounds made away from the player |
| **C: Steam Audio** | Steam Audio simulates the room from a 3D mesh extruded from the tilemap collider |

By Tyler Crump, COMP3180 Final Project, Macquarie University, 2026.

---

## What's in this folder

```
ReflectiveAudio2D/
├── Runtime/    the system (the scripts below)
├── Demo/       DemoPlayer2D, plus the demo scene, wall tile and material once created
├── Editor/     Tools > Reflective Audio 2D > Create Demo Scene
└── README.md
```

| Script | What it does |
|---|---|
| `Listener2D` | Keeps the FMOD listener on the player at z = 0, at ear height (**Ear Offset**) |
| `AcousticProbe2D` | Casts a ring of rays and writes the global parameters `RoomSize` and `Enclosure` (B) |
| `EarlyReflections2D` | Measures the nearest wall left, right and above and sets the delay and gain of each Reflect return (B) |
| `ImageSourceEchoes2D` | Plays the echoes of a one-shot made away from the player, such as a hit on a far wall (B) |
| `AcousticZone2D` | A trigger zone that fades an FMOD snapshot in while the player is inside (A) |
| `ConditionManager` | Turns one condition on and mutes the other conditions' buses. **7 / 8 / 9** switch between A, B and C |
| `TilemapAcousticProxy` | Builds a 3D mesh from a tilemap's Composite Collider 2D for Steam Audio (C) |
| `SteamAudioFmodPluginLoader` | Loads Steam Audio's FMOD plugin when FMOD for Unity can't find it (C) |
| `OccludedEmitter2D` | Muffles an emitter when walls are between it and the listener |
| `SurfaceType` | Tags a surface with a footstep material name and a reflectivity |
| `AcousticsGalleryBuilder` | Draws five rooms of known size with zones and listening spots (right-click → **Build Rooms**) |
| `RoomEstimationTask` | Study tool: blindfolded room-size estimates, saved to CSV |
| `AudioBenchmarkLogger` | Study tool: frame time and FMOD CPU per condition, saved to CSV |
| `KeyInput` | Key checks that work with both the Input System and the old Input Manager |

## Requirements

- Unity 6 (built with 6000.3.10f1). The demo scene suits a 2D URP project.
- **FMOD for Unity 2.03**, with a matching FMOD Studio 2.03.
- **Input System** package. The scripts also run under the old Input Manager.
- *Optional, Condition C only:* Steam Audio's Unity package and FMOD Studio plugin, from Valve's Steam Audio releases on GitHub. Built with Steam Audio 4.8.1.

## Install

1. Install FMOD for Unity and link your FMOD Studio project (**FMOD → Edit Settings**).
2. **Assets → Import Package → Custom Package…** and choose `ReflectiveAudio2D.unitypackage`.
3. Check that the Console shows no errors.

## Quick start: the demo scene

1. **Tools → Reflective Audio 2D → Create Demo Scene.** It builds the five rooms (4×4, a 20×3 corridor, 10×8, 25×15 and an open area), a stand-in player with the listener rig, a Condition Manager and the room-estimation task. If your project lacks the `Solid` and `Player` layers, it adds them.
2. Check the Console line `Walls: N tiles, N collision outline(s)`. If it warns that Player doesn't collide with Solid, tick that pair in **Project Settings → Physics 2D → Layer Collision Matrix**.
3. On **Player → Demo Player 2D**, set **Impact Event** to any short one-shot from your FMOD project, then save the scene.
4. **Condition A only:** on each zone under **Acoustic Zones (Condition A)**, set **Zone Snapshot** to the matching snapshot (for example `Zone_SmallRoom`). The outdoor zone needs none. Without snapshots, Condition A stays dry.
5. Make sure your FMOD project has the buses and parameters listed below, then build the banks.
6. Press **Play**:
   - **A / D** walk.
   - **Space** jumps.
   - **Left-click a wall** to fire at it.
   - **7 / 8 / 9** switch between conditions A, B and C.

The demo starts in Condition B. Fire at the far wall of each room: as the room gets bigger, the echo arrives later and quieter. It comes from the side of the wall it bounced off.

## Condition C (Steam Audio) in the demo scene

Create Demo Scene sets up Conditions A and B. Condition C needs these extra steps:

1. **Install Steam Audio 4.8.1**: the Unity package and its FMOD Studio integration for Unity, which provides `phonon_fmod.dll`. In Steam Audio's settings, set **Audio Engine** to **FMOD Studio**.
2. **Load the FMOD plugin.**
   1. Choose **Assets → Create → Reflective Audio 2D → Steam Audio FMOD Plugin Loader**.
   2. Drag the new asset into **FMOD → Edit Settings → Callback Handler**.
   3. Remove `phonon_fmod` from **Dynamic Plugins** if it's listed.
   4. On the asset, check that **Editor Folder** is the folder holding `phonon_fmod.dll`, and **Editor Dependency Folder** the folder holding `phonon.dll`. Both are relative to `Assets`.
   5. Press Play. The Console should show `[SteamAudioFmodPluginLoader] Loaded phonon_fmod.dll`.
3. **Build the proxy mesh.**
   1. Create an empty GameObject at (0, 0, 0) called *Acoustic Proxy* and add **Tilemap Acoustic Proxy** to it.
   2. Drag the **Walls** object's Composite Collider 2D into its **Source Collider** field.
   3. Right-click the component header and choose **Build Acoustic Mesh**.
4. **Export the geometry.** Add **Steam Audio Geometry** to *Acoustic Proxy*, with a material such as Concrete, then run **Steam Audio → Export Active Scene**. Export again whenever the walls change.
5. **Add the listener.** On **Player → Listener**, add **Steam Audio Listener** and tick **Apply Reverb**.
6. **Hand the proxy to the Condition Manager.** Add *Acoustic Proxy* to its **Steam Audio Objects** list, so the proxy is only active in Condition C.
7. **Test it.** Press Play, press **9**, and fire in the hall.

**Not using Steam Audio?** The example FMOD project's **Steam Audio Reverb** return holds Steam Audio's plugin effect. Without the plugin, Unity can't load the Master bank at all: you get `Missing DSP plugin 'Steam Audio Reverb'`, then `ERR_PLUGIN_MISSING`. In FMOD Studio, delete that effect from the return, keep the return itself, and rebuild. Conditions A and B then work, and C stays silent.

## What your FMOD project needs

The scripts drive FMOD through bus paths and **global** parameters. Start from the example FMOD project released alongside this package, or build the following in your own.

**Return buses.** Put these at the top level, with Dry Level at −∞, fed by sends from your sound groups:

| Bus path | Used by |
|---|---|
| `bus:/Reverb Zone` | Condition A (the snapshots set its reverb) |
| `bus:/Reverb Dynamic` | Condition B reverb |
| `bus:/Reflections`, a group holding **Reflect L**, **Reflect R** and **Reflect UP** | Condition B early reflections. Each holds a Delay with Feedback 0%, panned −90°, +90° and 0° |
| `bus:/Steam Audio Reverb` | Condition C |

**Global parameters:**

| Parameter | Range | Automate |
|---|---|---|
| `RoomSize` | 0–1 | Reverb Dynamic: Reverb Time (about 0.4 s → 4 s) |
| `Enclosure` | 0–1 | Reverb Dynamic: volume (about −40 dB → 0 dB) |
| `Brightness` | 0–1 | Optional |
| `ReflectDelayL`, `ReflectDelayR`, `ReflectDelayUp` | 0–250 | Delay time on each Reflect return (ms) |
| `ReflectGainL`, `ReflectGainR`, `ReflectGainUp` | 0–1 | Wet level on each Reflect return |

You can change the bus paths on **Condition Manager**, and the parameter names on **Acoustic Probe 2D** and **Early Reflections 2D**.

**Sound sent to the Reflect returns must not also be echoed by `ImageSourceEchoes2D`**, or it echoes twice. In the example project, beam hits go through a group that sends to the reverbs but not to the Reflect returns.

## Using it in your own game

1. Put walls on a layer such as `Solid`. Tag the player `Player`.
2. Give the player a child object with these components, and set their wall layers:
   - **Studio Listener**
   - **Listener 2D** (set its Player and Ear Offset)
   - **Acoustic Probe 2D**
   - **Early Reflections 2D**
   - **Image Source Echoes 2D**
3. Add one **Condition Manager** to the scene and give it the probe and the reflections. For a game that ships only Condition B, set the starting condition to B.
4. For a sound made away from the player, play the one-shot as usual, then call `ImageSourceEchoes2D.Emit(eventReference, position)`.
5. *Condition A:* add trigger zones with **Acoustic Zone 2D**, each with a snapshot.
6. *Condition C:*
   1. Add **Tilemap Acoustic Proxy** to the tilemap and right-click it → **Build Acoustic Mesh**.
   2. Add Steam Audio Geometry to the result.
   3. Load Steam Audio's FMOD plugin with the plugin loader, as in step 2 of *Condition C (Steam Audio) in the demo scene* above.

## Limitations

- **One FMOD project.** The mixer lives in the Master bank, and banks from two different FMOD projects can't be combined. Start from the example project, or rebuild the buses and parameters above in your own.
- Walls are assumed to be axis-aligned, as on a tilemap. Echoes are first-order only: one bounce per wall.
- Echoes are timed by a coroutine, so they can land up to one frame late (about 8 ms on average at 60 fps).
- The study tools write their CSV files to `Application.persistentDataPath`.

## Credits

- FMOD Studio by Firelight Technologies, Steam Audio by Valve, and Unity by Unity Technologies.
- The image-source method follows Allen and Berkley (1979), "Image method for efficiently simulating small-room acoustics", *Journal of the Acoustical Society of America* 65(4).

*Source project only:* **Tools → Reflective Audio 2D → Package Exporter** gathers, checks and exports this folder. It is left out of the package.
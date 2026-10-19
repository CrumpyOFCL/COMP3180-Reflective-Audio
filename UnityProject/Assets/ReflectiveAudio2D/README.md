# Reflective Audio 2D

Room acoustics for 2D tilemap games in Unity and FMOD. It has three interchangeable approaches:

| Condition | How the room sound is made |
|---|---|
| **A: Zone baseline** | Hand-placed trigger zones fade FMOD snapshots in and out |
| **B: Raycast dynamic** | Rays from the listener measure the room and drive a reverb, three directional early reflections and image-source echoes for sounds made away from the player |
| **C: Steam Audio** | Steam Audio simulates the room from a 3D mesh extruded from the tilemap collider |

By Tyler Crump, COMP3180 Final Project, Macquarie University, 2026.

---

## Requirements

- Unity 6 (built with 6000.3.10f1)
- **FMOD for Unity 2.03**, with a matching FMOD Studio 2.03
- **Input System** package. The scripts also run under the old Input Manager.
- *Optional, Condition C only:* the Steam Audio Unity package and its FMOD Studio plugin

## Install

1. Install FMOD for Unity and link your FMOD Studio project (**FMOD → Edit Settings**).
2. **Assets → Import Package → Custom Package…** and choose `ReflectiveAudio2D.unitypackage`.
3. Check the Console for compile errors. There should be none.

## Quick start: the demo scene

1. **Tools → Reflective Audio 2D → Create Demo Scene**. This builds five rooms of known size (4×4, 20×3 corridor, 10×8, 25×15, open area), a stand-in player with the listener rig, a Condition Manager and the room-estimation task. It also creates a `Solid` layer if your project has none.
2. On **Player → Demo Player 2D**, set **Impact Event** to any short one-shot from your FMOD project.
3. Make sure your FMOD project has the buses and parameters listed below, then build the banks.
4. Press **Play**. **A/D** walk, **Space** jumps, **left click** fires at a wall. **7 / 8 / 9** switch between conditions A, B and C.

In Condition B, fire at the far wall of each room and listen. The echo arrives later and quieter as the room gets bigger, and from the side of the wall that reflected it.

## What your FMOD project needs

The scripts drive FMOD through bus paths and **global** parameters, so your FMOD Studio project needs the following. The included `FMODProject` is a working example to start from.

**Return buses** (top level, Dry Level −∞, fed by sends from your sound groups):

| Bus path | Used by |
|---|---|
| `bus:/Reverb Zone` | Condition A (snapshots set its reverb) |
| `bus:/Reverb Dynamic` | Condition B reverb |
| `bus:/Reflections` (group) containing **Reflect L**, **Reflect R**, **Reflect UP** | Condition B early reflections. Each has a Delay with Feedback 0%, panned −90°, +90° and 0°. |
| `bus:/Steam Audio Reverb` | Condition C |

**Global parameters:**

| Parameter | Range | Automate |
|---|---|---|
| `RoomSize` | 0–1 | Reverb Dynamic: Reverb Time (about 0.4 s → 4 s) |
| `Enclosure` | 0–1 | Reverb Dynamic: volume (about −40 dB → 0 dB) |
| `Brightness` | 0–1 | Optional |
| `ReflectDelayL`, `ReflectDelayR`, `ReflectDelayUp` | 0–250 | Delay time on each Reflect return (ms) |
| `ReflectGainL`, `ReflectGainR`, `ReflectGainUp` | 0–1 | Wet level on each Reflect return |

The bus paths can be changed on the **Condition Manager** component. The parameter names can be changed on **Acoustic Probe 2D** and **Early Reflections 2D**.

## Using it in your own game

1. Put walls on a layer such as `Solid` and tag the player `Player`.
2. Add a child object to the player with **Studio Listener**, **Listener 2D** (set Player and Ear Offset), **Acoustic Probe 2D**, **Early Reflections 2D** and **Image Source Echoes 2D**. Set their wall layers.
3. Add one **Condition Manager** to the scene and give it the probe and reflections. For a game that ships with only Condition B, set the starting condition to B.
4. For sounds made away from the player, play the one-shot as usual, then call `ImageSourceEchoes2D.Emit(eventReference, position)`.
5. *Condition A:* add trigger zones with **Acoustic Zone 2D** and a snapshot each.
6. *Condition C:* add **Tilemap Acoustic Proxy** to the tilemap, right-click it and choose **Build Acoustic Mesh**, then add Steam Audio Geometry to the result. If Steam Audio's FMOD plugin fails to load (`ERR_FILE_NOTFOUND`), create **Assets → Create → Reflective Audio 2D → Steam Audio FMOD Plugin Loader** and set it as **FMOD → Edit Settings → Callback Handler**.

## Limitations

- **One FMOD project.** The mixer lives in the Master bank, and banks from two different FMOD projects can't be combined. Start from the example project, or rebuild the buses and parameters above in your own.
- Walls are assumed to be axis-aligned, as they are on a tilemap. Echoes are first-order only (one bounce per wall).
- Echoes are timed by a coroutine, so they can land up to one frame late (about 8 ms on average at 60 fps).
- The study tools (**Room Estimation Task**, **Audio Benchmark Logger**) write CSV files to `Application.persistentDataPath`.

## Credits

FMOD Studio by Firelight Technologies; Steam Audio by Valve; Unity by Unity Technologies. The image-source method follows Allen & Berkley (1979). 

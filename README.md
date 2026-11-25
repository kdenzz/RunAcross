# Run Across

A neon platformer-puzzler built in Unity 6 (URP). This repo is the rebuild. The in-game graphics match the style of the original Blender render, and the first playable level is in.

| Target (Blender render) | Main menu (the same shot, live) |
|---|---|
| ![](Docs/reference_target.jpg) | ![](Docs/menu.png) |

| Shoot targets to drop the laser gates | The bridge rises out of the fog |
|---|---|
| ![](Docs/play_gate.png) | ![](Docs/play_bridge.png) |

## The game so far

- **Main menu** on the hero shot from the Blender render. Only the mouse is active. **Play** blends the camera down to the courier and hands over control. **Settings**, **Controls** and **Exit** sit next to it.
- **Settings**, saved between sessions:
  - Graphics: **Low / Medium / High**.
  - Volume: **Master / Music / Effects**.
  - Mouse sensitivity, invert camera Y, and fullscreen (builds only).
- **Pause** with Esc: Resume, Restart run, Settings, Controls, Main menu, Exit.
- **Level 01 "The Arches"**:
  1. The start island is walled in. A laser gate in front of the arches drops when both orange targets are shot.
  2. A line of floating slabs runs into the fog.
  3. A gap too wide to jump has a bridge that rises when the target across it is shot.
  4. A second gate's targets hang out beyond the lasers, so you shoot through them.
  5. Stepping pads lead to the goal ring, which has a light beacon visible from the start.
  6. Falling sends you back to the last checkpoint.
- **HUD**: objective with a target count, a run timer, a crosshair and toasts. The finish screen shows your time and your best time.

### Graphics presets

| | Low | Medium | High |
|---|---|---|---|
| Render scale | 75% | 100% | 100% |
| MSAA | off | 2x | 4x |
| Shadows | off | soft, 1024 | soft, 2048 |
| Wet-floor reflection | 30% res | 55% res | full res |
| Depth of field | off | Gaussian | Bokeh |
| Anti-aliasing (post) | none | FXAA | SMAA |

Each preset is a Unity quality level with its own URP asset (`Settings/RP_*.asset`). `GraphicsApplier` adds the per-preset post-processing and reflection settings on top.

## Controls

| Input | Action |
|---|---|
| WASD / left stick | Move |
| Mouse / right stick | Look / aim |
| Left click / right trigger | Shoot |
| Space | Jump (with coyote time and a jump buffer) |
| Shift | Sprint (needed to clear the gaps) |
| Esc / Start | Pause |

## Layout

```
Art/Source/SF.blend        source scene (Blender 4.5, Git LFS)
Tools/export_fbx.py        headless Blender export -> Unity
Tools/gen_audio.py         synthesizes the placeholder music + SFX (stdlib Python)
RunAcross/                 Unity 6000.6.5f1 project (URP)
  Assets/_Project/
    Art/                   models, textures, materials (generated)
    Audio/                 music_neon_loop.wav, sfx_*.wav (generated, replaceable)
    Editor/LevelBuilder.cs     rebuilds Level_01; all look values live here
    Editor/CourseBuilder.cs    the course layout: platforms, targets, gates, bridge, goal
    Editor/QualityPresets.cs   Low / Medium / High quality levels + URP assets
    Scenes/Level_01.unity
    Scripts/Core/          GameFlow (menu/play/pause/complete), GameSettings, GraphicsApplier
    Scripts/Gameplay/      ShootTarget, PathOpener, Checkpoint, GoalZone
    Scripts/Player/        PlayerMotor, PlayerShooter
    Scripts/Audio/         GameAudio (master / music / fx)
    Scripts/UI/GameUI.cs + UI/GameUI.uxml, GameUI.uss   (UI Toolkit)
    Scripts/Rendering/PlanarReflection.cs
    Shaders/NeonWater.shader, GradientSky.shader
```

## Pipeline

1. Edit `Art/Source/SF.blend` in Blender.
2. Export it:
   ```sh
   /Applications/Blender.app/Contents/MacOS/Blender -b Art/Source/SF.blend -P Tools/export_fbx.py
   ```
   This step also bakes the floor's procedural bump into a normal map and a smoothness map, and packs the glTF metal/roughness maps into Unity's MetallicSmoothness format.
3. Optional: regenerate the audio with `python3 Tools/gen_audio.py`. To use a real track or effect, drop in a WAV with the same file name.
4. In Unity, run **RunAcross > Build Level 01**. It rebuilds the materials, the quality presets, the scene, the course, the UI and the audio wiring.

## How the look is made

- **Emissives**: HDR emission with ACES tonemapping and bloom (threshold 0.9, scatter 0.8). The hot cores turn yellow and the edges stay orange.
- **Wet floor**: `PlanarReflection` renders a mirrored camera into an HDR texture, so the neon reflections still bloom. `NeonWater` samples it with ripple distortion and fresnel. URP has no screen-space reflections.
- **Atmosphere**: exponential fog matched to the horizon colour of the gradient skybox, a dark blue sun, and trilight ambient.
- **Lens**: bokeh depth of field. It is focused at 11.5 m on the menu shot and at 5 m on the courier in play.
- **Building windows**: Blender lights them with a red area light, and URP area lights are bake-only. Emissive panes stand in for it, plus a point light that spills the colour onto the floor.

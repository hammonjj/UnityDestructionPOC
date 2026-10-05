# Unity Destruction POC — Destruction Lab

A small, playable lab for checking believable structural failure in Unity. The behaviour it is built around is the in-between state: a damaged structural connection fails while a weaker residual attachment survives. A floor or wall then tilts, sags, hangs, settles against another surface, or tears free. Load, contact and damage drive that sequence, never a timer.

It is an independent implementation inspired by the published principles behind THE FINALS. See [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md) for the approximations and how they relate to the [research notes](Docs/THE_FINALS_Destruction_Research.md).

![Partial attachment: the floor hangs from a residual hinge](Docs/screenshots/4a_hanging.png)

## Run it

1. Open the `DestructionPOC/` folder in Unity **6000.5.10f1** (URP, Input System). Any 6000.5 editor should work.
2. Open `Assets/Scenes/DestructionLab.unity`. It is the only scene in Build Settings.
3. Press **Play**. The lab starts on scenario **4a · Partial attachment → separation**.

Nothing else is needed. All geometry is procedural boxes, with no art, packages or mesh preparation.

## First three things to try

1. **4a:** press **T**. Both props are knocked out. The floor's wall joint fails by overload and turns into a residual hinge. The floor sags and hangs for about 3 s, then tears under its own weight. The log on the bottom right shows each step and its reason.
2. **4b:** press **N**, then **T**. The same floor and hinge come down onto a low wall and stop there. Select **4 Inspect** and click the floor. The joint now carries only part of the weight, residual q is below 1, and residual damage stops growing.
3. **3b:** press **B** twice to go back, then **T**. The balcony still has a path to the ground through narrow pier 5. That narrow connection is overloaded, fails as "structural overload", and cannot hold the balcony.

## Controls

| Input | Action |
|---|---|
| Left click | Use the current tool on the structure. Clicks that start or end over the UI are ignored. |
| 1 / 2 / 3 / 4 | Tools: Damage, Explode, Drop block, Inspect |
| Right drag / middle drag (or Shift + right drag) / wheel | Orbit / pan / zoom |
| W A S D, Q E | Move the camera pivot. **F** re-frames the scenario. |
| T | Run the scenario's trigger, the canonical action for that demo |
| R | Reset the scenario: simulation, diagnostics, selection, time scale and camera |
| N / B (PageDown / PageUp) | Next / previous scenario |
| Space | Pause / resume |
| . (period) | Advance exactly one fixed physics step, which pauses first |
| [ / ] | Slow motion: 1×, 0.25×, 0.1× |
| F1 | Diagnostics overlay on/off |
| F2 | Piece tint: material / load / cluster / sleep state |
| H | Hide the help panel |
| Esc | Clear the selection |

The on-screen panels give the same information. The scenario panel is on the left, and tools, playback and tuning are on the right. Help is bottom-left. Stats, the selected connection and the break log are bottom-right.

## Scenarios

| # | What you do | What you should see |
|---|---|---|
| 1 · Intact stability | Nothing; wait 60 s | Nothing moves, no failures. The intact structure has no rigid bodies at all. |
| 2 · Local damage | Damage-click the outlined first-floor panel 3× (or **T**) | Only that panel drops out. Two clicks plus a short wait also work, because the weakened joints then fail by overload. |
| 3 · Loss of support | **T** demolishes the middle bridge pier | Both inner deck joints are overloaded, become residual hinges, and the deck sags into a V. A hinge may tear. The abutments stand. |
| 3b · Capacity vs connectivity | **T** cuts the balcony from piers 1–4 | The narrow pier 5 still connects it to the ground, but the joint fails by overload. Its residual is far too weak and the balcony drops. |
| 4a · Partial attachment → separation | **T** knocks out the props | Overload, then a residual hinge, a plastic sag, and a hang lasting a few seconds. It then tears from residual-joint failure. |
| 4b · Partial attachment → arrested | **T** knocks out the props | The floor hinges down onto a low wall and stays attached. Damage stays flat. |
| 5 · Secondary collapse | **T** cuts the top balcony | It falls onto the next balcony, whose joint fails by impact and hangs with the debris. Further failures from impact or overload stay bounded, and the log goes quiet. |
| 6 · Persistent rubble | Wait for sleep (F2 → sleep tint), then **T** | A 2 t block lands on the pile. The pieces it touches wake, the pile supports it, and everything sleeps again. |

The frame building in scenarios 1–2 is also the free-play sandbox. Try the Explode tool on a column, or drop blocks on a floor.

## Reading the diagnostics

- **Connection lines (x-ray):** green → yellow → red shows the structural load ratio q. Magenta is a residual hinge with a live joint. Purple is a dormant residual, which has failed but the two pieces are still in one rigid body. Grey is severed. White squares are ground anchors.
- **Crosses:** orange is an active rigid body and blue is sleeping. Yellow marks a hinge point, with a line to the hanging body's centre of mass.
- **Inspect** a piece to see its nearest connection. Capacities and loads are shown in kN and kN·m. q is a dimensionless load ÷ capacity ratio with its normal, shear and bending components. The panel also shows damage D. For residual hinges it shows joint force against capacity, hinge moment against yield, sag angle, residual q and residual damage.
- **Break log:** every transition is listed with its reason: direct damage, direct damage (explosion), structural overload, residual-joint failure, impact, or residual budget. Each entry also shows q and D at the time.
- **Stats:** FPS, physics step time (Profiler `Physics.Simulate`, recent maximum), structural work time per fixed step, pieces, active and sleeping bodies, joints, connection counts, failures, and pending failures.

## Tuning

Routine experimentation needs no code edits.

- **Runtime sliders** cover click damage, explosion radius and impulse, drop-block mass, the residual strength multiplier (applies live), the bond strength multiplier (applies on reset) and the impact speed gate. Toggles switch residual joints and impact damage on or off. Slider changes affect only the running copy and are not saved.
- **`Assets/Destruction/Settings/DestructionSettings.asset`** holds every constant, grouped as structure, residual, physics, impact, tools and materials, with tooltips and SI units. **Destruction Lab → Reset Settings To Defaults** restores the code defaults. **Destruction Lab → Rebuild Lab Scene** regenerates the scene and materials.

The most useful knobs:

| Setting | Effect |
|---|---|
| `structure.safetyFactor`, `designLiveLoad` | How much extra load intact structure tolerates |
| `materials[].tensileStrength` | Bond and bending strength, so how easily overload failures happen |
| `materials[].residualForcePerMeter` | Whether hinges hold or tear. 4a sits just above capacity on purpose. |
| `materials[].residualYieldMomentPerMeter`, `residual.sagRateDegreesPerSecond` | How far and how fast hinges sag |
| `impact.minRelativeSpeed`, `materials[].impactToughness` | How hard a hit must be to do damage |
| `impact.maxFailuresPerStep` | Cascade bound per fixed step. The rest wait and are never dropped. |

## Tests

Pure logic runs in EditMode, 16 tests: adjacency, load model including the cantilever disproving experiment, calibration, damage law, impact filter and hinge math. Physics behaviour runs in PlayMode, 14 tests. These step the simulation deterministically with `SimulationMode.Script` and cover spike S1–S3 (hang, arrest, tear), joint recreation, split pose and velocity continuity, explosion impulse applied once, reset ×10, and every scenario.

```sh
cd DestructionPOC
unity test . --mode EditMode
unity test . --mode PlayMode
```

You can also use **Window → General → Test Runner** in the editor.

## Validation results (2026-10-05)

Measured on an Apple M5 Pro (16 cores, 24 GB) in the editor, Play mode, Game view 1600×900, URP PC asset, fixed step 0.02 s.

| Check | Result |
|---|---|
| Compile | 0 errors, 0 warnings from project code |
| EditMode / PlayMode tests | 16/16 and 14/14 passing |
| Console during a full scenario sweep | 0 errors, 0 warnings |
| Spike S1 hang | Settles at the 80° limit. Drift < 1 cm over 10 s, joint force 95.3 kN against 94.2 kN weight, residual q 0.98 with the test's 1.6× residual strength |
| Spike S2 arrest | Floor rests at 22.7° on the low wall. Residual damage changed by 0.000 over 20 s. |
| Spike S3 tear | Tears more than 3 s after the hinge forms, by residual-joint failure; the test enforces > 3 s. In live Play mode the hinge formed at 2.60 s and tore at 5.80 s. |
| Reset ×10 | Piece, connection, joint, body and log counts back to initial values each time |
| Worst observed collapse | Two large explosions in the 52-piece frame: 133 transitions, peak 43 bodies and 9 joints. About 700 FPS, physics ≤ 0.30 ms per step, structural ≤ 2.3 ms per step. Everything asleep after about 10 s. |

Provisional target: ≥ 60 FPS with ≤ 150 active bodies and ≤ 40 joints. The worst case above is well inside it.

**Still to check by hand:** real mouse and keyboard input in the Game view. That covers that clicking a UI button does not also damage the structure, orbit and pan feel, and slow-motion smoothness. Automated checks drove the controller through its public methods, not a physical mouse.

## Limitations

- The structural model is a quasi-static load propagation, not a stress solver. It is exact for a single piece on its supports. It ignores load sharing between pieces at the same graph level. It does not transmit bending moment through joints, so multi-piece cantilevers are underestimated. Calibration against the intact structure absorbs the remaining artefacts.
- Pieces are axis-aligned boxes. There is no runtime mesh cutting, true deformation, fragment layer or multiplayer.
- Residual hinges are PhysX `ConfigurableJoint`s. Clusters joined by a residual joint with a mass ratio above 10:1 are mass-scaled. At most two parallel hinges per cluster survive; others are severed and logged as "residual budget".
- Fully detached debris shrinks 4% so it cannot wedge in the exact-fit hole it came from.
- PhysX is not deterministic across platforms. Scenario construction, including the seeded rubble pile, is repeatable.
- Optional milestone not built: a first-person controller with a hitscan gun ([#14](https://github.com/hammonjj/UnityDestructionPOC/issues/14)).

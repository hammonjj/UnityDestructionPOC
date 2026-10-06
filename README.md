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

## Game flow: title, lobby, split screen

`Assets/Scenes/Title.unity` (menu **Destruction Lab → Rebuild Title Scene**) is first in the build. **Start** opens a lobby where up to two players join: any key (keyboard + mouse) or **A** (a gamepad). **Enter** / **Start** begins; **Esc** / **B** goes back. The game scene, `ConvenienceStore.unity`, then spawns one player per slot. Two players split the screen side by side, each with their own camera, HUD and devices: a keyboard + mouse or one gamepad each. Two players cannot climb into the same machine. Opening the scene directly (or the tests) gives one player on every device, as before.

| Action | Keyboard + mouse | Gamepad |
|---|---|---|
| Respawn the machine you are in (on foot: the nearest one within 14 m) | X | Y |
| Respawn yourself at your spawn point (leaves any machine) | Q | Back |
| Reset everything | Backspace | Start |

The lab's debug HUD and diagnostic overlays are not built in the game scene (the plain lab keeps them). While driving, a dial bottom-right shows the bucket tilt against the horizon (skid steer, wheel loader; the pour angle is marked) or, on machines whose upper body turns on the tracks (wrecking crane, excavator), where the tracks point relative to the cab, with an arrow for drive-forward.

## Crane test scene

`Assets/Scenes/CraneTest.unity` (menu **Destruction Lab → Build Crane Test Scene** rebuilds it) puts the wrecking crane and the brick warehouse on an open plane, with a first-person player. Walk to the crane's cab steps (left side of the machine) and press **E** / **X** to climb in. The crane is a prop, not a structure: its ball is a free 25 t rigid body on a rope limit, and it damages the warehouse through the normal contact path. Impacts need roughly 6 m/s at this ball mass to break brick, so swing it rather than nudge it.

| Action | Keyboard + mouse | Gamepad |
|---|---|---|
| Move / look / run / jump | WASD / mouse / Shift / Space | Left stick / right stick / L-stick click / A |
| Enter or leave the cab | E | X (enter), X or B (leave) |
| Slew the upper carriage | A / D | D-pad left / right |
| Raise / lower the boom | W / S | D-pad up / down |
| Pay out (ball down) / reel in (ball up) | F / R | Right trigger / left trigger |
| Drive / turn the tracks | Arrow keys | Left stick |
| Look around the cab | Mouse | Right stick |
| Rebuild the warehouse and reset the crane | Backspace | Start |
| Release / recapture the mouse | Esc / click | |

Driving does not collide with the warehouse, so the crane can be driven through it. The code is in `Runtime/Crane/`.

### Excavator yard

Four excavators stand in a row south of the crane, each facing a labelled destructible target: a **concrete crusher** (capped concrete wall and columns), a **steel shear** (segmented steel portal frame), a **hydraulic breaker** (3 × 3 ground slab) and a **sorting grapple** (loose chunks and timbers, plus one block over the 2.5 t lift limit). They share one Blender model (`Tools/blender/build_excavator.py`, handoff in `Tools/blender/Excavator/HANDOFF.md`) with interchangeable attachments. Walk to a machine's cab steps and use the same enter/exit buttons as the crane. While you operate any machine, a controls panel in the bottom-left corner lists its live bindings for the device you last used.

| Action | Keyboard + mouse | Gamepad |
|---|---|---|
| Drive forward / back | W / S | Left stick up / down |
| Turn the tracks | A / D | Left stick left / right |
| Swing the upper body | Z / C | Right stick left / right |
| Raise / lower the boom | R / F | Right stick up / down |
| Extend / retract the stick | T / G | Hold LB + right stick up / down |
| Curl the attachment in / out | Y / H | Hold LB + right stick left / right |
| Close jaws / run the breaker | Left mouse (hold) | Right trigger (proportional; breaker above 35%) |
| Open jaws (not on the breaker) | Right mouse (hold) | Left trigger |
| Leave | E | B or X |

E is the enter/exit key, so the suggested Q/E swing moved to Z/C and the curl to Y/H. While LB is held, the right stick moves only the stick and wrist. Leaving stops all powered motion and holds the pose; a grapple keeps its load until you open it. Reset (Backspace / Start) takes you out of any machine and rebuilds every target.

Collision: every track, swing, boom, stick, curl and jaw move is refused if it would push the machine further into static structure, intact targets, the crane or another excavator. The tool also stops at the ground. Loose debris and fragments do not block; the moving parts push them aside, though only gently, because the parts are kinematic and simply push overlapping debris clear.

Approximations: jaws stop when they press on something solid, or at a fixed closure while something valid is in the bite. The shear frees the steel segment in its throat by severing that segment's joints rather than cutting a mesh. The attachment housings' collision box stops above the bite, and the boom, stick and jaws use convex hulls, so contact is close but not exact.

### Loader yard (debris cleanup)

East of the excavators (x ≈ 39–51, z ≈ −35…−12) stand an **articulated wheel loader**, a **wheeled skid-steer loader**, a pile of loose concrete rubble in front of each (small chunks, stacked layers, one oversized block each) and a stationary open-top **collection container** (12 × 2.8 m, 1.1 m walls, with a sloped loading chute on the near wall). Models: `Tools/blender/build_wheel_loader.py` and `build_skid_steer.py` (sources in `Tools/blender/WheelLoader` and `SkidSteer`, node/pivot contract in each `HANDOFF.md`, FBX in `Assets/Destruction/Models/`). Walk to the cab steps and use the usual enter/exit buttons.

Loop: drive the bucket into the rubble, curl back and raise, carry it to the container, hold the arms high enough to clear the wall and tip forward. Nothing needs a "collect" button.

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Forward / reverse | W / S | Left stick Y |
| Steer (wheel loader bends, skid steer turns, in place too) | A / D | Left stick X |
| Raise / lower arms | R / F | Right stick Y |
| Curl back / tip forward | Z / C | Right stick X |
| Exit | E | B or X |

E is enter/exit, so the suggested Q/E bucket tilt is Z/C (the excavators' swing keys). Sticks use a dead zone (0.18) and a response curve; inversion per axis is `LoaderTuning.invertDrive/Steer/Lift/Tilt` on the bootstrap. Every drive, steering, lift, tilt, capacity and articulation limit is on `CraneTestBootstrap.wheelLoaderTuning` / `skidSteerTuning`. The controls panel (bottom-left) shows the live bindings and bucket load; a bar in the top-right shows cleanup progress and the bucket's load against its capacity.

| | Wheel loader | Skid steer |
|---|---|---|
| Steering | chassis bends ±38° at the central pivot (articulated-vehicle kinematics) | left/right wheel sides at different speeds; spins in place |
| Top speed | 5.0 m/s | 3.6 m/s |
| Bucket capacity | 4,500 kg, pieces up to 2,000 kg / 1.4 m | 900 kg, pieces up to 400 kg / 0.9 m |
| Arms | 0–64° | 0–66° |

**Load model (an approximation).** Debris stays ordinary destruction pieces, including fragments produced in play. A piece is scooped when its centre is inside the bucket cavity, or just ahead of the cutting edge while the bucket moves forward into it, with a clear line of sight from the lip (nothing is taken through walls, from behind or from the sides). It then glides into a height-map packing of the cavity and is frozen there as a kinematic body (impact damage off), so loads are stable at any speed and nothing is lost; when the packing is full or the mass limit is reached the rest stays outside. Oversized or too heavy pieces are never taken. Tipping the opening past 20° (or rolling the bucket on its side) releases pieces one at a time, lip first, as dynamic bodies with a short push along the floor. There is no fluid or per-fragment aggregate; a huge number of tiny fragments is limited by the cavity packing. The buckets tip down to 95° (wheel loader) and 140° (skid steer) from their arms, so material can be shaken out steeply; the wheel loader's bucket back meets its arm mesh slightly beyond about 67°, a visual overlap only.

**Accounting.** Mass is credited once per piece, only when an unloaded piece has stayed inside the container's interior for 0.5 s and is not being carried; hovering above the container with a full bucket earns nothing. Accepted pieces are marked no-shatter (so fragmenting cannot duplicate credit) and freeze where they settle, so the rubble pile in the container grows and cannot be thrown back out. Nothing in the world removes debris except the kill plane, so a loaded piece cannot vanish. Reset releases both buckets, restores both machines, rebuilds the debris and clears the ledger.

## Scenarios

| # | What you do | What you should see |
|---|---|---|
| 1 · Intact stability | Nothing; wait 60 s | Nothing moves, no failures. The intact structure has no rigid bodies at all. |
| 2 · Local damage | Damage-click the outlined first-floor panel 3× (or **T**) | Three clicks shatter only that panel into rubble. With two clicks and a short wait, the weakened joints fail by overload and the panel drops out whole. |
| 3 · Loss of support | **T** demolishes the middle bridge pier | Both inner deck joints are overloaded, become residual hinges, and the deck sags into a V. A hinge may tear. The abutments stand. |
| 3b · Capacity vs connectivity | **T** cuts the balcony from piers 1–4 | The narrow pier 5 still connects it to the ground, but the joint fails by overload. Its residual is far too weak and the balcony drops. |
| 4a · Partial attachment → separation | **T** knocks out the props | Overload, then a residual hinge, a plastic sag, and a hang lasting a few seconds. It then tears from residual-joint failure. |
| 4b · Partial attachment → arrested | **T** knocks out the props | The floor hinges down onto a low wall and stays attached. Damage stays flat. |
| 5 · Secondary collapse | **T** cuts the top balcony | It falls onto the next balcony, whose joint fails by impact and hangs with the debris. Further failures from impact or overload stay bounded, and the log goes quiet. |
| 6 · Persistent rubble | Wait for sleep (F2 → sleep tint), then **T** | A 2 t block lands on the pile. The pieces it touches wake, the pile supports it, and everything sleeps again. |
| 7 · Shatter into rubble | **T** sets off a charge against the middle of a panel wall | Panels inside the blast core shatter into chunks that fly, fall and pile up. Panels further out lose their joints and drop or hinge whole. The rubble sleeps and stays collidable. |
| S · Stress test | **T** blasts the middle of a ~390-piece block | Not a demonstration, a cost reference. Use it to see how frame time, draw calls and structural time move as a structure grows. |
| 8 · Authored building (Blender) | **T** blows out a ground-floor corner | A cottage modelled in Blender, not in code. It behaves like the procedural scenarios: the corner pier shatters, the upper storey loses support, and panels hinge where joints are overloaded. |

### Shattering vs hinging

Pieces now break into fragments, but only on **violent** failures:

- an explosion core,
- three Damage clicks on the same piece,
- or a hard impact, measured as impact energy per kilogram of the struck piece. This also covers a piece that falls far and hits the ground.

**Overload never shatters.** Overloaded slabs still hinge, sag, hang and tear as whole pieces, which is what keeps partial attachment readable. Fragments are ordinary rubble: they inherit the piece's motion, receive the blast impulse once, collide, sleep and support things. They never shatter again.

The chunks are irregular convex cells, not a grid of identical boxes. Each piece is cut by a Voronoi pattern, so the chunks differ in shape and size (typically a 3–4× spread in mass) while still filling the original piece exactly, which keeps the total mass right. Switch `fragments.shape` to `Boxes` for the older uniform split.

The frame building in scenarios 1–2 is also the free-play sandbox. Try the Explode tool on a column, or drop blocks on a floor.

## Cost on large structures

The lab was sized for roughly 150 pieces. Imported buildings can be much larger, so the per-frame work is kept off the piece count where possible:

- Pieces share **one material per material type**, and only genuinely tinted pieces (damaged, residual, or in the cluster and sleep tint modes) carry a `MaterialPropertyBlock`. That block is what makes a renderer ineligible for batching, so an undamaged structure of any size carries none.
- Colours are applied **when they change**, not on a timer. Building the stress scenario applies 390 colour changes in total and then nothing until something breaks.
- The diagnostics overlay rebuilds its line mesh **30 times a second, and not at all while hidden** (F1).

The stats panel shows draw calls, how many pieces are currently tinted, and frame time, so you can see the cost of your own model. In the editor, draw-call counts cover whichever view drew last, so treat them as a rough guide and compare frame time instead.

If you hit **"Ran out of Graphics Ring Buffer space"**, it is an editor rendering warning rather than a simulation problem; results are unaffected. Check the piece count and the tinted count first, try F1 to hide the overlay and F2 to leave the cluster and sleep tints, and as a stopgap add `-gfx-ring-buffer-size=64` to the editor's launch arguments.

## Reading the diagnostics

- **Connection lines (x-ray):** green → yellow → red shows the structural load ratio q. Magenta is a residual hinge with a live joint. Purple is a dormant residual, which has failed but the two pieces are still in one rigid body. Grey is severed. White squares are ground anchors.
- **Crosses:** orange is an active rigid body and blue is sleeping. Yellow marks a hinge point, with a line to the hanging body's centre of mass.
- **Inspect** a piece to see its nearest connection. Capacities and loads are shown in kN and kN·m. q is a dimensionless load ÷ capacity ratio with its normal, shear and bending components. The panel also shows damage D. For residual hinges it shows joint force against capacity, hinge moment against yield, sag angle, residual q and residual damage.
- **Break log:** every transition is listed with its reason: direct damage, direct damage (explosion), structural overload, residual-joint failure, impact, or residual budget. Each entry also shows q and D at the time.
- **Stats:** FPS, physics step time (Profiler `Physics.Simulate`, recent maximum), structural work time per fixed step, pieces, active and sleeping bodies, joints, connection counts, failures, and pending failures.

## Building your own structures in Blender

Scenario 8 is imported from `Tools/blender/SampleBuilding.blend`, exported to
`Assets/Destruction/Models/SampleBuilding.fbx`. You can model your own the same way.

**The convention**

1. **One mesh object per structural piece.** The piece is the unit that breaks off, hinges and shatters, so split walls into panels rather than modelling one wall object.
2. **Every object is a solid box, unrotated.** Rotations in multiples of 90° are fine. Apply scale and rotation before exporting. Rotated meshes, rounded or tapered ones, and walls with openings cut through them are all skipped with a warning naming the object, because a piece has to fill its own bounding box.
3. **Metres, and pieces touch rather than overlap.** The importer finds connections from shared faces, so interpenetration is reported as a warning and leaves the structure misread.
4. **Anything resting on y = 0 is anchored.** The model is dropped so its lowest point sits on the ground.
5. **Material comes from a name suffix:** `__concrete` (the default), `__wood`, `__brick`. Add `__noshatter` to a piece that may break off but should never fragment.
6. **The name prefix hints at the role,** which only affects the design live load: `Floor`/`Slab`, `Pier`/`Column`/`Post`, `Wall`/`Panel`. Otherwise the shape decides.

So `Wall_U_N0__brick` is an upper-storey north wall panel in brick.

**Export from Blender:** FBX, selected objects, scale 1, `-Z` forward and `Y` up, no space-transform baking. The sample script does this for you:

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background \
    --python Tools/blender/build_sample_building.py
```

It also fails loudly if any two pieces interpenetrate, which is the easiest mistake to make.

**Check before exporting.** `Tools/blender/skills/destruction-blockout/validate_blockout.py` applies the same rules inside Blender and names every offending object, including pieces that touch nothing or have no path to the ground. It exits non-zero when it finds errors.

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background your_building.blend \
    --python Tools/blender/skills/destruction-blockout/validate_blockout.py
```

The same folder holds `SKILL.md`, a self-contained brief for a modelling agent.

**In Unity:** drop the FBX into `Assets/Destruction/Models/`, run **Destruction Lab → Reimport Authored Model** to apply the import settings the lab needs (file scale, readable meshes, no generated colliders), then assign it to the `Destruction Lab` object's **Authored Model** field. It appears as the last scenario. The scenario panel reports how many pieces were read and how many were skipped; warnings go to the console.

The importer reads world-space bounds, so it does not care how the exporter mapped Blender's Z-up axes onto Unity's Y-up ones.

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
| `fragments.enabled`, `targetSize`, `min/maxPerPiece` | Whether pieces shatter, and how many chunks they make |
| `fragments.shape` | Irregular convex cells (default) or uniform box splits |
| `fragments.maxShattersPerStep` | Pieces shattered per fixed step, default 2. The rest queue and follow, so a big blast costs no single spike. |
| `fragments.impactShatterEnergyPerKg` | How hard a hit must be to shatter a piece. A 3 m fall onto concrete gives about 13 J/kg; the default is 20. |
| `fragments.maxLiveFragments` | Fragment budget, default 300. Over budget, a piece detaches whole instead of shattering. |
| `tools.explosionMaxDeltaV` | Caps the velocity an explosion gives any one body, so small chunks are not launched |

The HUD has sliders for the shatter impact threshold and the fragment size, plus a Fragmentation on/off toggle.

## Tests

Pure logic runs in EditMode, 33 tests: adjacency, load model including the cantilever disproving experiment, calibration, damage law, impact filter, hinge math, the box split pattern, and the convex-cell fracture (exact tiling, convexity, irregularity, determinism, outward winding), and the model importer (bounds, ground drop, rotated and sliver rejection, overlap warnings, name suffixes). Physics behaviour runs in PlayMode, 28 tests. These step the simulation deterministically with `SimulationMode.Script` and cover:

- spike S1–S3 (hang, arrest, tear), joint recreation, and split pose and velocity continuity;
- explosion impulse applied once, reset ×10, and every scenario;
- shattering: mass conservation, the fragment budget, overload never shattering, impact shattering, and a regression test across frame boundaries.

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
| EditMode / PlayMode tests | 49/49 and 32/32 passing |
| Console during a full scenario sweep | 0 errors, 0 warnings from project code |
| Authored Blender model (2026-10-05) | 51 pieces read with no warnings, 141 connections, 13 ground anchors. Stable with 0 bodies and 0 failures. A corner blast shattered the pier, left residual hinges on the storey above, and settled with 26 sleeping bodies. |
| Spike S1 hang | Settles at 77.6° and holds for 60 s. Angle change 0.0°, \|ω\| < 0.0001 rad/s, anchor drift < 1 cm. Joint force 94.3 kN against 94.2 kN weight. Hinge moment 53.5 kN·m against 52.0 analytic. Residual q 0.70, using the test's 1.6× residual strength. |
| Spike S2 arrest | Floor rests at 22.7° on the low wall. Residual damage changed by 0.000 over 20 s, and residual q is 0.68. |
| Spike S3 tear | Tears more than 3 s after the hinge forms, by residual-joint failure; the test enforces > 3 s. In live Play mode the hinge formed at 2.60 s and tore at 5.80 s. |
| Reset ×10 | Piece, connection, joint, body and log counts back to initial values each time |
| Worst observed collapse | Two large explosions in the 52-piece frame: 133 transitions, peak 43 bodies and 9 joints. About 700 FPS, physics ≤ 0.30 ms per step, structural ≤ 2.3 ms per step. Everything asleep after about 10 s. |
| Same collapse with fragmentation (2026-10-05) | 18 pieces shattered into 104 irregular fragments, with 132 transitions and peak 120 bodies, at 530–690 FPS. Physics ≤ 1.2 ms per step. Structural work ≤ 7.9 ms on the step that also re-clusters the whole building; a shatter-only step is about 2.7 ms. All bodies asleep and 0 console errors. |

Provisional target: ≥ 60 FPS with ≤ 150 active bodies and ≤ 40 joints. The worst case above is well inside it.

**Measurement caveat:** editor frame time on the stress scenario varied between 2.1 ms and 4.3 ms across identical runs, so the rendering changes above are justified by counters (property blocks in use, colour updates applied) rather than by a frame-rate claim. A standalone player build would be needed for a trustworthy figure.

**Still to check by hand:** real mouse and keyboard input in the Game view. That covers that clicking a UI button does not also damage the structure, orbit and pan feel, and slow-motion smoothness. Automated checks drove the controller through its public methods, not a physical mouse.

## Limitations

- The structural model is a quasi-static load propagation, not a stress solver. It is exact for a single piece on its supports. It ignores load sharing between pieces at the same graph level. It does not transmit bending moment through joints, so multi-piece cantilevers are underestimated. Calibration against the intact structure absorbs the remaining artefacts.
- Structural pieces are axis-aligned boxes, whether written in code or modelled in Blender. Their fragments are irregular convex chunks, but still convex, so you get wedges and slabs rather than concave or splintered shapes. Arbitrary rotated or organic meshes need the work in [#20](https://github.com/hammonjj/UnityDestructionPOC/issues/20). There is no runtime mesh cutting, true deformation or multiplayer.
- Residual hinges are PhysX `ConfigurableJoint`s. Clusters joined by a residual joint with a mass ratio above 10:1 are mass-scaled. At most two parallel hinges per cluster survive; others are severed and logged as "residual budget".
- Fully detached debris shrinks 4% so it cannot wedge in the exact-fit hole it came from.
- PhysX is not deterministic across platforms. Scenario construction, including the seeded rubble pile, is repeatable.
- Optional milestone not built: a first-person controller with a hitscan gun ([#14](https://github.com/hammonjj/UnityDestructionPOC/issues/14)).

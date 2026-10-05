# Architecture note

This note explains how the Destruction Lab is built, which approximations it makes, and how those relate to the [research notes](THE_FINALS_Destruction_Research.md). It is an independent implementation. Nothing here is Embark code or a claim about how THE FINALS works internally.

## What we borrowed and what we did not

| Idea | Status in the research | Used here |
|---|---|---|
| Separate structural integrity, constrained failure motion, and free rigid motion | Documented as the overall direction (GDC 2024; 2026 announcement of partially attached structures) | Yes. This is the core split: load model, residual joints, PhysX bodies. |
| Coarse structural graph separate from the visual fracture set | Documented (Forslund thesis) | Partly. v1 uses one shared graph whose nodes are pieces. `regionId == piece id` is the seam for a later fragment layer. |
| Baseline-relative break scoring with normal / shear / bending terms | Documented at slide level (GDC 2024) | Adapted. See calibration below. |
| Third connection state: structural → residual → severed | Behaviour announced, solver not published | Yes, as a reconstruction |
| Sparse Cholesky structural solver | Documented for the shipped game | No. We use a much simpler quasi-static propagation. A dense solve is the documented fallback. |
| XPBD, NVIDIA Blast, PhysX D6 residual joints | Alternatives; not established as Embark's | Only PhysX joints, through Unity's `ConfigurableJoint` |
| Accumulated damage law `D += h·k·max(0,q−1)^p` | The research's own suggestion, not Embark's | Yes |
| Contact as both damage source and new support | Research recommendation | Yes, for both impact damage and resting support loads |

## Representations

```
ScenarioLibrary (code)     procedural boxes → PieceDef list
        │ StructureGraph.Build
StructureGraph             pieces (= regions), connections (+ virtual ground node), capacities
        │
LoadModel                  per step: load estimate and q per Structural connection
        │ failures
DestructionWorld           the only writer of topology and physics objects
  ├─ clusters              union-find over Structural edges → RigidCluster (Rigidbody + compound colliders)
  ├─ residual joints       ConfigurableJoint per Residual connection between different clusters
  ├─ damage sources        click, explosion, demolish, drop block, filtered contacts
  └─ event log             every transition with its reason
Lab layer                  camera, tools, HUD, diagnostics overlay (reads the world, queues actions)
```

- **Pieces** are axis-aligned boxes. Each has a mass from material density × volume. Colliders are inset 1 cm per side so neighbours never touch, which replaces per-pair collision filtering. Pieces inside one rigid cluster are one compound body.
- **Connections** are created from shared faces. Each stores the interface centre, normal, tangent (the longer side, which becomes the hinge axis) and dimensions. Capacities come from material × area: tension, compression, shear, and bending through the section modulus `w·d²/6`. A piece touching the ground gets a ground connection. Anchors are pieces with a Structural ground connection. Foundations are 8× stronger.
- **Rigid clusters** are a runtime grouping. Everything still connected to the ground through Structural connections is one static cluster with no Rigidbody, so an intact building costs nothing to simulate and cannot jitter. A component that loses its ground path becomes a dynamic cluster. A dynamic cluster that loses an internal connection splits.

## Structural load model (chosen) and its limits

The load model runs every fixed step, which costs well under a millisecond at this scale.

1. BFS levels from anchors over Structural edges only. Residual edges are never support paths, which prevents a tiny surviving attachment from keeping a section "supported". Pieces hanging from a residual hinge on static structure act as pseudo-anchors, so loads inside a hanging assembly are still evaluated.
2. Pieces are processed from the farthest level inward. Each carries its own weight plus loads handed to it, and the centre of mass of that load. The load is split across connections to the next-lower level by interface area × alignment: bearing 1.0, hanging 0.6, lateral 0.35.
3. Per connection, the model computes normal force (tension or compression), shear, and **bending = supported weight × horizontal distance from its centre of mass to the convex hull of the support interfaces**. A slab on four columns carries no bending. A cantilever carries the full lever arm.
4. Handed-down load enters the next piece at the interface centre. That is a force transfer with no moment transfer.
5. `q = max(normal, shear, bending ratios)`, using capacity × (1 − D). Taking the max avoids adding N·s to N·m·s.

**Calibration** is our reading of "baseline + break offset". At build time the intact structure is solved with a design live load of 5 kPa on slabs. Each capacity becomes `max(authored, design load × safety factor 2.5)`. Authored structures are therefore stable in their original configuration and still react to changed loading. Connections that carry nothing intact keep their authored capacity, so q cannot blow up from a near-zero baseline. We never recalibrate during play, which would erase evidence of overload.

**Damage law:** `D += dt·k·max(0, q−1)^p` with k = 1.5 and p = 2, plus instant failure at q ≥ 2. Because capacity scales with (1 − D), clicks and overload compound. Failures are batched against the same solved state and committed together. At most 8 transitions are committed per fixed step; the rest wait and are never dropped.

**Known limits:**

- Load sharing between pieces at the same BFS level is ignored.
- Moment is not transmitted through joints, so multi-piece cantilevers are underestimated by roughly 25% in a three-piece chain.
- Arches and portal frames are approximate.
- There is no stiffness-based redistribution and no buckling.

These limits are why calibration exists. The disproving experiment from the plan, `Cantilever_ThinConnectionLoadsFarMoreThanFullEdge`, passes. With the same slab, material and lever arm, a 0.3 m connection reaches more than 5× the q of a full 4 m edge.

**Why not a constraint solver:** a sparse Cholesky or XPBD solve gives better load paths in redundant frames. The POC has at most about 150 pieces, though, and the scenarios need lever-arm sensitivity and redistribution, not stress accuracy. The simple model is deterministic, O(E), and fully explainable in the overlay. If frame artefacts become a problem, the fallback is a small dense solve per anchored component behind the same `LoadModel` interface.

## Residual attachments (constrained failure motion)

When a Structural connection fails by **overload** or a non-violent **impact**, it becomes **Residual**. **Direct damage**, **explosion** inner radius and **violent** impacts sever outright.

- If both pieces are still in the same rigid body, or both are static, the residual is **dormant**. No joint is created; you cannot joint fragments of one body. It activates if the pieces later end up in different clusters.
- Otherwise a `ConfigurableJoint` is placed on the moving cluster, connected to the other cluster, or to the world for static structure:
  - **Anchor:** the interface edge in the direction of gravity and the moving piece's centre of mass, so the piece swings away from its support instead of into it.
  - **Axis:** the interface tangent.
  - **Linear motion:** locked.
  - **Angular X:** limited to ±sag. Angular Y and Z are limited to ±4°.
  - **Slerp drive:** damping only.
  - **Other settings:** `enableCollision = false`, `enablePreprocessing = false`, projection as a safety net. Solver iterations are raised to 16 / 4 on jointed bodies only.
- **Plastic sag** is what makes a floor visibly sag rather than drop. It starts at 1°. While the filtered hinge moment exceeds the yield moment (residual capacity per metre × width), the limit grows at up to 20°/s, easing into an 80° stop. It stops as soon as contact or geometry relieves the moment.
- **No built-in breaking:** `breakForce` and `breakTorque` are infinite, because PhysX tests the raw constraint force. That force spikes several times over on arrest-by-contact and right after re-parenting.
  - Severing comes from our own law instead. EMA-filtered (α = 0.12) joint force and off-axis torque against residual capacity × (1 − D_res) give residual q. Residual damage accumulates through the same damage law. A catastrophic gate needs raw q ≥ 6 for 3 consecutive steps.
  - Arrest by contact lowers the joint load, so damage stops and the piece stays attached. Nothing here is a timer.
- **Budget:** each cluster keeps at most 2 residual hinges, and only parallel ones. Perpendicular hinges would lock the piece. Excess hinges are severed with the reason "residual budget".
- **Mass ratio:** above 10:1 between joined clusters, the joint's mass scale brings the effective ratio down to 10.
- **Measured in the spike tests:** joint force matched the hanging weight to within 1.2%. Hinge moment about the anchor matched the analytic gravity moment to within 25%; Unity reports `currentTorque` about the anchor, as verified. Recreating a joint mid-hang preserves the absolute sag range. Unity's angular-X sign relative to our twist measure is −1, as verified.

## Damage sources and units

Everything is SI: kg, m, s, N, N·m, J. Damage D is dimensionless in [0, 1]. q is dimensionless.

| Source | Effect |
|---|---|
| Damage click | +0.35 D on every connection of the piece, Structural or Residual. Reaching D = 1 by direct damage severs. |
| Explosion | Damage falls off linearly to the radius, and the inner 35% severs violently. The impulse is applied **after** the commit, once per distinct Rigidbody, including bodies created by the same blast. |
| Demolish / sever (scenario triggers) | Destroys the named connections; demolish also removes the piece |
| Impact | `Physics.ContactEvent` is used, which reports each pair once. Relative normal speed at the contact point comes from the pre-collision body velocities the event provides. Contacts below 2 m/s are ignored. Energy is ½·impulse·speed, split half to each body. Damage is energy ÷ total toughness of the piece's live interfaces (J/m² × m²). There are per-pair and per-connection cooldowns. |
| Resting contact | Never damage: it is ≈ 0 m/s, whatever the impulse. It does become a **support load**. Debris resting on static pieces adds its contact force at the contact point to the load model, and so do residual hinges hanging from static pieces. Readings from sleeping bodies are kept, because PhysX stops reporting them. |

## Timing and safe mutation

`DestructionWorld` runs at execution order −100, before the physics step, in this order:

1. Read the residual joints from the previous solve.
2. Process contacts collected during the previous step.
3. Apply queued tool actions.
4. Run the load model and damage law.
5. Commit transitions: re-cluster, re-sync joints, wake overlapping bodies.
6. Apply explosion impulses.

Callbacks only enqueue; nothing mutates colliders or joints inside them. Splits keep world pose: transforms are synced from `Rigidbody.position` first because of interpolation. Children inherit `v + ω × (c_child − c_parent)` and the same ω. Joints are destroyed with `DestroyImmediate` and old clusters are deactivated before destruction, so they never act for an extra step.

Pause switches `Physics.simulationMode` to `Script`. Single-step calls the same pre-step and then `Physics.Simulate`. PlayMode tests use the same path, so physics is reproducible on one machine. Cross-platform determinism is not claimed.

Reset destroys every cluster, piece and joint. It clears the queues, contact and support caches, cooldowns, the event log, stats, selection, time scale and camera. It then rebuilds from the scenario definition. The rubble pile uses a fixed seed.

## Diagnostics

- The overlay is an x-ray line mesh, rendered with `Hidden/Internal-Colored` with ZTest off. It is rebuilt each frame from the graph, joints and bodies.
- The HUD shows q components with units, residual force against capacity, hinge moment against yield, sag, and the reason for every failure.
- Timings come from the `Physics.Simulate` profiler marker and a stopwatch around the director's step.

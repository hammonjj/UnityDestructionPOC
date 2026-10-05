# THE FINALS destruction: architecture, algorithms, and implementation references

Research date: October 4, 2026, America/Denver.

The central idea is to separate **structural integrity**, **constrained motion during failure**, and **ordinary rigid-body motion**. That separation makes it possible for a structure to lose its original load-bearing behavior while remaining physically attached. A slab can tip, load a surviving attachment, strike another floor, and only then detach.

This report distinguishes documented Embark implementation from an engineering reconstruction. The reconstruction is a way to build comparable behavior, not recovered source code. I located and inspected the GDC slides, the Embark-related thesis, official announcements, NVIDIA documentation and public code. I located the new official video and verified its title and description, but could not retrieve its transcript; I therefore do not attribute detailed solver claims to its narration.

## 1. What is actually documented

| Evidence | Verified information | Scope |
|---|---|---|
| Måns Isaksson, GDC 2024, *Engineering Mayhem* [1] | Custom structural rigid-body solver; sparse direct solution using Cholesky decomposition; incremental Cholesky; material-weighted tension/compression, shear, and bending; baseline-relative breaking. Transform quantization, snapshot/delta compression, and a GPU transform pool. | Historical architecture, not proof of every 2026 implementation detail. |
| Elliot Forslund, 2023 thesis [2] | Baked destruction graph; a lower-resolution graph for the in-house Strain solver; clients interpolate server-driven destructible transforms; graph-generation optimization. | Particularly valuable for data representation and authoring. Not a complete runtime solver specification. |
| Adrian Björkerud, SideFX, February 2026 [3] | Buildings are pre-fractured in Houdini. Watertight geometry, interior surfaces, collision generation, and modular procedural authoring are deliberate production requirements. | Asset pipeline. |
| Official July 2025 / Season 8 notes [4,5] | Earlier improvements to gravity-driven collapse, impact propagation, connected rubble, and traversability. | Already shipped behavior. |
| Official September 18, 2026 announcement [6] | Partially attached structures, sagging floors, leaning walls, stronger cascades, and readable collapse cues announced for October 20. | Upcoming overhaul as of this research date. |

The new official video is **DYNAMISM UPGRADE | THE FINALS PRIME TIME** [7]. Its description also identifies October 20 as the release date. This is distinct from the older feature named **Smooth Destruction**.

Do not reduce this to “Unreal Chaos does everything.” The primary evidence documents substantial custom technology. Nor does the availability of NVIDIA Blast demonstrate that Embark uses Blast. The sources below establish reusable mechanisms without establishing Embark's current physics middleware version or exact residual-joint solver.

## 2. Three representations of the same building

The thesis's separation between destruction and Strain graphs is particularly important [2]. A practical reconstruction should maintain separate representations:

| Representation | Data | Question it answers |
|---|---|---|
| Render/fracture geometry | Mesh fragments, hidden internal faces, materials, IDs | What does damage expose? |
| Structural graph | Coarser regions, connections, load capacities, ground anchors | Which connections can still carry the load? |
| Physics bodies and joints | Rigid clusters, collision shapes, poses, velocities, residual attachments | How do the surviving pieces move? |

These are not necessarily one-to-one. A visually detailed wall might have many fragments but only a few structural regions. Many intact fragments can share one rigid-body transform. A region should split into additional bodies only when it needs relative motion.

Suggested authoring records, not Embark's field names:

```text
Fragment:
    stable ID, mesh reference, collision hulls
    material, mass contribution, local transform
    structural region ID, current rigid cluster ID

StructuralRegion:
    stable ID, member fragments
    mass, center of mass, inertia
    support/anchor classification
    incident connection IDs

Connection:
    endpoint region IDs
    local attachment frames, interface normal
    strength parameters and intact baseline score
    state: structural / residual / severed
    optional residual-joint configuration
```

The interface normal and attachment positions matter. Two connections at different edges of a floor resist tipping differently, even if they have identical nominal strength.

## 3. Pre-fracturing does not mean a scripted collapse

Embark's Houdini workflow prepares fragments before gameplay. Its collision pipeline uses simplified planar geometry, 2D convex decomposition, extrusion, and post-fracture hull merging [3].

That fixes the possible fracture boundaries, not the sequence of events. With hundreds of possible connections, different damage locations can produce different surviving groups, pivots, contacts, and cascades. Precomputed geometry and emergent motion are entirely compatible.

An implementation also needs rules for the scale of fragmentation. Turning every wall fragment into an independent body at the first impact gives gravel-like collapse and a large simulation bill. Retaining connected slabs produces recognizable architectural motion and useful rubble.

## 4. Structural integrity: connectivity is necessary but insufficient

A graph traversal can tell you whether a region still has a path to an anchor. It cannot tell you whether that path is strong enough.

Imagine a floor attached to a wall by one remaining narrow section. Connectivity says it is supported. A load calculation can say that its weight and lever arm overload that section.

For an idealized load F acting at displacement r from an attachment:

\[
\tau = r \times F
\]

A one-tonne section under ordinary gravity exerts about 9.81 kN. With its center of mass two meters horizontally from its attachment, the associated moment is approximately 19.6 kN·m. The same mass directly above a support produces a very different failure problem.

These are elementary mechanics examples, not THE FINALS tuning values.

### A plausible constraint formulation

The GDC deck identifies a sparse Cholesky structural solver but does not publish a complete matrix assembly [1]. The following is a standard velocity-constraint formulation that explains how such a system can work:

\[
v^* = v + h M^{-1}f_{ext}
\]

\[
(JM^{-1}J^T + R)\lambda = -(Jv^* + b)
\]

Here M includes mass and rotational inertia, J maps body motion into connection-relative motion, b is a stabilization/target term, R is regularization or softness, and lambda contains constraint impulses. The solution tells you the corrective impulses required to maintain the selected connections.

For a suitably conditioned symmetric positive-definite system, factor A = LL^T and solve two triangular systems. Unanchored modes, redundant constraints, and degenerate geometry require explicit handling; blindly applying Cholesky to an arbitrary graph is not sufficient.

A direct solver resolves coupled connections together. A sequential impulse solver instead iterates through local corrections. The appropriate choice depends on graph size, conditioning, topology changes, and the quality of force estimates required. The two approaches can coexist: a structural solver can evaluate integrity while a different solver handles moving contacts.

### From connection impulses to break decisions

The presentation separates normal loading, shear, and bending, applies material multipliers, and compares a combined impulse with an intact baseline plus a break offset [1].

An explanatory adaptation is:

```text
normalComponent = dot(linearImpulse, interfaceNormal)
normalImpulse   = interfaceNormal * normalComponent
shearImpulse    = linearImpulse - normalImpulse

normalWeight = tensionWeight or compressionWeight
score = normalWeight * length(normalImpulse)
      + shearWeight * length(shearImpulse)
      + bendWeight * length(angularImpulse * angularConversion)

if score > intactBaselineScore + breakAllowance:
    failStructuralConnection()
```

Sign convention determines which direction counts as tension. The angular conversion and weights need consistent units; do not add newton-seconds directly to newton-meter-seconds and call it physical stress.

My interpretation of the baseline is that it permits artist-authored structures to remain stable in their original configuration while still reacting to changed loading. This is an engineering reading of the published threshold, not a verbatim explanation from the talk. Do not continually replace the intact baseline with the damaged structure's current score: that would erase evidence of accumulating overload.

## 5. The new in-between state

The official announcement confirms partially attached collapse, but does not identify its numerical solver [6]. The following residual-constraint model is a reconstruction consistent with that behavior.

Previously, a simple implementation might offer only an intact rigid connection or no connection. Add a third state:

```text
STRUCTURAL --integrity failure--> RESIDUAL --final failure--> SEVERED
STRUCTURAL --violent break---------------------------------> SEVERED
RESIDUAL   --loads settle----------------------------------> RESIDUAL
```

The final line matters: a weakened floor need not eventually vanish or fall on a timer. It can settle into a stable tilted configuration.

When a structural connection fails:

1. Remove its original load-bearing constraint from the integrity model.
2. Split any rigid cluster that must now move internally.
3. Preserve the new bodies' poses and inherited velocities.
4. At suitable interfaces, add a weaker physical attachment.
5. Let gravity, collisions, and the residual attachment determine motion.
6. Remove that attachment when its own failure condition is reached.

Possible residual attachments:

| Model | Result | Limitation |
|---|---|---|
| Hinge | Floor tilts around a surviving edge | Requires a meaningful hinge axis |
| Damped angular joint | Wall bends while still attached | Needs stiffness, damping, and strength tuning |
| Several breakable attachments | Progressive peeling or tearing | More constraints and potentially poor conditioning |
| Compliant six-degree-of-freedom joint | Translation and rotation with finite resistance | More complex material behavior and tuning |

PhysX explicitly provides breakable joints, local attachment frames, hinge and D6 types, and force/torque reporting [8]. Those are sufficient building blocks for a prototype; that does not prove Embark uses them for its new layer.

Two easy implementation mistakes destroy the intended behavior:

- **Running flood-fill across only structural bonds and releasing everything it disconnects.** A section may have lost rigid support while remaining attached through residual joints.
- **Creating joints between fragments that still belong to the same rigid body.** They cannot move relative to one another until the cluster is split.

A robust representation distinguishes connectivity within rigid clusters from the graph of joints connecting those clusters.

### Softness, strength, and permanent deformation are different

Stiffness determines how much a connection deflects. Strength determines when it fails. Damping removes oscillation. Plasticity changes its preferred rest configuration. A weak connection can be stiff until it snaps; a strong connection can bend substantially without snapping.

For a prototype, one possible accumulated failure law is:

\[
D_{t+h}=\operatorname{clamp}\left(D_t+h k_d\max(0,q-1)^p,0,1\right)
\]

q is a dimensionless load-to-capacity ratio. Break when D reaches one or a separate catastrophic threshold is exceeded. This is an optional design, not a documented Embark equation. Unlike a fixed timer, it allows load redistribution or new contact support to halt further damage.

XPBD is a research-backed alternative for implementing compliant constraints [9]. Its core update is:

\[
\Delta\lambda = \frac{-C(x)-\tilde\alpha\lambda}
{\nabla C M^{-1}\nabla C^T+\tilde\alpha},\qquad
\tilde\alpha=\alpha/h^2
\]

It provides a useful relation between compliance and constraint-force estimates. This is a possible implementation route, not evidence that THE FINALS uses XPBD. The related *Small Steps* paper investigates why smaller substeps can outperform additional iterations in a larger step [10].

## 6. Reconstructed runtime algorithm

This is integration pseudocode, not leaked/recovered Embark code. Function names describe responsibilities; solver internals must still be implemented or supplied by middleware.

```text
BAKE(level):
    prepare fracture pieces, internal surfaces, collision, stable IDs
    build fine destruction graph
    build coarser structural graph and mappings
    identify authored fixed supports
    validate connection geometry and material parameters
    calculate intact structural baseline scores
    save assets and graph data for server and clients

SERVER_FIXED_STEP(dt):
    queue gameplay damage and previous step's qualified contact impacts
    apply direct fragment/bond damage
    mark affected structural islands dirty

    while dirty islands remain and structural work budget remains:
        island = next dirty island
        update its structural graph and boundary loads
        handle unanchored modes / disconnected components
        assemble or update its sparse constraint system
        factor or safely update cached factorization
        solve connection impulses
        evaluate all failures against the same solved state

        commit failures as a batch:
            split rigid clusters where relative motion is now possible
            transfer mass, inertia, poses, and velocities
            create permitted residual joints at surviving interfaces
            or sever connections completely
            mark affected neighbors dirty

    for each physics substep:
        integrate external forces
        solve contacts and residual joints in a coupled simulation
        advance body poses
        collect joint reactions and contact events
        queue final joint failures for a safe mutation point

    commit queued final failures and wake affected bodies
    filter contact events into new destruction loads
    preserve sleeping rubble's gameplay collision
    replicate topology changes and changed body states

CLIENT_FRAME(renderTime):
    apply topology changes with compatible state versions
    interpolate available authoritative body snapshots
    update fragment transforms through shared transform storage
    reveal newly exposed surfaces
    emit local dust, chips, sound, and other presentation effects
```

The structural work budget must not silently discard failures. Deferred work needs a queue and a bounded, consistent gameplay policy. Production systems may interleave phases differently; this schedule is chosen for clarity and safe mutation.

### Momentum at a cluster split

For a new child body's center of mass c_i, a useful rigid-motion inheritance rule is:

\[
v_i = v_{parent} + \omega_{parent}\times(c_i-c_{parent}),
\qquad \omega_i=\omega_{parent}
\]

Keep the geometry in the same world pose during the split, and recompute child mass and inertia consistently. Resetting child velocities to zero produces visible hesitation; applying an arbitrary new outward impulse can inject energy. Explosion impulses should be accounted for once.

### Cascades and temporary supports

A falling floor can strike another floor, create a concentrated load, and trigger more failure. It can also become supported by rubble before its remaining attachment breaks. Contact is therefore both a potential damage source and a new support condition.

An implementation should distinguish resting contact from an impact. A contact solver generates support impulses every frame for a motionless stack. Converting all of them directly into impact damage can make rubble damage itself forever. Use pre-impact relative velocity, thresholds, contact aggregation, material rules, and a consistent impulse/energy model.

NVIDIA's stress documentation makes an analogous point about boundary conditions: gravity on a fully free-falling assembly does not itself create internal support loads [11]. This is a useful check against an incorrect “every chunk's weight breaks its neighbors” model.

## 7. Multiplayer and performance

Embark's thesis describes interpolated server-driven motion [2]. The GDC slides show transform quantization and delta/snapshot compression; the demonstrated destruction bandwidth peaks around 175 kbit/s after optimization, versus an earlier large event averaging roughly 400 kbit/s. These are presentation examples, not current production guarantees. The deck also identifies a GPU transform pool [1].

For a comparable architecture, separate topology from motion:

| Stream | Examples | Recommended handling |
|---|---|---|
| Topology | Broken bonds, new cluster membership, joint state, IDs | Reliable or recoverable/versioned |
| Motion | Pose, velocity when useful, sleep/wake state | Time-stamped snapshots with interpolation |
| Presentation | Dust, tiny chips, some sounds | Local effects where gameplay permits |

Client/server agreement does not require every client to reproduce the same physics simulation deterministically. It requires clients to consume a consistent authoritative outcome. Late join and replay still need a recoverable topology state, not just a stream of transforms.

Glenn Fiedler's networking articles, explicitly referenced in the GDC deck, explain quantization, quaternion compression, acknowledged snapshot baselines, and interpolation [12,13]. Do not delta-encode against a state the receiver may never have received. Similarly, a transform referring to a new rigid cluster is not useful until its topology exists on the client.

Recommended engineering priorities, not claimed Embark implementation details:

- Keep the structural graph much smaller than the visual fracture set.
- Analyze affected connected regions and boundary loads, not merely a fixed-radius neighborhood; removing a column can change loads far away.
- Cache sparse structure, ordering, and factorization when valid. Rebuild when topology changes invalidate the shortcut.
- Retain rigid clusters until internal relative motion is necessary.
- Sleep settled bodies without removing collision needed for cover or traversal.
- Budget active joints, contact pairs, collision hulls, replication, and rendering separately.
- Batch rendering and transform updates rather than allocating a heavyweight gameplay object for every chip.
- Keep authoritative destruction quality consistent across clients; scale cosmetic work locally.

The thesis reports a graph-baking improvement from approximately 3.56 to 1.6 seconds through parallel work and overlap-query optimization [2]. That is an editor iteration improvement, not a runtime frame-rate benchmark. Fast iteration matters because connectivity errors, collision errors, and bad fracture patterns otherwise become expensive to fix.

## 8. Actual code worth reading

### NVIDIA Blast stress solver

The public implementation is at [NvBlastExtStressSolver.cpp][14]. Inspect the lifecycle, force application, update, and fracture-command generation paths. The underlying stress implementation is in [stress.cpp][15]. The associated [stress documentation][11] explains setup and solver behavior.

Blast's separation of fracture/topology from physics and rendering is particularly relevant [16]. It is a reference for this architectural problem, not a source release of THE FINALS.

### NVIDIA PhysX joint examples

[SnippetJoint.cpp][17] contains actual implementations named `createBreakableFixed` and `createDampedD6`. These show the real API for break thresholds and driven angular constraints. The pinned source revision is `da950a3537927784951853c66618036f332ca0ce`; match your SDK version before copying calls.

Here is a complete, original helper header illustrating one possible residual hinge. It uses documented PhysX APIs; it is not Embark code, and it was not compiled in this research environment.

```cpp
// ResidualHinge.h
#pragma once

#include <PxPhysicsAPI.h>
#include <extensions/PxRevoluteJoint.h>
#include <cmath>
#include <stdexcept>

namespace destruction_example
{
struct ResidualHingeSettings
{
    // SI values when the application's PhysX scale uses meters and kilograms.
    physx::PxReal breakForceNewtons;
    physx::PxReal breakTorqueNewtonMeters;
};

// Call only at a safe scene-mutation point, after separating rigid clusters.
// worldAttachment's local X axis defines the hinge axis.
// support == nullptr anchors the hinge to the immovable world.
// Caller owns the returned joint and must release it when no longer needed.
inline physx::PxRevoluteJoint* CreateResidualHinge(
    physx::PxPhysics& physics,
    physx::PxRigidActor* support,
    physx::PxRigidDynamic& movingPart,
    const physx::PxTransform& worldAttachment,
    const ResidualHingeSettings& settings)
{
    if (support == &movingPart || !worldAttachment.isValid())
    {
        throw std::invalid_argument("Invalid residual hinge attachment");
    }

    if (movingPart.getRigidBodyFlags() & physx::PxRigidBodyFlag::eKINEMATIC)
    {
        throw std::invalid_argument("Moving part must be dynamic");
    }

    if (!std::isfinite(settings.breakForceNewtons)
        || !std::isfinite(settings.breakTorqueNewtonMeters)
        || settings.breakForceNewtons <= 0.0f
        || settings.breakTorqueNewtonMeters <= 0.0f)
    {
        throw std::invalid_argument("Break thresholds must be finite and positive");
    }

    const physx::PxTransform supportFrame = support != nullptr
        ? support->getGlobalPose().getInverse() * worldAttachment
        : worldAttachment;
    const physx::PxTransform movingFrame =
        movingPart.getGlobalPose().getInverse() * worldAttachment;

    physx::PxRevoluteJoint* joint = physx::PxRevoluteJointCreate(
        physics, support, supportFrame, &movingPart, movingFrame);

    if (joint == nullptr)
    {
        throw std::runtime_error("PhysX could not create residual hinge");
    }

    joint->setBreakForce(
        settings.breakForceNewtons, settings.breakTorqueNewtonMeters);
    movingPart.wakeUp();
    return joint;
}
} // namespace destruction_example
```

This helper permits free rotation about one axis. Consequently, it does **not** model bending resistance or fatigue about that free axis. For a slab that resists bending before yielding, use a suitable angular drive/limit, a compliant D6 formulation, or a custom constraint and damage law. Handle break callbacks and release objects at safe mutation points. Also configure pair collision filtering deliberately; touching neighboring chunks can otherwise fight their attachment constraints.

### PositionBasedDynamics

[InteractiveComputerGraphics/PositionBasedDynamics][18] is a public implementation covering rigid bodies and deformable simulation. Read it with the XPBD and Small Steps papers if you want to implement the numerical machinery rather than integrate a packaged joint solver. It is an alternative research route, not an Embark dependency claim.

## 9. A practical prototype and diagnostics

Start with one two-story building, a modest number of authored structural regions, and substantially more visual fragments. A useful progression is:

1. Establish fragment/cluster mapping and reliable collision.
2. Add anchor connectivity and clean cluster splitting.
3. Add load-based failure; test a long cantilever that remains graph-connected.
4. Add residual hinges/joints; test a slab that can settle while attached.
5. Add filtered impact-driven cascades.
6. Add server snapshots and topology recovery.
7. Scale active simulation and rendering only after behavior is understandable.

Measure active bodies/joints, contact pairs, dirty-region size, matrix factor/update cost, solve residual, number of failures per tick, penetration depth, topology backlog, replication bytes, and client interpolation error. Visualize connection load/capacity, normals, attachment frames, anchor paths, and rigid-cluster colors.

Useful adversarial cases include a bridge with one support removed, a hanging floor arrested by rubble, a free-falling intact assembly, a deep settled rubble pile, an explosion across several buildings, and a late-joining client during a collapse. These distinguish structural realism from an attractive but fragile demonstration.

## 10. What remains unknown

The sources inspected do not establish the 2026 residual layer's exact joint formulation, strength law, graph-reduction rules, scheduling, solver iteration counts, server frequency, packet format, or production performance budget. They also do not establish that Embark uses XPBD, NVIDIA Blast, a volumetric finite-element simulation, or runtime mesh cutting for this feature.

The publishable lesson is nevertheless concrete: an economical structural model can determine failure, a separate constrained-motion layer can make failure unfold over time, and shared authoritative physics can turn the resulting rubble into gameplay geometry.

## Sources and suggested reading order

1. [Engineering Mayhem — GDC slides landing page](https://www.gdcvault.com/play/1034307/Engineering-Mayhem-Technical-Deep-Dive). Måns Isaksson, Embark, GDC 2024. [Direct slide PDF](https://media.gdcvault.com/gdc2024/Slides/GDC+slide+presentations/Isaksson_Mans_Engineering+Mayham+Technical.pdf). Structural details: PDF pages 20–30; networking/rendering: 32–40. [Video listing](https://www.gdcvault.com/play/1034280/Engineering-Mayhem-Technical-Deep-Dive). Slides inspected; narration not inspected.
2. [Optimising 3D object destruction tools for improved performance and designer efficiency in video game development](https://www.diva-portal.org/smash/get/diva2:1776335/FULLTEXT02.pdf). Elliot Forslund, Blekinge Institute of Technology, June 2023. Especially sections 2.4.4, 2.4.6, and 4.1.2–4.2. Full PDF inspected.
3. [Making the Procedural Buildings of THE FINALS](https://www.sidefx.com/community/making-the-procedural-buildings-of-the-finals-using-houdini/). Adrian Björkerud, February 11, 2026.
4. [Official Update 7.3.0](https://www.reachthefinals.com/patchnotes/730). July 3, 2025.
5. [Official Season 8 notes](https://www.reachthefinals.com/patchnotes/800). Smooth Destruction and Kyoto upgrades.
6. [A New Chapter](https://www.reachthefinals.com/patchnotes/a-new-chapter). Official announcement, September 18, 2026.
7. [DYNAMISM UPGRADE | THE FINALS PRIME TIME](https://www.youtube.com/watch?v=Zm9sF-ijcB4). Official video. Located and metadata checked; transcript retrieval unavailable.
8. [PhysX joint documentation](https://nvidia-omniverse.github.io/PhysX/physx/5.4.1/docs/Joints.html). Frames, joint types, reactions, breakage, and drives.
9. [XPBD: Position-Based Simulation of Compliant Constrained Dynamics](https://matthias-research.github.io/pages/publications/XPBD.pdf). Macklin, Müller, Chentanez, 2016.
10. [Small Steps in Physics Simulation](https://matthias-research.github.io/pages/publications/smallsteps.pdf). Macklin et al., 2019.
11. [NVIDIA Blast stress solver documentation](https://nvidia-omniverse.github.io/PhysX/blast/docs/api/extensions/ext_stress.html).
12. [Snapshot Compression](https://gafferongames.com/post/snapshot_compression/). Glenn Fiedler, 2015.
13. [Snapshot Interpolation](https://gafferongames.com/post/snapshot_interpolation/). Glenn Fiedler, 2014.
14. [NVIDIA Blast stress solver source](https://github.com/NVIDIA-Omniverse/PhysX/blob/da950a3537927784951853c66618036f332ca0ce/blast/source/sdk/extensions/stress/NvBlastExtStressSolver.cpp).
15. [NVIDIA Blast underlying stress implementation](https://github.com/NVIDIA-Omniverse/PhysX/blob/da950a3537927784951853c66618036f332ca0ce/blast/source/shared/stress_solver/stress.cpp).
16. [NVIDIA Blast architecture documentation](https://nvidia-omniverse.github.io/PhysX/blast/index.html).
17. [NVIDIA PhysX SnippetJoint.cpp](https://github.com/NVIDIA-Omniverse/PhysX/blob/da950a3537927784951853c66618036f332ca0ce/physx/snippets/snippetjoint/SnippetJoint.cpp).
18. [PositionBasedDynamics source](https://github.com/InteractiveComputerGraphics/PositionBasedDynamics).
19. [Living in a Stressful World: Real-time Stress Calculation for Destroyable Environments](https://www.gdcvault.com/play/1014658/Living-in-a-Stressful-World). Eric Arnold, Volition, GDC 2011. Historical companion talk; listing verified, presentation not inspected.

[1]: https://www.gdcvault.com/play/1034307/Engineering-Mayhem-Technical-Deep-Dive
[2]: https://www.diva-portal.org/smash/get/diva2:1776335/FULLTEXT02.pdf
[3]: https://www.sidefx.com/community/making-the-procedural-buildings-of-the-finals-using-houdini/
[4]: https://www.reachthefinals.com/patchnotes/730
[5]: https://www.reachthefinals.com/patchnotes/800
[6]: https://www.reachthefinals.com/patchnotes/a-new-chapter
[7]: https://www.youtube.com/watch?v=Zm9sF-ijcB4
[8]: https://nvidia-omniverse.github.io/PhysX/physx/5.4.1/docs/Joints.html
[9]: https://matthias-research.github.io/pages/publications/XPBD.pdf
[10]: https://matthias-research.github.io/pages/publications/smallsteps.pdf
[11]: https://nvidia-omniverse.github.io/PhysX/blast/docs/api/extensions/ext_stress.html
[12]: https://gafferongames.com/post/snapshot_compression/
[13]: https://gafferongames.com/post/snapshot_interpolation/
[14]: https://github.com/NVIDIA-Omniverse/PhysX/blob/da950a3537927784951853c66618036f332ca0ce/blast/source/sdk/extensions/stress/NvBlastExtStressSolver.cpp
[15]: https://github.com/NVIDIA-Omniverse/PhysX/blob/da950a3537927784951853c66618036f332ca0ce/blast/source/shared/stress_solver/stress.cpp
[16]: https://nvidia-omniverse.github.io/PhysX/blast/index.html
[17]: https://github.com/NVIDIA-Omniverse/PhysX/blob/da950a3537927784951853c66618036f332ca0ce/physx/snippets/snippetjoint/SnippetJoint.cpp
[18]: https://github.com/InteractiveComputerGraphics/PositionBasedDynamics

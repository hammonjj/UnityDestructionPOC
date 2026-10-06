using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DestructionLab.Tests
{
    /// <summary>F1 (#16): shattering into rubble.</summary>
    public class FragmentationTests
    {
        WorldFixture f;

        [TearDown]
        public void TearDown() => f?.Dispose();

        System.Collections.Generic.List<Piece> Fragments(string of) =>
            f.world.pieces.Where(p => p.isFragment && !p.removed && p.name.StartsWith(of + " frag")).ToList();

        [UnityTest]
        public IEnumerator ExplosionCore_ShattersPanel_ConservingMassAndPose()
        {
            f = WorldFixture.Create("shatter");
            int target = f.Piece("Panel 01");
            float mass = f.world.Graph.mass[target];
            Vector3 center = f.world.pieces[target].transform.position;
            f.world.Explode(new Vector3(0f, 1.6f, -0.6f), 2.6f, 1.6f, 0f); // no impulse: check pose and mass
            f.Steps(1);
            Assert.IsTrue(f.world.pieces[target].removed, f.LogText());
            var frags = Fragments("Panel 01");
            Assert.That(frags.Count, Is.InRange(2, f.world.Settings.fragments.maxPerPiece));
            float fragMass = frags.Sum(p => f.world.Graph.mass[p.index]);
            Assert.That(fragMass, Is.EqualTo(mass).Within(0.01f * mass));
            Vector3 com = frags.Aggregate(Vector3.zero, (acc, p) => acc + f.world.Graph.mass[p.index] * p.transform.position) / fragMass;
            Assert.That(Vector3.Distance(com, center), Is.LessThan(0.05f), "fragments start at the panel's pose");
            Assert.That(Enumerable.Range(0, f.world.log.Count).Any(i => f.world.log[i].IsShatter && f.world.log[i].reason == FailureReason.Explosion));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Trigger_ShattersCore_OuterPanelsDetachWhole_AndRubbleSettles()
        {
            f = WorldFixture.Create("shatter");
            f.Trigger();
            f.Steps(1);
            Assert.IsTrue(f.world.pieces[f.Piece("Panel 01")].removed);
            Assert.IsTrue(f.world.pieces[f.Piece("Panel 11")].removed);
            Assert.IsFalse(f.world.pieces[f.Piece("Panel 00")].removed, "outer panels are not shattered");
            int live = f.world.stats.liveFragments;
            Assert.That(live, Is.GreaterThanOrEqualTo(4));
            var moving = f.world.pieces.Where(p => p.isFragment && !p.removed).Count(p => p.cluster.body.linearVelocity.magnitude > 1f);
            Assert.That(moving, Is.GreaterThanOrEqualTo(live / 2), "the blast pushes the new fragments (impulse after the shatter)");
            Assert.That(f.world.pieces.Where(p => p.isFragment && !p.removed).All(p => p.cluster.body.linearVelocity.magnitude <= f.world.Settings.tools.explosionMaxDeltaV + 0.5f), "velocity change is capped");
            f.Seconds(12f);
            var frags = f.world.pieces.Where(p => p.isFragment && !p.removed).ToList();
            Debug.Log($"[Shatter] fragments {frags.Count}, sleeping {f.world.stats.sleepingBodies}/{f.world.stats.sleepingBodies + f.world.stats.dynamicBodies}\n{f.LogText()}");
            Assert.That(frags.All(p => p.transform.position.y < 3f), "fragments fell");
            Assert.That(frags.Count(p => p.cluster.body.IsSleeping()), Is.GreaterThanOrEqualTo((int)(frags.Count * 0.8f)), "rubble settles and sleeps");
            Assert.That(f.world.SimTime - f.world.log.LastEventTime, Is.GreaterThan(3f), "rubble stops damaging itself");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThreeClicks_Shatter_TwoClicks_DoNot()
        {
            f = WorldFixture.Create("shatter");
            int a = f.Piece("Panel 00");
            f.world.Damage(a, 0.35f);
            f.world.Damage(a, 0.35f);
            f.Steps(2);
            Assert.IsFalse(f.world.pieces[a].removed, "two clicks do not shatter");
            f.world.Damage(a, 0.35f);
            f.Steps(1);
            Assert.IsTrue(f.world.pieces[a].removed, "third click shatters");
            Assert.That(Fragments("Panel 00").Count, Is.GreaterThanOrEqualTo(2));
            yield return null;
        }

        /// <summary>
        /// Regression: shattering the only piece of a dynamic cluster destroys that cluster at end of frame.
        /// The removed piece must survive it, and stepping must continue without exceptions across frames.
        /// </summary>
        [UnityTest]
        public IEnumerator ShatterInDynamicCluster_SurvivesFrameBoundaries()
        {
            f = WorldFixture.Create("hanging");
            f.Trigger();
            f.Seconds(1f);
            int floor = f.Piece("Floor");
            Assert.IsFalse(f.world.pieces[floor].cluster.isStatic, "floor hangs in its own dynamic cluster");
            for (int k = 0; k < 3; k++) f.world.Damage(floor, 0.35f);
            f.Steps(1);
            Assert.IsTrue(f.world.pieces[floor].removed);
            yield return null; // let deferred Destroy run
            yield return null;
            Assert.IsTrue(f.world.pieces[floor] != null, "removed piece object is parked, not destroyed with its cluster");
            float t0 = f.world.SimTime;
            f.Seconds(3f);
            Assert.That(f.world.SimTime, Is.GreaterThan(t0 + 2.9f));
            yield return null;
            f.Seconds(6f);
            var frags = f.world.pieces.Where(p => p.isFragment && !p.removed).ToList();
            Assert.That(frags.Count(p => p.cluster.body.IsSleeping()), Is.GreaterThanOrEqualTo((int)(frags.Count * 0.8f)), "rubble settles");
        }

        [UnityTest]
        public IEnumerator Fragments_AreIrregularConvexMeshes()
        {
            f = WorldFixture.Create("shatter");
            int target = f.Piece("Panel 01");
            float mass = f.world.Graph.mass[target];
            f.world.Explode(new Vector3(0f, 1.6f, -0.6f), 2.6f, 1.6f, 0f);
            f.Steps(1);
            var frags = Fragments("Panel 01");
            Assert.That(frags.Count, Is.GreaterThanOrEqualTo(3));

            foreach (var p in frags)
            {
                var mc = p.shape as MeshCollider;
                Assert.IsNotNull(mc, $"{p.name} uses a MeshCollider");
                Assert.IsTrue(mc.convex);
                Assert.That(mc.sharedMesh.vertexCount, Is.GreaterThanOrEqualTo(12));
            }

            var masses = frags.Select(p => f.world.Graph.mass[p.index]).OrderBy(m => m).ToList();
            Debug.Log($"[Shapes] {frags.Count} fragments, mass {masses.First():0} .. {masses.Last():0} kg, total {masses.Sum():0} vs piece {mass:0}");
            Assert.That(masses.Sum(), Is.EqualTo(mass).Within(0.02f * mass), "mass is conserved");
            Assert.That(masses.Last(), Is.GreaterThan(1.5f * masses.First()), "fragments differ in size");
            yield return null;
        }

        static float Largest(Piece p)
        {
            var mc = p.shape as MeshCollider;
            Vector3 s = mc != null ? Vector3.Scale(mc.sharedMesh.bounds.size, p.transform.localScale) : p.transform.localScale;
            return Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        }

        /// <summary>A breaker hammering rubble keeps breaking it until every chunk is under the minimum size.</summary>
        [UnityTest]
        public IEnumerator Hammering_Fragments_BreaksThemDownToTheMinimumSize()
        {
            f = WorldFixture.Create("shatter");
            var s = f.world.Settings.fragments;
            s.maxLiveFragments = 2000;
            s.maxShattersPerStep = 32;
            int target = f.Piece("Panel 01");
            float mass = f.world.Graph.mass[target];
            f.world.Explode(new Vector3(0f, 1.6f, -0.6f), 2.6f, 1.6f, 0f);
            f.Steps(1);
            int firstGeneration = Fragments("Panel 01").Count;
            Assert.That(Fragments("Panel 01").Max(Largest), Is.GreaterThan(s.minShatterSize), "the first shatter leaves chunks to break");

            for (int round = 0; round < 12; round++)
            {
                var big = Fragments("Panel 01").Where(p => Largest(p) >= s.minShatterSize).ToList();
                if (big.Count == 0) break;
                foreach (var p in big) f.world.Damage(p.index, s.directShatterDamage);
                f.Steps(1);
            }

            var frags = Fragments("Panel 01");
            Debug.Log($"[Reshatter] {firstGeneration} first-generation chunks became {frags.Count}, largest {frags.Max(Largest):0.00} m, smallest {frags.Min(Largest):0.00} m");
            Assert.That(frags.Count, Is.GreaterThan(firstGeneration), "fragments shattered again");
            Assert.That(frags.All(p => Largest(p) < s.minShatterSize), "no chunk is left above the minimum size");
            Assert.IsTrue(frags.All(p => p.shape is MeshCollider mc && mc.convex), "re-shattered chunks are still convex meshes");
            Assert.That(frags.Sum(p => f.world.Graph.mass[p.index]), Is.EqualTo(mass).Within(0.02f * mass), "mass is conserved through every generation (parents are removed)");

            // Chunks under the minimum ignore further hammering.
            int before = frags.Count;
            foreach (var p in frags) f.world.Damage(p.index, s.directShatterDamage * 3f);
            f.Steps(1);
            Assert.AreEqual(before, Fragments("Panel 01").Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Fragments_DoNotReshatter_WhenDisabled()
        {
            f = WorldFixture.Create("shatter");
            f.world.Settings.fragments.fragmentsCanShatter = false;
            f.world.Explode(new Vector3(0f, 1.6f, -0.6f), 2.6f, 1.6f, 0f);
            f.Steps(1);
            var frags = Fragments("Panel 01");
            foreach (var p in frags) f.world.Damage(p.index, 5f);
            f.Steps(2);
            Assert.AreEqual(frags.Count, Fragments("Panel 01").Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BoxFragmentShape_StillWorks()
        {
            f = WorldFixture.Create("shatter");
            f.world.Settings.fragments.shape = FragmentShape.Boxes;
            int target = f.Piece("Panel 01");
            float mass = f.world.Graph.mass[target];
            f.world.Explode(new Vector3(0f, 1.6f, -0.6f), 2.6f, 1.6f, 0f);
            f.Steps(1);
            var frags = Fragments("Panel 01");
            Assert.That(frags.Count, Is.GreaterThanOrEqualTo(2));
            Assert.IsTrue(frags.All(p => p.shape is BoxCollider));
            Assert.That(frags.Sum(p => f.world.Graph.mass[p.index]), Is.EqualTo(mass).Within(0.01f * mass));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ShattersPerStep_AreQueued_NotDropped()
        {
            f = WorldFixture.Create("shatter");
            f.world.Settings.fragments.maxShattersPerStep = 1;
            f.Trigger();
            f.Steps(1);
            Assert.AreEqual(1, f.world.stats.shatteredPieces, "only one piece shatters in a step");
            Assert.That(f.world.stats.pendingShatters, Is.GreaterThanOrEqualTo(1), "the rest are queued");
            f.Steps(2);
            Assert.AreEqual(2, f.world.stats.shatteredPieces, "the queue drains on later steps");
            Assert.AreEqual(0, f.world.stats.pendingShatters);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OverloadFailures_NeverShatter()
        {
            f = WorldFixture.Create("hanging");
            f.Trigger();
            f.Seconds(8f);
            Assert.AreEqual(0, f.world.stats.shatteredPieces, f.LogText());
            Assert.IsFalse(f.world.pieces[f.Piece("Floor")].removed);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Budget_IsRespected_OverBudgetDetachesWhole()
        {
            f = WorldFixture.Create("shatter");
            f.world.Settings.fragments.maxLiveFragments = 5;
            f.Trigger();
            f.Steps(1);
            Assert.That(f.world.stats.liveFragments, Is.LessThanOrEqualTo(5));
            Assert.That(f.world.stats.shatterSkippedForBudget, Is.GreaterThanOrEqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Reset_RemovesFragments()
        {
            f = WorldFixture.Create("shatter");
            int pieces = f.world.Graph.PieceCount;
            f.Trigger();
            f.Seconds(1f);
            f.world.Build(f.world.CurrentScenario);
            Assert.AreEqual(pieces, f.world.Graph.PieceCount);
            Assert.AreEqual(0, f.world.stats.liveFragments);
            Assert.AreEqual(0, f.world.pieces.Count(p => p.isFragment));
            yield return null;
        }

        [UnityTest]
        public IEnumerator HardImpact_ShattersLooseBlock()
        {
            f = WorldFixture.Create("shatter");
            f.world.Settings.fragments.impactShatterEnergyPerKg = 20f;
            f.world.DropBlock(new Vector3(8f, 30f, 0f), 1000f, 0f); // ~30 m fall onto the ground
            f.Seconds(4f);
            Debug.Log(f.LogText());
            Assert.That(Enumerable.Range(0, f.world.log.Count).Any(i => f.world.log[i].IsShatter && f.world.log[i].reason == FailureReason.Impact));
            yield return null;
        }
    }
}

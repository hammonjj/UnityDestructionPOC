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

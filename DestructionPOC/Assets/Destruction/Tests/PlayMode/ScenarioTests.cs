using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DestructionLab.Tests
{
    /// <summary>Runtime checks for the demonstration scenarios (validation matrix rows 1–6, reset).</summary>
    public class ScenarioTests
    {
        WorldFixture f;

        [TearDown]
        public void TearDown() => f?.Dispose();

        [UnityTest]
        public IEnumerator Scenario1_Intact_StableFor60Seconds()
        {
            f = WorldFixture.Create("intact");
            var start = f.world.pieces.Select(p => p.transform.position).ToArray();
            f.Seconds(60f);
            float maxMove = f.world.pieces.Select((p, i) => Vector3.Distance(p.transform.position, start[i])).Max();
            Assert.AreEqual(0, f.world.log.Total, f.LogText());
            Assert.AreEqual(0, f.world.stats.dynamicBodies + f.world.stats.sleepingBodies);
            Assert.That(maxMove, Is.LessThan(0.001f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Scenario2_LocalDamage_OnlyTargetDetaches()
        {
            f = WorldFixture.Create("local");
            int target = f.Piece("Slab F1 11");
            float y0 = f.world.pieces[target].transform.position.y;
            f.Trigger();
            f.Seconds(6f);
            // Three clicks shatter the panel; nothing else in the frame may move.
            var detached = f.world.pieces.Where(p => !p.removed && !p.isFragment && !p.cluster.isStatic).Select(p => p.name).ToList();
            var frags = f.world.pieces.Where(p => p.isFragment && !p.removed).ToList();
            Debug.Log($"[Local] shattered: {f.world.pieces[target].removed}, fragments {frags.Count}, other detached: {string.Join(", ", detached)}");
            Debug.Log(f.LogText());
            Assert.IsTrue(f.world.pieces[target].removed, "panel shatters");
            Assert.That(frags.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(detached.Count, Is.LessThanOrEqualTo(1), "damage must stay local");
            Assert.That(frags.Average(p => p.transform.position.y), Is.LessThan(y0 - 1.5f), "rubble drops out");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Scenario3_LossOfSupport_DeckJointsFailByOverload()
        {
            f = WorldFixture.Create("support");
            f.Trigger();
            f.Seconds(8f);
            Debug.Log(f.LogText());
            var c12 = f.Between("Deck 1", "Deck 2");
            Assert.AreNotEqual(ConnectionState.Structural, c12.state);
            Assert.That(Enumerable.Range(0, f.world.log.Count).Select(i => f.world.log[i])
                .Any(e => e.reason == FailureReason.StructuralOverload && e.to == ConnectionState.Residual), "overload should leave a residual hinge");
            Assert.IsTrue(f.world.pieces[f.Piece("Abutment W")].cluster.isStatic);
            Assert.IsTrue(f.world.pieces[f.Piece("Abutment E")].cluster.isStatic);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Scenario3b_ThinConnection_FailsDespiteGraphPath()
        {
            f = WorldFixture.Create("thin");
            float y0 = f.PieceTransform("Balcony").position.y;
            f.Trigger();
            f.Seconds(1.5f);
            var thin = f.Between("Balcony", "Pier 5");
            Assert.AreNotEqual(ConnectionState.Structural, thin.state, "narrow connection must fail even though it still connects to the ground");
            Assert.That(Enumerable.Range(0, f.world.log.Count).Select(i => f.world.log[i])
                .Any(e => e.connection == thin.id && e.from == ConnectionState.Structural && e.reason == FailureReason.StructuralOverload),
                "the narrow connection fails by structural overload, not by direct damage");
            f.Seconds(4f);
            Debug.Log(f.LogText());
            Assert.That(f.PieceTransform("Balcony").position.y, Is.LessThan(y0 - 1f), "balcony should not hang from the narrow pier");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Scenario5_Cascade_IsBounded_AndQuiets()
        {
            f = WorldFixture.Create("cascade");
            f.Trigger();
            f.Seconds(20f);
            var log = Enumerable.Range(0, f.world.log.Count).Select(i => f.world.log[i]).ToList();
            Debug.Log($"[Cascade] total {f.world.log.Total}, last at {f.world.log.LastEventTime:0.0}s, impacts {f.world.log.reasonCounts[(int)FailureReason.Impact]}, " +
                      $"max bodies {f.world.stats.maxDynamicBodies}, max joints {f.world.stats.maxActiveJoints}\n{f.LogText()}");
            Assert.That(f.world.log.reasonCounts[(int)FailureReason.Impact], Is.GreaterThanOrEqualTo(1), "a falling section must damage another on impact");
            Assert.AreNotEqual(ConnectionState.Structural, f.Between("Balcony 2", "Tower wall").state, "secondary failure");
            Assert.That(f.world.log.Total, Is.LessThanOrEqualTo(2 * f.world.Graph.connections.Count), "chain must be bounded");
            Assert.IsTrue(f.world.pieces[f.Piece("Tower wall")].cluster.isStatic, "tower stands");
            Assert.That(f.world.SimTime - f.world.log.LastEventTime, Is.GreaterThan(5f), "rubble must stop damaging itself");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Scenario6_Rubble_SleepsSupportsAndWakes()
        {
            f = WorldFixture.Create("rubble");
            f.Seconds(10f);
            int bodies = f.world.stats.dynamicBodies + f.world.stats.sleepingBodies;
            Assert.That(f.world.stats.sleepingBodies, Is.GreaterThanOrEqualTo(bodies * 0.8f), "pile should settle and sleep");
            f.Trigger();
            int woke = 0;
            for (int i = 0; i < 150; i++)
            {
                f.Steps(1);
                woke = Mathf.Max(woke, f.world.stats.dynamicBodies);
            }
            Assert.That(woke, Is.GreaterThan(1), "impact should wake pile pieces");
            f.Seconds(15f);
            var block = f.world.pieces.Last();
            float bottom = block.box.bounds.min.y;
            Debug.Log($"[Rubble] block bottom {bottom:0.00} m, sleeping {f.world.stats.sleepingBodies}/{f.world.stats.dynamicBodies + f.world.stats.sleepingBodies}");
            Assert.That(bottom, Is.GreaterThan(0.2f), "block rests on the pile, not the ground");
            Assert.That(f.world.stats.sleepingBodies, Is.GreaterThanOrEqualTo((f.world.stats.dynamicBodies + f.world.stats.sleepingBodies) * 0.8f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Reset_TenTimes_RestoresInitialState()
        {
            f = WorldFixture.Create("support");
            var s = f.world.CurrentScenario;
            int pieces = f.world.Graph.PieceCount, conns = f.world.Graph.connections.Count;
            for (int i = 0; i < 10; i++)
            {
                f.Trigger();
                f.Seconds(1f);
                f.world.Build(s);
                Assert.AreEqual(pieces, f.world.Graph.PieceCount);
                Assert.AreEqual(conns, f.world.Graph.connections.Count);
                Assert.AreEqual(0, f.world.log.Total);
                Assert.AreEqual(0, f.world.Joints.Count);
                Assert.AreEqual(0, f.world.stats.dynamicBodies + f.world.stats.sleepingBodies);
                Assert.AreEqual(0f, f.world.SimTime);
            }
            yield return null;
        }

        /// <summary>Two connected blocks with no ground connection: one dynamic cluster with two colliders.</summary>
        static Scenario Pair(float height, Vector3 velocity) => new Scenario
        {
            id = "pair", title = "pair",
            build = () =>
            {
                var a = PieceDef.Box("A", new Vector3(-0.5f, height, 0f), Vector3.one, PieceKind.Block);
                var b = PieceDef.Box("B", new Vector3(0.5f, height, 0f), Vector3.one, PieceKind.Block);
                a.initialVelocity = b.initialVelocity = velocity;
                return new System.Collections.Generic.List<PieceDef> { a, b };
            },
        };

        [UnityTest]
        public IEnumerator Explosion_AppliesImpulseOncePerBody()
        {
            // A two-collider cluster resting on the ground; OverlapSphere returns both colliders.
            f = WorldFixture.Create(Pair(0.6f, Vector3.zero));
            f.Seconds(3f);
            var rb = f.world.pieces[0].cluster.body;
            Assert.AreSame(rb, f.world.pieces[1].cluster.body);
            Vector3 p0 = rb.mass * rb.linearVelocity;
            const float impulse = 10000f;
            f.world.Explode(rb.worldCenterOfMass + new Vector3(0f, 0f, -1.2f), 3f, 0f, impulse);
            f.Steps(1);
            float dp = (rb.mass * rb.linearVelocity - p0).magnitude;
            Debug.Log($"[Explosion] Δp {dp:0} N·s for impulse {impulse:0}, bodies {f.world.LastExplosionBodyCount}");
            Assert.AreEqual(1, f.world.LastExplosionBodyCount);
            Assert.That(dp, Is.GreaterThan(0.2f * impulse));
            Assert.That(dp, Is.LessThan(1.05f * impulse), "impulse applied more than once");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Split_PreservesPoseAndVelocity()
        {
            f = WorldFixture.Create(Pair(20f, new Vector3(3f, 0f, 1f)));
            var k = f.world.pieces[0].cluster;
            k.body.angularVelocity = new Vector3(0f, 1.5f, 0.5f);
            f.Seconds(0.3f);
            var g = f.world.Graph;
            var internalConn = g.connections.First(c => !c.IsGround);
            var rb = k.body;
            int child = internalConn.b;
            var pt = f.world.pieces[child].transform;
            Vector3 v = rb.GetPointVelocity(pt.position);
            Vector3 pos = pt.position;
            f.world.Sever(new[] { internalConn.id });
            f.Steps(1);
            var childRb = f.world.pieces[child].cluster.body;
            Assert.AreNotSame(rb, childRb);
            Vector3 expectedPos = pos + v * Time.fixedDeltaTime;
            Vector3 vChild = childRb.GetPointVelocity(pt.position);
            Vector3 vExpected = v + Physics.gravity * Time.fixedDeltaTime;
            Debug.Log($"[Split] pos err {(pt.position - expectedPos).magnitude * 1000f:0.0} mm, vel err {(vChild - vExpected).magnitude:0.000} m/s (|v| {v.magnitude:0.00})");
            Assert.That((pt.position - expectedPos).magnitude, Is.LessThan(0.01f));
            Assert.That((vChild - vExpected).magnitude, Is.LessThan(0.05f + 0.05f * v.magnitude));
            yield return null;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace DestructionLab.Tests
{
    public class StructureTests
    {
        static DestructionSettings Settings() => DestructionSettings.CreateDefault();

        static (List<Vector3> pos, List<Quaternion> rot) Poses(StructureGraph g)
        {
            return (g.pieces.Select(p => p.center).ToList(), g.pieces.Select(_ => Quaternion.identity).ToList());
        }

        static void Solve(StructureGraph g, DestructionSettings s)
        {
            var (pos, rot) = Poses(g);
            new LoadModel().Solve(g, pos, rot, null, null, null, s.structure);
        }

        [Test]
        public void SharedFace_ProducesOneConnectionWithCorrectFrame()
        {
            var defs = new List<PieceDef>
            {
                PieceDef.Box("A", new Vector3(0f, 5f, 0f), new Vector3(2f, 1f, 1f), PieceKind.Block),
                PieceDef.Box("B", new Vector3(2f, 5f, 0f), new Vector3(2f, 1f, 1f), PieceKind.Block),
            };
            var g = StructureGraph.Build(defs, Settings());
            Assert.AreEqual(1, g.connections.Count);
            var c = g.connections[0];
            Assert.AreEqual(Vector3.right, c.normal);
            Assert.That(Vector3.Distance(defs[0].center + c.centerOffsetA, new Vector3(1f, 5f, 0f)), Is.LessThan(1e-5f));
            Assert.AreEqual(1f, c.area, 1e-5f);
        }

        [Test]
        public void EdgeOnlyAndSeparatedBoxes_ProduceNoConnection()
        {
            var defs = new List<PieceDef>
            {
                PieceDef.Box("A", new Vector3(0f, 5f, 0f), Vector3.one, PieceKind.Block),
                PieceDef.Box("Diagonal", new Vector3(1f, 6f, 0f), Vector3.one, PieceKind.Block),
                PieceDef.Box("Gap", new Vector3(1.2f, 5f, 0f), Vector3.one * 0.3f, PieceKind.Block),
            };
            var g = StructureGraph.Build(defs, Settings());
            Assert.AreEqual(0, g.connections.Count);
        }

        [Test]
        public void PieceOnGround_GetsGroundConnection()
        {
            var g = StructureGraph.Build(new List<PieceDef>
            {
                PieceDef.Box("Col", new Vector3(0f, 1.5f, 0f), new Vector3(0.4f, 3f, 0.4f), PieceKind.Column),
            }, Settings());
            Assert.AreEqual(1, g.connections.Count);
            Assert.IsTrue(g.connections[0].IsGround);
        }

        [Test]
        public void BuildingTwice_IsDeterministic()
        {
            var a = StructureGraph.Build(ScenarioLibrary.Frame(2, true), Settings());
            var b = StructureGraph.Build(ScenarioLibrary.Frame(2, true), Settings());
            Assert.AreEqual(a.connections.Count, b.connections.Count);
            for (int i = 0; i < a.connections.Count; i++)
            {
                Assert.AreEqual(a.connections[i].a, b.connections[i].a);
                Assert.AreEqual(a.connections[i].b, b.connections[i].b);
            }
        }

        /// <summary>The disproving experiment for the load model: same slab, same material, same lever arm.</summary>
        [Test]
        public void Cantilever_ThinConnectionLoadsFarMoreThanFullEdge()
        {
            float Q(float supportWidth)
            {
                var defs = new List<PieceDef>
                {
                    PieceDef.Box("Wall", new Vector3(0f, 2.5f, -0.2f), new Vector3(supportWidth, 5f, 0.4f), PieceKind.Wall),
                    PieceDef.Box("Slab", new Vector3(0f, 4.125f, 2f), new Vector3(4f, 0.25f, 4f), PieceKind.Slab),
                };
                var s = Settings();
                var g = StructureGraph.Build(defs, s);
                Solve(g, s);
                return g.Find(0, 1).q;
            }
            float thin = Q(0.3f), full = Q(4f);
            Assert.That(thin, Is.GreaterThan(5f * full), $"thin q={thin}, full q={full}");
            Assert.That(thin, Is.GreaterThan(1f));
        }

        [Test]
        public void IntactFrame_AfterCalibration_IsWithinSafetyFactor()
        {
            var s = Settings();
            var g = StructureGraph.Build(ScenarioLibrary.Frame(2, true), s);
            Solve(g, s);
            g.Calibrate(s.structure.safetyFactor);
            Solve(g, s);
            float max = g.connections.Max(c => c.q);
            Assert.That(max, Is.LessThanOrEqualTo(1f / s.structure.safetyFactor + 1e-3f));
        }

        [Test]
        public void SlabOnFourSupports_HasNoBending_OneSupportHasFullArm()
        {
            var s = Settings();
            var defs = new List<PieceDef> { PieceDef.Box("Slab", new Vector3(0f, 3.125f, 0f), new Vector3(4f, 0.25f, 4f), PieceKind.Slab) };
            foreach (var x in new[] { -1.8f, 1.8f })
            foreach (var z in new[] { -1.8f, 1.8f })
                defs.Add(PieceDef.Box($"Col {x} {z}", new Vector3(x, 1.5f, z), new Vector3(0.4f, 3f, 0.4f), PieceKind.Column));
            var g = StructureGraph.Build(defs, s);
            Solve(g, s);
            foreach (int cid in g.adjacency[0]) Assert.AreEqual(0f, g.connections[cid].loadBending, 1e-2f);

            // Remove three supports: the slab is now a cantilever on one column.
            for (int i = 2; i <= 4; i++) g.Find(0, i).state = ConnectionState.Severed;
            Solve(g, s);
            var last = g.Find(0, 1);
            float expected = g.mass[0] * LoadModel.Gravity * 1.6f * Mathf.Sqrt(2f); // to the column-top hull corner
            Assert.That(last.loadBending, Is.EqualTo(expected).Within(0.15f * expected));
        }

        [Test]
        public void FreeComponent_CarriesNoLoad()
        {
            var s = Settings();
            var defs = new List<PieceDef>
            {
                PieceDef.Box("A", new Vector3(0f, 10f, 0f), new Vector3(2f, 1f, 1f), PieceKind.Block),
                PieceDef.Box("B", new Vector3(2f, 10f, 0f), new Vector3(2f, 1f, 1f), PieceKind.Block),
            };
            var g = StructureGraph.Build(defs, s);
            Solve(g, s);
            Assert.AreEqual(0f, g.connections[0].q);
        }

        [Test]
        public void ResidualEdges_AreNotSupportPaths()
        {
            var s = Settings();
            var defs = new List<PieceDef>
            {
                PieceDef.Box("Wall", new Vector3(0f, 2.5f, -0.2f), new Vector3(4f, 5f, 0.4f), PieceKind.Wall),
                PieceDef.Box("Slab", new Vector3(0f, 4.125f, 2f), new Vector3(4f, 0.25f, 4f), PieceKind.Slab),
            };
            var g = StructureGraph.Build(defs, s);
            g.Find(0, 1).state = ConnectionState.Residual;
            var comp = ClusterMath.Components(g, out var anchored);
            Assert.AreNotEqual(comp[0], comp[1]);
            Assert.IsTrue(anchored[comp[0]]);
            Assert.IsFalse(anchored[comp[1]]);
        }

        [Test]
        public void DamageLaw_OnlyAccumulatesAboveCapacity()
        {
            Assert.AreEqual(0.3f, DamageLaw.Accumulate(0.3f, 0.99f, 0.02f, 1.5f, 2f));
            float d = 0f;
            for (int i = 0; i < 10; i++)
            {
                float next = DamageLaw.Accumulate(d, 1.5f, 0.02f, 1.5f, 2f);
                Assert.That(next, Is.GreaterThan(d));
                d = next;
            }
            Assert.That(DamageLaw.Accumulate(0.99f, 10f, 1f, 1f, 2f), Is.EqualTo(1f));
        }

        [Test]
        public void ImpactFilter_RejectsRestingContact_AcceptsImpact()
        {
            const float dt = 0.02f, g = 9.81f;
            // 20 t section resting on rubble: large impulse, ~zero relative speed.
            Assert.AreEqual(0f, ImpactMath.Energy(20000f * g * dt, 0.05f, 2f));
            // 1 t piece after a 3 m fall.
            float v = Mathf.Sqrt(2f * g * 3f);
            float e = ImpactMath.Energy(1000f * v, v, 2f);
            Assert.That(e, Is.EqualTo(0.5f * 1000f * v * v).Within(1f));
            Assert.That(ImpactMath.PieceDamage(e, 4 * 20000f, 1f), Is.GreaterThan(0.1f));
        }

        [Test]
        public void Hinge_LimitsKeepAbsoluteTwistInRange()
        {
            HingeMath.Limits(30f, 0f, 1f, out float lo, out float hi);
            Assert.AreEqual(-30f, lo, 1e-4f);
            Assert.AreEqual(30f, hi, 1e-4f);
            HingeMath.Limits(30f, 20f, 1f, out lo, out hi);
            Assert.AreEqual(-50f, lo, 1e-4f);
            Assert.AreEqual(10f, hi, 1e-4f);
            HingeMath.Limits(30f, 20f, -1f, out lo, out hi);
            Assert.AreEqual(-10f, lo, 1e-4f);
            Assert.AreEqual(50f, hi, 1e-4f);
        }

        [Test]
        public void Hinge_TwistMeasuresRotationAboutAxis()
        {
            var q = Quaternion.AngleAxis(25f, Vector3.right) * Quaternion.AngleAxis(3f, Vector3.up);
            Assert.AreEqual(25f, HingeMath.TwistDegrees(q, Vector3.right), 1f);
        }

        [Test]
        public void Sag_GrowsOnlyAboveYield_AndStopsAtMax()
        {
            Assert.AreEqual(5f, HingeMath.Sag(5f, 900f, 1000f, 0.02f, 20f, 80f));
            Assert.That(HingeMath.Sag(5f, 3000f, 1000f, 0.02f, 20f, 80f), Is.GreaterThan(5f));
            Assert.AreEqual(80f, HingeMath.Sag(79.9f, 1e6f, 1000f, 1f, 20f, 80f));
        }

        [Test]
        public void HingePoint_ForVerticalInterface_IsBottomEdge()
        {
            var p = HingeMath.HingePoint(new Vector3(0f, 3f, 0f), Vector3.up, 0.25f, new Vector3(0f, 3f, 2f));
            Assert.AreEqual(2.875f, p.y, 1e-4f);
        }

        [Test]
        public void VelocityInheritance_MatchesRigidMotion()
        {
            var v = ClusterMath.InheritedVelocity(new Vector3(1f, 0f, 0f), new Vector3(0f, 2f, 0f), Vector3.zero, new Vector3(0f, 0f, 1f));
            Assert.That(Vector3.Distance(v, new Vector3(3f, 0f, 0f)), Is.LessThan(1e-5f));
        }
    }
}

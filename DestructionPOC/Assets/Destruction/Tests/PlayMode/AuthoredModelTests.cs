using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DestructionLab.Tests
{
    /// <summary>
    /// F4 (#19): an authored blockout drives the lab end to end. The model here is built in code with the
    /// same convention a Blender export follows, so the test does not depend on an imported asset.
    /// </summary>
    public class AuthoredModelTests
    {
        WorldFixture f;
        GameObject model;

        [TearDown]
        public void TearDown()
        {
            f?.Dispose();
            if (model != null) Object.DestroyImmediate(model);
            ScenarioLibrary.AuthoredModel = null;
        }

        /// <summary>Two piers carrying a slab, with a wall panel on top: enough to exercise support and overload.</summary>
        GameObject BuildModel()
        {
            model = new GameObject("AuthoredTestModel");
            void Box(string name, Vector3 center, Vector3 size)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                Object.DestroyImmediate(go.GetComponent<BoxCollider>());
                go.transform.SetParent(model.transform, false);
                go.transform.position = center;
                go.transform.localScale = size;
            }
            Box("Pier_W__concrete", new Vector3(-1.6f, 1.5f, 0f), new Vector3(0.5f, 3f, 0.5f));
            Box("Pier_E__concrete", new Vector3(1.6f, 1.5f, 0f), new Vector3(0.5f, 3f, 0.5f));
            Box("Floor_0__concrete", new Vector3(0f, 3.125f, 0f), new Vector3(4f, 0.25f, 2f));
            Box("Wall_0__brick", new Vector3(0f, 4.05f, 0f), new Vector3(3f, 1.6f, 0.3f));
            Box("Beam_0__wood", new Vector3(0f, 4.95f, 0f), new Vector3(3.4f, 0.2f, 0.4f));
            model.SetActive(false);
            return model;
        }

        [UnityTest]
        public IEnumerator AuthoredModel_ImportsAndIsStableForSixtySeconds()
        {
            ScenarioLibrary.AuthoredModel = BuildModel();
            var scenario = ScenarioLibrary.ById("authored");
            Assert.IsNotNull(scenario, "the authored scenario is offered once a model is supplied");

            f = WorldFixture.Create(scenario);
            Assert.AreEqual(5, f.world.Graph.PieceCount, ScenarioLibrary.AuthoredReport);

            // Materials and kinds come from the name suffixes.
            var g = f.world.Graph;
            Assert.AreEqual(DestructionSettings.Brick, g.pieces.First(p => p.name.StartsWith("Wall")).material);
            Assert.AreEqual(DestructionSettings.Wood, g.pieces.First(p => p.name.StartsWith("Beam")).material);
            Assert.AreEqual(PieceKind.Slab, g.pieces.First(p => p.name.StartsWith("Floor")).kind);

            // It sits on the ground and is anchored there.
            Assert.That(g.pieces.Min(p => p.center.y - p.size.y * 0.5f), Is.EqualTo(0f).Within(1e-3f));
            Assert.IsNotNull(g.GroundConnection(f.Piece("Pier_W__concrete")));
            Assert.That(g.connections.Count, Is.GreaterThanOrEqualTo(6), "piers, slab, wall and beam are connected");

            var start = f.world.pieces.Select(p => p.transform.position).ToArray();
            f.Seconds(60f);
            float moved = f.world.pieces.Select((p, i) => Vector3.Distance(p.transform.position, start[i])).Max();
            Assert.AreEqual(0, f.world.log.Total, f.LogText());
            Assert.That(moved, Is.LessThan(0.001f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthoredModel_RespondsToDestructionLikeProceduralOnes()
        {
            ScenarioLibrary.AuthoredModel = BuildModel();
            f = WorldFixture.Create(ScenarioLibrary.ById("authored"));

            // Blow out one pier: the slab loses half its support and the structure reacts.
            f.world.Explode(new Vector3(-1.6f, 1.5f, 0f), 1.6f, 2f, 8000f);
            f.Seconds(6f);
            Debug.Log($"[Authored] {ScenarioLibrary.AuthoredReport}\n{f.LogText()}");

            Assert.That(f.world.log.Total, Is.GreaterThan(0), "the blast does something");
            Assert.That(f.world.stats.shatteredPieces, Is.GreaterThanOrEqualTo(1), "the blast core shatters a piece");
            Assert.IsFalse(f.world.pieces[f.Piece("Pier_E__concrete")].removed, "the far pier survives");
            Assert.That(f.world.stats.dynamicBodies + f.world.stats.sleepingBodies, Is.GreaterThan(0), "something came loose");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthoredModel_NoShatterTagSurvivesABlast()
        {
            model = new GameObject("NoShatterModel");
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Plinth__concrete__noshatter";
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.transform.SetParent(model.transform, false);
            go.transform.position = new Vector3(0f, 0.6f, 0f);
            go.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            model.SetActive(false);
            ScenarioLibrary.AuthoredModel = model;

            f = WorldFixture.Create(ScenarioLibrary.ById("authored"));
            Assert.IsTrue(f.world.Graph.pieces[0].noShatter);
            f.world.Explode(new Vector3(0f, 0.6f, 0f), 2f, 3f, 5000f);
            f.Seconds(2f);
            Assert.AreEqual(0, f.world.stats.shatteredPieces, "a __noshatter piece never fragments");
            Assert.IsFalse(f.world.pieces[0].removed);
            yield return null;
        }
    }
}

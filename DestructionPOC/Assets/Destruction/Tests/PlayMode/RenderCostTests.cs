using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DestructionLab.Tests
{
    /// <summary>
    /// F7 (#22): the lab must not do per-piece rendering work that scales with the size of a structure.
    /// A MaterialPropertyBlock is what makes a renderer ineligible for batching, so the rule is that only
    /// genuinely tinted pieces carry one.
    /// </summary>
    public class RenderCostTests
    {
        WorldFixture f;

        [TearDown]
        public void TearDown() => f?.Dispose();

        [UnityTest]
        public IEnumerator UntintedPieces_ShareOneMaterialPerKind_AndCarryNoPropertyBlock()
        {
            f = WorldFixture.Create("stress");
            Assert.That(f.world.Graph.PieceCount, Is.GreaterThan(300), "the stress scenario is the large case");

            var byMaterial = f.world.pieces
                .Where(p => !p.removed)
                .GroupBy(p => f.world.Graph.pieces[p.index].material);

            foreach (var group in byMaterial)
            {
                var materials = group.Select(p => p.meshRenderer.sharedMaterial).Distinct().ToList();
                Assert.AreEqual(1, materials.Count, $"material {group.Key} should use one shared material, found {materials.Count}");
            }

            Assert.That(byMaterial.Count(), Is.GreaterThan(1), "the structure uses more than one material");
            Assert.AreEqual(0, f.world.TintedPieces, "an undamaged structure needs no property blocks");
            Assert.IsFalse(f.world.pieces.Any(p => p.meshRenderer.HasPropertyBlock()));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TintWork_IsDoneOncePerChange_NotEveryFrame()
        {
            f = WorldFixture.Create("stress");
            int pieces = f.world.Graph.PieceCount;
            var overlay = new GameObject("Overlay").AddComponent<DiagnosticsOverlay>();
            try
            {
                int afterBuild = f.world.stats.tintUpdates;
                Assert.That(afterBuild, Is.EqualTo(pieces), "each piece is coloured once when it is created");

                // Re-applying the same colours must cost nothing.
                for (int i = 0; i < pieces; i++)
                    f.world.SetPieceColor(i, f.world.Settings.Material(f.world.Graph.pieces[i].material).color);
                Assert.AreEqual(afterBuild, f.world.stats.tintUpdates, "unchanged pieces are skipped");

                // A real change is applied, and leaves exactly one tinted piece behind.
                f.world.SetPieceColor(0, Color.magenta);
                Assert.AreEqual(afterBuild + 1, f.world.stats.tintUpdates);
                Assert.AreEqual(1, f.world.TintedPieces);

                // Returning it to its material colour drops the property block again.
                f.world.SetPieceColor(0, f.world.Settings.Material(f.world.Graph.pieces[0].material).color);
                Assert.AreEqual(0, f.world.TintedPieces);
                Assert.IsFalse(f.world.pieces[0].meshRenderer.HasPropertyBlock());
            }
            finally
            {
                Object.DestroyImmediate(overlay.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Fragments_UseTheMaterialOfThePieceTheyCameFrom()
        {
            f = WorldFixture.Create("shatter");
            int panel = f.Piece("Panel 01");
            var sharedBefore = f.world.pieces[panel].meshRenderer.sharedMaterial;
            f.world.Explode(new Vector3(0f, 1.6f, -0.6f), 2.6f, 1.6f, 0f);
            f.Steps(1);

            var frags = f.world.pieces.Where(p => p.isFragment && !p.removed).ToList();
            Assert.That(frags.Count, Is.GreaterThan(1));
            Assert.IsTrue(frags.All(p => p.meshRenderer.sharedMaterial == sharedBefore),
                "fragments share the parent's material so they batch with it");
            yield return null;
        }

        [UnityTest]
        public IEnumerator StressScenario_IsStableAndCostsLittleStructuralWork()
        {
            f = WorldFixture.Create("stress");
            f.Seconds(10f);
            Assert.AreEqual(0, f.world.log.Total, "the stress structure stands on its own: " + f.LogText());
            Assert.AreEqual(0, f.world.stats.dynamicBodies + f.world.stats.sleepingBodies);
            Debug.Log($"[Stress] {f.world.Graph.PieceCount} pieces, {f.world.Graph.connections.Count} connections, " +
                      $"structural max {f.world.stats.maxStructuralMs:0.0} ms");
            yield return null;
        }
    }
}

using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DestructionLab.Tests
{
    /// <summary>The convenience-store FBX imports cleanly and the lab offers it as a scenario.</summary>
    public class ConvenienceStoreModelTests
    {
        const string ModelPath = "Assets/Destruction/Models/ConvenienceStore.fbx";

        [TearDown]
        public void TearDown() => ScenarioLibrary.ConvenienceStoreModel = null;

        [Test]
        public void Model_ReadsAsStructuralPiecesWithNoSkips()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.IsNotNull(model, ModelPath);
            var instance = Object.Instantiate(model);
            try
            {
                var result = StructureImporter.Read(instance, DestructionSettings.CreateDefault());
                Assert.IsFalse(result.HasErrors, string.Join("\n", result.issues));
                Assert.AreEqual(0, result.Skipped);
                Assert.AreEqual(215, result.pieces.Count);
                Assert.That(result.pieces.Min(p => p.center.y - p.size.y * 0.5f), Is.EqualTo(0f).Within(1e-3f), "sits on y = 0");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void CleanupGauge_CountsEveryPieceButGroundLevelPaving()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var instance = Object.Instantiate(model);
            try
            {
                var pieces = StructureImporter.Read(instance, DestructionSettings.CreateDefault()).pieces;
                var paving = pieces.Where(p => !CleanupLedger.CountsAsBuilding(p)).ToList();
                Assert.AreEqual(24, paving.Count, "sidewalk, curbs, pump island and wheel stops");
                Assert.IsTrue(paving.All(p => p.name.StartsWith("Slab_")), string.Join(", ", paving.Select(p => p.name)));
                Assert.AreEqual(191, pieces.Count - paving.Count);
                // Cars, poles, signs, the boundary and fire debris lying on the ground are part of the building.
                foreach (string name in new[] { "Car_A_Body", "Car_A_Wheel0", "Post_Light0", "Sign_PylonCabinet", "Wall_BoundaryN0",
                             "Roof_00", "Roof_Fallen0", "Dumpster_Body", "Slab_PumpCanopyFallen", "Fascia_Fallen" })
                    Assert.IsTrue(pieces.Any(p => p.name.StartsWith(name) && CleanupLedger.CountsAsBuilding(p)), name);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void CleanupGauge_FragmentsMapBackToTheirAuthoredPiece()
        {
            Assert.AreEqual("Wall_W3__brick", CleanupLedger.RootName("Wall_W3__brick"));
            Assert.AreEqual("Wall_W3__brick", CleanupLedger.RootName("Wall_W3__brick frag 4"));
            Assert.AreEqual("Wall_W3__brick", CleanupLedger.RootName("Wall_W3__brick frag 4 frag 1"));
        }

        [Test]
        public void StoreScenarioIsOfferedOnlyWhenAModelIsSupplied()
        {
            Assert.IsNull(ScenarioLibrary.ById("store"));
            ScenarioLibrary.ConvenienceStoreModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.IsNotNull(ScenarioLibrary.ById("store"));
        }
    }
}

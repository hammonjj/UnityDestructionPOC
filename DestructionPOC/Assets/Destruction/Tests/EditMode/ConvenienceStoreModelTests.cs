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
                Assert.AreEqual(232, result.pieces.Count);
                Assert.That(result.pieces.Min(p => p.center.y - p.size.y * 0.5f), Is.EqualTo(0f).Within(1e-3f), "sits on y = 0");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
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

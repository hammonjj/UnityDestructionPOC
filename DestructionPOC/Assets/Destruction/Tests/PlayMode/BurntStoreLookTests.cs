using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DestructionLab.Tests
{
    /// <summary>The convenience store is a fire-damaged lot: its pieces render with the burnt-piece material.</summary>
    public sealed class BurntStoreLookTests
    {
        [UnitySetUp]
        public IEnumerator Load()
        {
            GameSession.Clear();
            yield return GameScenes.LoadLevel("ConvenienceStore");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            yield return GameScenes.UnloadAll();
        }

        [UnityTest]
        public IEnumerator StorePiecesUseTheLevelsBurntMaterialWithTheirMaterialColour()
        {
            var level = GameLevel.Current;
            Assert.IsNotNull(level.pieceMaterial, "the level sets a piece material");
            var world = level.World;
            Assert.Greater(world.pieces.Count, 0);
            foreach (var p in world.pieces)
            {
                var mat = p.meshRenderer.sharedMaterial;
                Assert.AreEqual("DestructionLab/Burnt Piece", mat.shader.name, p.name);
                var spec = world.Settings.Material(world.Graph.pieces[p.index].material);
                Color c = mat.GetColor("_BaseColor");
                Assert.Less(Mathf.Abs(c.r - spec.color.r) + Mathf.Abs(c.g - spec.color.g) + Mathf.Abs(c.b - spec.color.b), 1e-3f, p.name);
            }
            // The settings asset itself is untouched: other levels keep the plain piece material.
            Assert.AreNotSame(level.pieceMaterial, world.settingsAsset == null ? null : world.settingsAsset.pieceMaterial);
            yield return null;
        }
    }
}

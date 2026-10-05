using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace DestructionLab.Tests
{
    public class StructureImporterTests
    {
        GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        GameObject Model(params (string name, Vector3 center, Vector3 size, Vector3 euler)[] boxes)
        {
            root = new GameObject("Model");
            foreach (var (name, center, size, euler) in boxes)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                Object.DestroyImmediate(go.GetComponent<BoxCollider>());
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(center, Quaternion.Euler(euler));
                go.transform.localScale = size;
            }
            return root;
        }

        static DestructionSettings Settings() => DestructionSettings.CreateDefault();

        [Test]
        public void Read_TurnsBoxObjectsIntoPieces()
        {
            var m = Model(
                ("Pier__concrete", new Vector3(0f, 1.5f, 0f), new Vector3(0.5f, 3f, 0.5f), Vector3.zero),
                ("Wall__brick", new Vector3(2f, 1.5f, 0f), new Vector3(3f, 3f, 0.3f), Vector3.zero));
            var r = StructureImporter.Read(m, Settings());

            Assert.IsFalse(r.HasErrors, string.Join("\n", r.issues));
            Assert.AreEqual(2, r.pieces.Count);
            var pier = r.pieces.First(p => p.name.StartsWith("Pier"));
            Assert.That(pier.size, Is.EqualTo(new Vector3(0.5f, 3f, 0.5f)).Using<Vector3>((a, b) => (a - b).magnitude < 1e-3f ? 0 : 1));
            Assert.AreEqual(DestructionSettings.Concrete, pier.material);
            Assert.AreEqual(PieceKind.Column, pier.kind);
            Assert.AreEqual(DestructionSettings.Brick, r.pieces.First(p => p.name.StartsWith("Wall")).material);
        }

        [Test]
        public void Read_DropsTheModelOntoTheGround()
        {
            var m = Model(("Block", new Vector3(0f, 10f, 0f), Vector3.one, Vector3.zero));
            var r = StructureImporter.Read(m, Settings());
            Assert.That(r.pieces[0].center.y, Is.EqualTo(0.5f).Within(1e-3f), "the lowest point lands on y = 0");
        }

        [Test]
        public void Read_KeepsRelativeLayoutWhenDropping()
        {
            var m = Model(
                ("Lower", new Vector3(0f, 5.5f, 0f), Vector3.one, Vector3.zero),
                ("Upper", new Vector3(0f, 6.5f, 0f), Vector3.one, Vector3.zero));
            var r = StructureImporter.Read(m, Settings());
            float lower = r.pieces.First(p => p.name == "Lower").center.y;
            float upper = r.pieces.First(p => p.name == "Upper").center.y;
            Assert.That(lower, Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(upper - lower, Is.EqualTo(1f).Within(1e-3f));
        }

        GameObject Primitive(PrimitiveType type, Vector3 center, Vector3 size)
        {
            root = new GameObject("Model");
            var go = GameObject.CreatePrimitive(type);
            go.name = type.ToString();
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            go.transform.position = center;
            go.transform.localScale = size;
            return root;
        }

        [TestCase(PrimitiveType.Cylinder)]
        [TestCase(PrimitiveType.Sphere)]
        [TestCase(PrimitiveType.Capsule)]
        public void Read_SkipsRoundShapes(PrimitiveType type)
        {
            // These fill their bounding box far less than a box does, so importing them as boxes would give
            // the wrong collision, the wrong mass and overlapping neighbours.
            var r = StructureImporter.Read(Primitive(type, new Vector3(0f, 1f, 0f), Vector3.one * 2f), Settings());
            Assert.AreEqual(0, r.pieces.Count);
            Assert.AreEqual(1, r.Skipped);
            Assert.That(r.issues.Any(i => i.message.Contains("not a solid box")), Is.True, string.Join("\n", r.issues));
        }

        [Test]
        public void Read_AcceptsASolidBoxPrimitive()
        {
            var r = StructureImporter.Read(Primitive(PrimitiveType.Cube, new Vector3(0f, 1f, 0f), Vector3.one * 2f), Settings());
            Assert.AreEqual(1, r.pieces.Count, string.Join("\n", r.issues));
            Assert.AreEqual(0, r.Skipped);
        }

        [Test]
        public void Read_SkipsAWallWithAHoleCutInIt()
        {
            // A boolean window leaves a box-shaped bounding volume but much less mesh inside it.
            root = new GameObject("Model");
            var go = new GameObject("Wall_with_window__brick", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(0f, 1.5f, 0f);
            go.GetComponent<MeshFilter>().sharedMesh = FrameMesh(3f, 3f, 0.3f, 1.2f);

            var r = StructureImporter.Read(root, Settings());
            Assert.AreEqual(0, r.pieces.Count);
            Assert.That(r.issues.Any(i => i.message.Contains("holes cut into it")), Is.True, string.Join("\n", r.issues));
        }

        /// <summary>A closed wall of `outer` size with a square opening through its thickness.</summary>
        static Mesh FrameMesh(float width, float height, float depth, float hole)
        {
            var combine = new List<CombineInstance>();
            float side = (width - hole) / 2f;
            float band = (height - hole) / 2f;
            void Slab(Vector3 center, Vector3 size)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                combine.Add(new CombineInstance
                {
                    mesh = cube.GetComponent<MeshFilter>().sharedMesh,
                    transform = Matrix4x4.TRS(center, Quaternion.identity, size),
                });
                Object.DestroyImmediate(cube);
            }
            Slab(new Vector3(-(hole + side) / 2f, 0f, 0f), new Vector3(side, height, depth));
            Slab(new Vector3((hole + side) / 2f, 0f, 0f), new Vector3(side, height, depth));
            Slab(new Vector3(0f, (hole + band) / 2f, 0f), new Vector3(hole, band, depth));
            Slab(new Vector3(0f, -(hole + band) / 2f, 0f), new Vector3(hole, band, depth));
            var mesh = new Mesh();
            mesh.CombineMeshes(combine.ToArray(), true, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        [Test]
        public void Read_SkipsRotatedObjectsWithAWarning()
        {
            var m = Model(
                ("Good", new Vector3(0f, 0.5f, 0f), Vector3.one, Vector3.zero),
                ("Tilted", new Vector3(3f, 0.5f, 0f), Vector3.one, new Vector3(0f, 0f, 30f)));
            var r = StructureImporter.Read(m, Settings());

            Assert.AreEqual(1, r.pieces.Count);
            Assert.AreEqual(1, r.Skipped);
            Assert.IsFalse(r.HasErrors);
            var issue = r.issues.First(i => i.piece == "Tilted");
            Assert.That(issue.message, Does.Contain("rotated"));
        }

        [Test]
        public void Read_AcceptsNinetyDegreeRotations()
        {
            // A box turned by a right angle is still axis-aligned, which authoring tools do all the time.
            var m = Model(("Beam", new Vector3(0f, 1f, 0f), new Vector3(3f, 0.4f, 0.4f), new Vector3(0f, 90f, 0f)));
            var r = StructureImporter.Read(m, Settings());
            Assert.AreEqual(1, r.pieces.Count, string.Join("\n", r.issues));
            Assert.That(r.pieces[0].size.z, Is.EqualTo(3f).Within(1e-3f), "the long axis follows the rotation");
        }

        [Test]
        public void Read_SkipsSliversAndReportsEmptyModels()
        {
            var m = Model(("Paper", new Vector3(0f, 0.5f, 0f), new Vector3(1f, 0.005f, 1f), Vector3.zero));
            var r = StructureImporter.Read(m, Settings());
            Assert.AreEqual(0, r.pieces.Count);
            Assert.AreEqual(1, r.Skipped);
            Assert.IsTrue(r.HasErrors, "a model with nothing usable is an error");
        }

        [Test]
        public void Read_WarnsAboutOverlappingPieces()
        {
            var m = Model(
                ("A", new Vector3(0f, 0.5f, 0f), Vector3.one, Vector3.zero),
                ("B", new Vector3(0.5f, 0.5f, 0f), Vector3.one, Vector3.zero));
            var r = StructureImporter.Read(m, Settings());
            Assert.That(r.issues.Any(i => i.message.Contains("overlaps")), Is.True);
            Assert.IsFalse(r.HasErrors, "overlap is a warning, not a failure");
        }

        [Test]
        public void Read_TouchingPiecesAreNotTreatedAsOverlapping()
        {
            var m = Model(
                ("A", new Vector3(0f, 0.5f, 0f), Vector3.one, Vector3.zero),
                ("B", new Vector3(1f, 0.5f, 0f), Vector3.one, Vector3.zero));
            var r = StructureImporter.Read(m, Settings());
            Assert.That(r.issues.Any(i => i.message.Contains("overlaps")), Is.False);
        }

        [Test]
        public void Read_ReportsAnEmptyOrMissingModel()
        {
            Assert.IsTrue(StructureImporter.Read(null, Settings()).HasErrors);
            root = new GameObject("Empty");
            Assert.IsTrue(StructureImporter.Read(root, Settings()).HasErrors);
        }

        [Test]
        public void NameSuffixes_SelectMaterialAndFlags()
        {
            var s = Settings();
            Assert.AreEqual(DestructionSettings.Wood, StructureImporter.MaterialFor("Beam_3__wood", s));
            Assert.AreEqual(DestructionSettings.Brick, StructureImporter.MaterialFor("Wall_G_N0__brick", s));
            Assert.AreEqual(DestructionSettings.Concrete, StructureImporter.MaterialFor("Floor_1_00__concrete", s));
            Assert.AreEqual(DestructionSettings.Concrete, StructureImporter.MaterialFor("Unlabelled", s), "concrete is the default");
            Assert.IsTrue(StructureImporter.IsNoShatter("Statue__noshatter"));
            Assert.IsFalse(StructureImporter.IsNoShatter("Statue__wood"));
        }

        [Test]
        public void KindGuess_UsesNameThenShape()
        {
            Assert.AreEqual(PieceKind.Slab, StructureImporter.KindFor("Floor_1_00__concrete", Vector3.one));
            Assert.AreEqual(PieceKind.Column, StructureImporter.KindFor("Pier_WS", Vector3.one));
            Assert.AreEqual(PieceKind.Wall, StructureImporter.KindFor("Wall_G", Vector3.one));
            Assert.AreEqual(PieceKind.Slab, StructureImporter.KindFor("Thing", new Vector3(2f, 0.25f, 2f)));
            Assert.AreEqual(PieceKind.Column, StructureImporter.KindFor("Thing", new Vector3(0.4f, 3f, 0.4f)));
        }
    }
}

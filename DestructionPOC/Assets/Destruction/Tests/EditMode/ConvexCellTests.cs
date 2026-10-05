using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace DestructionLab.Tests
{
    public class ConvexCellTests
    {
        static readonly Vector3 Panel = new Vector3(2f, 1.6f, 0.3f);

        [Test]
        public void Box_HasSixFacesAndCorrectVolume()
        {
            var c = ConvexCell.Box(Panel);
            Assert.AreEqual(6, c.faces.Count);
            Assert.That(c.volume, Is.EqualTo(2f * 1.6f * 0.3f).Within(1e-4f));
            Assert.That(c.centroid.magnitude, Is.LessThan(1e-4f));
        }

        [Test]
        public void Clip_HalvingTheBox_HalvesTheVolume()
        {
            var c = ConvexCell.Box(Vector3.one * 2f);
            Assert.IsTrue(c.Clip(Vector3.right, 0f));
            Assert.That(c.volume, Is.EqualTo(4f).Within(1e-3f));
            Assert.That(c.centroid.x, Is.EqualTo(-0.5f).Within(1e-3f));
            foreach (var poly in c.faces)
            foreach (var p in poly)
                Assert.That(p.x, Is.LessThanOrEqualTo(1e-4f), "no vertex survives on the clipped side");
        }

        [Test]
        public void Clip_CornerCut_ProducesATriangularCap()
        {
            var c = ConvexCell.Box(Vector3.one * 2f);
            Assert.IsTrue(c.Clip(new Vector3(1f, 1f, 1f).normalized, 1.2f));
            Assert.That(c.volume, Is.LessThan(8f));
            Assert.That(c.volume, Is.GreaterThan(7f));
            Assert.AreEqual(7, c.faces.Count, "six box faces plus the cap");
        }

        [Test]
        public void Clip_PlaneMissingTheSolid_ChangesNothing()
        {
            var c = ConvexCell.Box(Vector3.one);
            float before = c.volume;
            Assert.IsTrue(c.Clip(Vector3.up, 5f));
            Assert.That(c.volume, Is.EqualTo(before).Within(1e-5f));
            Assert.AreEqual(6, c.faces.Count);
        }

        [Test]
        public void Clip_RemovingEverything_ReportsEmpty()
        {
            var c = ConvexCell.Box(Vector3.one);
            Assert.IsFalse(c.Clip(Vector3.up, -5f));
            Assert.AreEqual(0f, c.volume);
        }

        [Test]
        public void Cells_TileTheBox_VolumeConserved()
        {
            var cells = VoronoiFracture.Cells(Panel, 8, 1234);
            Assert.That(cells.Count, Is.GreaterThanOrEqualTo(6));
            float total = cells.Sum(c => c.volume);
            Assert.That(total, Is.EqualTo(Panel.x * Panel.y * Panel.z).Within(0.02f * Panel.x * Panel.y * Panel.z));
        }

        [Test]
        public void Cells_AreIrregular_NotAllTheSameSizeOrAxisAligned()
        {
            var cells = VoronoiFracture.Cells(Panel, 8, 1234);
            float min = cells.Min(c => c.volume), max = cells.Max(c => c.volume);
            Assert.That(max, Is.GreaterThan(1.5f * min), $"cell volumes vary (min {min}, max {max})");
            Assert.That(cells.Any(c => c.faces.Count != 6), "at least one cell is not a six-sided box");

            bool anyOblique = false;
            foreach (var cell in cells)
            foreach (var poly in cell.faces)
            {
                Vector3 n = Vector3.Cross(poly[1] - poly[0], poly[2] - poly[0]).normalized;
                float axis = Mathf.Max(Mathf.Abs(n.x), Mathf.Max(Mathf.Abs(n.y), Mathf.Abs(n.z)));
                if (axis < 0.98f) { anyOblique = true; break; }
            }
            Assert.IsTrue(anyOblique, "at least one face is not axis-aligned");
        }

        [Test]
        public void Cells_AreConvexAndClosed()
        {
            foreach (var cell in VoronoiFracture.Cells(Panel, 8, 7))
            {
                Assert.That(cell.volume, Is.GreaterThan(0f));
                Assert.That(cell.faces.Count, Is.GreaterThanOrEqualTo(4));
                var verts = cell.faces.SelectMany(f => f).ToList();
                foreach (var poly in cell.faces)
                {
                    Vector3 n = Vector3.Cross(poly[1] - poly[0], poly[2] - poly[0]).normalized;
                    float d = Vector3.Dot(n, poly[0]);
                    foreach (var p in verts)
                        Assert.That(Vector3.Dot(n, p) - d, Is.LessThan(1e-3f), "every vertex is behind every face plane");
                }
            }
        }

        [Test]
        public void Cells_AreDeterministicPerSeed()
        {
            var a = VoronoiFracture.Cells(Panel, 8, 55);
            var b = VoronoiFracture.Cells(Panel, 8, 55);
            var c = VoronoiFracture.Cells(Panel, 8, 56);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.That(a[i].volume, Is.EqualTo(b[i].volume).Within(1e-6f));
            Assert.That(a.Select(x => x.volume), Is.Not.EqualTo(c.Select(x => x.volume)));
        }

        [Test]
        public void Cells_StayInsideTheBox()
        {
            foreach (var cell in VoronoiFracture.Cells(Panel, 10, 3))
            foreach (var poly in cell.faces)
            foreach (var p in poly)
            {
                Assert.That(Mathf.Abs(p.x), Is.LessThanOrEqualTo(Panel.x * 0.5f + 1e-3f));
                Assert.That(Mathf.Abs(p.y), Is.LessThanOrEqualTo(Panel.y * 0.5f + 1e-3f));
                Assert.That(Mathf.Abs(p.z), Is.LessThanOrEqualTo(Panel.z * 0.5f + 1e-3f));
            }
        }

        [Test]
        public void Cells_WorkForThinSlabsAndCubes()
        {
            foreach (var size in new[] { new Vector3(4f, 0.25f, 4f), Vector3.one * 2f, new Vector3(0.4f, 3f, 0.4f) })
            {
                var cells = VoronoiFracture.Cells(size, 8, 11);
                Assert.That(cells.Count, Is.GreaterThanOrEqualTo(4), $"size {size}");
                float total = cells.Sum(c => c.volume);
                float want = size.x * size.y * size.z;
                Assert.That(total, Is.EqualTo(want).Within(0.03f * want), $"size {size}");
            }
        }

        [Test]
        public void Mesh_IsBuiltAroundTheCentroid()
        {
            var cell = VoronoiFracture.Cells(Panel, 6, 9)[0];
            var mesh = FragmentMesh.Build(cell, "test");
            try
            {
                Assert.That(mesh.vertexCount, Is.GreaterThanOrEqualTo(12));
                Assert.That(mesh.bounds.center.magnitude, Is.LessThan(0.2f), "mesh is centred on the centre of mass");
                Assert.That(mesh.triangles.Length % 3, Is.EqualTo(0));

                // Every triangle must face out of the solid, or the chunks render inside-out.
                var v = mesh.vertices;
                var n = mesh.normals;
                var tris = mesh.triangles;
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 a = v[tris[t]], b = v[tris[t + 1]], c = v[tris[t + 2]];
                    Vector3 geometric = Vector3.Cross(b - a, c - a).normalized;
                    Vector3 outward = ((a + b + c) / 3f).normalized;
                    Assert.That(Vector3.Dot(geometric, outward), Is.GreaterThan(0f), "triangle winding faces outward");
                    Assert.That(Vector3.Dot(n[tris[t]], geometric), Is.GreaterThan(0.9f), "vertex normal agrees with the winding");
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}

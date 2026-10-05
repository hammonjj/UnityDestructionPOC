using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace DestructionLab.Tests
{
    public class FragmenterTests
    {
        [Test]
        public void Split_TilesTheBox_VolumeConserved()
        {
            var size = new Vector3(2f, 1.6f, 0.3f);
            var boxes = Fragmenter.Split(size, 8, 0.15f, 42);
            Assert.AreEqual(8, boxes.Count);
            Assert.That(boxes.Sum(b => b.Volume), Is.EqualTo(size.x * size.y * size.z).Within(1e-4f));
            foreach (var b in boxes)
            {
                Vector3 min = b.center - b.size * 0.5f, max = b.center + b.size * 0.5f;
                for (int k = 0; k < 3; k++)
                {
                    Assert.That(min[k], Is.GreaterThanOrEqualTo(-size[k] * 0.5f - 1e-4f));
                    Assert.That(max[k], Is.LessThanOrEqualTo(size[k] * 0.5f + 1e-4f));
                }
            }
        }

        [Test]
        public void Split_FragmentsDoNotOverlap()
        {
            var boxes = Fragmenter.Split(new Vector3(4f, 0.25f, 4f), 12, 0.15f, 7);
            for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                float overlap = 1f;
                for (int k = 0; k < 3; k++)
                {
                    float lo = Mathf.Max(boxes[i].center[k] - boxes[i].size[k] / 2f, boxes[j].center[k] - boxes[j].size[k] / 2f);
                    float hi = Mathf.Min(boxes[i].center[k] + boxes[i].size[k] / 2f, boxes[j].center[k] + boxes[j].size[k] / 2f);
                    overlap *= Mathf.Max(0f, hi - lo);
                }
                Assert.That(overlap, Is.LessThan(1e-6f), $"fragments {i} and {j} overlap");
            }
        }

        [Test]
        public void Split_IsDeterministicPerSeed()
        {
            var a = Fragmenter.Split(Vector3.one * 2f, 10, 0.15f, 99);
            var b = Fragmenter.Split(Vector3.one * 2f, 10, 0.15f, 99);
            var c = Fragmenter.Split(Vector3.one * 2f, 10, 0.15f, 100);
            Assert.IsTrue(a.Select(x => x.center).SequenceEqual(b.Select(x => x.center)));
            Assert.IsFalse(a.Select(x => x.center).SequenceEqual(c.Select(x => x.center)));
        }

        [Test]
        public void Split_RespectsMinimumSize()
        {
            var boxes = Fragmenter.Split(new Vector3(0.4f, 0.2f, 0.4f), 50, 0.15f, 3);
            Assert.That(boxes.Count, Is.LessThan(50));
            foreach (var b in boxes) Assert.That(Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z)), Is.GreaterThanOrEqualTo(0.15f - 1e-4f).Or.EqualTo(0.2f).Within(1e-4f));
        }

        [Test]
        public void TargetCount_ScalesWithSizeAndClamps()
        {
            Assert.AreEqual(6, Fragmenter.TargetCount(new Vector3(2f, 0.25f, 2f), 0.8f, 3, 12));
            Assert.AreEqual(12, Fragmenter.TargetCount(new Vector3(4f, 0.25f, 4f), 0.8f, 3, 12));
            Assert.AreEqual(3, Fragmenter.TargetCount(new Vector3(0.5f, 0.5f, 0.5f), 0.8f, 3, 12));
        }
    }
}

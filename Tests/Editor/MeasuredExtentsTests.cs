using System;
using NUnit.Framework;
using UniMob.UI.Rendering;

namespace UniMob.UI.Tests
{
    public class MeasuredExtentsTests
    {
        private const float Average = 50f;
        private const float Spacing = 4f;

        // The model written the slow, obvious way.
        private static float ExpectedLeadingEdge(float?[] measured, int index)
        {
            var edge = 0f;
            for (var i = 0; i < index; i++)
                edge += (i < measured.Length ? measured[i] ?? Average : Average) + Spacing;
            return edge;
        }

        [Test]
        public void LeadingEdge_WithNothingMeasured_IsTheAverageStride()
        {
            var extents = new MeasuredExtents();

            Assert.AreEqual(0f, extents.LeadingEdge(0, Average, Spacing));
            Assert.AreEqual(10 * (Average + Spacing), extents.LeadingEdge(10, Average, Spacing));
        }

        [Test]
        public void LeadingEdge_MixesMeasuredAndEstimatedSlots_AcrossGrowthAndOverwrites()
        {
            var random = new Random(12345);
            var extents = new MeasuredExtents();
            var measured = new float?[1000];

            for (var round = 0; round < 3000; round++)
            {
                // Indices climb with the rounds so the backing storage is regrown many times, and
                // repeat so that a slot is overwritten with a different extent.
                var index = random.Next(0, Math.Min(measured.Length, 8 + round));
                var extent = random.Next(0, 400);
                extents.Set(index, extent);
                measured[index] = extent;

                var probe = random.Next(0, measured.Length + 50);
                Assert.AreEqual(
                    ExpectedLeadingEdge(measured, probe),
                    extents.LeadingEdge(probe, Average, Spacing),
                    0.01f,
                    $"round {round}: leading edge of slot {probe} after setting slot {index} to {extent}"
                );
            }
        }

        [Test]
        public void IndexAt_AnswersTheSlotAnOffsetFallsIn()
        {
            var random = new Random(999);
            var extents = new MeasuredExtents();
            const int slotCount = 500;

            for (var i = 0; i < 300; i++)
                extents.Set(random.Next(0, slotCount), random.Next(1, 400));

            var contentEnd = extents.LeadingEdge(slotCount, Average, Spacing);
            for (var probe = 0; probe < 2000; probe++)
            {
                var offset = (float)(random.NextDouble() * contentEnd);
                var index = extents.IndexAt(offset, slotCount, Average, Spacing);

                var start = extents.LeadingEdge(index, Average, Spacing);
                var nextStart = extents.LeadingEdge(index + 1, Average, Spacing);
                Assert.That(
                    start <= offset && (offset < nextStart || index == slotCount - 1),
                    $"offset {offset} resolved to slot {index}, which spans [{start}, {nextStart})"
                );
            }
        }

        [Test]
        public void IndexAt_ClampsToTheSlotsThatExist()
        {
            var extents = new MeasuredExtents();
            extents.Set(0, 100f);

            Assert.AreEqual(0, extents.IndexAt(-500f, 10, Average, Spacing), "before the content");
            Assert.AreEqual(9, extents.IndexAt(1e9f, 10, Average, Spacing), "past the content");
            Assert.AreEqual(0, extents.IndexAt(50f, 1, Average, Spacing), "a single slot");
        }

        [Test]
        public void Clear_ForgetsEveryMeasurement()
        {
            var extents = new MeasuredExtents();
            extents.Set(3, 500f);
            extents.Set(70, 500f);

            extents.Clear();

            Assert.AreEqual(0, extents.Count);
            Assert.IsFalse(extents.TryGet(3, out _));
            Assert.AreEqual(100 * (Average + Spacing), extents.LeadingEdge(100, Average, Spacing));

            extents.Set(3, 20f);
            Assert.AreEqual(1, extents.Count);
            Assert.AreEqual(
                20f + 9 * Average + 10 * Spacing,
                extents.LeadingEdge(10, Average, Spacing),
                0.01f
            );
        }
    }
}

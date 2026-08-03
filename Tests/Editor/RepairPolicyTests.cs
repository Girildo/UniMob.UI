using NUnit.Framework;
using UniMob.UI.Diagnostics;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // One rule in several places: a non-finite axis is clamped to zero, on that axis alone, at the
    // point of detection, in every build. These assert the rule rather than any one site's wording,
    // so a site that drifts back to a different answer -- last frame's RectTransform, the enclosing
    // viewport, or nothing at all -- fails here.
    public class RepairPolicyTests
    {
        private const float Inf = float.PositiveInfinity;

        private static RenderFlex Mount(Widget widget) =>
            (RenderFlex)TestHarness.Mount(widget).RenderObject;

        private static Widget Box(float width, float height) =>
            new FixedSizeBox { Size = new Vector2(width, height) };

        [Test]
        public void InflexibleChild_KeepsItsCrossAxis_WhenTheMainAxisIsZeroed()
        {
            using var log = RecordingReporter.Capture();
            var row = new Row();
            row.Children.Add(Box(Inf, 42));

            var render = Mount(row);
            render.Layout(LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(new Vector2(0, 42), render.ChildrenLayout[0].Size);
        }

        // Per-axis means per-axis: a Column zeroes the height and leaves the width alone.
        [Test]
        public void TheZeroedAxisIsTheMainAxis_NotAlwaysTheWidth()
        {
            using var log = RecordingReporter.Capture();
            var column = new Column();
            column.Children.Add(Box(42, Inf));

            var render = Mount(column);
            render.Layout(LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(new Vector2(42, 0), render.ChildrenLayout[0].Size);
        }

        [Test]
        public void FlexibleAndInflexibleChildren_GetTheSameRepair()
        {
            using var log = RecordingReporter.Capture();
            var row = new Row();
            row.Children.Add(Box(Inf, 10));
            row.Children.Add(new Expanded { Child = Box(5, 10) });

            var render = Mount(row);
            render.Layout(new LayoutConstraints(0, 0, Inf, 10));

            Assert.AreEqual(new Vector2(0, 10), render.ChildrenLayout[0].Size);
            Assert.AreEqual(new Vector2(0, 10), render.ChildrenLayout[1].Size);
        }

        // Zero is only honest when something says so, so the clamp is never adopted alone: the report
        // and the per-child marker are half of the decision, not decoration on top of it.
        [Test]
        public void EveryClamp_IsAccompaniedByAReportAndAMarker()
        {
            using var log = RecordingReporter.Capture();
            var row = new Row();
            row.Children.Add(Box(Inf, 10));

            var render = Mount(row);
            render.Layout(LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(1, log.Count);
            Assert.IsNotNull(log[0].Remedy);
            Assert.AreEqual(LayoutIssueCode.NonFiniteChildSize, render.ChildrenLayout[0].Issue);
        }
    }
}

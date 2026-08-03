using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class RenderPositionedBoxTests
    {
        private class FakePositionedBoxState : FakeSingleChildLayoutState, IPositionedBoxState
        {
            public Alignment Alignment { get; set; } = Alignment.Center;
            public float? WidthFactor { get; set; }
            public float? HeightFactor { get; set; }
        }

        [Test]
        public void NoFactors_UnboundedConstraints_ShrinkWrapsToChildSize()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(30, 40) });
            var state = new FakePositionedBoxState { Child = child };

            var box = new RenderPositionedBox(state);
            box.Layout(LayoutConstraints.Unbounded());

            Assert.AreEqual(new Vector2(30, 40), box.PeekSize());
        }

        [Test]
        public void NoFactors_BoundedConstraints_ExpandsToFill_IgnoringChildSize()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(30, 40) });
            var state = new FakePositionedBoxState { Child = child };

            var box = new RenderPositionedBox(state);
            box.Layout(LayoutConstraints.Tight(200, 150));

            Assert.AreEqual(new Vector2(200, 150), box.PeekSize());
        }

        [Test]
        public void WidthFactor_ScalesChildWidth_RegardlessOfBoundedness()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(20, 10) });
            var state = new FakePositionedBoxState { Child = child, WidthFactor = 2f };

            var box = new RenderPositionedBox(state);
            box.Layout(LayoutConstraints.Loose(1000, 1000));

            // Width is shrink-wrapped via the factor (20 * 2); height has no factor and the incoming
            // constraints are bounded, so it expands to fill instead of shrink-wrapping.
            Assert.AreEqual(new Vector2(40, 1000), box.PeekSize());
        }

        [Test]
        public void Alignment_PositionsChild_WithinTheFinalSize()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(20, 20) });
            var state = new FakePositionedBoxState { Child = child, Alignment = Alignment.BottomRight };

            var box = new RenderPositionedBox(state);
            box.Layout(LayoutConstraints.Tight(100, 100));

            Assert.AreEqual(new Vector2(80, 80), box.ChildPosition);
        }

        [Test]
        public void NoChild_CollapsesToZero_AndPositionsAtOrigin()
        {
            var state = new FakePositionedBoxState { Child = null, WidthFactor = 2f };

            var box = new RenderPositionedBox(state);
            box.Layout(LayoutConstraints.Loose(1000, 1000));

            Assert.AreEqual(0f, box.PeekSize().x);
            Assert.AreEqual(Vector2.zero, box.ChildPosition);
        }
    }
}

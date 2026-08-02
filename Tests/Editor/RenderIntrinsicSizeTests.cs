using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class RenderIntrinsicSizeTests
    {
        private class FakeIntrinsicSizeState : FakeSingleChildLayoutState, IIntrinsicSizeState
        {
            public Axis Axis { get; set; }
        }

        [Test]
        public void HorizontalAxis_TightensWidthToChildIntrinsicWidth_LeavesHeightLoose()
        {
            // WidthProbeBox's intrinsic width echoes back whatever height bound it's asked about, and
            // its PerformSizing echoes back the constraints it actually receives -- together these make
            // it possible to observe exactly which axis RenderIntrinsicSize tightened.
            var child = TestHarness.Mount(new WidthProbeBox());
            var state = new FakeIntrinsicSizeState { Axis = Axis.Horizontal, Child = child };

            var render = new RenderIntrinsicSize(state);
            render.Layout(new LayoutConstraints(0, 0, 1000, 77));

            // Width got tightened to the child's intrinsic width (measured at the bounded height, 77);
            // height was left loose, so the child collapsed to its own minimum (0).
            Assert.AreEqual(new Vector2(77, 0), render.Size);

            // ChildSize/ChildPosition are what the View applies to the child's RectTransform --
            // distinct from render.Size, which is only reported to this render object's own parent.
            Assert.AreEqual(render.Size, render.ChildSize);
            Assert.AreEqual(Vector2.zero, render.ChildPosition);
        }

        [Test]
        public void VerticalAxis_TightensHeightToChildIntrinsicHeight_LeavesWidthLoose()
        {
            var child = TestHarness.Mount(new WidthProbeBox());
            var state = new FakeIntrinsicSizeState { Axis = Axis.Vertical, Child = child };

            var render = new RenderIntrinsicSize(state);
            render.Layout(new LayoutConstraints(0, 0, 77, 1000));

            Assert.AreEqual(new Vector2(0, 77), render.Size);
        }
    }
}

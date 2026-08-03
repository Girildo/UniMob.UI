using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class RenderConstrainedBoxTests
    {
        private class FakeConstrainedBoxState : FakeSingleChildLayoutState, IConstrainedBoxState
        {
            public LayoutConstraints BoxConstraints { get; set; }
        }

        [Test]
        public void ForcesChildToBoxConstraints_WhenParentIsLooser()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(10, 10) });
            var state = new FakeConstrainedBoxState { BoxConstraints = LayoutConstraints.Tight(50, 50), Child = child };

            var box = new RenderConstrainedBox(state);
            box.Layout(LayoutConstraints.Loose(1000, 1000));

            Assert.AreEqual(new Vector2(50, 50), box.PeekSize());
        }

        [Test]
        public void BoxConstraintsAreClamped_ByAStricterParent()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(10, 10) });
            var state = new FakeConstrainedBoxState { BoxConstraints = LayoutConstraints.Tight(200, 200), Child = child };

            var box = new RenderConstrainedBox(state);
            box.Layout(LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(new Vector2(100, 100), box.PeekSize());
        }

        [Test]
        public void NoChild_SizesToBoxConstraintsMinimum()
        {
            var state = new FakeConstrainedBoxState
            {
                BoxConstraints = new LayoutConstraints(20, 30, 20, 30),
                Child = null,
            };

            var box = new RenderConstrainedBox(state);
            box.Layout(LayoutConstraints.Loose(1000, 1000));

            Assert.AreEqual(new Vector2(20, 30), box.PeekSize());
        }

        [Test]
        public void NoChild_UnboundedEverywhere_CollapsesToParentMinimum()
        {
            var state = new FakeConstrainedBoxState { BoxConstraints = LayoutConstraints.Expanded(), Child = null };

            var box = new RenderConstrainedBox(state);
            box.Layout(LayoutConstraints.Unbounded());

            Assert.AreEqual(Vector2.zero, box.PeekSize());
        }

        [Test]
        public void IntrinsicWidth_WhenBoxIsTight_IgnoresChild()
        {
            var state = new FakeConstrainedBoxState { BoxConstraints = LayoutConstraints.Tight(50, 60), Child = null };

            var box = new RenderConstrainedBox(state);

            Assert.AreEqual(50f, box.GetIntrinsicWidth(0f));
        }

        [Test]
        public void IntrinsicWidth_WhenBoxIsLoose_ClampsChildsIntrinsicWidth()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(200, 10) });
            var state = new FakeConstrainedBoxState { BoxConstraints = LayoutConstraints.Loose(100, 100), Child = child };

            var box = new RenderConstrainedBox(state);

            Assert.AreEqual(100f, box.GetIntrinsicWidth(0f));
        }
    }
}

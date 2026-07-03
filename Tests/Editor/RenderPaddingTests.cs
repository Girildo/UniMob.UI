using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class RenderPaddingTests
    {
        private class FakePaddingState : FakeSingleChildLayoutState, IPaddingState
        {
            public RectPadding Padding { get; set; }
        }

        [Test]
        public void WithChild_AddsPaddingAroundChildSize_AndOffsetsChildByPadding()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(30, 40) });
            var state = new FakePaddingState { Padding = RectPadding.All(10), Child = child };

            var padding = new RenderPadding(state);
            padding.PerformLayoutImmediate(LayoutConstraints.Loose(1000, 1000));

            Assert.AreEqual(new Vector2(50, 60), padding.Size);
            Assert.AreEqual(new Vector2(10, 10), padding.ChildPosition);
        }

        [Test]
        public void NoChild_SizesToPaddingItself()
        {
            var state = new FakePaddingState { Padding = RectPadding.All(15), Child = null };

            var padding = new RenderPadding(state);
            padding.PerformLayoutImmediate(LayoutConstraints.Loose(1000, 1000));

            Assert.AreEqual(new Vector2(30, 30), padding.Size);
        }

        [Test]
        public void IntrinsicWidth_AddsHorizontalPadding_ToChildsIntrinsicWidth()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(50, 10) });
            var state = new FakePaddingState
            {
                Padding = RectPadding.Only(left: 5, right: 5, top: 10, bottom: 10),
                Child = child,
            };

            var padding = new RenderPadding(state);

            Assert.AreEqual(60f, padding.GetIntrinsicWidth(100f));
        }
    }
}

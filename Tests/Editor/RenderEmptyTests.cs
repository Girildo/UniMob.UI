using NUnit.Framework;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class RenderEmptyTests
    {
        private sealed class EmptyLayoutState : FakeState { }

        private static RenderEmpty Build() => new RenderEmpty(new EmptyLayoutState());

        [Test]
        public void AlwaysSizesToZero_RegardlessOfConstraints()
        {
            var render = Build();

            render.Layout(new LayoutConstraints(20, 30, 100, 100));

            // Documents actual behavior: PerformSizing returns Vector2.zero unconditionally, without
            // running it through constraints.Constrain() -- so a nonzero MinWidth/MinHeight is not honored.
            Assert.AreEqual(Vector2.zero, render.PeekSize());
        }

        [Test]
        public void IntrinsicSize_IsAlwaysZero()
        {
            var render = Build();

            Assert.AreEqual(0f, render.GetIntrinsicWidth(100f));
            Assert.AreEqual(0f, render.GetIntrinsicHeight(100f));
        }

        /// <summary>
        ///     Each Empty owns its render object. A shared one would carry a single constraints atom
        ///     for the whole app, so laying out one Empty would invalidate the layout of every other.
        /// </summary>
        [Test]
        public void EachEmptyOwnsItsOwnRenderObject()
        {
            var first = TestHarness.Mount(new UniMob.UI.Widgets.Empty());
            var second = TestHarness.Mount(new UniMob.UI.Widgets.Empty());

            Assert.AreNotSame(first.RenderObject, second.RenderObject);
        }
    }
}

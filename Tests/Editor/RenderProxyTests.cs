using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class RenderProxyTests
    {
        [Test]
        public void WithChild_SizesToChildSize_ConstraintsPassThroughUnchanged()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(30, 40) });
            var state = new FakeSingleChildLayoutState { Child = child };

            var proxy = new RenderProxy(state);
            proxy.Layout(LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(new Vector2(30, 40), proxy.Size);
            Assert.AreEqual(Vector2.zero, proxy.ChildPosition);
        }

        [Test]
        public void WithoutChild_CollapsesToSmallestAllowedByConstraints()
        {
            var state = new FakeSingleChildLayoutState { Child = null };

            var proxy = new RenderProxy(state);
            proxy.Layout(new LayoutConstraints(10, 20, 100, 100));

            Assert.AreEqual(new Vector2(10, 20), proxy.Size);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class MeasuredRenderProxyTests
    {
        [Test]
        public void OnSize_FiresOnce_ThenNotAgainForTheSameResolvedSize()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(30, 40) });
            var state = new FakeSingleChildLayoutState { Child = child };
            var reported = new List<Vector2>();

            var proxy = new MeasuredRenderProxy(state, reported.Add);

            proxy.Layout(LayoutConstraints.Loose(100, 100));
            proxy.Layout(LayoutConstraints.Loose(100, 100)); // same resolved size again

            CollectionAssert.AreEqual(new[] { new Vector2(30, 40) }, reported);
        }

        [Test]
        public void OnSize_FiresAgain_WhenResolvedSizeActuallyChanges()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(30, 40) });
            var state = new FakeSingleChildLayoutState { Child = child };
            var reported = new List<Vector2>();

            var proxy = new MeasuredRenderProxy(state, reported.Add);

            proxy.Layout(LayoutConstraints.Loose(100, 100));
            // Tighten the box below the child's natural size so the resolved size actually shrinks.
            proxy.Layout(new LayoutConstraints(0, 0, 10, 10));

            CollectionAssert.AreEqual(new[] { new Vector2(30, 40), new Vector2(10, 10) }, reported);
        }
    }
}

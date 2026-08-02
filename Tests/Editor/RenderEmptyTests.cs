using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class RenderEmptyTests
    {
        [Test]
        public void AlwaysSizesToZero_RegardlessOfConstraints()
        {
            RenderEmpty.Shared.Layout(new LayoutConstraints(20, 30, 100, 100));

            // Documents actual behavior: PerformSizing returns Vector2.zero unconditionally, without
            // running it through constraints.Constrain() -- so a nonzero MinWidth/MinHeight is not honored.
            Assert.AreEqual(Vector2.zero, RenderEmpty.Shared.Size);
        }

        [Test]
        public void IntrinsicSize_IsAlwaysZero()
        {
            Assert.AreEqual(0f, RenderEmpty.Shared.GetIntrinsicWidth(100f));
            Assert.AreEqual(0f, RenderEmpty.Shared.GetIntrinsicHeight(100f));
        }
    }
}

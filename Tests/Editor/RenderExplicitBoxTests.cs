using System;
using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class RenderExplicitBoxTests
    {
        [Test]
        public void AxisSizeMin_SizesToLeafConstraintsMinimum()
        {
            var box = new RenderExplicitBox(LayoutConstraints.Loose(50, 30), AxisSize.Min);

            box.PerformLayoutImmediate(LayoutConstraints.Loose(1000, 1000));

            Assert.AreEqual(new Vector2(0, 0), box.Size);
        }

        [Test]
        public void AxisSizeMax_SizesToLeafConstraintsMaximum()
        {
            var box = new RenderExplicitBox(LayoutConstraints.Tight(50, 30), AxisSize.Max);

            box.PerformLayoutImmediate(LayoutConstraints.Loose(1000, 1000));

            Assert.AreEqual(new Vector2(50, 30), box.Size);
        }

        [Test]
        public void AxisSizeMax_WithUnboundedConstraints_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new RenderExplicitBox(LayoutConstraints.Unbounded(), AxisSize.Max));
        }

        [Test]
        public void IntrinsicSize_ReflectsAxisSize()
        {
            var minBox = new RenderExplicitBox(new LayoutConstraints(10, 20, 50, 60), AxisSize.Min);
            var maxBox = new RenderExplicitBox(new LayoutConstraints(10, 20, 50, 60), AxisSize.Max);

            Assert.AreEqual(10f, minBox.GetIntrinsicWidth(0f));
            Assert.AreEqual(20f, minBox.GetIntrinsicHeight(0f));
            Assert.AreEqual(50f, maxBox.GetIntrinsicWidth(0f));
            Assert.AreEqual(60f, maxBox.GetIntrinsicHeight(0f));
        }
    }
}

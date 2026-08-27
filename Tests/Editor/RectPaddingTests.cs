using NUnit.Framework;

namespace UniMob.UI.Tests
{
    public class RectPaddingTests
    {
        // Every value distinct: a factory that transposes two sides passes any test whose
        // arguments are symmetric.
        [Test]
        public void Only_AssignsEachSideToTheSideItNames()
        {
            var padding = RectPadding.Only(left: 1, right: 2, top: 3, bottom: 4);

            Assert.AreEqual(1f, padding.Left, "Left");
            Assert.AreEqual(2f, padding.Right, "Right");
            Assert.AreEqual(3f, padding.Top, "Top");
            Assert.AreEqual(4f, padding.Bottom, "Bottom");
        }

        [Test]
        public void Only_LeavesUnnamedSidesAtZero()
        {
            var padding = RectPadding.Only(right: 396, bottom: 36);

            Assert.AreEqual(0f, padding.Left, "Left");
            Assert.AreEqual(396f, padding.Right, "Right");
            Assert.AreEqual(0f, padding.Top, "Top");
            Assert.AreEqual(36f, padding.Bottom, "Bottom");
        }

        [Test]
        public void FromLTRB_AssignsSidesInLeftTopRightBottomOrder()
        {
            var padding = RectPadding.FromLTRB(1, 2, 3, 4);

            Assert.AreEqual(1f, padding.Left, "Left");
            Assert.AreEqual(2f, padding.Top, "Top");
            Assert.AreEqual(3f, padding.Right, "Right");
            Assert.AreEqual(4f, padding.Bottom, "Bottom");
        }

        [Test]
        public void Symmetric_MirrorsHorizontalAndVerticalOntoOpposingSides()
        {
            var padding = RectPadding.Symmetric(horizontal: 5, vertical: 9);

            Assert.AreEqual(5f, padding.Left, "Left");
            Assert.AreEqual(5f, padding.Right, "Right");
            Assert.AreEqual(9f, padding.Top, "Top");
            Assert.AreEqual(9f, padding.Bottom, "Bottom");
        }

        [Test]
        public void HorizontalAndVertical_SumOpposingSides()
        {
            var padding = RectPadding.Only(left: 1, right: 2, top: 3, bottom: 4);

            Assert.AreEqual(3f, padding.Horizontal, "Horizontal");
            Assert.AreEqual(7f, padding.Vertical, "Vertical");
        }
    }
}

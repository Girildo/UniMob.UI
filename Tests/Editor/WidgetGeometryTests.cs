using NUnit.Framework;
using UniMob.UI.Layout;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // Pure value-type coverage for the geometry snapshot: no Unity view or canvas involved. Verifies the
    // canvas-space AABB, alignment resolution (GetPoint), corner ordering, and value equality that the
    // reactive GlobalGeometry atom relies on to suppress no-op updates.
    public class WidgetGeometryTests
    {
        // Builds an axis-aligned geometry from a canvas-space rect (world == canvas for these tests).
        private static WidgetGeometry FromRect(Rect canvasRect)
        {
            var quad = new Quad(
                bottomLeft: new Vector2(canvasRect.xMin, canvasRect.yMin),
                topLeft: new Vector2(canvasRect.xMin, canvasRect.yMax),
                topRight: new Vector2(canvasRect.xMax, canvasRect.yMax),
                bottomRight: new Vector2(canvasRect.xMax, canvasRect.yMin));

            return new WidgetGeometry(quad, quad);
        }

        [Test]
        public void Empty_IsDefaultAndZeroSized()
        {
            Assert.AreEqual(default(WidgetGeometry), WidgetGeometry.Empty);
            Assert.AreEqual(Vector2.zero, WidgetGeometry.Empty.Size);
        }

        [Test]
        public void Aabb_And_Size_MatchCanvasCorners()
        {
            var geo = FromRect(new Rect(10, 20, 100, 50));

            Assert.AreEqual(new Rect(10, 20, 100, 50), geo.Aabb);
            Assert.AreEqual(new Vector2(100, 50), geo.Size);
        }

        [Test]
        public void GetPoint_ResolvesAlignmentsInCanvasSpace()
        {
            // BL=(10,20), TR=(110,70), center=(60,45).
            var geo = FromRect(new Rect(10, 20, 100, 50));

            Assert.AreEqual(new Vector2(60, 45), geo.GetPoint(Alignment.Center));
            Assert.AreEqual(new Vector2(10, 20), geo.GetPoint(Alignment.BottomLeft));
            Assert.AreEqual(new Vector2(110, 70), geo.GetPoint(Alignment.TopRight));
            Assert.AreEqual(new Vector2(60, 70), geo.GetPoint(Alignment.TopCenter));
            Assert.AreEqual(new Vector2(60, 20), geo.GetPoint(Alignment.BottomCenter));
        }

        [Test]
        public void Quad_IndexerFollowsWorldCornerOrder()
        {
            var quad = new Quad(new Vector2(1, 2), new Vector2(3, 4), new Vector2(5, 6), new Vector2(7, 8));

            Assert.AreEqual(new Vector2(1, 2), quad[0], "index 0 is bottom-left");
            Assert.AreEqual(new Vector2(3, 4), quad[1], "index 1 is top-left");
            Assert.AreEqual(new Vector2(5, 6), quad[2], "index 2 is top-right");
            Assert.AreEqual(new Vector2(7, 8), quad[3], "index 3 is bottom-right");
        }

        [Test]
        public void Equality_IsByCanvasCorners()
        {
            var a = FromRect(new Rect(0, 0, 10, 10));
            var b = FromRect(new Rect(0, 0, 10, 10));
            var c = FromRect(new Rect(0, 0, 10, 11));

            Assert.IsTrue(a == b);
            Assert.AreEqual(a, b);
            Assert.IsTrue(a != c);
            Assert.AreNotEqual(a, c);
        }
    }
}

using NUnit.Framework;
using UniMob.UI.Editor;
using UnityEngine;
using Node = UniMob.UI.Editor.WidgetTreeSnapshot.Node;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     What a point in the running app resolves to, which is what the picker selects.
    /// </summary>
    /// <remarks>
    ///     Every one of these is a rule about overlapping boxes, and the reason they are worth writing
    ///     down is that each reads as obviously correct and three of them were wrong in the app. Boxes
    ///     nest, siblings overlap, and a container is routinely larger than anything you can see of it.
    ///     <para>
    ///         The geometric path on purpose. A raycast needs a live <c>EventSystem</c> and a scene that
    ///         paints, neither of which exists here -- and it is the boxes that carry the rules, since
    ///         the raycast side is uGUI's answer rather than ours.
    ///     </para>
    /// </remarks>
    public class WidgetTreeSnapshotTests
    {
        private GameObject _canvasGo;

        [SetUp]
        public void SetUp()
        {
            _canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            _canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [TearDown]
        public void TearDown()
        {
            // Every box is parented to the canvas, so this takes the whole fixture with it.
            Object.DestroyImmediate(_canvasGo);
        }

        /// <summary>A node with a real box on screen, in canvas units from the bottom-left.</summary>
        private Node Box(Rect rect, params Node[] children)
        {
            var go = new GameObject("Box", typeof(RectTransform));
            var transform = (RectTransform)go.transform;
            transform.SetParent(_canvasGo.transform, worldPositionStays: false);
            transform.anchorMin = transform.anchorMax = Vector2.zero;
            transform.pivot = Vector2.zero;
            transform.sizeDelta = rect.size;
            transform.anchoredPosition = rect.position;

            var node = new Node { Target = go };
            node.Children.AddRange(children);
            return node;
        }

        /// <summary>A node with no view of its own -- a wrapper, most of any real tree.</summary>
        private static Node BuildOnly(params Node[] children)
        {
            var node = new Node();
            node.Children.AddRange(children);
            return node;
        }

        private static WidgetTreeSnapshot Snapshot(params Node[] roots)
        {
            var snapshot = new WidgetTreeSnapshot();
            snapshot.Roots.AddRange(roots);
            return snapshot;
        }

        private static Node HitTest(WidgetTreeSnapshot snapshot, Vector2 point) =>
            snapshot.HitTest(point, geometric: true);

        // The drawer, and the bug this replaced: ranking by depth picks whichever subtree happens to
        // nest more deeply, and a drawer is a shallow branch laid over a deep one.
        [Test]
        public void HitTest_PrefersTheTopmostLeaf_OverADeeperOneBehindIt()
        {
            var behind = Box(new Rect(10, 10, 100, 100));
            var page = Box(
                new Rect(0, 0, 400, 400),
                BuildOnly(Box(new Rect(0, 0, 200, 200), BuildOnly(behind)))
            );

            var drawer = Box(new Rect(0, 0, 200, 200));

            var hit = HitTest(Snapshot(BuildOnly(page, drawer)), new Vector2(50, 50));

            Assert.AreSame(drawer, hit, "the later sibling is drawn over the earlier one");
        }

        // The full-screen positioning container. An Align with no size factor asks for infinity on
        // both axes, so one placing a 120px rail is a screen-sized box sitting after the page -- and
        // ranking by paint order alone hands it every point in the app.
        [Test]
        public void HitTest_PassesThroughAContainer_WhoseChildrenAllMissThePoint()
        {
            var content = Box(new Rect(0, 0, 400, 400));
            var rail = Box(new Rect(0, 0, 40, 400));
            var fullScreenAlign = Box(new Rect(0, 0, 400, 400), rail);

            var hit = HitTest(Snapshot(BuildOnly(content, fullScreenAlign)), new Vector2(200, 200));

            Assert.AreSame(content, hit, "a container claiming a point no child of it wants");
        }

        [Test]
        public void HitTest_ReachesTheContainer_WhereTheChildDoesWantThePoint()
        {
            var rail = Box(new Rect(0, 0, 40, 400));
            var fullScreenAlign = Box(new Rect(0, 0, 400, 400), rail);
            var content = Box(new Rect(0, 0, 400, 400));

            var hit = HitTest(Snapshot(BuildOnly(content, fullScreenAlign)), new Vector2(20, 200));

            Assert.AreSame(rail, hit);
        }

        // Passing through must not make a point unanswerable: a gap inside a container still resolves,
        // to the same node this window gave before any of this.
        [Test]
        public void HitTest_FallsBackToTheDeepestContainingNode_WhenNothingBottomsOutInALeaf()
        {
            var elsewhere = Box(new Rect(300, 300, 20, 20));
            var outer = Box(new Rect(0, 0, 400, 400), BuildOnly(elsewhere));

            var hit = HitTest(Snapshot(outer), new Vector2(50, 50));

            Assert.AreSame(outer, hit);
        }

        // Nothing is pruned on a parent's box, because a positioned child or an anchored box sits
        // outside its parent on purpose -- and pruning would make exactly those unpickable.
        [Test]
        public void HitTest_FindsAChildDrawnOutsideItsParent()
        {
            var escapee = Box(new Rect(200, 200, 50, 50));
            var parent = Box(new Rect(0, 0, 50, 50), escapee);

            var hit = HitTest(Snapshot(parent), new Vector2(220, 220));

            Assert.AreSame(escapee, hit);
        }

        [Test]
        public void HitTest_FindsNothing_WhereNothingIs()
        {
            var snapshot = Snapshot(Box(new Rect(0, 0, 50, 50)));

            Assert.IsNull(HitTest(snapshot, new Vector2(300, 300)));
        }

        [Test]
        public void HitTest_IgnoresABuildOnlyNode_WhichHasNoBoxToContainAnything()
        {
            var leaf = Box(new Rect(0, 0, 100, 100));

            var hit = HitTest(Snapshot(BuildOnly(BuildOnly(leaf))), new Vector2(50, 50));

            Assert.AreSame(leaf, hit);
        }

        // A wrapper's extent is the union of what it renders, which is what the highlight has to draw
        // for the majority of the tree -- every HocState and every proxy owner has no box of its own.
        [Test]
        public void ScreenRectOf_OfABuildOnlyNode_IsTheUnionOfItsChildren()
        {
            var node = BuildOnly(Box(new Rect(10, 10, 20, 20)), Box(new Rect(100, 100, 20, 20)));

            var rect = WidgetTreeSnapshot.ScreenRectOf(node);

            Assert.AreEqual(10f, rect.xMin, 0.01f);
            Assert.AreEqual(10f, rect.yMin, 0.01f);
            Assert.AreEqual(120f, rect.xMax, 0.01f);
            Assert.AreEqual(120f, rect.yMax, 0.01f);
        }

        [Test]
        public void ScreenRectOf_OfNothing_IsEmpty()
        {
            Assert.AreEqual(Rect.zero, WidgetTreeSnapshot.ScreenRectOf(null));
        }
    }
}

using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Behaviour of a layout subtree sitting under build-only wrappers -- states that build a child
    ///     but own no render object of their own (<see cref="HocState{TWidget}"/> and the element behind
    ///     <see cref="StatelessWidget"/>).
    /// </summary>
    /// <remarks>
    ///     The render-object fixtures drive layout by calling the render object directly, so they are
    ///     blind to who scheduled a pass and to how constraints got there. Everything asserted here is
    ///     invisible to them, and all of it is about the reactive layer rather than layout arithmetic.
    ///     <para>
    ///         Assertions are deliberately phrased against what a sizing pass was <em>told</em> and what
    ///         it <em>produced</em>, never against the accessor a wrapper happens to expose, so they
    ///         survive a change of ownership of the layout atom.
    ///     </para>
    /// </remarks>
    public class WrapperLayoutTests
    {
        private static readonly Vector2 LeafSize = new Vector2(30, 40);

        /// <summary>Minimal StatelessWidget: contributes an element level and nothing else.</summary>
        private sealed class PassThrough : StatelessWidget
        {
            public Widget Child { get; set; }

            public override Widget Build(BuildContext context) => Child;
        }

        private static Widget Hoc(Widget child) => new Builder(_ => child);

        private static Widget Stateless(Widget child) => new PassThrough { Child = child };

        private static CountingBox Leaf() => new CountingBox { BoxSize = LeafSize };

        // A build-only wrapper owns a proxy over its child, so the leaf's render object sits at the
        // bottom of the chain rather than at the root. InnerViewState already walks to the widget that
        // actually renders, which is the counting box in every tree here.
        private static RenderCountingBox RenderOf(State root) =>
            (RenderCountingBox)root.InnerViewState.RenderObject;

        // == Tranche 1: passes today, must still pass ==============================================
        //
        // Constraints written at a root reach the leaf under any number of build-only wrappers, and a
        // widget change triggers re-layout. This is the behaviour-preservation signal the render-object
        // fixtures structurally cannot give, and it is what de-risks reworking how wrappers are driven.

        private static void AssertConstraintsReachLeaf(Widget tree)
        {
            var root = TestHarness.Mount(tree);
            var constraints = LayoutConstraints.Tight(80, 60);

            TestHarness.DriveFrame(root, constraints);

            Assert.AreEqual(
                constraints,
                RenderOf(root).LastConstraints,
                "The leaf's sizing pass must run against the constraints written at the root."
            );
            Assert.AreEqual(new Vector2(80, 60), root.RenderObject.Size);
        }

        [Test]
        public void ConstraintsReachLeaf_Bare()
        {
            AssertConstraintsReachLeaf(Leaf());
        }

        [Test]
        public void ConstraintsReachLeaf_ThroughStatelessWidget()
        {
            AssertConstraintsReachLeaf(Stateless(Leaf()));
        }

        [Test]
        public void ConstraintsReachLeaf_ThroughHocState()
        {
            AssertConstraintsReachLeaf(Hoc(Leaf()));
        }

        [Test]
        public void ConstraintsReachLeaf_ThroughNestedBuildOnlyWrappers()
        {
            AssertConstraintsReachLeaf(Hoc(Stateless(Hoc(Leaf()))));
        }

        [Test]
        public void ConstraintChangesReachLeaf_ThroughBuildOnlyWrappers()
        {
            var root = TestHarness.Mount(Hoc(Stateless(Leaf())));

            TestHarness.DriveFrame(root, LayoutConstraints.Tight(80, 60));
            TestHarness.DriveFrame(root, LayoutConstraints.Tight(20, 25));

            Assert.AreEqual(LayoutConstraints.Tight(20, 25), RenderOf(root).LastConstraints);
            Assert.AreEqual(new Vector2(20, 25), root.RenderObject.Size);
        }

        /// <summary>
        ///     A replacement child is built between two pushes, so nothing hands it constraints
        ///     directly. Whatever route delivers constraints has to serve a subtree built after the
        ///     fact, not just one that was present when the push happened.
        /// </summary>
        [Test]
        public void ConstraintsReachAChildBuiltAfterTheConstraintsWereSet()
        {
            var childKey = Atom.Value(Key.Of("first"));

            // The atom must be read inside the builder: handing Hoc() a prebuilt widget would return
            // the same instance on every build and nothing would ever rebuild.
            var root = TestHarness.Mount(
                new Builder(_ => new CountingBox { Key = childKey.Value, BoxSize = LeafSize })
            );
            var constraints = LayoutConstraints.Tight(80, 60);

            TestHarness.DriveFrame(root, constraints);
            // The wrapper's own proxy survives a child rebuild, so the check looks at the leaf.
            var firstRenderObject = root.InnerViewState.RenderObject;

            // A new Key makes the child irreconcilable, so it is discarded and rebuilt from scratch.
            childKey.Value = Key.Of("second");
            TestHarness.DriveFrame(root, constraints);

            Assert.AreNotSame(
                firstRenderObject,
                root.InnerViewState.RenderObject,
                "The child should have been rebuilt."
            );
            Assert.AreEqual(constraints, RenderOf(root).LastConstraints);
            Assert.AreEqual(new Vector2(80, 60), root.RenderObject.Size);
        }

        /// <summary>Changing a widget property must re-run layout rather than serve a stale size.</summary>
        [Test]
        public void WidgetChangeTriggersRelayout_ThroughBuildOnlyWrappers()
        {
            var size = Atom.Value(new Vector2(30, 40));
            var root = TestHarness.Mount(
                new Builder(_ => new CountingBox { BoxSize = size.Value })
            );
            var constraints = LayoutConstraints.Loose(100, 100);

            TestHarness.DriveFrame(root, constraints);
            Assert.AreEqual(new Vector2(30, 40), root.RenderObject.Size);

            size.Value = new Vector2(55, 65);
            TestHarness.DriveFrame(root, constraints);

            Assert.AreEqual(new Vector2(55, 65), root.RenderObject.Size);
        }

        // == Tranche 2: fails today, must flip =====================================================
        //
        // A render object must be laid out exactly once per frame. A build-only wrapper exposes its
        // child's render object, so today that object is reachable from two states and is driven by
        // both -- once via the parent's push onto the wrapper, once via the view pass on the leaf.
        // Box layout is idempotent, which is the only reason this has stayed invisible; a render
        // object that accumulates across passes instead of recomputing applies its update twice.

        [Test]
        public void SizingRunsOncePerFrame_Bare()
        {
            var root = TestHarness.Mount(Leaf());

            TestHarness.DriveFrame(root, LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(1, RenderOf(root).SizingPasses);
        }

        [Test]
        public void SizingRunsOncePerFrame_UnderStatelessWidget()
        {
            var root = TestHarness.Mount(Stateless(Leaf()));

            TestHarness.DriveFrame(root, LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(1, RenderOf(root).SizingPasses);
        }

        [Test]
        public void SizingRunsOncePerFrame_UnderHocState()
        {
            var root = TestHarness.Mount(Hoc(Leaf()));

            TestHarness.DriveFrame(root, LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(1, RenderOf(root).SizingPasses);
        }

        [Test]
        public void SizingRunsOncePerFrame_UnderNestedBuildOnlyWrappers()
        {
            var root = TestHarness.Mount(Hoc(Stateless(Hoc(Leaf()))));

            TestHarness.DriveFrame(root, LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(1, RenderOf(root).SizingPasses);
        }

        /// <summary>A second frame with nothing changed must not lay out again at all.</summary>
        [Test]
        public void SizingIsNotRepeated_WhenNothingChanged()
        {
            var root = TestHarness.Mount(Hoc(Leaf()));
            var constraints = LayoutConstraints.Loose(100, 100);

            TestHarness.DriveFrame(root, constraints);
            TestHarness.DriveFrame(root, constraints);

            Assert.AreEqual(1, RenderOf(root).SizingPasses);
        }

        [Test]
        public void SizingRunsOncePerConstraintChange()
        {
            var root = TestHarness.Mount(Hoc(Leaf()));

            TestHarness.DriveFrame(root, LayoutConstraints.Loose(100, 100));
            TestHarness.DriveFrame(root, LayoutConstraints.Loose(50, 50));

            Assert.AreEqual(2, RenderOf(root).SizingPasses);
        }
    }
}

using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // Covers RenderObject's own base-class contract, independent of any specific subclass -- notably
    // not otherwise exercised directly anywhere else (individual RenderObjects mostly assume they're
    // never called with a disposed lifetime in the first place).
    public class RenderObjectTests
    {
        // A render object now names the state it belongs to rather than a bare lifetime, so this is
        // the smallest thing that can own one: a state whose lifetime the test controls, and which
        // throws on everything else it is not supposed to be asked for.
        private class OwnerState : FakeState
        {
            public Lifetime? FakeLifetime { get; set; }

            public override Lifetime StateLifetime => FakeLifetime ?? Lifetime.Eternal;
        }

        private class CountingRenderObject : RenderObject
        {
            public int SizingCalls;
            public int PositioningCalls;
            public Vector2 LastPositioningSize;

            public CountingRenderObject(Lifetime lifetime)
                : base(new OwnerState { FakeLifetime = lifetime }) { }

            protected override Vector2 PerformSizing(LayoutConstraints constraints)
            {
                SizingCalls++;
                return new Vector2(1, 1);
            }

            protected override void PerformPositioning(Vector2 size)
            {
                PositioningCalls++;
                LastPositioningSize = size;
            }

            protected override float ComputeIntrinsicWidth(float height) => 0f;

            protected override float ComputeIntrinsicHeight(float width) => 0f;
        }

        // A render object owns atoms registered on its lifetime, so it can only be built while that
        // lifetime is alive -- which is also the only way it happens, since CreateRenderObject runs
        // from InflateWidget on a freshly mounted state. The case worth covering is therefore death
        // *after* construction, not construction against something already dead.
        [Test]
        public void Layout_SkipsSizingAndPositioning_WhenLifetimeIsDisposed()
        {
            var controller = new LifetimeController();
            var renderObject = new CountingRenderObject(controller.Lifetime);

            controller.Dispose();
            renderObject.Layout(LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(0, renderObject.SizingCalls);
            Assert.AreEqual(0, renderObject.PositioningCalls);
            Assert.AreEqual(Vector2.zero, renderObject.PeekSize());
        }

        [Test]
        public void Layout_IsMemoized_AcrossRepeatedCallsWithTheSameConstraints()
        {
            var renderObject = new CountingRenderObject(Lifetime.Eternal);
            var constraints = LayoutConstraints.Loose(100, 100);

            renderObject.Layout(constraints);
            renderObject.Layout(constraints);
            renderObject.Layout(constraints);

            Assert.AreEqual(1, renderObject.SizingCalls);
            Assert.AreEqual(1, renderObject.PositioningCalls);
        }

        [Test]
        public void Layout_RunsAgain_WhenConstraintsChange()
        {
            var renderObject = new CountingRenderObject(Lifetime.Eternal);

            renderObject.Layout(LayoutConstraints.Loose(100, 100));
            renderObject.Layout(LayoutConstraints.Loose(50, 50));

            Assert.AreEqual(2, renderObject.SizingCalls);
            Assert.AreEqual(2, renderObject.PositioningCalls);
        }

        [Test]
        public void Constraints_ReportUnsetUntilLaidOut()
        {
            var renderObject = new CountingRenderObject(Lifetime.Eternal);
            Assert.IsNull(renderObject.Constraints);

            renderObject.Layout(LayoutConstraints.Tight(20, 30));

            Assert.AreEqual(LayoutConstraints.Tight(20, 30), renderObject.Constraints);
        }

        [Test]
        public void Layout_RunsSizingThenPositioning_WhenLifetimeIsAlive()
        {
            var renderObject = new CountingRenderObject(Lifetime.Eternal);
            renderObject.Layout(LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(1, renderObject.SizingCalls);
            Assert.AreEqual(1, renderObject.PositioningCalls);
            Assert.AreEqual(new Vector2(1, 1), renderObject.PeekSize());
        }

        // The parameter is the only channel between the two phases: sizing returns a value, the base
        // commits it, and positioning is handed exactly that. If the base ever passed anything else
        // (a stale field, a re-derived value), children would be placed against a box their parent
        // never committed to.
        [Test]
        public void Positioning_ReceivesTheSizeThePassCommitted()
        {
            var renderObject = new CountingRenderObject(Lifetime.Eternal);

            var returned = renderObject.Layout(LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(returned, renderObject.LastPositioningSize);
        }

        // PeekSize exists for readers that must not drive anything: diagnostics and debugger
        // displays evaluated mid-layout. NoWatch alone cannot give that guarantee -- it stops the
        // subscription, but a tracked accessor still recomputes a dirty pass on read, and a report
        // that lays a subtree out mid-report is a fault of its own.
        [Test]
        public void PeekSize_ServesTheMemoWithoutRecomputing()
        {
            var renderObject = new CountingRenderObject(Lifetime.Eternal);
            renderObject.Layout(LayoutConstraints.Loose(100, 100));

            renderObject.InvalidateLayout();
            var peeked = renderObject.PeekSize();

            Assert.AreEqual(1, renderObject.SizingCalls, "a peek must not run the pass");
            Assert.AreEqual(new Vector2(1, 1), peeked);

            Assert.AreEqual(new Vector2(1, 1), renderObject.WatchedSize());
            Assert.AreEqual(2, renderObject.SizingCalls, "a watched read serves the fresh pass");
        }
    }
}

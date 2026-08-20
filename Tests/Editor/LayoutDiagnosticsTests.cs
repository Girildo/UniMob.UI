using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // Diagnostics run inside a sizing pass, which is a tracked computation, and the tree is exactly
    // what a diagnostic wants to read. These cover that hazard rather than the wording of any message.
    public class LayoutDiagnosticsTests
    {
        /// <summary>
        ///     Column &gt; PaddingBox &gt; Column, where the inner column is given an unbounded main axis
        ///     and holds a flexible child, which is what makes it report.
        /// </summary>
        private static (IState root, IState reporter, RenderCountingBox leaf) BuildReportingTree()
        {
            var root = TestHarness.Mount(
                new Column
                {
                    Children =
                    {
                        new PaddingBox
                        {
                            Padding = RectPadding.All(5),
                            Child = new Column
                            {
                                Children =
                                {
                                    new CountingBox { BoxSize = new Vector2(10, 10) },
                                    new Flexible
                                    {
                                        Child = new FixedSizeBox { Size = new Vector2(5, 5) },
                                    },
                                },
                            },
                        },
                    },
                }
            );

            var padding = (ISingleChildLayoutState)((IMultiChildLayoutState)root).Children[0];
            var reporter = padding.Child;
            var leaf = (RenderCountingBox)
                ((IMultiChildLayoutState)reporter).Children[0].RenderObject;

            return (root, reporter, leaf);
        }

        // Describing the tree is the natural thing for a report to do, and the tree is atoms. Read
        // without care, reporting a fault subscribes the reporting render object's layout to every
        // ancestor's constraints, so every later ancestor layout drives it again -- forever, and for
        // a fault that has not changed.
        [Test]
        public void Reporting_LeavesTheReporterUnsubscribedFromItsAncestors()
        {
            using var log = RecordingReporter.Capture();
            var (root, reporter, leaf) = BuildReportingTree();

            // Frame 1: lays the tree out and fires the report, wiring up whatever it reads.
            TestHarness.Layout((State)root, LayoutConstraints.Loose(100, 200));

            using var probeLifetime = new LifetimeController();
            var reporterPasses = 0;
            var probe = Atom.Computed(
                probeLifetime.Lifetime,
                () =>
                {
                    reporterPasses++;
                    return reporter.RenderObject.WatchLayout();
                }
            );
            probe.Get();

            Assert.AreEqual(
                1,
                reporterPasses,
                "probe should have run once against the laid-out tree"
            );
            Assert.AreEqual(1, leaf.SizingPasses, "the leaf should have been sized once");

            // Frame 2: only the root's own main-axis maximum moves. A column measures its inflexible
            // children against an unbounded main axis regardless, so the constraints pushed to the
            // padding -- and through it to the reporter -- are identical to frame 1.
            TestHarness.Layout((State)root, LayoutConstraints.Loose(100, 300));
            probe.Get();

            // The reporter is the whole blast radius, and only because its own children are shielded
            // by the constraint-equality cutoff in Layout. Pinned so a leaf pass count is not mistaken
            // for a guard against this: it reads 1 whether or not the subscription is there.
            Assert.AreEqual(
                1,
                leaf.SizingPasses,
                "the constraint-equality cutoff below the reporter stopped holding"
            );
            Assert.AreEqual(
                1,
                reporterPasses,
                "the reporter was laid out again after an ancestor-only constraint change, so the "
                    + "report subscribed its layout to that ancestor"
            );
        }
    }
}

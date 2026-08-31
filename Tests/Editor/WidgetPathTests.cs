using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Diagnostics;
using UniMob.UI.Internal.Views;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class WidgetPathTests
    {
        // Created but never mounted, so Context.Parent is null: the walk has to stop there rather
        // than dereference it. Named without the usual suffix so the assertion below reads as the
        // path it is checking rather than as the fallback's "State" trim.
        private sealed class Orphan : FakeState
        {
            private readonly BuildContext _context;

            public Orphan() => _context = new BuildContext(this, null);

            public override Key Key => null;
            public override BuildContext Context => _context;
            public override RenderObject RenderObject => null;
        }

        private static IState MountChain(Widget leaf)
        {
            var root = TestHarness.Mount(
                new Column
                {
                    Children =
                    {
                        new PaddingBox { Padding = RectPadding.All(5), Child = leaf },
                    },
                }
            );

            var padding = (ISingleChildLayoutState)((IMultiChildLayoutState)root).Children[0];
            return padding.Child;
        }

        [Test]
        public void From_ReadsRootFirst_DownToTheStateItWasAskedAbout()
        {
            var leaf = MountChain(new FixedSizeBox());

            Assert.AreEqual(
                "Column > PaddingBox \"RectPadding: 5\" > FixedSizeBox",
                WidgetPath.From(leaf)
            );
        }

        [Test]
        public void From_MarksTruncation_RatherThanSilentlyShortening()
        {
            var leaf = MountChain(new FixedSizeBox());

            Assert.AreEqual(
                "... > PaddingBox \"RectPadding: 5\" > FixedSizeBox",
                WidgetPath.From(leaf, maxDepth: 2)
            );
        }

        [Test]
        public void From_StopsAtAnUnmountedState_InsteadOfThrowing()
        {
            Assert.AreEqual("Orphan", WidgetPath.From(new Orphan()));
        }

        [Test]
        public void From_RendersNothing_AsNull()
        {
            Assert.AreEqual("<null>", WidgetPath.From(null));
        }

        // The A1 regression, one level up. Describing a node runs the author's label, which is
        // allowed to read atoms; if the walk registered those reads, reporting a fault would make
        // whoever reported it depend on whatever the labels happened to look at.
        [Test]
        public void From_RegistersNoDependency_OnWhatAWidgetsLabelReads()
        {
            var live = Atom.Value(1);
            var leaf = MountChain(new LabelledBox { LiveLabel = () => $"count={live.Value}" });

            using var lifetime = new LifetimeController();
            var walks = 0;
            var probe = Atom.Computed(
                lifetime.Lifetime,
                () =>
                {
                    walks++;
                    return WidgetPath.From(leaf);
                }
            );

            Assert.AreEqual(
                "Column > PaddingBox \"RectPadding: 5\" > LabelledBox \"count=1\"",
                probe.Get()
            );

            live.Value = 2;
            probe.Get();

            Assert.AreEqual(1, walks, "the walk subscribed to an atom a label happened to read");
        }
    }
}

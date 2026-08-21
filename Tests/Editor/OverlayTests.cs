using System;
using System.Linq;
using NUnit.Framework;
using UniMob.UI.Internal;
using UniMob.UI.Internal.Views;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     What an <see cref="Overlay"/> promises its entries: a stable position in paint order, a
    ///     full-bleed box to place themselves in, independence from each other, and a removal that
    ///     cannot silently fail.
    /// </summary>
    /// <remarks>
    ///     Layout is driven directly rather than through frames. Every property here is a consequence
    ///     of one layout pass over the layer, and a clock would only add a way for the assertion to be
    ///     reached at the wrong moment.
    /// </remarks>
    public class OverlayTests
    {
        private static readonly LayoutConstraints Screen = LayoutConstraints.Tight(800, 600);

        private static OverlayState MountOverlay() =>
            (OverlayState)TestHarness.Mount(new Overlay());

        /// <summary>
        ///     A mounted state to insert on behalf of, with a lifetime a test can end. Real rather than
        ///     faked: the ownership rule is about a state being disposed, which is the one thing a fake
        ///     lifetime would not reproduce.
        /// </summary>
        private static State MountInserter() =>
            TestHarness.Mount(new FixedSizeBox { Size = Vector2.zero });

        private static Widget AnyContent() => new FixedSizeBox { Size = new Vector2(10, 10) };

        // The leaf under the entry's own build-only Builder wrapper.
        private static RenderCountingBox CountingRenderOf(IState entryState) =>
            (RenderCountingBox)entryState.InnerViewState.RenderObject;

        [Test]
        public void InsertionOrderIsPaintOrder()
        {
            var overlay = MountOverlay();
            var inserter = MountInserter();

            var first = overlay.Insert(inserter.Context, _ => AnyContent());
            var second = overlay.Insert(inserter.Context, _ => AnyContent());
            var third = overlay.Insert(inserter.Context, _ => AnyContent());

            CollectionAssert.AreEqual(
                new[] { Key.Of(first), Key.Of(second), Key.Of(third) },
                overlay.Children.Select(child => child.Key)
            );
        }

        [Test]
        public void TheChildIsTheBottomLayer()
        {
            var overlay = (OverlayState)
                TestHarness.Mount(
                    new Overlay { Child = new FixedSizeBox { Size = new Vector2(5, 5) } }
                );
            var inserter = MountInserter();

            var entry = overlay.Insert(inserter.Context, _ => AnyContent());

            Assert.AreEqual(2, overlay.Children.Length);
            Assert.IsNull(overlay.Children[0].Key, "the child holds position zero unkeyed");
            Assert.AreEqual(Key.Of(entry), overlay.Children[1].Key);
        }

        [Test]
        public void RemovingTheLastEntry_LeavesTheChildAlone()
        {
            var overlay = (OverlayState)
                TestHarness.Mount(
                    new Overlay { Child = new FixedSizeBox { Size = new Vector2(5, 5) } }
                );
            var inserter = MountInserter();

            var entry = overlay.Insert(inserter.Context, _ => AnyContent());
            var childState = overlay.Children[0];

            entry.Remove();

            Assert.AreEqual(1, overlay.Children.Length);
            Assert.AreSame(childState, overlay.Children[0]);
            Assert.IsFalse(childState.StateLifetime.IsDisposed);
        }

        [Test]
        public void RemovingAMiddleEntry_LeavesTheOrderOfTheRest()
        {
            var overlay = MountOverlay();
            var inserter = MountInserter();

            var first = overlay.Insert(inserter.Context, _ => AnyContent());
            var second = overlay.Insert(inserter.Context, _ => AnyContent());
            var third = overlay.Insert(inserter.Context, _ => AnyContent());

            second.Remove();

            CollectionAssert.AreEqual(
                new[] { Key.Of(first), Key.Of(third) },
                overlay.Children.Select(child => child.Key)
            );
        }

        /// <summary>
        ///     The stable-key requirement, stated as what it protects. Reconciliation matches by key and
        ///     runtime type, so entries keyed only by position would make removing the middle one tear
        ///     down the last one and rebuild it in the middle slot.
        /// </summary>
        [Test]
        public void RemovingAMiddleEntry_LeavesItsSiblingsStatesIntact()
        {
            var overlay = MountOverlay();
            var inserter = MountInserter();

            overlay.Insert(inserter.Context, _ => AnyContent());
            var second = overlay.Insert(inserter.Context, _ => AnyContent());
            overlay.Insert(inserter.Context, _ => AnyContent());

            var before = overlay.Children;
            var firstState = before[0];
            var thirdState = before[2];

            second.Remove();

            var after = overlay.Children;
            Assert.AreSame(firstState, after[0]);
            Assert.AreSame(thirdState, after[1]);
            Assert.IsFalse(thirdState.StateLifetime.IsDisposed);
        }

        [Test]
        public void AnEntryRebuildsOnItsOwnAtoms_WithoutRebuildingItsSiblings()
        {
            var overlay = MountOverlay();
            var inserter = MountInserter();

            var watched = Atom.Value(0);
            var watchedBuilds = 0;
            var quietBuilds = 0;

            overlay.Insert(
                inserter.Context,
                _ =>
                {
                    watchedBuilds++;
                    watched.Get();
                    return AnyContent();
                }
            );
            overlay.Insert(
                inserter.Context,
                _ =>
                {
                    quietBuilds++;
                    return AnyContent();
                }
            );

            TestHarness.Layout(overlay, Screen);
            Assert.AreEqual(1, watchedBuilds, "the watched entry should have built once");
            Assert.AreEqual(1, quietBuilds, "the quiet entry should have built once");

            watched.Value = 1;
            TestHarness.Layout(overlay, Screen);

            Assert.AreEqual(2, watchedBuilds, "the watched entry should have rebuilt");
            Assert.AreEqual(1, quietBuilds, "the quiet entry should not have rebuilt");
        }

        /// <summary>
        ///     Tight and full-bleed, which is the opposite of the obvious guess and the reason
        ///     <c>AnchoredBox</c> works inside an entry: it measures as zero under a parent that
        ///     stretches instead of constraining.
        /// </summary>
        [Test]
        public void AnEntryIsLaidOutTightToTheLayer()
        {
            var overlay = MountOverlay();
            var inserter = MountInserter();

            overlay.Insert(
                inserter.Context,
                _ => new CountingBox { BoxSize = new Vector2(10, 10) }
            );

            TestHarness.Layout(overlay, Screen);

            var constraints = CountingRenderOf(overlay.Children[0]).LastConstraints;
            Assert.IsTrue(constraints.IsTight, "an entry is laid out tight, not loose");
            Assert.AreEqual(800, constraints.MaxWidth);
            Assert.AreEqual(600, constraints.MaxHeight);
        }

        [Test]
        public void EveryEntryFillsTheLayer_WhateverItsContentMeasures()
        {
            var overlay = MountOverlay();
            var inserter = MountInserter();

            overlay.Insert(inserter.Context, _ => new FixedSizeBox { Size = new Vector2(10, 10) });
            overlay.Insert(inserter.Context, _ => new FixedSizeBox { Size = new Vector2(20, 40) });

            var size = TestHarness.Layout(overlay, Screen);
            var render = (RenderOverlay)overlay.RenderObject;

            Assert.AreEqual(new Vector2(800, 600), size);
            CollectionAssert.AreEqual(
                new[] { new Vector2(800, 600), new Vector2(800, 600) },
                render.ChildrenLayout.Select(layout => layout.Size)
            );
            CollectionAssert.AreEqual(
                new[] { Vector2.zero, Vector2.zero },
                render.ChildrenLayout.Select(layout => layout.Position)
            );
        }

        /// <summary>
        ///     The hit policy, stated where it is actually decided. The layer's view is a transform and
        ///     a mapper with nothing that paints, so it installs no raycast target -- which is what
        ///     lets an entry that draws only an anchored child leave the rest of the layer alone. An
        ///     entry that means to block includes its own full-bleed <c>GestureDetector</c>.
        /// </summary>
        [Test]
        public void TheLayerPaintsNothingOfItsOwn()
        {
            var overlay = MountOverlay();

            var factory = typeof(Overlay)
                .Assembly.GetCustomAttributes(
                    typeof(RegisterComponentViewFactoryAttribute),
                    inherit: false
                )
                .Cast<RegisterComponentViewFactoryAttribute>()
                .Single(attribute => attribute.Name == overlay.View.Path);

            CollectionAssert.AreEquivalent(
                new[] { typeof(RectTransform), typeof(MultiChildLayoutView) },
                factory.Components
            );
        }

        [Test]
        public void RemoveIsIdempotent()
        {
            var overlay = MountOverlay();
            var inserter = MountInserter();

            var entry = overlay.Insert(inserter.Context, _ => AnyContent());

            entry.Remove();
            entry.Remove();

            Assert.AreEqual(0, overlay.Count);
            Assert.IsFalse(entry.IsMounted);
        }

        [Test]
        public void RemoveDoesNotDismiss()
        {
            var overlay = MountOverlay();
            var inserter = MountInserter();

            var dismissals = 0;
            var entry = overlay.Insert(
                inserter.Context,
                _ => AnyContent(),
                onDismissed: () => dismissals++
            );

            entry.Remove();

            Assert.AreEqual(0, dismissals, "the owner asked for the removal, so nobody is told");
        }

        [Test]
        public void DismissTellsTheOwnerExactlyOnce()
        {
            var overlay = MountOverlay();
            var inserter = MountInserter();

            var dismissals = 0;
            var entry = overlay.Insert(
                inserter.Context,
                _ => AnyContent(),
                onDismissed: () => dismissals++
            );

            entry.Dismiss();
            entry.Dismiss();

            Assert.AreEqual(1, dismissals);
            Assert.AreEqual(0, overlay.Count);
        }

        [Test]
        public void ClearDismissesEveryEntry()
        {
            var overlay = MountOverlay();
            var inserter = MountInserter();

            var dismissals = 0;

            for (var i = 0; i < 3; i++)
            {
                overlay.Insert(
                    inserter.Context,
                    _ => AnyContent(),
                    onDismissed: () => dismissals++
                );
            }

            overlay.Clear();

            Assert.AreEqual(3, dismissals);
            Assert.AreEqual(0, overlay.Count);
            Assert.AreEqual(0, overlay.Children.Length);
        }

        [Test]
        public void UnmountingTheOverlay_EndsItsEntriesWithoutDismissingThem()
        {
            var overlay = MountOverlay();
            var inserter = MountInserter();

            var dismissals = 0;
            var entry = overlay.Insert(
                inserter.Context,
                _ => AnyContent(),
                onDismissed: () => dismissals++
            );

            var entryState = overlay.Children[0];

            StateUtilities.DeactivateChild(overlay);

            Assert.IsFalse(entry.IsMounted);
            Assert.IsTrue(entryState.StateLifetime.IsDisposed);
            Assert.AreEqual(
                0,
                dismissals,
                "an unmount is not a dismissal: whoever would be told is going away with it"
            );
        }

        /// <summary>
        ///     The rule the context-smuggling story rests on. An entry whose builder closes over its
        ///     inserter's context must not outlive that context, or the closure resolves against a dead
        ///     scope -- which a disposed VContainer scope answers by constructing a duplicate rather
        ///     than throwing.
        /// </summary>
        [Test]
        public void AnEntryDiesWithItsInserter()
        {
            var overlay = MountOverlay();
            var inserter = MountInserter();

            var dismissals = 0;
            var entry = overlay.Insert(
                inserter.Context,
                _ => AnyContent(),
                onDismissed: () => dismissals++
            );

            StateUtilities.DeactivateChild(inserter);

            Assert.IsFalse(entry.IsMounted);
            Assert.AreEqual(0, overlay.Count);
            Assert.AreEqual(0, dismissals, "the owner is gone; there is nobody to tell");
        }

        [Test]
        public void InsertingFromADisposedState_IsRefused()
        {
            var overlay = MountOverlay();
            var inserter = MountInserter();

            StateUtilities.DeactivateChild(inserter);

            Assert.Throws<InvalidOperationException>(() =>
                overlay.Insert(inserter.Context, _ => AnyContent())
            );
        }

        /// <summary>
        ///     The builder is invoked with the INSERTER's context, not with the one the entry is mounted
        ///     under. That is what lets a builder resolve from the declaring scope, and its limit is
        ///     exact: it covers what the builder resolves in its own closure, not what its descendants
        ///     resolve during their own builds.
        /// </summary>
        [Test]
        public void TheBuilderSeesTheInsertersContext()
        {
            var overlay = MountOverlay();
            var inserter = MountInserter();

            BuildContext seen = null;

            overlay.Insert(
                inserter.Context,
                context =>
                {
                    seen = context;
                    return AnyContent();
                }
            );

            TestHarness.Layout(overlay, Screen);

            Assert.AreSame(inserter.Context, seen);
        }

        [Test]
        public void OfOrNull_IsNullWithoutAnOverlayAbove()
        {
            var loose = MountInserter();

            Assert.IsNull(Overlay.OfOrNull(loose.Context));
        }
    }
}

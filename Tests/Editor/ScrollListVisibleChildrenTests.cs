using System.Linq;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // The view renders IMultiChildLayoutState.Children, so that array has to hold the States the
    // build window holds now. A change read only inside ItemBuilder rebuilds the window without
    // moving it: the visible indices stay the same while the States behind them do not.
    public class ScrollListVisibleChildrenTests
    {
        private static readonly LayoutConstraints Constraints = LayoutConstraints.Tight(800, 700);

        private static IState[] LayOutAndReadChildren(State state)
        {
            TestHarness.Layout(state, Constraints);
            return ((IMultiChildLayoutState)state).Children;
        }

        private static string Describe(IState[] children) =>
            string.Join(
                ", ",
                children.Select(c => $"{c.Key}{(c.StateLifetime.IsDisposed ? " (disposed)" : "")}")
            );

        [Test]
        public void ItemReplacedUnderUnchangedIndices_IsReplacedInTheVisibleChildren()
        {
            var swapped = Atom.Value(false);
            var widget = new ScrollList
            {
                ItemCount = 50,
                ItemBuilder = (context, index) =>
                    new FixedSizeBox
                    {
                        Key = index == 2 && swapped.Value ? Key.Of("replacement") : Key.Of(index),
                        Size = new Vector2(800, 100),
                    },
            };
            var state = TestHarness.Mount(widget);

            var before = LayOutAndReadChildren(state);
            Assert.AreEqual(Key.Of(2), before[2].Key, "precondition: the third child is item 2");

            swapped.Value = true;
            var after = LayOutAndReadChildren(state);

            Assert.AreEqual(
                Key.Of("replacement"),
                after[2].Key,
                $"the visible children still hold the State that was replaced: {Describe(after)}"
            );
            Assert.IsFalse(
                after.Any(c => c.StateLifetime.IsDisposed),
                $"a disposed State is handed to the view: {Describe(after)}"
            );
        }

        [Test]
        public void ItemsReorderedUnderUnchangedIndices_AreReorderedInTheVisibleChildren()
        {
            var reversed = Atom.Value(false);
            var widget = new ScrollList
            {
                ItemCount = 5,
                ItemBuilder = (context, index) =>
                    new FixedSizeBox
                    {
                        Key = Key.Of(reversed.Value ? 4 - index : index),
                        Size = new Vector2(800, 100),
                    },
            };
            var state = TestHarness.Mount(widget);

            LayOutAndReadChildren(state);
            reversed.Value = true;
            var after = LayOutAndReadChildren(state);

            CollectionAssert.AreEqual(
                new[] { 4, 3, 2, 1, 0 }.Select(i => Key.Of(i)).ToArray(),
                after.Select(c => c.Key).ToArray(),
                $"the visible children kept the order from before the reorder: {Describe(after)}"
            );
        }
    }
}

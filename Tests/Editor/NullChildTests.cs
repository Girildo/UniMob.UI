using System.Collections.Generic;
using NUnit.Framework;
using UniMob.UI.Internal;
using UniMob.UI.Layout;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A single-child slot says "no child" with null: the outgoing child's state is disposed, not
    ///     stranded, and a child that comes back is inflated fresh. A children list never holds a null.
    /// </summary>
    public class NullChildTests
    {
        private static CountingBox Leaf() => new CountingBox { BoxSize = new Vector2(30, 40) };

        private static State ChildOf(State root) => (State)((ISingleChildLayoutState)root).Child;

        [Test]
        public void AChildReplacedByNull_IsDisposed_AndTheSlotReadsEmpty()
        {
            var root = TestHarness.Mount(new SizedBox { Child = Leaf() });
            var child = ChildOf(root);
            Assert.IsNotNull(child, "the child is mounted while its widget is in the slot");
            Assert.IsFalse(child.StateLifetime.IsDisposed);

            TestHarness.Update(root, SizedBox.Shrink());

            Assert.IsNull(ChildOf(root), "the slot reads empty once its widget has no child");
            Assert.IsTrue(
                child.StateLifetime.IsDisposed,
                "the outgoing child's state is disposed, not left alive with nothing rendering it"
            );
        }

        [Test]
        public void AChildThatComesBackAfterNull_IsInflatedFresh()
        {
            var root = TestHarness.Mount(new SizedBox { Child = Leaf() });
            var first = ChildOf(root);

            // Read between the two updates, as a frame would: the holder is pull-based, and two
            // updates with no read between them coalesce into one in-place update of the old child.
            TestHarness.Update(root, SizedBox.Shrink());
            Assert.IsNull(ChildOf(root));
            TestHarness.Update(root, new SizedBox { Child = Leaf() });

            var second = ChildOf(root);
            Assert.IsNotNull(second, "a child that comes back is mounted again");
            Assert.AreNotSame(first, second, "it is a fresh state, not the disposed one");
            Assert.IsFalse(second.StateLifetime.IsDisposed);
        }

        [Test]
        public void ANullEntryInAChildrenList_IsRejected_NamingItsIndex()
        {
            var widgets = new List<Widget> { Leaf(), null };

            var exception = Assert.Throws<UnityEngine.Assertions.AssertionException>(() =>
                StateUtilities.UpdateChildren(new BuildContext(null, null), new State[0], widgets)
            );

            StringAssert.Contains(
                "Children[1]",
                exception.Message,
                "the failure names the offending index"
            );
        }
    }
}

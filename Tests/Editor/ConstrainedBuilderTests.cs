using System.Collections.Generic;
using NUnit.Framework;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     <see cref="ConstrainedBuilder"/> builds its child from the constraints it was laid out
    ///     under, which makes it the one widget whose build depends on a layout result.
    /// </summary>
    /// <remarks>
    ///     That inverts the usual order, and it only resolves because of a specific sequence:
    ///     <c>RenderObject.Layout</c> writes the constraints before pulling anything, and the proxy's
    ///     sizing pass pulls <c>Child</c> afterwards, so the build always runs against the value just
    ///     written. Nothing in the type system holds that order.
    ///     <para>
    ///         So the assertions here are deliberately about the <em>result</em> -- what the builder
    ///         was handed, and what the subtree it produced measured at -- rather than about the
    ///         mechanism. If the write and the pull ever swap, the builder starts seeing the previous
    ///         frame's constraints, and that is a wrong number rather than a crash: silent, one frame
    ///         late, and invisible to every other fixture in this package.
    ///     </para>
    /// </remarks>
    public class ConstrainedBuilderTests
    {
        /// <summary>Records every constraint the builder is handed, in order.</summary>
        private sealed class Recorder
        {
            public readonly List<LayoutConstraints> Seen = new List<LayoutConstraints>();

            public LayoutConstraints Last => this.Seen[this.Seen.Count - 1];

            public int Count => this.Seen.Count;
        }

        private static Widget Build(Recorder recorder, Vector2 childSize)
        {
            return new ConstrainedBuilder
            {
                Builder = (_, constraints) =>
                {
                    recorder.Seen.Add(constraints);
                    return new CountingBox { BoxSize = childSize };
                },
            };
        }

        [Test]
        public void BuilderIsHandedTheConstraintsTheWidgetWasLaidOutUnder()
        {
            var recorder = new Recorder();
            var root = TestHarness.Mount(Build(recorder, new Vector2(10, 10)));

            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Tight(80, 60));

            Assert.AreEqual(LayoutConstraints.Tight(80, 60), recorder.Last);
        }

        [Test]
        public void BuilderIsHandedNewConstraintsWhenTheyChange()
        {
            var recorder = new Recorder();
            var root = TestHarness.Mount(Build(recorder, new Vector2(10, 10)));

            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Tight(80, 60));
            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Tight(20, 25));

            Assert.AreEqual(LayoutConstraints.Tight(20, 25), recorder.Last);
        }

        /// <summary>
        ///     The subtree the builder produced has to be the one that gets measured. Reading the
        ///     right constraints but laying out a child built from the previous ones would satisfy
        ///     every assertion above and still put the wrong thing on screen.
        /// </summary>
        [Test]
        public void TheChildBuiltFromTheConstraintsIsTheChildThatGetsMeasured()
        {
            var root = TestHarness.Mount(
                new ConstrainedBuilder
                {
                    // Half the width it is offered, so the resulting size can only be right if the
                    // build and the measurement agree on which constraints are current.
                    Builder = (_, constraints) =>
                        new CountingBox { BoxSize = new Vector2(constraints.MaxWidth / 2f, 10) },
                }
            );

            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Loose(200, 100));
            Assert.AreEqual(new Vector2(100, 10), root.RenderObject.PeekSize());

            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Loose(60, 100));
            Assert.AreEqual(
                new Vector2(30, 10),
                root.RenderObject.PeekSize(),
                "a stale build would still measure 100 wide here."
            );
        }

        [Test]
        public void NothingIsRebuiltWhenTheConstraintsAreUnchanged()
        {
            var recorder = new Recorder();
            var root = TestHarness.Mount(Build(recorder, new Vector2(10, 10)));
            var constraints = LayoutConstraints.Tight(80, 60);

            TestHarness.DriveLayoutAndView(root, constraints);
            var afterFirstFrame = recorder.Count;

            TestHarness.DriveLayoutAndView(root, constraints);

            Assert.AreEqual(
                afterFirstFrame,
                recorder.Count,
                "a second frame with the same constraints must not rebuild the subtree."
            );
        }

        [Test]
        public void EachConstraintChangeRebuildsExactlyOnce()
        {
            var recorder = new Recorder();
            var root = TestHarness.Mount(Build(recorder, new Vector2(10, 10)));

            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Tight(80, 60));
            var afterFirstFrame = recorder.Count;

            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Tight(20, 25));

            Assert.AreEqual(
                afterFirstFrame + 1,
                recorder.Count,
                "one constraint change is one rebuild."
            );
        }

        /// <summary>
        ///     Build-only wrappers own a proxy rather than forwarding a render object, so the
        ///     constraints reaching the builder have travelled one per level. Nesting is where an
        ///     off-by-one-level push would show up.
        /// </summary>
        [Test]
        public void ConstraintsReachTheBuilderThroughBuildOnlyWrappers()
        {
            var recorder = new Recorder();
            var root = TestHarness.Mount(
                new Builder(_ => new Builder(__ => Build(recorder, new Vector2(10, 10))))
            );

            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Tight(80, 60));

            Assert.AreEqual(LayoutConstraints.Tight(80, 60), recorder.Last);
        }

        /// <summary>
        ///     A ConstrainedBuilder inside another one: the inner build runs during the outer's
        ///     sizing pass, which is the deepest the write-then-pull ordering gets exercised.
        /// </summary>
        [Test]
        public void ANestedBuilderSeesTheConstraintsItsParentPassedDown()
        {
            var outer = new Recorder();
            var inner = new Recorder();

            var root = TestHarness.Mount(
                new ConstrainedBuilder
                {
                    Builder = (_, constraints) =>
                    {
                        outer.Seen.Add(constraints);
                        return new SizedBox
                        {
                            Width = constraints.MaxWidth / 2f,
                            Child = Build(inner, new Vector2(10, 10)),
                        };
                    },
                }
            );

            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Loose(200, 100));

            Assert.AreEqual(200f, outer.Last.MaxWidth, 0.01f);
            Assert.AreEqual(
                100f,
                inner.Last.MaxWidth,
                0.01f,
                "the inner builder is inside a box the outer one sized to half its own width."
            );
        }
    }
}

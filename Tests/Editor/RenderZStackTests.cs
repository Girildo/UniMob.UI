using System;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class RenderZStackTests
    {
        // RenderZStack reads IZStackState, so the stack's own state is a fake. Children still go
        // through TestHarness.Mount: they are laid out, and that needs a real render object.
        private class FakeZStackState : FakeState, IZStackState
        {
            public IState[] Children { get; set; } = Array.Empty<IState>();
            public Alignment Alignment { get; set; } = Alignment.Center;
        }

        /// <summary>
        ///     A positioned widget written outside the framework: it shares no base class with
        ///     <see cref="Positioned"/> and answers only <see cref="IPositionedState"/>.
        /// </summary>
        private class Anchored : SingleChildLayoutWidget
        {
            public float? Left { get; set; }
            public float? Top { get; set; }

            public override State CreateState() => new AnchoredState();

            public override RenderObject CreateRenderObject(BuildContext context, IState state) =>
                new RenderProxy((AnchoredState)state);
        }

        private class AnchoredState : SingleChildLayoutState<Anchored>, IPositionedState
        {
            public float? Left => this.Widget.Left;
            public float? Top => this.Widget.Top;
            public float? Right => null;
            public float? Bottom => null;
            public float? Width => null;
            public float? Height => null;
        }

        private static RenderZStack Layout(
            IState[] children,
            LayoutConstraints constraints,
            Alignment? alignment = null
        )
        {
            var state = new FakeZStackState
            {
                Children = children,
                Alignment = alignment ?? Alignment.Center,
            };
            var render = new RenderZStack(state);
            render.Layout(constraints);
            return render;
        }

        [Test]
        public void NonPositionedChildren_SizeIsMaxAcrossChildren()
        {
            var children = new[]
            {
                TestHarness.Mount(new FixedSizeBox { Size = new Vector2(50, 30) }),
                TestHarness.Mount(new FixedSizeBox { Size = new Vector2(20, 80) }),
            };

            var render = Layout(children, LayoutConstraints.Loose(200, 200));

            Assert.AreEqual(new Vector2(50, 80), render.PeekSize());
        }

        [Test]
        public void NonPositionedChild_FollowsAlignment_BottomRight()
        {
            var children = new[]
            {
                TestHarness.Mount(new FixedSizeBox { Size = new Vector2(20, 20) }),
            };

            var render = Layout(children, LayoutConstraints.Tight(100, 100), Alignment.BottomRight);

            Assert.AreEqual(new Vector2(80, 80), render.ChildrenLayout[0].Position);
        }

        [Test]
        public void NonPositionedChild_FollowsAlignment_Center()
        {
            var children = new[]
            {
                TestHarness.Mount(new FixedSizeBox { Size = new Vector2(20, 20) }),
            };

            var render = Layout(children, LayoutConstraints.Tight(100, 100), Alignment.Center);

            Assert.AreEqual(new Vector2(40, 40), render.ChildrenLayout[0].Position);
        }

        [Test]
        public void PositionedChild_LeftTopOnly_DoesNotAffectStackSize()
        {
            var children = new[]
            {
                // Gives the stack a real (non-positioned-derived) size to assert against.
                TestHarness.Mount(new FixedSizeBox { Size = new Vector2(10, 10) }),
                TestHarness.Mount(
                    new Positioned
                    {
                        Left = 40,
                        Top = 5,
                        Child = new FixedSizeBox { Size = new Vector2(20, 20) },
                    }
                ),
            };

            var render = Layout(children, LayoutConstraints.Loose(200, 200));

            Assert.AreEqual(
                new Vector2(10, 10),
                render.PeekSize(),
                "positioned children must not contribute to the stack's own size"
            );
            Assert.AreEqual(new Vector2(40, 5), render.ChildrenLayout[1].Position);

            // Left and Top pin neither axis, so the child is laid out unbounded and keeps its own
            // size -- larger than the stack it sits in. An overlay is routinely bigger than the thing
            // it is anchored to, so the stack's size is not a ceiling on it.
            Assert.AreEqual(new Vector2(20, 20), render.ChildrenLayout[1].Size);
        }

        [Test]
        public void PositionedChild_LeftAndRight_DerivesWidthFromStackSize_NotOwnIntrinsicWidth()
        {
            var children = new[]
            {
                TestHarness.Mount(new FixedSizeBox { Size = new Vector2(100, 100) }),
                TestHarness.Mount(
                    new Positioned
                    {
                        Left = 10,
                        Right = 20,
                        Top = 0,
                        // Deliberately huge natural width -- must be ignored in favor of the derived one.
                        Child = new FixedSizeBox { Size = new Vector2(9999, 30) },
                    }
                ),
            };

            var render = Layout(children, LayoutConstraints.Loose(200, 200));

            // stack size is 100 (from the non-positioned child) => positioned width = 100 - 10 - 20 = 70.
            Assert.AreEqual(70f, render.ChildrenLayout[1].Size.x, 0.01f);
        }

        /// <summary>
        ///     The shape PictureGallery's full-screen footer is built from: an edge-to-edge strip
        ///     anchored to the bottom, wrapping content that aligns itself within it.
        /// </summary>
        /// <remarks>
        ///     The two axes are asymmetric here and that is the whole point. Left and Right together
        ///     state a width, so the strip spans the stack. Bottom alone states no height, so the
        ///     strip takes its content's -- and an Align under an unbounded axis hugs its child rather
        ///     than expanding to fill.
        ///     <para>
        ///         Bounding the unpinned axis to the stack instead is not a clip here, it is a
        ///         misplacement: the Align would fill the stack's full height and centre its child
        ///         vertically on screen, while <c>Bottom</c> asked for it near the bottom edge. The
        ///         positioning arithmetic then compounds it, resolving the child's corner to a
        ///         negative y. That was the shipped behaviour, and nothing failed on it.
        ///     </para>
        /// </remarks>
        [Test]
        public void PositionedChild_BottomWithBothSideEdges_HugsItsContentAndSitsAboveTheBottomEdge()
        {
            var children = new[]
            {
                // Gives the stack a 300x200 box for the strip to be measured against.
                TestHarness.Mount(new FixedSizeBox { Size = new Vector2(300, 200) }),
                TestHarness.Mount(
                    new Positioned
                    {
                        Bottom = 24,
                        Left = 24,
                        Right = 24,
                        Child = new Align
                        {
                            Alignment = Alignment.CenterRight,
                            Child = new FixedSizeBox { Size = new Vector2(80, 40) },
                        },
                    }
                ),
            };

            var render = Layout(children, LayoutConstraints.Loose(400, 400));

            var strip = render.ChildrenLayout[1];

            Assert.AreEqual(
                new Vector2(252, 40),
                strip.Size,
                "Left and Right state a width (300 - 24 - 24), while Bottom alone leaves the height "
                    + "to the content, so the strip is as tall as the 40pt box inside it."
            );
            Assert.AreEqual(
                new Vector2(24, 136),
                strip.Position,
                "Bottom 24 measured from the stack's bottom edge: 200 - 40 - 24."
            );
        }

        /// <summary>
        ///     The stack positions whatever answers <see cref="IPositionedState"/>, so positioning is
        ///     extensible rather than reserved for <see cref="Positioned"/>.
        /// </summary>
        [Test]
        public void CustomPositionedState_IsPlacedAgainstTheStackEdges()
        {
            var render = Layout(AnchoredChildren(), LayoutConstraints.Loose(200, 200));

            Assert.AreEqual(
                new Vector2(10, 10),
                render.PeekSize(),
                "a custom positioned child must not contribute to the stack's own size"
            );
            Assert.AreEqual(new Vector2(40, 5), render.ChildrenLayout[1].Position);
            Assert.AreEqual(new Vector2(20, 20), render.ChildrenLayout[1].Size);
        }

        [Test]
        public void CustomPositionedState_IsIgnoredByIntrinsicSize()
        {
            var render = new RenderZStack(new FakeZStackState { Children = AnchoredChildren() });

            Assert.AreEqual(10f, render.GetIntrinsicWidth(float.PositiveInfinity), 0.01f);
            Assert.AreEqual(10f, render.GetIntrinsicHeight(float.PositiveInfinity), 0.01f);
        }

        // A 10x10 box giving the stack a size, and a larger anchored child that must not change it.
        private static IState[] AnchoredChildren() =>
            new[]
            {
                TestHarness.Mount(new FixedSizeBox { Size = new Vector2(10, 10) }),
                TestHarness.Mount(
                    new Anchored
                    {
                        Left = 40,
                        Top = 5,
                        Child = new FixedSizeBox { Size = new Vector2(20, 20) },
                    }
                ),
            };

        [Test]
        public void ComputeIntrinsicSize_IgnoresPositionedChildren()
        {
            var children = new[]
            {
                TestHarness.Mount(new FixedSizeBox { Size = new Vector2(30, 40) }),
                TestHarness.Mount(
                    new Positioned
                    {
                        Left = 0,
                        Top = 0,
                        Child = new FixedSizeBox { Size = new Vector2(500, 500) },
                    }
                ),
            };

            var state = new FakeZStackState { Children = children };
            var render = new RenderZStack(state);

            Assert.AreEqual(30f, render.GetIntrinsicWidth(float.PositiveInfinity), 0.01f);
            Assert.AreEqual(40f, render.GetIntrinsicHeight(float.PositiveInfinity), 0.01f);
        }
    }
}

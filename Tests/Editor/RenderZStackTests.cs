using System;
using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class RenderZStackTests
    {
        // Fully headless: since RenderZStack now depends on IZStackState (not the concrete ZStackState --
        // see Phase 1.1 of the RenderObjects consistency pass), it no longer needs a real ZStack widget
        // mounted just to get a state object. Individual children still go through TestHarness.Mount
        // since RenderZStack itself distinguishes them by checking for the framework's own concrete
        // PositionedState, which can't be faked without real mounting.
        private class FakeZStackState : FakeState, IZStackState
        {
            public IState[] Children { get; set; } = Array.Empty<IState>();
            public Alignment Alignment { get; set; } = Alignment.Center;
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

            Assert.AreEqual(new Vector2(50, 80), render.Size);
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
                render.Size,
                "positioned children must not contribute to the stack's own size"
            );
            Assert.AreEqual(new Vector2(40, 5), render.ChildrenLayout[1].Position);

            // Clamped to the stack rather than free at its requested 20x20: on an axis a positioned
            // child does not size itself, the stack's size is its upper bound. Loose, not tight, so a
            // smaller child still stays smaller.
            //
            // Flutter passes fully unbounded constraints here instead (BoxConstraints.tightFor with
            // both dimensions null, since width is only pinned when left AND right are given), so the
            // same child would keep its 20x20. The divergence is deliberate, but not because Flutter's
            // children tolerate an unbounded axis -- a Flutter Column errors on one exactly as
            // RenderFlex does here. It is the failure path that differs: Flutter reports the error and
            // expects the Positioned to be constrained, while bounding to the stack lets an anchored
            // overlay clip instead of erroring. The price is that a positioned child can never exceed
            // its stack, so an overlay larger than the box it is anchored to is silently cut off.
            Assert.AreEqual(new Vector2(10, 10), render.ChildrenLayout[1].Size);
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

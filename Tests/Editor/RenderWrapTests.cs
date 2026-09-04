using System;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class RenderWrapTests
    {
        private class FakeWrapState : FakeState, IWrapState
        {
            public IState[] Children { get; set; } = Array.Empty<IState>();
            public Axis Direction { get; set; } = Axis.Horizontal;
            public float Spacing { get; set; }
            public float RunSpacing { get; set; }
            public MainAxisAlignment Alignment { get; set; } = MainAxisAlignment.Start;
            public CrossAxisAlignment CrossAxisAlignment { get; set; } = CrossAxisAlignment.Start;
            public MainAxisAlignment RunAlignment { get; set; } = MainAxisAlignment.Start;
        }

        private static IState Box(float width, float height) =>
            TestHarness.Mount(new FixedSizeBox { Size = new Vector2(width, height) });

        [Test]
        public void ChildrenWrapIntoNewRun_WhenTheyExceedTheMainAxisConstraint()
        {
            var state = new FakeWrapState
            {
                Children = new[] { Box(40, 20), Box(40, 20), Box(40, 20) },
            };

            var wrap = new RenderWrap(state);
            wrap.Layout(new LayoutConstraints(0, 0, 100, 1000));

            // First two fit in one row (80 <= 100); the third overflows into a second row.
            Assert.AreEqual(new Vector2(80, 40), wrap.PeekSize());
            Assert.AreEqual(0f, wrap.ChildrenLayout[0].Position.y);
            Assert.AreEqual(0f, wrap.ChildrenLayout[1].Position.y);
            Assert.AreEqual(20f, wrap.ChildrenLayout[2].Position.y);
        }

        [Test]
        public void RunAlignment_SpaceBetween_DistributesRunsAcrossTheCrossAxis()
        {
            // Each child alone already exceeds the main-axis constraint, so every child lands in its
            // own run (3 runs of 1 item each).
            var state = new FakeWrapState
            {
                Children = new[] { Box(250, 10), Box(250, 10), Box(250, 10) },
                RunAlignment = MainAxisAlignment.SpaceBetween,
            };

            var wrap = new RenderWrap(state);
            wrap.Layout(LayoutConstraints.Tight(200, 100));

            // Total run cross size is 30 (3 runs x 10), leaving 70 free to split across 2 gaps (35 each).
            Assert.AreEqual(0f, wrap.ChildrenLayout[0].Position.y, 0.01f);
            Assert.AreEqual(45f, wrap.ChildrenLayout[1].Position.y, 0.01f);
            Assert.AreEqual(90f, wrap.ChildrenLayout[2].Position.y, 0.01f);
        }

        [Test]
        public void Spacing_IsAddedBetweenItems_ButNotTrailing()
        {
            var state = new FakeWrapState
            {
                Children = new[] { Box(30, 10), Box(30, 10) },
                Spacing = 5,
            };

            var wrap = new RenderWrap(state);
            wrap.Layout(new LayoutConstraints(0, 0, 1000, 1000));

            Assert.AreEqual(65f, wrap.PeekSize().x, 0.01f);
            Assert.AreEqual(35f, wrap.ChildrenLayout[1].Position.x, 0.01f);
        }

        [Test]
        public void CrossAxisAlignment_Stretch_ExpandsShorterChildrenToRunCrossSize()
        {
            var state = new FakeWrapState
            {
                Children = new[] { Box(30, 30), Box(30, 10) },
                CrossAxisAlignment = CrossAxisAlignment.Stretch,
            };

            var wrap = new RenderWrap(state);
            wrap.Layout(new LayoutConstraints(0, 0, 1000, 1000));

            Assert.AreEqual(30f, wrap.ChildrenLayout[1].Size.y, 0.01f);
        }

        [Test]
        public void IntrinsicWidth_Horizontal_SumsChildWidthsPlusSpacing()
        {
            var state = new FakeWrapState
            {
                Children = new[] { Box(10, 10), Box(10, 10), Box(10, 10) },
                Spacing = 5,
            };

            var wrap = new RenderWrap(state);

            Assert.AreEqual(40f, wrap.GetIntrinsicWidth(100f), 0.01f);
        }

        [Test]
        public void IntrinsicHeight_Vertical_SumsChildHeightsPlusSpacing()
        {
            var state = new FakeWrapState
            {
                Children = new[] { Box(10, 10), Box(10, 10), Box(10, 10) },
                Direction = Axis.Vertical,
                Spacing = 5,
            };

            var wrap = new RenderWrap(state);

            Assert.AreEqual(40f, wrap.GetIntrinsicHeight(100f), 0.01f);
        }

        [Test]
        public void IntrinsicWidth_Vertical_IsTheWidthOfTheColumnsTheChildrenPackInto()
        {
            var state = new FakeWrapState
            {
                Children = new[] { Box(30, 50), Box(30, 50), Box(30, 50) },
                Direction = Axis.Vertical,
            };

            var wrap = new RenderWrap(state);

            // Two children fit in a 100-tall column, so the third starts a second one: 2 x 30 wide.
            // The cross axis has no closed form -- it is a function of where the runs break -- so this
            // is the branch that has to pack rather than sum.
            Assert.AreEqual(60f, wrap.GetIntrinsicWidth(100f), 0.01f);
        }

        [Test]
        public void IntrinsicHeight_Horizontal_IsTheHeightOfTheRowsTheChildrenPackInto()
        {
            var state = new FakeWrapState
            {
                Children = new[] { Box(30, 50), Box(30, 50), Box(30, 50) },
            };

            var wrap = new RenderWrap(state);

            // All three fit across 100, so there is one row and the answer is one child tall.
            Assert.AreEqual(50f, wrap.GetIntrinsicHeight(100f), 0.01f);
            // Narrow enough to force a second row, and the answer doubles.
            Assert.AreEqual(100f, wrap.GetIntrinsicHeight(70f), 0.01f);
        }

        [Test]
        public void IntrinsicHeight_Horizontal_CountsRunSpacingBetweenRows()
        {
            var state = new FakeWrapState
            {
                Children = new[] { Box(30, 50), Box(30, 50), Box(30, 50) },
                RunSpacing = 7,
            };

            var wrap = new RenderWrap(state);

            Assert.AreEqual(107f, wrap.GetIntrinsicHeight(70f), 0.01f);
        }

        [Test]
        public void IntrinsicQueriesLayNoChildOut()
        {
            var boxes = new[]
            {
                new CountingBox { BoxSize = new Vector2(30, 50) },
                new CountingBox { BoxSize = new Vector2(30, 50) },
                new CountingBox { BoxSize = new Vector2(30, 50) },
            };
            var children = Array.ConvertAll(boxes, box => (IState)TestHarness.Mount(box));
            var renders = Array.ConvertAll(
                children,
                child => (RenderCountingBox)child.RenderObject
            );

            var state = new FakeWrapState { Children = children };
            var wrap = new RenderWrap(state);

            wrap.Layout(new LayoutConstraints(0, 0, 100, 1000));
            var passesAfterLayout = Array.ConvertAll(renders, render => render.SizingPasses);

            // Both intrinsics, at extents deliberately unlike the constraints laid out above:
            // a query that laid children out would have to re-measure them to answer.
            wrap.GetIntrinsicWidth(45f);
            wrap.GetIntrinsicHeight(45f);

            // The invariant the whole intrinsic protocol rests on. A query that reaches for
            // LayoutChild answers correctly and still strands its children at constraints nothing is
            // going to draw them in -- silently, until the next pass happens to repair it.
            CollectionAssert.AreEqual(
                passesAfterLayout,
                Array.ConvertAll(renders, render => render.SizingPasses),
                "an intrinsic query laid a child out"
            );

            // And it must not have disturbed the run list positioning reads either.
            Assert.AreEqual(new Vector2(90, 50), wrap.PeekSize());
            Assert.AreEqual(new Vector2(60, 0), wrap.ChildrenLayout[2].Position);
        }
    }
}

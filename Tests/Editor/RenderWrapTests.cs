using System;
using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
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
            wrap.PerformLayoutImmediate(new LayoutConstraints(0, 0, 100, 1000));

            // First two fit in one row (80 <= 100); the third overflows into a second row.
            Assert.AreEqual(new Vector2(80, 40), wrap.Size);
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
            wrap.PerformLayoutImmediate(LayoutConstraints.Tight(200, 100));

            // Total run cross size is 30 (3 runs x 10), leaving 70 free to split across 2 gaps (35 each).
            Assert.AreEqual(0f, wrap.ChildrenLayout[0].Position.y, 0.01f);
            Assert.AreEqual(45f, wrap.ChildrenLayout[1].Position.y, 0.01f);
            Assert.AreEqual(90f, wrap.ChildrenLayout[2].Position.y, 0.01f);
        }

        [Test]
        public void Spacing_IsAddedBetweenItems_ButNotTrailing()
        {
            var state = new FakeWrapState { Children = new[] { Box(30, 10), Box(30, 10) }, Spacing = 5 };

            var wrap = new RenderWrap(state);
            wrap.PerformLayoutImmediate(new LayoutConstraints(0, 0, 1000, 1000));

            Assert.AreEqual(65f, wrap.Size.x, 0.01f);
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
            wrap.PerformLayoutImmediate(new LayoutConstraints(0, 0, 1000, 1000));

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
    }
}

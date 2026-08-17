using System;
using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    // RenderFlex only ever touches Children/CrossAxisAlignment/MainAxisAlignment/MainAxisSize/Spacing/
    // StateLifetime on its *own* state -- everything else is inherited from FakeState as a throwing stub.
    internal class FakeFlexContainerState : FakeState, IFlexContainerState
    {
        public IState[] Children { get; set; } = Array.Empty<IState>();
        public CrossAxisAlignment CrossAxisAlignment { get; set; } = CrossAxisAlignment.Start;
        public MainAxisAlignment MainAxisAlignment { get; set; } = MainAxisAlignment.Start;
        public AxisSize MainAxisSize { get; set; } = AxisSize.Min;
        public float Spacing { get; set; }
    }

    public class RenderFlexTests
    {
        private static IState Box(float width, float height) =>
            TestHarness.Mount(new FixedSizeBox { Size = new Vector2(width, height) });

        [Test]
        public void Row_LaysOutChildrenLeftToRight_WithSpacing()
        {
            var state = new FakeFlexContainerState
            {
                Children = new[] { Box(10, 20), Box(30, 15), Box(5, 25) },
                Spacing = 4,
            };

            var flex = new RenderFlex(state, Axis.Horizontal);
            flex.Layout(LayoutConstraints.Loose(1000, 1000));

            Assert.AreEqual(new Vector2(10 + 30 + 5 + 4 * 2, 25), flex.PeekSize());
            Assert.AreEqual(0f, flex.ChildrenLayout[0].Position.x);
            Assert.AreEqual(10 + 4, flex.ChildrenLayout[1].Position.x);
            Assert.AreEqual(10 + 4 + 30 + 4, flex.ChildrenLayout[2].Position.x);
        }

        [Test]
        public void MainAxisAlignment_Center_CentersChildrenInFreeSpace()
        {
            var state = new FakeFlexContainerState
            {
                Children = new[] { Box(10, 10), Box(10, 10) },
                MainAxisAlignment = MainAxisAlignment.Center,
            };

            var flex = new RenderFlex(state, Axis.Horizontal);
            flex.Layout(LayoutConstraints.Tight(100, 10));

            // free space = 100 - 20 = 80, centered => 40 leading offset
            Assert.AreEqual(40f, flex.ChildrenLayout[0].Position.x, 0.01f);
            Assert.AreEqual(50f, flex.ChildrenLayout[1].Position.x, 0.01f);
        }

        [Test]
        public void MainAxisAlignment_SpaceBetween_PutsAllFreeSpaceBetweenChildren()
        {
            var state = new FakeFlexContainerState
            {
                Children = new[] { Box(10, 10), Box(10, 10), Box(10, 10) },
                MainAxisAlignment = MainAxisAlignment.SpaceBetween,
            };

            var flex = new RenderFlex(state, Axis.Horizontal);
            flex.Layout(LayoutConstraints.Tight(100, 10));

            // free space = 100 - 30 = 70, split across 2 gaps => 35 each
            Assert.AreEqual(0f, flex.ChildrenLayout[0].Position.x, 0.01f);
            Assert.AreEqual(45f, flex.ChildrenLayout[1].Position.x, 0.01f);
            Assert.AreEqual(90f, flex.ChildrenLayout[2].Position.x, 0.01f);
        }

        [Test]
        public void CrossAxisAlignment_Stretch_ForcesNonFlexChildrenToCrossAxisSize()
        {
            var state = new FakeFlexContainerState
            {
                Children = new[] { Box(10, 5) },
                CrossAxisAlignment = CrossAxisAlignment.Stretch,
            };

            var flex = new RenderFlex(state, Axis.Horizontal);
            flex.Layout(LayoutConstraints.Loose(100, 40));

            Assert.AreEqual(40f, flex.ChildrenLayout[0].Size.y, 0.01f);
        }

        [Test]
        public void FlexChildren_SplitRemainingSpace_ByFlexRatio()
        {
            var fixedChild = Box(100, 10);
            var flexChild1 = TestHarness.Mount(
                new Expanded
                {
                    Flex = 1,
                    Child = new FixedSizeBox { Size = new Vector2(0, 10) },
                }
            );
            var flexChild2 = TestHarness.Mount(
                new Expanded
                {
                    Flex = 2,
                    Child = new FixedSizeBox { Size = new Vector2(0, 10) },
                }
            );

            var state = new FakeFlexContainerState
            {
                Children = new[] { fixedChild, flexChild1, flexChild2 },
            };

            var flex = new RenderFlex(state, Axis.Horizontal);
            flex.Layout(LayoutConstraints.Tight(400, 50));

            // free space = 400 - 100 = 300, split 1:2 => 100 and 200
            Assert.AreEqual(100f, flex.ChildrenLayout[1].Size.x, 0.01f);
            Assert.AreEqual(200f, flex.ChildrenLayout[2].Size.x, 0.01f);
        }

        // FlexFit governs the main axis. It must not also decide whether the child is told about the
        // cross axis: a tight-fit child used to be handed an unbounded one, so anything sizing itself
        // from what it was given (a fill-me leaf, a scroll viewport) had nothing to resolve against,
        // and anything with a size of its own overflowed the flex silently. The oversized box below
        // reports 999 on the cross axis unless the flex's own bound reaches it.
        [Test]
        public void Row_TightFlexChild_IsStillBoundedByTheRowsOwnCrossAxis()
        {
            var flexChild = TestHarness.Mount(
                new Expanded { Child = new FixedSizeBox { Size = new Vector2(0, 999) } }
            );

            var state = new FakeFlexContainerState { Children = new[] { flexChild } };

            var flex = new RenderFlex(state, Axis.Horizontal);
            flex.Layout(LayoutConstraints.Tight(200, 50));

            Assert.AreEqual(50f, flex.ChildrenLayout[0].Size.y, 0.01f);
        }

        [Test]
        public void Column_TightFlexChild_IsStillBoundedByTheColumnsOwnCrossAxis()
        {
            var flexChild = TestHarness.Mount(
                new Expanded { Child = new FixedSizeBox { Size = new Vector2(999, 0) } }
            );

            var state = new FakeFlexContainerState { Children = new[] { flexChild } };

            var flex = new RenderFlex(state, Axis.Vertical);
            flex.Layout(LayoutConstraints.Tight(50, 200));

            Assert.AreEqual(50f, flex.ChildrenLayout[0].Size.x, 0.01f);
        }

        // A loose fit already kept the cross axis, and must go on doing so: the two fits differ on
        // the main axis alone.
        [Test]
        public void Row_LooseFlexChild_KeepsBothTheMainAxisShareAndTheCrossAxisBound()
        {
            var flexChild = TestHarness.Mount(
                new Flexible
                {
                    Fit = FlexFit.Loose,
                    Child = new FixedSizeBox { Size = new Vector2(999, 999) },
                }
            );

            var state = new FakeFlexContainerState { Children = new[] { flexChild } };

            var flex = new RenderFlex(state, Axis.Horizontal);
            flex.Layout(LayoutConstraints.Tight(200, 50));

            Assert.AreEqual(new Vector2(200, 50), flex.ChildrenLayout[0].Size);
        }

        [Test]
        public void Overflow_ClampsFreeSpaceToZero_AndLogsWarning_InsteadOfThrowing()
        {
            var state = new FakeFlexContainerState
            {
                Children = new[] { Box(80, 10), Box(80, 10) },
            };

            var flex = new RenderFlex(state, Axis.Horizontal);

            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex("overflowed")
            );

            Assert.DoesNotThrow(() => flex.Layout(LayoutConstraints.Tight(100, 10)));
        }

        [Test]
        public void ComputeIntrinsicHeight_Row_MeasuresFlexChildAtItsDistributedWidth()
        {
            // FixedSizeBox's intrinsic height never depends on the width it's asked about, so it
            // can't reveal whether RenderFlex handed the flex child the *correct* distributed width.
            // WidthProbeBox echoes the width straight back as its "intrinsic height", making that
            // value directly observable.
            var fixedChild = Box(50, 10);
            var flexChild = TestHarness.Mount(
                new Expanded { Flex = 1, Child = new WidthProbeBox() }
            );

            var state = new FakeFlexContainerState { Children = new[] { fixedChild, flexChild } };
            var flex = new RenderFlex(state, Axis.Horizontal);

            // total width 200, fixed child takes 50 => flex child must be measured at width 150.
            var intrinsicHeight = flex.GetIntrinsicHeight(200f);
            Assert.AreEqual(150f, intrinsicHeight, 0.01f);
        }
    }
}

using System;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Diagnostics;
using UniMob.UI.Internal.Views;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class LayoutTreeTests
    {
        // A virtualized list hands back its realized window, and an index in it that has not been
        // built yet is simply null.
        private sealed class SparseChildren : FakeState, IMultiChildLayoutState
        {
            public IState[] Children { get; set; } = Array.Empty<IState>();

            public override Key Key => null;
            public override RenderObject RenderObject => null;
        }

        private static string[] Lines(string described) => described.TrimEnd('\n').Split('\n');

        private static State MountChain() =>
            TestHarness.Mount(
                new Column
                {
                    Children =
                    {
                        new PaddingBox
                        {
                            Padding = RectPadding.All(5),
                            Child = new FixedSizeBox { Size = new Vector2(10, 10) },
                        },
                    },
                }
            );

        [Test]
        public void Describe_IndentsByDepth_AndIndexesEveryChild()
        {
            var root = MountChain();
            TestHarness.Layout(root, LayoutConstraints.Loose(100, 200));

            var lines = Lines(LayoutTree.Describe(root));

            Assert.AreEqual(3, lines.Length);
            StringAssert.StartsWith("Column ", lines[0]);
            StringAssert.StartsWith("  [0] PaddingBox ", lines[1]);
            StringAssert.StartsWith("    [0] FixedSizeBox ", lines[2]);
        }

        [Test]
        public void Describe_ReportsConstraintsAndSize_PerNode()
        {
            var root = MountChain();
            TestHarness.Layout(root, LayoutConstraints.Loose(100, 200));

            var lines = Lines(LayoutTree.Describe(root));

            StringAssert.Contains("Constraints(w:[0-100], h:[0-200])", lines[0]);
            StringAssert.Contains("->", lines[0]);
        }

        // Nullable and printed as such: Tight(0,0) is a plausible-looking box, and "nothing has laid
        // this out" is not a box at all.
        [Test]
        public void Describe_RendersANeverLaidOutNode_AsAnAbsence()
        {
            var described = LayoutTree.Describe(MountChain());

            StringAssert.Contains("<not laid out>", described);
        }

        [Test]
        public void Describe_CapsDepth_Visibly()
        {
            var root = MountChain();
            TestHarness.Layout(root, LayoutConstraints.Loose(100, 200));

            var lines = Lines(LayoutTree.Describe(root, maxDepth: 1));

            Assert.AreEqual(3, lines.Length);
            StringAssert.StartsWith("  [0] PaddingBox ", lines[1]);
            Assert.AreEqual("    ...", lines[2]);
        }

        [Test]
        public void Describe_RendersAnUnbuiltChild_WithoutRenumberingTheRest()
        {
            var sparse = new SparseChildren
            {
                Children = new[]
                {
                    TestHarness.Mount(new FixedSizeBox()),
                    null,
                    TestHarness.Mount(new CountingBox()),
                },
            };

            var lines = Lines(LayoutTree.Describe(sparse));

            StringAssert.StartsWith("  [0] FixedSizeBox ", lines[1]);
            Assert.AreEqual("  [1] <not built>", lines[2]);
            StringAssert.StartsWith("  [2] CountingBox ", lines[3]);
        }

        // The hazard this whole class is written around: ChildrenLayout's getter drives a pass, so
        // describing a tree would silently lay it out. Walking states and reading each render
        // object's own numbers does not.
        [Test]
        public void Describe_DrivesNoLayoutPass()
        {
            var root = TestHarness.Mount(
                new Column { Children = { new CountingBox { BoxSize = new Vector2(10, 10) } } }
            );
            TestHarness.Layout(root, LayoutConstraints.Loose(100, 200));

            var leaf = (RenderCountingBox)((IMultiChildLayoutState)root).Children[0].RenderObject;
            var before = leaf.SizingPasses;

            LayoutTree.Describe(root);

            Assert.AreEqual(before, leaf.SizingPasses);
        }
    }
}

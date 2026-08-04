using NUnit.Framework;
using UniMob.UI.Diagnostics;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // One node, four channels, and the rule that a failure in any one of them costs only that one.
    public class DiagnosticNodeTests
    {
        // Every member throws, which is FakeState's whole point and the worst input this renderer can
        // be handed.
        private sealed class ThrowingFake : FakeState { }

        [Test]
        public void Describe_NamesTheWidget_NotTheStateBehindIt()
        {
            var state = TestHarness.Mount(new FixedSizeBox());

            Assert.AreEqual("FixedSizeBox", DiagnosticNode.Describe(state));
        }

        [Test]
        public void Describe_ExpandsGenericArguments()
        {
            var state = TestHarness.Mount(new GenericBox<FixedSizeBox>());

            Assert.AreEqual("GenericBox<FixedSizeBox>", DiagnosticNode.Describe(state));
        }

        // Channel 1, on a widget the author is merely composing. Key.Of's own ToString is
        // "[Key: body]", which is why this needs a rendering rule of its own.
        [Test]
        public void Describe_RendersAnObjectKey_AsAHashSuffix()
        {
            var state = TestHarness.Mount(new Column { Key = Key.Of("body") });

            Assert.AreEqual("Column#body", DiagnosticNode.Describe(state));
        }

        // A widget already carrying a GlobalKey has spent channel 1 on functional identity, and this
        // is what that looks like: a marker rather than something mistakable for a label.
        [Test]
        public void Describe_RendersAGlobalKey_AsASpentChannel()
        {
            var state = TestHarness.Mount(new Column { Key = new GlobalKey<CountingBoxState>() });

            Assert.AreEqual("Column#global<CountingBoxState>", DiagnosticNode.Describe(state));
        }

        [Test]
        public void Describe_OmitsTheLabel_WhenTheWidgetHasNothingToAdd()
        {
            var state = TestHarness.Mount(new LabelledBox());

            Assert.AreEqual("LabelledBox", DiagnosticNode.Describe(state));
        }

        // The gap: before GetDiagnosticInfo moved onto the Widget interface, a stateful widget could
        // not supply a label at all.
        [Test]
        public void Describe_LetsAStatefulWidgetLabelItself()
        {
            var state = TestHarness.Mount(new LabelledBox { WidgetLabel = "Add to cart" });

            Assert.AreEqual("LabelledBox \"Add to cart\"", DiagnosticNode.Describe(state));
        }

        [Test]
        public void Describe_PrefersTheStatesLabel_OverItsWidgets()
        {
            var state = TestHarness.Mount(
                new LabelledBox { WidgetLabel = "configured", StateLabel = "running" }
            );

            Assert.AreEqual("LabelledBox \"running\"", DiagnosticNode.Describe(state));
        }

        [Test]
        public void Describe_CombinesAllChannels()
        {
            var state = TestHarness.Mount(
                new LabelledBox { Key = Key.Of("submit"), WidgetLabel = "Add to cart" }
            );

            Assert.AreEqual("LabelledBox#submit \"Add to cart\"", DiagnosticNode.Describe(state));
        }

        // The half of the contract that is enforced rather than trusted. A node is one line in all
        // three readers, and the label most likely to contain a newline -- a Text echoing content the
        // app did not write -- is exactly the one that would break them.
        [Test]
        public void Describe_FlattensAMultiLineLabel_OntoOneLine()
        {
            var state = TestHarness.Mount(
                new LabelledBox { WidgetLabel = "first\r\nsecond\tthird" }
            );

            Assert.AreEqual("LabelledBox \"first  second third\"", DiagnosticNode.Describe(state));
        }

        // The backstop, not a budget: it sits far above any deliberate label, and catches only a
        // widget echoing a paragraph, which would otherwise eat a whole ancestor chain.
        [Test]
        public void Describe_CutsAPathologicallyLongLabel()
        {
            var state = TestHarness.Mount(new LabelledBox { WidgetLabel = new string('x', 500) });

            var described = DiagnosticNode.Describe(state);

            Assert.AreEqual("LabelledBox \"" + new string('x', 120) + "...\"", described);
        }

        [Test]
        public void Truncate_LeavesAValueThatFits_Untouched()
        {
            Assert.AreEqual("Add to cart", DiagnosticNode.Truncate("Add to cart", 24));
            Assert.IsNull(DiagnosticNode.Truncate(null, 24));
        }

        // Cutting silently would make two nodes sharing a prefix look identical, which is the failure
        // a label exists to prevent, so a cut is always visible.
        [Test]
        public void Truncate_MarksTheCut()
        {
            Assert.AreEqual("Add to...", DiagnosticNode.Truncate("Add to cart", 6));
        }

        // A label is author code called mid-layout. Losing it must not cost the type name or the key
        // as well, which is the whole reason the guards are per channel.
        [Test]
        public void Describe_KeepsTheOtherChannels_WhenTheLabelThrows()
        {
            var state = TestHarness.Mount(new ThrowingLabelBox { Key = Key.Of("submit") });

            Assert.AreEqual(
                "ThrowingLabelBox#submit <label threw: InvalidOperationException>",
                DiagnosticNode.Describe(state)
            );
        }

        // FakeState's guarantee is that every member throws, and it survives contact with this
        // renderer intact: the type name falls back to the state's own, and the key says what
        // happened rather than taking the node down with it.
        [Test]
        public void Describe_StillNamesAState_WhenEveryMemberThrows()
        {
            var described = DiagnosticNode.Describe(new ThrowingFake());

            Assert.AreEqual("ThrowingFake <key threw: NotImplementedException>", described);
        }

        [Test]
        public void Describe_RendersNothing_AsNull()
        {
            Assert.AreEqual("<null>", DiagnosticNode.Describe(null));
        }

        // What the DebuggerDisplay attribute on State has named since it was written. It resolves to
        // a real method now, so the watch window shows the widget instead of an evaluation error.
        [Test]
        public void ToDiagnosticString_AddsGeometry_ToTheNode()
        {
            var state = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(10, 20) });
            TestHarness.Layout(state, LayoutConstraints.Loose(100, 100));

            var described = state.ToDiagnosticString();

            StringAssert.StartsWith("FixedSizeBox ", described);
            StringAssert.Contains("Constraints(w:[0-100], h:[0-100])", described);
            StringAssert.Contains("(10.00, 20.00)", described);
        }

        [Test]
        public void ToDiagnosticString_SaysSo_WhenNothingHasLaidTheStateOut()
        {
            var state = TestHarness.Mount(new FixedSizeBox());

            StringAssert.EndsWith("<not laid out>", state.ToDiagnosticString());
        }
    }
}

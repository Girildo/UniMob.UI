using NUnit.Framework;
using UniMob.UI.Diagnostics;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;

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
    }
}

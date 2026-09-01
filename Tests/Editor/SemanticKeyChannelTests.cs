using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UniMob.UI.Diagnostics;
using UniMob.UI.Widgets;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Covers <see cref="DiagnosticNode.SemanticKeyResolver"/>, the out-of-band channel an
    ///     application uses to give a widget a stable test address.
    /// </summary>
    /// <remarks>
    ///     The keys live in a side table the application owns, so this fixture stands in for one with a
    ///     table of its own. Cleared in teardown: the resolver is a static, and one left installed would
    ///     rewrite what every other fixture describing a tree expects to read.
    /// </remarks>
    public class SemanticKeyChannelTests
    {
        private readonly ConditionalWeakTable<Widget, string> _keys =
            new ConditionalWeakTable<Widget, string>();

        [TearDown]
        public void ClearResolver() => DiagnosticNode.SemanticKeyResolver = null;

        private void InstallResolver() =>
            DiagnosticNode.SemanticKeyResolver = widget =>
                _keys.TryGetValue(widget, out var key) ? key : null;

        // The guarantee the whole design rests on: an application that installs nothing gets the node
        // it got before this channel existed, so no existing expectation moves.
        [Test]
        public void Describe_RendersNothing_WhileNoResolverIsInstalled()
        {
            var widget = new LabelledBox { Key = Key.Of("submit"), WidgetLabel = "Anmelden" };
            _keys.Add(widget, "Login/SubmitButton");

            var state = TestHarness.Mount(widget);

            Assert.AreEqual("LabelledBox#submit \"Anmelden\"", DiagnosticNode.Describe(state));
        }

        [Test]
        public void Describe_RendersTheKey_BetweenTheKeyChannelAndTheLabel()
        {
            var widget = new LabelledBox { Key = Key.Of("submit"), WidgetLabel = "Anmelden" };
            _keys.Add(widget, "Login/SubmitButton");
            InstallResolver();

            var state = TestHarness.Mount(widget);

            Assert.AreEqual(
                "LabelledBox#submit @Login/SubmitButton \"Anmelden\"",
                DiagnosticNode.Describe(state)
            );
        }

        // Annotated on demand, never up front, so most nodes have no key and must read as though the
        // channel were not there.
        [Test]
        public void Describe_OmitsTheChannel_ForAWidgetTheResolverDoesNotKnow()
        {
            InstallResolver();

            var state = TestHarness.Mount(new LabelledBox { WidgetLabel = "Anmelden" });

            Assert.AreEqual("LabelledBox \"Anmelden\"", DiagnosticNode.Describe(state));
        }

        // Application code, called at the worst possible moment, like every other channel: it costs its
        // own marker and leaves the rest of the node intact.
        [Test]
        public void Describe_KeepsTheOtherChannels_WhenTheResolverThrows()
        {
            DiagnosticNode.SemanticKeyResolver = _ =>
                throw new InvalidOperationException("a resolver that throws");

            var state = TestHarness.Mount(
                new LabelledBox { Key = Key.Of("submit"), WidgetLabel = "Anmelden" }
            );

            Assert.AreEqual(
                "LabelledBox#submit <semantic key threw: InvalidOperationException> \"Anmelden\"",
                DiagnosticNode.Describe(state)
            );
        }

        // A node is one line wherever it is read, and a key is authored text like any other.
        [Test]
        public void Describe_FlattensAMultiLineKey_OntoOneLine()
        {
            var widget = new LabelledBox();
            _keys.Add(widget, "Login/\nSubmitButton");
            InstallResolver();

            var state = TestHarness.Mount(widget);

            Assert.AreEqual("LabelledBox @Login/ SubmitButton", DiagnosticNode.Describe(state));
        }

        // The dump picks the channel up through DiagnosticNode.AppendTo, so LayoutTree knows nothing
        // about it and there is nothing there to keep in step.
        [Test]
        public void LayoutTreeDescribe_CarriesTheKey_IntoTheFullDump()
        {
            var leaf = new LabelledBox { WidgetLabel = "Anmelden" };
            _keys.Add(leaf, "Login/SubmitButton");
            InstallResolver();

            var root = TestHarness.Mount(new Column { Children = { leaf } });

            var described = LayoutTree.Describe(root);

            StringAssert.Contains("@Login/SubmitButton", described);
            StringAssert.Contains("\"Anmelden\"", described);
        }

        [Test]
        public void LayoutTreeDescribe_IsUnchanged_WhileNoResolverIsInstalled()
        {
            var leaf = new LabelledBox { WidgetLabel = "Anmelden" };
            _keys.Add(leaf, "Login/SubmitButton");

            var root = TestHarness.Mount(new Column { Children = { leaf } });

            StringAssert.DoesNotContain("@", LayoutTree.Describe(root));
        }
    }
}

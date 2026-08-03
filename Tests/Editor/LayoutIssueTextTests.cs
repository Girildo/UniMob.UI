using NUnit.Framework;
using UniMob.UI.Diagnostics;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class LayoutIssueTextTests
    {
        private static IState MountRow()
        {
            var row = new Row();
            row.Children.Add(new FixedSizeBox { Size = new Vector2(80, 10) });
            row.Children.Add(new LabelledBox { WidgetLabel = "Add to cart" });
            return TestHarness.Mount(row);
        }

        // Unity's console list shows only the first line of an entry, so everything a reader needs to
        // decide whether to open it has to be in that line: which widget, and how much.
        [Test]
        public void Summary_LeadsWithTheWidgetAndTheNumber()
        {
            var issue = new LayoutIssue(
                LayoutIssueCode.Overflow,
                MountRow(),
                LayoutAxes.Horizontal,
                remedy: "make room",
                amount: 60f
            );

            Assert.AreEqual(
                "Row overflowed by 60.0px on the horizontal axis.",
                LayoutIssueText.Summary(issue)
            );
        }

        [Test]
        public void Summary_IsAlwaysOneLine()
        {
            foreach (LayoutIssueCode code in System.Enum.GetValues(typeof(LayoutIssueCode)))
            {
                var issue = new LayoutIssue(code, MountRow(), LayoutAxes.Both, remedy: null);

                StringAssert.DoesNotContain("\n", LayoutIssueText.Summary(issue));
            }
        }

        [Test]
        public void Summary_NamesBothAxes_WhenAFaultIsOnBoth()
        {
            var issue = new LayoutIssue(
                LayoutIssueCode.NonFiniteSize,
                MountRow(),
                LayoutAxes.Both,
                remedy: null
            );

            StringAssert.Contains("horizontal and vertical axes", LayoutIssueText.Summary(issue));
        }

        // An overflow is a layout that still renders; the other three are a widget that will not.
        // Derived from the code so no site can disagree with another about the same fault.
        [Test]
        public void Severity_WarnsForOverflow_AndErrorsForTheRest()
        {
            Assert.AreEqual(LogType.Warning, LayoutIssueText.Severity(LayoutIssueCode.Overflow));
            Assert.AreEqual(
                LogType.Error,
                LayoutIssueText.Severity(LayoutIssueCode.UnboundedConstraint)
            );
            Assert.AreEqual(
                LogType.Error,
                LayoutIssueText.Severity(LayoutIssueCode.NonFiniteChildSize)
            );
            Assert.AreEqual(LogType.Error, LayoutIssueText.Severity(LayoutIssueCode.NonFiniteSize));
        }

        [Test]
        public void Compose_LeadsWithTheSummary_ThenWhereAndWhatToDo()
        {
            var subject = MountRow();
            var issue = new LayoutIssue(
                LayoutIssueCode.Overflow,
                subject,
                LayoutAxes.Horizontal,
                remedy: "Let it scroll.",
                constraints: LayoutConstraints.Tight(100, 10),
                size: new Vector2(100, 10),
                amount: 60f
            );

            var lines = LayoutIssueText.Compose(issue).TrimEnd('\n').Split('\n');

            Assert.AreEqual(LayoutIssueText.Summary(issue), lines[0]);
            StringAssert.StartsWith("  at      ", lines[1]);
            StringAssert.Contains("Row", lines[1]);
            StringAssert.StartsWith("  fix     ", lines[2]);
            StringAssert.Contains("Let it scroll.", lines[2]);
            StringAssert.StartsWith("  got     ", lines[3]);
            StringAssert.StartsWith("  gave    ", lines[4]);
        }

        // Naming a type identifies nothing in a row of four of them, so a culprit is placed among its
        // siblings and carries whatever its author said about it.
        [Test]
        public void Compose_PlacesTheCulpritAmongItsSiblings()
        {
            var subject = MountRow();
            var culprit = ((IMultiChildLayoutState)subject).Children[1];
            var issue = new LayoutIssue(
                LayoutIssueCode.Overflow,
                subject,
                LayoutAxes.Horizontal,
                remedy: null,
                culprit: culprit,
                amount: 60f
            );

            var composed = LayoutIssueText.Compose(issue);

            StringAssert.Contains("largest", composed);
            StringAssert.Contains("child [1] of 2: LabelledBox \"Add to cart\"", composed);
        }
    }
}

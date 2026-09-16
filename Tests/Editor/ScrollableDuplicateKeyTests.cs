using System.Linq;
using NUnit.Framework;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     What a virtualized scrollable does with two eager children carrying the same
    ///     <see cref="Key" />: report it once, naming the widget, and build nothing.
    /// </summary>
    /// <remarks>
    ///     Both widgets index their eager keys through the same bridge, so they answer alike. Building
    ///     the children anyway would leave a list whose keys do not identify its items: reconciliation
    ///     and <c>ScrollTo(Key)</c> would both pick whichever one they met first.
    /// </remarks>
    public class ScrollableDuplicateKeyTests
    {
        private static readonly Key Shared = Key.Of("shared");

        [Test]
        public void AScrollList_ReportsTheDuplicateOnce_AndLaysOutNoChildren()
        {
            using var errors = RecordingErrors.Capture();

            var state = TestHarness.Mount(new ScrollList { Children = { Box(), Box(), Box() } });
            TestHarness.Layout(state, LayoutConstraints.Tight(100f, 100f));

            AssertReportedOnce(errors, state, nameof(ScrollList));
            AssertNoChildren(state);
        }

        [Test]
        public void AScrollGrid_ReportsTheDuplicateOnce_AndLaysOutNoChildren()
        {
            using var errors = RecordingErrors.Capture();

            var state = TestHarness.Mount(
                new ScrollGrid { CrossAxisCount = 2, Children = { Box(), Box(), Box() } }
            );
            TestHarness.Layout(state, LayoutConstraints.Tight(100f, 100f));

            AssertReportedOnce(errors, state, nameof(ScrollGrid));
            AssertNoChildren(state);
        }

        private static FixedSizeBox Box() =>
            new FixedSizeBox { Key = Shared, Size = new Vector2(10f, 10f) };

        private static void AssertReportedOnce(
            RecordingErrors errors,
            State state,
            string widgetName
        )
        {
            Assert.AreEqual(
                1,
                errors.Count,
                "three children on one key is one mistake, and a layout pass that re-read the children "
                    + $"must not restate it: {string.Join(" | ", errors.Select(f => f.Phase))}"
            );
            StringAssert.Contains(
                widgetName,
                errors[0].Phase,
                "the report must name the widget the author wrote, or it points at no source line"
            );
            Assert.AreSame(
                state,
                errors[0].Subject,
                "the subject is the scrollable that noticed the duplicate"
            );
        }

        private static void AssertNoChildren(State state)
        {
            Assert.IsEmpty(
                ((IMultiChildrenRenderObject)state.RenderObject).ChildrenLayout,
                "the children are dropped rather than laid out under ambiguous keys"
            );
        }
    }
}

using NUnit.Framework;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Why <see cref="UniMob.UI.Internal.Views.LayoutTextView" /> guards its style assignment.
    /// </summary>
    /// <remarks>
    ///     Every other property the text view writes short-circuits inside TextMeshPro when the incoming
    ///     value equals the outgoing one, so writing them straight through costs nothing.
    ///     <c>textStyle</c> is the exception: its setter marks the object dirty unconditionally. Since
    ///     the view's <c>Render</c> re-runs on every layout pass, an unguarded assignment regenerates
    ///     every visible label every frame.
    ///     <para>
    ///         This fixture pins the property of TMP that makes the guard necessary rather than the
    ///         guard itself. If a future TextMeshPro adds the equality check, the first test fails and
    ///         says so, and the guard can go.
    ///     </para>
    /// </remarks>
    public class TextStyleReassignmentTests
    {
        private GameObject host = null!;
        private TextMeshProUGUI text = null!;

        [SetUp]
        public void CreateText()
        {
            this.host = new GameObject("style-reassignment-probe", typeof(RectTransform));
            this.text = this.host.AddComponent<TextMeshProUGUI>();
            this.text.text = "Boden abdecken und Randstreifen kleben";
        }

        [TearDown]
        public void DestroyText()
        {
            Object.DestroyImmediate(this.host);
        }

        [Test]
        public void ReassigningTheSameStyle_StillMarksTheTextDirty()
        {
            var style = TMP_Style.NormalStyle;

            this.text.textStyle = style;
            this.text.havePropertiesChanged = false;

            this.text.textStyle = style;

            Assert.IsTrue(
                this.text.havePropertiesChanged,
                "TextMeshPro's textStyle setter is expected to have no equality guard -- if this now "
                    + "passes an equal value through untouched, LayoutTextView's ReferenceEquals check "
                    + "is dead weight and should be deleted"
            );
        }

        [Test]
        public void EveryOtherPropertyTheViewWrites_ShortCircuitsOnAnEqualValue()
        {
            // The other half of the claim: the guard is needed for textStyle *specifically*, not for
            // property assignment in general. If one of these ever starts marking the text dirty it
            // belongs behind a guard too, and this is where that shows up.
            this.text.fontSize = 28f;
            this.text.fontWeight = FontWeight.Medium;
            this.text.color = Color.red;
            this.text.textWrappingMode = TextWrappingModes.Normal;
            this.text.overflowMode = TextOverflowModes.Ellipsis;
            this.text.maxVisibleLines = 2;
            this.text.horizontalAlignment = HorizontalAlignmentOptions.Left;
            this.text.verticalAlignment = VerticalAlignmentOptions.Middle;

            this.text.havePropertiesChanged = false;

            this.text.fontSize = 28f;
            this.text.fontWeight = FontWeight.Medium;
            this.text.color = Color.red;
            this.text.textWrappingMode = TextWrappingModes.Normal;
            this.text.overflowMode = TextOverflowModes.Ellipsis;
            this.text.maxVisibleLines = 2;
            this.text.horizontalAlignment = HorizontalAlignmentOptions.Left;
            this.text.verticalAlignment = VerticalAlignmentOptions.Middle;

            Assert.IsFalse(
                this.text.havePropertiesChanged,
                "these setters are expected to guard themselves -- one that stops doing so needs the "
                    + "same treatment textStyle got, and would otherwise regenerate every label every "
                    + "frame"
            );
        }

        [Test]
        public void TheSameStringAssignedTwice_DoesNotMarkTheTextDirty()
        {
            // Load-bearing for the scroll path: a recycled row that happens to land on the same content
            // must not regenerate. DiscardInheritedGeometry depends on this too, from the other side --
            // it empties the string precisely so the assignment that follows always differs.
            const string value = "Wandflaechen grundieren, zweifach streichen";

            this.text.text = value;
            this.text.havePropertiesChanged = false;

            // A distinct reference holding equal content, so this tests TMP's comparison rather
            // than reference identity.
            this.text.text = new string(value.ToCharArray());

            Assert.IsFalse(
                this.text.havePropertiesChanged,
                "TMP compares by content, not by reference -- an equal string must not schedule a rebuild"
            );
        }
    }
}

using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // Coverage for RenderText's TMP-measurement logic -- specifically the lineExtents-vs-advance-width
    // fix and the MaxLines/WrappingEnabled interactions documented in RenderText.cs, since those are
    // the parts most likely to silently regress (this exact class of bug was diagnosed and fixed
    // earlier in the same session that added this test file).
    //
    // Must run as a PlayMode test, not EditMode: RenderText's constructor calls
    // Object.DontDestroyOnLoad on its TMP measurer GameObject, which Unity only permits in Play Mode.
    public class RenderTextTests
    {
        [Test]
        public void MaxLinesOne_SingleLineWidth_MatchesUnclampedNaturalWidth()
        {
            // Before the fix, the MaxLines-clamped branch measured width from TMP's lineExtents (the
            // ink/glyph bounding box), which is narrower than the advance-based width the unclamped
            // GetPreferredValues() branch uses -- these two must agree for a single line of text.
            const string text = "Boden abdecken";

            var unclamped = TestHarness.Mount(new Text { Value = text, WrappingEnabled = false });
            var clamped = TestHarness.Mount(new Text { Value = text, WrappingEnabled = false, MaxLines = 1 });

            var unclampedSize = TestHarness.Layout(unclamped, LayoutConstraints.Unbounded());
            var clampedSize = TestHarness.Layout(clamped, LayoutConstraints.Unbounded());

            Assert.AreEqual(unclampedSize.x, clampedSize.x, 0.05f);
        }

        [Test]
        public void MaxLines_ClampsHeight_ToVisibleLineCount_NotFullWrappedText()
        {
            const string longText = "one two three four five six seven eight nine ten eleven twelve";
            var narrowConstraints = new LayoutConstraints(0, 0, 60, float.PositiveInfinity);

            var unclamped = TestHarness.Mount(new Text { Value = longText, WrappingEnabled = true });
            var clamped = TestHarness.Mount(new Text { Value = longText, WrappingEnabled = true, MaxLines = 2 });

            var unclampedSize = TestHarness.Layout(unclamped, narrowConstraints);
            var clampedSize = TestHarness.Layout(clamped, narrowConstraints);

            Assert.Greater(unclampedSize.y, clampedSize.y * 1.5f,
                "the unclamped text should wrap into meaningfully more lines than the 2-line clamp allows");
        }

        [Test]
        public void WrappingDisabled_MeasuresNaturalWidth_EvenWhenBoxIsNearlyTight()
        {
            // The ellipsis-measurement-circularity case: with wrapping off, PerformSizing must always
            // measure at an unconstrained extent regardless of the incoming maxWidth, then let
            // Constrain() do the clamping -- not measure directly against a tight maxWidth, which
            // would let TMP's own Ellipsis overflow mode silently truncate the *measurement* itself.
            const string text = "A reasonably long single line of title text";

            var naturalState = TestHarness.Mount(new Text { Value = text, WrappingEnabled = false, MaxLines = 1 });
            var naturalSize = TestHarness.Layout(naturalState, LayoutConstraints.Unbounded());

            var tightMaxWidth = naturalSize.x - 0.5f;
            var tightState = TestHarness.Mount(new Text { Value = text, WrappingEnabled = false, MaxLines = 1 });
            var tightSize = TestHarness.Layout(tightState, new LayoutConstraints(0, 0, tightMaxWidth, float.PositiveInfinity));

            // If measurement itself had silently ellipsized against the tight box, this would come out
            // noticeably smaller than tightMaxWidth (the ellipsis glyphs eat extra space); with correct
            // unconstrained measurement, Constrain() clamps it to exactly tightMaxWidth.
            Assert.AreEqual(tightMaxWidth, tightSize.x, 0.5f);
        }

        [Test]
        public void IdenticalTextWidgets_ProduceIdenticalMeasuredSize()
        {
            var a = TestHarness.Mount(new Text { Value = "Hello world", FontSize = 18, MaxLines = 2 });
            var b = TestHarness.Mount(new Text { Value = "Hello world", FontSize = 18, MaxLines = 2 });

            var sizeA = TestHarness.Layout(a, LayoutConstraints.Loose(300, 300));
            var sizeB = TestHarness.Layout(b, LayoutConstraints.Loose(300, 300));

            Assert.AreEqual(sizeA, sizeB);
        }

        [Test]
        public void DifferentFontSize_ProducesDifferentMeasuredWidth()
        {
            var small = TestHarness.Mount(new Text { Value = "Hello world", FontSize = 12 });
            var large = TestHarness.Mount(new Text { Value = "Hello world", FontSize = 36 });

            var smallSize = TestHarness.Layout(small, LayoutConstraints.Loose(1000, 1000));
            var largeSize = TestHarness.Layout(large, LayoutConstraints.Loose(1000, 1000));

            Assert.Greater(largeSize.x, smallSize.x);
        }

        [Test]
        public void DifferentMaxLines_ProducesDifferentCachedSize_ForWrappedText()
        {
            const string longText = "one two three four five six seven eight nine ten eleven twelve";
            var narrowConstraints = new LayoutConstraints(0, 0, 60, float.PositiveInfinity);

            var oneLine = TestHarness.Mount(new Text { Value = longText, WrappingEnabled = true, MaxLines = 1 });
            var threeLines = TestHarness.Mount(new Text { Value = longText, WrappingEnabled = true, MaxLines = 3 });

            var oneLineSize = TestHarness.Layout(oneLine, narrowConstraints);
            var threeLineSize = TestHarness.Layout(threeLines, narrowConstraints);

            Assert.Greater(threeLineSize.y, oneLineSize.y,
                "PreferredSizeCacheKey must include MaxLines -- otherwise these two would incorrectly share a cached size");
        }
    }
}

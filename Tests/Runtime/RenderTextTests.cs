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
            var clamped = TestHarness.Mount(
                new Text
                {
                    Value = text,
                    WrappingEnabled = false,
                    MaxLines = 1,
                }
            );

            var unclampedSize = TestHarness.Layout(unclamped, LayoutConstraints.Unbounded());
            var clampedSize = TestHarness.Layout(clamped, LayoutConstraints.Unbounded());

            Assert.AreEqual(unclampedSize.x, clampedSize.x, 0.05f);
        }

        [Test]
        public void MaxLines_ClampsHeight_ToVisibleLineCount_NotFullWrappedText()
        {
            const string longText =
                "one two three four five six seven eight nine ten eleven twelve";
            var narrowConstraints = new LayoutConstraints(0, 0, 60, float.PositiveInfinity);

            var unclamped = TestHarness.Mount(
                new Text { Value = longText, WrappingEnabled = true }
            );
            var clamped = TestHarness.Mount(
                new Text
                {
                    Value = longText,
                    WrappingEnabled = true,
                    MaxLines = 2,
                }
            );

            var unclampedSize = TestHarness.Layout(unclamped, narrowConstraints);
            var clampedSize = TestHarness.Layout(clamped, narrowConstraints);

            Assert.Greater(
                unclampedSize.y,
                clampedSize.y * 1.5f,
                "the unclamped text should wrap into meaningfully more lines than the 2-line clamp allows"
            );
        }

        [Test]
        public void WrappingDisabled_MeasuresNaturalWidth_EvenWhenBoxIsNearlyTight()
        {
            // The ellipsis-measurement-circularity case: with wrapping off, PerformSizing must always
            // measure at an unconstrained extent regardless of the incoming maxWidth, then let
            // Constrain() do the clamping -- not measure directly against a tight maxWidth, which
            // would let TMP's own Ellipsis overflow mode silently truncate the *measurement* itself.
            const string text = "A reasonably long single line of title text";

            var naturalState = TestHarness.Mount(
                new Text
                {
                    Value = text,
                    WrappingEnabled = false,
                    MaxLines = 1,
                }
            );
            var naturalSize = TestHarness.Layout(naturalState, LayoutConstraints.Unbounded());

            var tightMaxWidth = naturalSize.x - 0.5f;
            var tightState = TestHarness.Mount(
                new Text
                {
                    Value = text,
                    WrappingEnabled = false,
                    MaxLines = 1,
                }
            );
            var tightSize = TestHarness.Layout(
                tightState,
                new LayoutConstraints(0, 0, tightMaxWidth, float.PositiveInfinity)
            );

            // If measurement itself had silently ellipsized against the tight box, this would come out
            // noticeably smaller than tightMaxWidth (the ellipsis glyphs eat extra space); with correct
            // unconstrained measurement, Constrain() clamps it to exactly tightMaxWidth.
            Assert.AreEqual(tightMaxWidth, tightSize.x, 0.5f);
        }

        [Test]
        public void IdenticalTextWidgets_ProduceIdenticalMeasuredSize()
        {
            var a = TestHarness.Mount(
                new Text
                {
                    Value = "Hello world",
                    FontSize = 18,
                    MaxLines = 2,
                }
            );
            var b = TestHarness.Mount(
                new Text
                {
                    Value = "Hello world",
                    FontSize = 18,
                    MaxLines = 2,
                }
            );

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
            const string longText =
                "one two three four five six seven eight nine ten eleven twelve";
            var narrowConstraints = new LayoutConstraints(0, 0, 60, float.PositiveInfinity);

            var oneLine = TestHarness.Mount(
                new Text
                {
                    Value = longText,
                    WrappingEnabled = true,
                    MaxLines = 1,
                }
            );
            var threeLines = TestHarness.Mount(
                new Text
                {
                    Value = longText,
                    WrappingEnabled = true,
                    MaxLines = 3,
                }
            );

            var oneLineSize = TestHarness.Layout(oneLine, narrowConstraints);
            var threeLineSize = TestHarness.Layout(threeLines, narrowConstraints);

            Assert.Greater(
                threeLineSize.y,
                oneLineSize.y,
                "PreferredSizeCacheKey must include MaxLines -- otherwise these two would incorrectly share a cached size"
            );
        }

        // The shared measurer is reconfigured per measurement, so every property pushed onto it has to be
        // part of the cache key. WrappingEnabled was pushed but not keyed, which made two texts differing
        // only in wrapping collide -- the first to measure handed its size to the second. SelectableTabCard
        // hit this directly: it opts out of wrapping while most AppTexts take the wrapping default, so the
        // same string measured in both places returned one shared, and for one of them wrong, size.
        [Test]
        public void DifferentWrappingEnabled_ProducesDifferentCachedSize_ForOverlongText()
        {
            const string longText =
                "one two three four five six seven eight nine ten eleven twelve";
            var narrowConstraints = new LayoutConstraints(0, 0, 60, float.PositiveInfinity);

            var wrapped = TestHarness.Mount(new Text { Value = longText, WrappingEnabled = true });
            var unwrapped = TestHarness.Mount(
                new Text { Value = longText, WrappingEnabled = false }
            );

            var wrappedSize = TestHarness.Layout(wrapped, narrowConstraints);
            var unwrappedSize = TestHarness.Layout(unwrapped, narrowConstraints);

            // Height is the honest axis here: Constrain() clamps both widths to the 60pt box, but the
            // wrapped text genuinely needs many lines where the unwrapped one always needs exactly one.
            Assert.Greater(
                wrappedSize.y,
                unwrappedSize.y * 1.5f,
                "PreferredSizeCacheKey must include WrappingEnabled -- otherwise these two share a cached size"
            );
        }

        [Test]
        public void DifferentWrappingEnabled_ProducesDifferentCachedSize_RegardlessOfMeasurementOrder()
        {
            // Same collision, entered from the other side. The buggy key was order-dependent by nature:
            // whichever widget measured first populated the shared entry, so a test that only ever
            // measures in one order can pass by luck on a half-fixed key.
            const string longText =
                "alpha bravo charlie delta echo foxtrot golf hotel india juliett";
            var narrowConstraints = new LayoutConstraints(0, 0, 60, float.PositiveInfinity);

            var unwrappedFirst = TestHarness.Mount(
                new Text { Value = longText, WrappingEnabled = false }
            );
            var wrappedSecond = TestHarness.Mount(
                new Text { Value = longText, WrappingEnabled = true }
            );

            var unwrappedSize = TestHarness.Layout(unwrappedFirst, narrowConstraints);
            var wrappedSize = TestHarness.Layout(wrappedSecond, narrowConstraints);

            Assert.Greater(
                wrappedSize.y,
                unwrappedSize.y * 1.5f,
                "PreferredSizeCacheKey must include WrappingEnabled -- otherwise these two share a cached size"
            );
        }

        // OverflowMode is keyed too, but deliberately has no test of its own: swept across
        // {wrapping} x {no MaxLines, 1, 2} x {Overflow, Ellipsis, Truncate, Masking}, every combination
        // lays out to the same size. TMP does read overflowMode while measuring (TMP_Text.CalculatePreferredValues
        // gates line-breaking on it when wrapping is off), but PerformSizing's Constrain() clamps the
        // measured width back to the box, which is exactly where such a difference would have shown. So
        // it is keyed to keep the key honest about what the measurer was configured with -- not because
        // a collision is observable today -- and asserting a difference here would be asserting a fiction.

        [Test]
        public void SizeCache_SurvivesMoreDistinctMeasurementsThanItsCapacity()
        {
            // The cache is bounded by generation rollover, so an unbounded key space (MaxWidth is a
            // continuous float -- one fresh generation of keys per frame of a window drag) no longer grows
            // without limit. What must not regress is correctness across a rollover: a text measured
            // before the cache filled has to still measure the same afterwards, whether it was retained,
            // promoted from the demoted generation, or re-measured from scratch.
            const string text = "Boden abdecken";

            var probe = TestHarness.Mount(new Text { Value = text, WrappingEnabled = false });
            var sizeBefore = TestHarness.Layout(probe, LayoutConstraints.Unbounded());

            // Distinct MaxWidth values are exactly what a resize drag mints, and enough of them to roll the
            // cache over more than once.
            for (var i = 0; i < 5000; i++)
            {
                var filler = TestHarness.Mount(new Text { Value = text, WrappingEnabled = true });
                TestHarness.Layout(
                    filler,
                    new LayoutConstraints(0, 0, 40f + i * 0.01f, float.PositiveInfinity)
                );
            }

            var after = TestHarness.Mount(new Text { Value = text, WrappingEnabled = false });
            var sizeAfter = TestHarness.Layout(after, LayoutConstraints.Unbounded());

            Assert.AreEqual(sizeBefore, sizeAfter);
        }
    }
}

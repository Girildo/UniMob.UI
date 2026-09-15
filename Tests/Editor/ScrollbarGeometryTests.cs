using NUnit.Framework;
using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // Covers ScrollbarGeometry: the thumb a set of scroll metrics draws on a track, the offset a thumb
    // drag stands for, and the offset a track tap pages to.
    public class ScrollbarGeometryTests
    {
        private const float Track = 200f;
        private const float Viewport = 100f;
        private const float MinThumb = 18f;
        private const float MinOverscrollThumb = 8f;

        private static ScrollMetrics Metrics(float pixelOffset, float contentExtent) =>
            new(pixelOffset, contentExtent, Viewport, Axis.Vertical);

        private static ThumbGeometry? Thumb(
            ScrollMetrics metrics,
            float trackExtent = Track,
            float minThumbExtent = MinThumb,
            float minOverscrollThumbExtent = MinOverscrollThumb
        ) =>
            ScrollbarGeometry.Thumb(metrics, trackExtent, minThumbExtent, minOverscrollThumbExtent);

        [Test]
        public void ContentThatFitsTheViewport_HasNoThumb()
        {
            var metrics = Metrics(pixelOffset: 0f, contentExtent: 80f);

            Assert.IsNull(
                Thumb(metrics),
                $"content {metrics.ContentExtent} fits viewport {metrics.ViewportExtent}, "
                    + "so the scrollbar shows no thumb at all"
            );
        }

        [Test]
        public void ContentThatFitsTheViewport_PagesNowhere()
        {
            var metrics = Metrics(pixelOffset: 0f, contentExtent: 80f);

            Assert.AreEqual(
                0f,
                ScrollbarGeometry.PageTarget(metrics, towardStart: false),
                1e-3f,
                "paging forward through unscrollable content stays at offset 0"
            );
            Assert.AreEqual(
                0f,
                ScrollbarGeometry.PageTarget(metrics, towardStart: true),
                1e-3f,
                "paging back through unscrollable content stays at offset 0"
            );
        }

        [Test]
        public void ContentThatFitsTheViewport_MapsNoThumbDelta()
        {
            var metrics = Metrics(pixelOffset: 0f, contentExtent: 80f);

            Assert.AreEqual(
                0f,
                ScrollbarGeometry.PixelDeltaForThumbDelta(metrics, Track, 50f, 25f),
                1e-3f,
                "dragging the thumb of an unscrollable scrollbar moves the content by nothing"
            );
        }

        [Test]
        public void ProportionalThumb_IsTheVisibleFractionOfTheTrack()
        {
            var thumb = Thumb(Metrics(pixelOffset: 0f, contentExtent: 400f));

            Assert.IsNotNull(thumb, "content 400 over viewport 100 is scrollable");
            Assert.AreEqual(
                50f,
                thumb!.Value.Extent,
                1e-3f,
                $"track {Track} * viewport {Viewport} / content 400 is 50"
            );
            Assert.AreEqual(
                0f,
                thumb.Value.Offset,
                1e-3f,
                "at offset 0 the thumb sits at the start of the track"
            );
        }

        [Test]
        public void ProportionalThumb_AtTheEnd_TouchesTheEndOfTheTrack()
        {
            var metrics = Metrics(pixelOffset: 300f, contentExtent: 400f);

            Assert.AreEqual(
                300f,
                metrics.MaxScrollExtent,
                1e-3f,
                "content 400 minus viewport 100 leaves 300 scrollable pixels"
            );

            var thumb = Thumb(metrics);

            Assert.AreEqual(
                150f,
                thumb!.Value.Offset,
                1e-3f,
                $"at the max offset the 50px thumb ends at the track end: {Track} - 50"
            );
        }

        [Test]
        public void ProportionalThumb_Halfway_SitsHalfwayDownTheFreeTravel()
        {
            var thumb = Thumb(Metrics(pixelOffset: 150f, contentExtent: 400f));

            Assert.AreEqual(
                75f,
                thumb!.Value.Offset,
                1e-3f,
                "offset 150 of 300 is half of the 150px travel the 50px thumb leaves free"
            );
        }

        [Test]
        public void VeryLongContent_KeepsTheThumbAtTheMinimumExtent()
        {
            var thumb = Thumb(Metrics(pixelOffset: 0f, contentExtent: 100000f));

            Assert.AreEqual(
                MinThumb,
                thumb!.Value.Extent,
                1e-3f,
                $"track {Track} * viewport {Viewport} / content 100000 is 0.2, "
                    + $"which the minimum raises to {MinThumb}"
            );
        }

        [Test]
        public void ATrackShorterThanTheMinimum_CapsTheThumbAtTheTrack()
        {
            const float shortTrack = 10f;

            var thumb = Thumb(Metrics(pixelOffset: 0f, contentExtent: 100000f), shortTrack);

            Assert.AreEqual(
                shortTrack,
                thumb!.Value.Extent,
                1e-3f,
                $"a minimum of {MinThumb} cannot make the thumb longer than a {shortTrack}px track"
            );
        }

        [Test]
        public void ThumbDelta_RoundTripsThroughTheOffset_WithoutTheMinimum()
        {
            AssertRoundTrip(
                contentExtent: 400f,
                offsets: new[] { 20f, 75f, 150f, 280f },
                thumbDeltas: new[] { 10f, -5f }
            );
        }

        [Test]
        public void ThumbDelta_RoundTripsThroughTheOffset_WithTheMinimumEngaged()
        {
            AssertRoundTrip(
                contentExtent: 100000f,
                offsets: new[] { 5000f, 20000f, 60000f, 94000f },
                thumbDeltas: new[] { 10f, -4f }
            );
        }

        private static void AssertRoundTrip(
            float contentExtent,
            float[] offsets,
            float[] thumbDeltas
        )
        {
            foreach (var offset in offsets)
            {
                foreach (var thumbDelta in thumbDeltas)
                {
                    var from = Metrics(offset, contentExtent);
                    var fromThumb = Thumb(from)!.Value;

                    var pixelDelta = ScrollbarGeometry.PixelDeltaForThumbDelta(
                        from,
                        Track,
                        fromThumb.Extent,
                        thumbDelta
                    );

                    var to = Metrics(offset + pixelDelta, contentExtent);

                    // The round trip is exact only inside the scrollable range; an overscrolled
                    // offset shrinks the thumb and pins it, which is what the overscroll tests cover.
                    Assert.That(
                        to.PixelOffset,
                        Is.InRange(0f, to.MaxScrollExtent),
                        $"content {contentExtent}, offset {offset} plus {pixelDelta} leaves the "
                            + $"scrollable range [0, {to.MaxScrollExtent}]; pick a case that does not"
                    );

                    var toThumb = Thumb(to)!.Value;

                    Assert.AreEqual(
                        fromThumb.Offset + thumbDelta,
                        toThumb.Offset,
                        1e-3f,
                        $"content {contentExtent}, offset {offset} (thumb at {fromThumb.Offset}, "
                            + $"extent {fromThumb.Extent}): moving the thumb by {thumbDelta} maps to "
                            + $"{pixelDelta} scroll pixels, which must put the thumb back at "
                            + $"{fromThumb.Offset + thumbDelta}"
                    );
                }
            }
        }

        [Test]
        public void OverscrollAtTheStart_ShrinksTheThumbAndPinsItToTheStart()
        {
            var thumb = Thumb(Metrics(pixelOffset: -30f, contentExtent: 400f));

            Assert.AreEqual(
                20f,
                thumb!.Value.Extent,
                1e-3f,
                "30px of overscroll shrinks the 50px nominal thumb to 20px"
            );
            Assert.AreEqual(
                0f,
                thumb.Value.Offset,
                1e-3f,
                "a thumb overscrolled past the start stays pinned to the start of the track"
            );
        }

        [Test]
        public void OverscrollAtTheEnd_ShrinksTheThumbAndPinsItToTheEnd()
        {
            var thumb = Thumb(Metrics(pixelOffset: 330f, contentExtent: 400f));

            Assert.AreEqual(
                20f,
                thumb!.Value.Extent,
                1e-3f,
                "30px past the max offset of 300 shrinks the 50px nominal thumb to 20px"
            );
            Assert.AreEqual(
                Track - 20f,
                thumb.Value.Offset,
                1e-3f,
                $"a thumb overscrolled past the end stays pinned to the track end: {Track} - 20"
            );
        }

        [Test]
        public void DeepOverscroll_FloorsTheThumbAtTheOverscrollMinimum()
        {
            var thumb = Thumb(Metrics(pixelOffset: -500f, contentExtent: 400f));

            Assert.AreEqual(
                MinOverscrollThumb,
                thumb!.Value.Extent,
                1e-3f,
                "500px of overscroll would take the 50px thumb well below zero, so it stops at "
                    + $"{MinOverscrollThumb} instead of vanishing"
            );
            Assert.AreEqual(
                0f,
                thumb.Value.Offset,
                1e-3f,
                "a deeply overscrolled thumb is still pinned to the start of the track"
            );
        }

        [Test]
        public void AnOverscrollMinimumLongerThanTheTrack_IsCappedAtTheTrack()
        {
            var thumb = Thumb(
                Metrics(pixelOffset: -30f, contentExtent: 400f),
                minOverscrollThumbExtent: 500f
            );

            Assert.AreEqual(
                Track,
                thumb!.Value.Extent,
                1e-3f,
                $"an overscroll minimum of 500 cannot make the thumb longer than the {Track}px track"
            );
        }

        [Test]
        public void PageTarget_ClampsAtBothEnds()
        {
            Assert.AreEqual(
                0f,
                ScrollbarGeometry.PageTarget(
                    Metrics(pixelOffset: 0f, contentExtent: 400f),
                    towardStart: true
                ),
                1e-3f,
                "paging back from offset 0 clamps to 0 instead of -100"
            );
            Assert.AreEqual(
                300f,
                ScrollbarGeometry.PageTarget(
                    Metrics(pixelOffset: 300f, contentExtent: 400f),
                    towardStart: false
                ),
                1e-3f,
                "paging forward from the max offset 300 clamps to 300 instead of 400"
            );
        }

        [Test]
        public void PageTarget_InTheMiddle_MovesByExactlyOneViewport()
        {
            var metrics = Metrics(pixelOffset: 150f, contentExtent: 400f);

            Assert.AreEqual(
                50f,
                ScrollbarGeometry.PageTarget(metrics, towardStart: true),
                1e-3f,
                $"paging back from 150 moves one viewport of {Viewport}"
            );
            Assert.AreEqual(
                250f,
                ScrollbarGeometry.PageTarget(metrics, towardStart: false),
                1e-3f,
                $"paging forward from 150 moves one viewport of {Viewport}"
            );
        }

        [Test]
        public void DraggingForwardWhileTheContentEstimateChanges_NeverMovesBackwards()
        {
            var trace = DragTrace(startOffset: 0f, thumbDelta: 4f);

            for (var step = 1; step < trace.Length; step++)
            {
                Assert.GreaterOrEqual(
                    trace[step],
                    trace[step - 1] - 1e-3f,
                    $"step {step} of a forward drag moved the offset back from {trace[step - 1]} "
                        + $"to {trace[step]}"
                );
            }
        }

        [Test]
        public void DraggingBackwardWhileTheContentEstimateChanges_NeverMovesForwards()
        {
            var trace = DragTrace(startOffset: 300f, thumbDelta: -4f);

            for (var step = 1; step < trace.Length; step++)
            {
                Assert.LessOrEqual(
                    trace[step],
                    trace[step - 1] + 1e-3f,
                    $"step {step} of a backward drag moved the offset forward from "
                        + $"{trace[step - 1]} to {trace[step]}"
                );
            }
        }

        // Twenty drag updates against a lazy content estimate that grows 15% and shrinks 10% in turn,
        // each update mapped with the metrics current at that update.
        private static float[] DragTrace(float startOffset, float thumbDelta)
        {
            const int steps = 20;

            var trace = new float[steps + 1];
            var offset = startOffset;
            var contentExtent = 400f;
            trace[0] = offset;

            TestContext.WriteLine(
                $"drag of {thumbDelta}px per step from offset {startOffset}, content estimate 400"
            );

            for (var step = 1; step <= steps; step++)
            {
                contentExtent *= step % 2 == 1 ? 1.15f : 0.90f;

                var metrics = Metrics(offset, contentExtent);
                var thumbExtent = Thumb(metrics)?.Extent ?? 0f;
                var pixelDelta = ScrollbarGeometry.PixelDeltaForThumbDelta(
                    metrics,
                    Track,
                    thumbExtent,
                    thumbDelta
                );

                offset = Mathf.Clamp(offset + pixelDelta, 0f, metrics.MaxScrollExtent);
                trace[step] = offset;

                TestContext.WriteLine(
                    $"  step {step, 2}: content {contentExtent, 10:F2} "
                        + $"max {metrics.MaxScrollExtent, 10:F2} thumb {thumbExtent, 7:F2} "
                        + $"delta {pixelDelta, 9:F2} offset {offset, 10:F2}"
                );
            }

            return trace;
        }

        [Test]
        public void ATrackWithNoExtent_HasNoThumb()
        {
            Assert.IsNull(
                Thumb(Metrics(pixelOffset: 0f, contentExtent: 400f), trackExtent: 0f),
                "a scrollbar with no track has nowhere to draw a thumb"
            );
        }

        [Test]
        public void EmptyContent_HasNoThumb()
        {
            Assert.IsNull(
                Thumb(Metrics(pixelOffset: 0f, contentExtent: 0f)),
                "content with no extent has no visible fraction to represent"
            );
        }

        [Test]
        public void AThumbThatFillsTheTrack_MapsNoThumbDelta()
        {
            var metrics = Metrics(pixelOffset: 0f, contentExtent: 400f);

            Assert.AreEqual(
                0f,
                ScrollbarGeometry.PixelDeltaForThumbDelta(metrics, Track, Track, 25f),
                1e-3f,
                "a thumb as long as the track has no travel, so dragging it maps to nothing"
            );
        }
    }
}

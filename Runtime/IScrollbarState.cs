using System;

namespace UniMob.UI
{
    /// <summary>
    ///     A state that draws a scrollbar for an attached scrollable: the geometry its thumb is placed
    ///     from, and the fade and hit-testing its view applies.
    /// </summary>
    /// <remarks>
    ///     <see cref="ISingleChildLayoutState.Child" /> is the thumb, which the state builds itself:
    ///     a scrollbar has no layout child the caller supplies.
    /// </remarks>
    public interface IScrollbarState : ISingleChildLayoutState
    {
        /// <summary>
        ///     <b>[Atom]</b> The attached scrollable's geometry, or <c>null</c> while nothing is
        ///     attached or the attached scrollable has not been laid out yet.
        /// </summary>
        ScrollMetrics? Metrics { get; }

        /// <summary>The track's orientation, and the axis of <see cref="Metrics" /> that is read.</summary>
        Axis Axis { get; }

        /// <summary>The track's extent across <see cref="Axis" />, where the constraints leave it loose.</summary>
        float Thickness { get; }

        /// <summary>The shortest the thumb may be drawn.</summary>
        float MinThumbExtent { get; }

        /// <summary>The shortest the thumb may be drawn while the scrollable is overscrolled.</summary>
        float MinOverscrollThumbExtent { get; }

        /// <summary>The fade, which the view applies to the whole scrollbar.</summary>
        IAnimation<float> Opacity { get; }

        /// <summary>
        ///     <b>[Atom]</b> Whether the scrollbar takes pointer events at all. False while the bar is
        ///     hidden and while <see cref="Metrics" /> report content that fits, so a strip that draws
        ///     nothing never takes a drag meant for the list underneath it.
        /// </summary>
        bool BlocksPointer { get; }

        /// <summary>
        ///     <b>[Atom]</b> What a tap on the track does, or <c>null</c> when a tap there does
        ///     nothing and the track is therefore not a hit target. Null whenever
        ///     <see cref="BlocksPointer" /> is false.
        /// </summary>
        Action<TapDetails>? OnTrackTap { get; }
    }
}

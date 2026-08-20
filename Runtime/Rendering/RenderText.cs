using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TMPro;
using UniMob.UI.Diagnostics;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.TextCore.Text;
using Object = UnityEngine.Object;

namespace UniMob.UI.Rendering
{
    public class RenderText : LeafRenderObject
    {
        private static Dictionary<WidgetViewReference, TextMeshProUGUI?> s_textMeshProMeasurers =
            new();
        private static TMP_StyleSheet? s_styleSheet;

        // Measured sizes are cached across layout passes, but the key space is unbounded: MaxWidth is a
        // continuous float, so every frame of a window drag or a reflow mints a fresh generation of keys
        // that nothing would ever reclaim. Hence two generations rather than one dictionary -- when the
        // live one fills it is demoted and an empty one takes over, which bounds residency at
        // 2 * SizeCacheGenerationCapacity while still letting a working set larger than one generation
        // survive a rollover, since a hit in the demoted generation is promoted back. Reads stay a plain
        // dictionary lookup: an LRU's touch-on-read bookkeeping would sit on the hottest path in layout.
        private const int SizeCacheGenerationCapacity = 2048;

        private static Dictionary<PreferredSizeCacheKey, Vector2> s_sizeCache = new();
        private static Dictionary<PreferredSizeCacheKey, Vector2> s_demotedSizeCache = new();

        // TMP's own default for TMP_Text.maxVisibleLines -- used to reset the shared measurer
        // when a widget doesn't clamp its line count.
        private const int DefaultMaxVisibleLines = 99999;

        // A large-but-finite size to hand TMP's RectTransform-based layout when a dimension is
        // unconstrained (GenerateTextMesh reads rect.width/height, not float.PositiveInfinity).
        private const float UnconstrainedMeasureExtent = 1_000_000f;

        private readonly ITextState _state;

        public RenderText(ITextState state)
            : base(state)
        {
            _state = state;

            var viewRefence = _state.View;

            // Ensure static sizer is initialized for the given view reference.
            if (!s_textMeshProMeasurers.TryGetValue(viewRefence, out var sizer) || sizer == null)
            {
                var prefab = UniMobViewContext.Loader.LoadViewPrefab(viewRefence);

                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"No view could be loaded for {viewRefence}, so text cannot be measured."
                    );
                }

                var go = Object.Instantiate(prefab.gameObject);
                go.name = "TextMeshPro Measurer -- " + viewRefence;
                Object.DontDestroyOnLoad(go);
                //go.hideFlags = HideFlags.HideAndDontSave;

                var behaviour = go.GetComponent<UniMobTextMeshProBehaviour>();

                // Pin anchors so rect.width/height are driven purely by sizeDelta, regardless of
                // the prefab's authored anchors. We rely on this below to make the measurer's
                // RectTransform report exactly the width/height we hand it for constrained measurement.
                var measurerRect = behaviour.rectTransform;
                measurerRect.anchorMin = measurerRect.anchorMax = Vector2.up;
                measurerRect.pivot = Vector2.up;

                // GenerateTextMesh() (unlike GetPreferredValues()) dereferences this.canvas, which is
                // null for a free-floating, unparented GameObject. Give it a Canvas so that resolves.
                // Graphic.CacheCanvas() only accepts a canvas where isActiveAndEnabled is true, so we
                // can't just disable the component to hide it -- instead make it WorldSpace and park
                // it far from the origin so it resolves correctly but is never in view of any camera.
                var measurerCanvas = go.GetComponent<Canvas>();
                if (measurerCanvas == null)
                    measurerCanvas = go.AddComponent<Canvas>();
                measurerCanvas.renderMode = RenderMode.WorldSpace;
                go.transform.position = new Vector3(1_000_000f, 1_000_000f, 1_000_000f);

                // Assign, don't Add: the branch above is also taken when the key is already present but
                // its measurer was destroyed (Unity's fake-null), and Add on a live key throws.
                s_textMeshProMeasurers[viewRefence] = behaviour;
                s_styleSheet = TMP_Settings.defaultStyleSheet;
            }
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EditorInitialize()
        {
            foreach (var pair in s_textMeshProMeasurers)
            {
                if (pair.Value != null)
                {
                    Object.Destroy(pair.Value.gameObject);
                }
            }

            s_textMeshProMeasurers.Clear();
            s_styleSheet = null;
            ClearSizeCache();

            SubscribeToStyleChanges();
        }

        private static bool s_subscribedToStyleChanges;

        /// <summary>
        ///     Drops cached sizes when an edit changes what the measurer would report.
        /// </summary>
        /// <remarks>
        ///     Editor-only because that is the whole scope of these events: TMP raises them exclusively
        ///     from its own editor code, and its components subscribe under the same guard. Editing a font
        ///     asset, a style, or TMP's settings mutates definitions in place, so every size measured
        ///     against the old ones is stale while the keys still match. A style can change at runtime too,
        ///     via <see cref="UniMob.UI.Widgets.Text.StyleSheet" />, but that hands over a different
        ///     <see cref="TMP_Style" /> instance and the cache key discriminates on identity -- so there is
        ///     nothing for a runtime flush to do.
        ///     <para>
        ///         Subscribing once is guarded by a static that shares its lifetime with the subscription
        ///         itself: a domain reload clears both, and entering play mode without one keeps both.
        ///     </para>
        /// </remarks>
        private static void SubscribeToStyleChanges()
        {
            if (s_subscribedToStyleChanges)
                return;

            s_subscribedToStyleChanges = true;

            TMPro_EventManager.FONT_PROPERTY_EVENT.Add(OnFontPropertyChanged);
            TMPro_EventManager.TEXT_STYLE_PROPERTY_EVENT.Add(OnTextStylePropertyChanged);
            TMPro_EventManager.TMP_SETTINGS_PROPERTY_EVENT.Add(ClearSizeCache);
        }

        private static void OnFontPropertyChanged(bool isChanged, Object font) => ClearSizeCache();

        private static void OnTextStylePropertyChanged(bool isChanged) => ClearSizeCache();
#endif

        private static void ClearSizeCache()
        {
            s_sizeCache.Clear();
            s_demotedSizeCache.Clear();
        }

        private static bool TryGetCachedSize(in PreferredSizeCacheKey key, out Vector2 size)
        {
            if (s_sizeCache.TryGetValue(key, out size))
                return true;

            if (!s_demotedSizeCache.TryGetValue(key, out size))
                return false;

            // Still being measured despite the rollover, so carry it into the live generation rather
            // than letting the next one drop it.
            StoreCachedSize(key, size);
            return true;
        }

        private static void StoreCachedSize(in PreferredSizeCacheKey key, Vector2 size)
        {
            if (s_sizeCache.Count >= SizeCacheGenerationCapacity)
            {
                // Demote instead of clearing outright: whatever is still in use gets promoted back on its
                // next lookup, so a working set wider than one generation degrades to occasional
                // re-measurement rather than measuring everything again on every rollover.
                (s_sizeCache, s_demotedSizeCache) = (s_demotedSizeCache, s_sizeCache);
                s_sizeCache.Clear();
            }

            s_sizeCache[key] = size;
        }

        private Vector2 GetPreferredSize(float maxWidth, float maxHeight)
        {
            if (s_textMeshProMeasurers == null || s_styleSheet == null)
                return Vector2.zero;
            var key = new PreferredSizeCacheKey
            {
                Text = _state.Value,
                MaxWidth = maxWidth,
                MaxHeight = maxHeight,
                FontSize = _state.FontSize,
                FontWeight = _state.FontWeight,
                Style = _state.Style,
                MaxLines = _state.MaxLines,
                WrappingEnabled = _state.WrappingEnabled,
                OverflowMode = _state.OverflowMode,
                ViewReference = _state.View,
            };

            if (TryGetCachedSize(key, out var cachedSize))
                return cachedSize;

            var measurer = s_textMeshProMeasurers[_state.View];
            if (measurer == null)
            {
                Debug.LogWarning("The TextMeshPro measurer is null. This should not happen.");
                return Vector2.zero;
            }

            // The measurer is shared and reconfigured per measurement, so any property left unstated
            // leaks in from whichever prefab was loaded last. Auto-sizing is the one that cannot be
            // allowed to: it grows the font to fill the box, and everything below deliberately
            // measures against an unbounded height, so leaving it on asks TMP how tall the text is
            // when the font may grow forever. The answer is its 32767 clamp. The icon prefabs enable
            // it -- an icon fills its box that way at render time -- which is why every icon in the
            // app measured at that number and was saved only by the clamp at the end of PerformSizing.
            measurer.enableAutoSizing = false;

            // Configure the static sizer with all properties
            measurer.fontSize = _state.FontSize;
            measurer.fontWeight = _state.FontWeight;
            measurer.textStyle = _state.Style;
            measurer.textWrappingMode = _state.WrappingEnabled
                ? TextWrappingModes.Normal
                : TextWrappingModes.NoWrap;
            measurer.overflowMode = _state.OverflowMode;

            var maxLines = _state.MaxLines;
            Vector2 fullSize;

            if (maxLines is < int.MaxValue and > 0)
            {
                // GetPreferredValues() runs TMP's stripped-down CalculatePreferredValues() pass, which
                // computes the *natural* size and ignores maxVisibleLines entirely (a known TMP
                // limitation). Approximating the clamp from a single face's line height breaks as soon
                // as the text/style uses rich text or a style with its own font size/asset/line height.
                // Instead we run GetTextInfo(), which goes through the real GenerateTextMesh() pass --
                // the same style/rich-text-aware code path the actual renderer uses -- and read back the
                // true per-line metrics for just the lines that would actually be visible.
                measurer.maxVisibleLines = maxLines;

                // Width is only bounded to maxWidth when wrapping is actually enabled -- wrapping is a
                // genuine function of available width, so that bound is necessary there. When wrapping is
                // off (e.g. a single-line MaxLines=1 title), there's no wrapping decision to make, so
                // bounding the measurement width has the same circularity height always had: with
                // overflowMode=Ellipsis, GetTextInfo() below truncates as soon as the rect is even a hair
                // narrower than the content, so if maxWidth happens to already be close to the text's
                // natural width (as it typically is here, since a Flexible sibling usually allocates close
                // to what's asked), the *measurement* silently ellipsizes too -- reporting a preferred width
                // that's a hair under the true natural width instead of the true value. Constrain() (in
                // PerformSizing) then has nothing left to clamp, so the real box ends up just shy of what's
                // actually needed, and the real TMP component ellipsizes at render time even though there
                // was enough room. Height never has a legitimate bound to begin with -- see PerformSizing.
                var measureWidth =
                    _state.WrappingEnabled && !float.IsPositiveInfinity(maxWidth)
                        ? maxWidth
                        : UnconstrainedMeasureExtent;

                var measurerRect = measurer.rectTransform;
                measurerRect.sizeDelta = new Vector2(measureWidth, UnconstrainedMeasureExtent);

                var textInfo = measurer.GetTextInfo(_state.Value);
                var visibleLineCount = Mathf.Min(textInfo.lineCount, maxLines);

                var width = 0f;
                var height = 0f;
                for (var i = 0; i < visibleLineCount; i++)
                {
                    var line = textInfo.lineInfo[i];
                    height += line.lineHeight;

                    // lineExtents is the ink/glyph bounding box of the first/last visible characters,
                    // which is consistently narrower than the advance-based width GetPreferredValues()
                    // (CalculatePreferredValues) uses for the unclamped branch below -- the last glyph's
                    // ink extent stops short of its full advance (right-side bearing). Reconstruct the
                    // same advance-based width from characterInfo so both branches agree.
                    var firstChar = textInfo.characterInfo[line.firstVisibleCharacterIndex];
                    var lastChar = textInfo.characterInfo[line.lastVisibleCharacterIndex];
                    width = Mathf.Max(width, lastChar.xAdvance - firstChar.origin);
                }

                fullSize = new Vector2(width, height);
            }
            else
            {
                measurer.maxVisibleLines = DefaultMaxVisibleLines;
                fullSize = measurer.GetPreferredValues(_state.Value, maxWidth, maxHeight);
            }

            StoreCachedSize(key, fullSize);
            return fullSize;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            // Always measure the *natural* height (unbounded), then let Constrain() below clamp it to
            // whatever the parent actually allows -- never feed constraints.MaxHeight into the measurement
            // itself. Wrapping legitimately depends on width, so MaxWidth still bounds the measurer, but
            // height doesn't work the same way: if we bounded the measurer's rect to a tight incoming
            // MaxHeight, TMP's own line-fitting (GetTextInfo, see GetPreferredSize) would silently stop
            // short of MaxLines whenever that height constraint was tighter than the content needs (e.g. a
            // Text inside a Row's fixed-height cross axis). The real TMP component at render time still gets
            // the full, un-truncated MaxLines value (LayoutTextView sets it from the widget, not from this
            // measurement), so it would then try to fit more lines into a box only sized for the fewer lines
            // this method under-measured -- visibly cutting the extra line(s) off. Matches
            // ComputeIntrinsicHeight below, which already always measures unbounded for the same reason.
            var preferredSize = GetPreferredSize(constraints.MaxWidth, float.PositiveInfinity);

            // Vertical only. The height above is what the lines this text intends to show actually
            // need -- MaxLines is already applied inside GetPreferredSize -- so falling short of it
            // means whole lines disappear with nothing in the source saying they should. Width is a
            // different matter: wrapping has already fitted it to MaxWidth, and where wrapping is off
            // being wider than the box is the point, not the fault.
            ReportContentOverflow(constraints, preferredSize, GiveTheTextRoom, LayoutAxes.Vertical);

            return constraints.Constrain(preferredSize);
        }

        private const string GiveTheTextRoom =
            "An ancestor bounds this text's height more tightly than its lines need. Give it a flex "
            + "share instead of a fixed height, lower MaxLines, or let it scroll.";

        protected override float ComputeIntrinsicHeight(float width)
        {
            return GetPreferredSize(width, float.PositiveInfinity).y;
        }

        protected override float ComputeIntrinsicWidth(float height)
        {
            return GetPreferredSize(float.PositiveInfinity, height).x;
        }

        /// <summary>
        ///     Identifies one measurement of one string against one measurer configuration.
        /// </summary>
        /// <remarks>
        ///     The measurer is shared and reconfigured per measurement, so the invariant this key has to
        ///     hold is exact: every property <see cref="GetPreferredSize" /> pushes onto it belongs here.
        ///     A property that is pushed but not keyed makes two widgets differing only in that property
        ///     collide, and whichever measured first hands its size to the other.
        ///     <para>
        ///         The invariant is what decides membership, not whether a given field is observably
        ///         size-affecting right now. <see cref="OverflowMode" /> is the case in point: TMP reads it
        ///         while measuring, yet every wrapping/MaxLines/overflow combination lays out identically,
        ///         because <see cref="PerformSizing" /> clamps the measured width back to the box. It is
        ///         keyed anyway -- the cache stores the raw, pre-clamp measurement, and resting on a
        ///         coincidence of clamping is how the collision this key already had got written.
        ///     </para>
        /// </remarks>
        private struct PreferredSizeCacheKey : IEquatable<PreferredSizeCacheKey>
        {
            public string Text { get; set; }
            public float MaxWidth { get; set; }
            public float MaxHeight { get; set; }
            public int FontSize { get; set; }
            public FontWeight FontWeight { get; set; }
            public TMP_Style? Style { get; set; }
            public WidgetViewReference ViewReference { get; set; }
            public int MaxLines { get; set; }
            public bool WrappingEnabled { get; set; }
            public TextOverflowModes OverflowMode { get; set; }

            public bool Equals(PreferredSizeCacheKey other)
            {
                return Text == other.Text
                    && MaxWidth.Equals(other.MaxWidth)
                    && MaxHeight.Equals(other.MaxHeight)
                    && FontSize == other.FontSize
                    && FontWeight == other.FontWeight
                    // By identity, not by hashCode: that is the hash of the style's *name*, so styles
                    // named alike in two different sheets would share an entry despite defining
                    // different fonts, sizes and line heights. TMP_Style is a plain class and both
                    // sources of one -- TMP_StyleSheet.GetStyle and TMP_Style.NormalStyle -- return a
                    // stable instance, so identity is both safe to rely on and strictly sharper.
                    && ReferenceEquals(Style, other.Style)
                    && MaxLines == other.MaxLines
                    && WrappingEnabled == other.WrappingEnabled
                    && OverflowMode == other.OverflowMode
                    && ViewReference.Equals(other.ViewReference);
            }

            public override bool Equals(object? obj)
            {
                return obj is PreferredSizeCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                var hash = new HashCode();
                hash.Add(Text);
                hash.Add(MaxWidth);
                hash.Add(MaxHeight);
                hash.Add(FontSize);
                hash.Add((int)FontWeight);
                hash.Add(Style == null ? 0 : RuntimeHelpers.GetHashCode(Style));
                hash.Add(MaxLines);
                hash.Add(WrappingEnabled);
                hash.Add((int)OverflowMode);
                hash.Add(ViewReference);
                return hash.ToHashCode();
            }
        }
    }
}

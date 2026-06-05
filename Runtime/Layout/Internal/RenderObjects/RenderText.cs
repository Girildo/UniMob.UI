#nullable enable
using System;
using System.Collections.Generic;
using TMPro;
using UniMob.UI.Widgets;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.TextCore.Text;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    internal class RenderText : LeafRenderObject
    {
        private static Dictionary<WidgetViewReference, TextMeshProUGUI?> s_textMeshProMeasurers = new();
        private static TMP_StyleSheet? s_styleSheet;

        private static readonly Dictionary<PreferredSizeCacheKey, Vector2> s_sizeCache = new();

        private readonly ITextState _state;

        public RenderText(ITextState state) : base(state.StateLifetime)
        {
            _state = state;

            var viewRefence = _state.View;

            // Ensure static sizer is initialized for the given view reference.
            if (!s_textMeshProMeasurers.TryGetValue(viewRefence, out var sizer) || sizer == null)
            {
                var prefab = UniMobViewContext.Loader.LoadViewPrefab(viewRefence);
                var go = Object.Instantiate(prefab.gameObject);
                go.name = "TextMeshPro Measurer -- " + viewRefence;
                Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                s_textMeshProMeasurers.Add(viewRefence, go.GetComponent<UniMobTextMeshProBehaviour>());
                s_styleSheet = TMP_Settings.defaultStyleSheet;
            }
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EditorInitialize()
        {
            if (s_textMeshProMeasurers != null)
            {
                foreach (var pair in s_textMeshProMeasurers)
                {
                    if (pair.Value != null)
                    {
                        Object.Destroy(pair.Value.gameObject);
                    }
                }
            }

            s_textMeshProMeasurers.Clear();
            s_styleSheet = null;
            s_sizeCache.Clear();
        }
#endif

        private Vector2 GetPreferredSize(float maxWidth, float maxHeight)
        {
            if (s_textMeshProMeasurers == null || s_styleSheet == null) return Vector2.zero;
            var key = new PreferredSizeCacheKey
            {
                Text = _state.Value,
                MaxWidth = maxWidth,
                MaxHeight = maxHeight,
                FontSize = _state.FontSize,
                FontWeight = _state.FontWeight,
                Style = _state.Style,
                MaxLines = _state.MaxLines,
                ViewReference = _state.View
            };

            if (s_sizeCache.TryGetValue(key, out var cachedSize)) return cachedSize;

            var measurer = s_textMeshProMeasurers[_state.View];
            if (measurer == null)
            {
                Debug.LogWarning("The TextMeshPro measurer is null. This should not happen.");
                return Vector2.zero;
            }

            // Configure the static sizer with all properties
            measurer.fontSize = _state.FontSize;
            measurer.fontWeight = _state.FontWeight;
            measurer.textStyle = _state.Style;
            measurer.enableWordWrapping = _state.WrappingEnabled;
            measurer.overflowMode = _state.OverflowMode;


            var fullSize = measurer.GetPreferredValues(_state.Value, maxWidth, maxHeight);

            // The above full size is computed ignoring the max lines property. This is a limitation of TMP_Pro.
            // We recover here by manually computing the line height from the font face info and altering our
            // preferred size. 
            var maxLines = _state.MaxLines;
            if (maxLines is < int.MaxValue and > 0)
            {
                var font = measurer.font;
                var fontScale = measurer.fontSize / font.faceInfo.pointSize;
                var fontLineHeight = font.faceInfo.lineHeight * fontScale;

                var additionalLineSpacing = measurer.lineSpacing;

                // The total height of N lines is (N * font_line_height) + ((N-1) * additional_spacing).
                var maxLinesHeight = maxLines * fontLineHeight + Mathf.Max(0, maxLines - 1) * additionalLineSpacing;

                // 4. The final height is the SMALLER of the two.
                fullSize.y = Mathf.Min(fullSize.y, maxLinesHeight);
            }

            s_sizeCache[key] = fullSize;
            return fullSize;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            // The text must be sized based on its content, respecting the constraints.
            var preferredSize = GetPreferredSize(constraints.MaxWidth, constraints.MaxHeight);
            return constraints.Constrain(preferredSize);
        }

        protected override float ComputeIntrinsicHeight(float width)
        {
            return GetPreferredSize(width, float.PositiveInfinity).y;
        }

        protected override float ComputeIntrinsicWidth(float height)
        {
            return GetPreferredSize(float.PositiveInfinity, height).x;
        }


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

            public bool Equals(PreferredSizeCacheKey other)
            {
                return Text == other.Text && MaxWidth.Equals(other.MaxWidth) && MaxHeight.Equals(other.MaxHeight) &&
                       FontSize == other.FontSize && FontWeight == other.FontWeight
                       && Style?.hashCode == other.Style?.hashCode && MaxLines == other.MaxLines &&
                       ViewReference.Equals(other.ViewReference);
            }

            public override bool Equals(object? obj)
            {
                return obj is PreferredSizeCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Text, MaxWidth, MaxHeight, FontSize, (int) FontWeight, Style?.hashCode,
                    MaxLines, ViewReference);
            }
        }
    }
}
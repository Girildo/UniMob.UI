using TMPro;
using UniMob.UI.Diagnostics;
using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI.Widgets
{
    public class Text : StatefulWidget
    {
        public WidgetViewReference? ViewReference { get; init; }

        public string Value { get; init; } = string.Empty;
        public Color? Color { get; init; }

        /// <summary>
        ///     Animates the text's colour, overriding <see cref="Color"/> while set.
        /// </summary>
        /// <remarks>
        ///     Colour is the one animation that cannot be a wrapper effect: <see cref="CompositeTransition"/>
        ///     covers opacity, position, scale and rotation because each is a single CanvasGroup or Transform
        ///     operation on a subtree, whereas a tint has to be written onto every Graphic. So it belongs on
        ///     the leaf that owns the graphic, which is this widget. It is sampled in the view's render scope,
        ///     so a tick repaints without rebuilding or re-laying out anything.
        /// </remarks>
        public IAnimation<Color>? AnimatedColor { get; init; }
        public int? FontSize { get; init; }
        public string? StyleName { get; init; }

        public FontWeight? FontWeight { get; init; }
        public HorizontalTextAlignment? HorizontalTextAlignment { get; init; }
        public bool? WrappingEnabled { get; init; }
        public TextOverflowModes? OverflowMode { get; init; }

        public TMP_StyleSheet? StyleSheet { get; init; }

        public int? MaxLines { get; init; }

        /// <summary>
        ///     Forces the text to a fixed square size. Useful for icons or fixed-size text elements.
        ///     If not set, the text will size itself based on its content.
        /// </summary>
        public float? FixedSize { get; init; }

        public override State CreateState()
        {
            return new TextState();
        }

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderText((TextState)state);
        }

        // The one label every tree needs: a screen full of Text nodes is otherwise indistinguishable.
        // Short, because a chain of ancestors carries up to twelve of these on one line.
        public override string? GetDiagnosticInfo() => DiagnosticNode.Truncate(this.Value, 24);
    }

    public class TextState : ViewState<Text>, ITextState
    {
        private TMP_StyleSheet StyleSheet => Widget.StyleSheet ?? TMP_Settings.defaultStyleSheet;

        // Exposing all properties for the View, resolving defaults from context.
        public string Value => Widget.Value;
        public Color Color => Widget.Color ?? Color.white;

        public IAnimation<Color> AnimatedColor =>
            Widget.AnimatedColor ?? new ConstAnimation<Color>(Color);
        public int FontSize => Widget.FontSize ?? 14;

        public float? FixedSize => Widget.FixedSize;

        public TMP_Style Style
        {
            get
            {
                if (
                    !string.IsNullOrEmpty(Widget.StyleName)
                    && StyleSheet?.GetStyle(Widget.StyleName) is { } style
                )
                    return style;

                return TMP_Style.NormalStyle;
            }
        }

        public int MaxLines => Widget.MaxLines ?? int.MaxValue;

        public FontWeight FontWeight => Widget.FontWeight ?? FontWeight.Regular;

        public HorizontalTextAlignment HorizontalTextAlign =>
            Widget.HorizontalTextAlignment ?? HorizontalTextAlignment.Left;

        public bool WrappingEnabled => Widget.WrappingEnabled ?? true;
        public TextOverflowModes OverflowMode => Widget.OverflowMode ?? TextOverflowModes.Ellipsis;

        public override WidgetViewReference View =>
            Widget.ViewReference ?? WidgetViewReference.Registered("UniMob.LayoutTextView");
    }
}

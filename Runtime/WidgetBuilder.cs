using System;

namespace UniMob.UI
{
    /// <summary>
    /// Builds the widget for one slot. The constraint permits a nullable <typeparamref name="TWidget"/>
    /// so that a builder can answer with no widget at all, which empties the slot.
    /// </summary>
    public delegate TWidget WidgetBuilder<out TWidget>(BuildContext context)
        where TWidget : Widget?;

    /// <summary>Builds the widget at one index of a list.</summary>
    public delegate Widget IndexedWidgetBuilder(BuildContext context, int index);

    /// <summary>
    /// Builds the widget for one slot from a piece of local state and a setter that replaces it.
    /// Calling <paramref name="setState"/> stores the new value and rebuilds the slot.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="TState"/> is invariant: it is read as a value and written through
    /// <see cref="Action{T}"/>.
    /// </remarks>
    public delegate Widget StatefulWidgetBuilder<TState>(
        BuildContext context,
        TState state,
        Action<TState> setState
    );
}

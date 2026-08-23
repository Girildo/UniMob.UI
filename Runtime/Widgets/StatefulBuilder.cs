using System;

namespace UniMob.UI.Widgets
{
    /// <summary>
    /// Builds a subtree that owns one piece of local state, without declaring a widget and a state
    /// class for it. The builder is handed the current value and a setter; calling the setter stores
    /// the new value and rebuilds the subtree.
    /// </summary>
    /// <remarks>
    /// <see cref="InitialValue"/> seeds the state on the first build only. Rebuilding this widget
    /// with a different initial value leaves the stored value alone, because the state belongs to
    /// the position in the tree rather than to the widget describing it. Give the widget a
    /// different <see cref="StatefulWidget.Key"/> to replace the state and seed it again.
    /// <para>
    /// <typeparamref name="TState"/> is a single slot. Local state with more than one field goes in
    /// a record or a tuple.
    /// </para>
    /// </remarks>
    public class StatefulBuilder<TState> : StatefulWidget
    {
        public StatefulBuilder(TState initialValue, StatefulWidgetBuilder<TState> builder)
        {
            InitialValue = initialValue;
            Builder = builder;
        }

        /// <summary>The value the state holds until the setter first replaces it.</summary>
        public TState InitialValue { get; }

        public StatefulWidgetBuilder<TState> Builder { get; }

        public override State CreateState() => new StatefulBuilderState<TState>();
    }

    internal class StatefulBuilderState<TState> : HocState<StatefulBuilder<TState>>
    {
        // Assigned in InitState, which runs after Update has supplied the widget to read the seed
        // from. Nothing builds before then.
        private MutableAtom<TState> _value = null!;

        public override void InitState()
        {
            base.InitState();

            _value = Atom.Value(StateLifetime, Widget.InitialValue);
        }

        public override Widget Build(BuildContext context) =>
            Widget.Builder.Invoke(context, _value.Value, SetValue)
            ?? throw new InvalidOperationException(
                "The builder of a StatefulBuilder returned null. A builder with nothing to show "
                    + "returns an Empty."
            );

        // The setter is called from inside the builder, so an unguarded write would let the
        // computed that is building take a dependency on the atom it is writing.
        private void SetValue(TState value)
        {
            using (Atom.NoWatch)
            {
                _value.Value = value;
            }
        }
    }
}

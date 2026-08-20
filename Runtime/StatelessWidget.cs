using System;
using UniMob.UI;
using UniMob.UI.Rendering;

namespace UniMob.UI
{
    /// <summary>
    /// A StatelessWidget is a widget that does not own mutable state.
    /// </summary>
    public abstract class StatelessWidget : Widget
    {
        private Type? _type;

        public Type Type => _type ?? (_type = GetType());

        public Key? Key { get; init; }

        public abstract Widget Build(BuildContext context);

        // Unreachable: StatelessElement owns a proxy over the widget it builds and never asks the
        // widget for a render object. Present only because Widget declares it.
        public RenderObject CreateRenderObject(BuildContext context, IState state) =>
            throw new System.NotSupportedException(
                "A StatelessWidget's render object is the proxy owned by its StatelessElement."
            );

        /// <inheritdoc/>
        public virtual string? GetDiagnosticInfo() => null;

        public State? CreateState(StateProvider provider) => null;

        public State CreateState()
        {
            return new StatelessElement(this);
        }
    }

    internal sealed class StatelessElement : State, ISingleChildLayoutState
    {
        private readonly StateHolder _stateHolder;
        private readonly MutableAtom<StatelessWidget> _widget;

        // Build is declared to return a widget, so the holder always has a state to forward to.
        public override IViewState InnerViewState => _stateHolder.Value!.InnerViewState;
        public IState? Child => _stateHolder.Value;

        // Owns a proxy over the widget it builds, rather than exposing that widget's own render
        // object. See HocState.CreateOwnRenderObject for why forwarding had to go.
        internal sealed override RenderObject CreateOwnRenderObject() => new RenderProxy(this);

        public StatelessElement(StatelessWidget widget)
        {
            _widget = Atom.Value(widget);
            _stateHolder = Create<Widget, IState>(
                StateLifetime,
                new BuildContext(this, Context),
                BuildChild
            );
        }

        private Widget BuildChild(BuildContext context)
        {
            // Reads _widget.Value, establishing a reactive dependency.
            // When the widget is replaced in Update(), this computed re-runs.
            return _widget.Value.Build(context);
        }

        internal override void Update(Widget widget)
        {
            base.Update(widget);

            if (widget is StatelessWidget statelessWidget)
            {
                _widget.Value = statelessWidget;
            }
            else
            {
                throw new InvalidOperationException(
                    "StatelessElement can only update with a StatelessWidget"
                );
            }
        }
    }
}

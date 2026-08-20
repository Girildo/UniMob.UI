using System;
using JetBrains.Annotations;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI
{
    /// <summary>
    /// A StatelessWidget is a widget that does not own mutable state.
    /// </summary>
    public abstract class StatelessWidget : Widget
    {
        private Type _type;

        public Type Type => _type ?? (_type = GetType());

        [CanBeNull] public Key Key { get; set; }

        public abstract Widget Build(BuildContext context);


        // Unreachable: StatelessElement owns a proxy over the widget it builds and never asks the
        // widget for a render object. Present only because Widget declares it.
        public RenderObject CreateRenderObject(BuildContext context, IState state) =>
            throw new System.NotSupportedException(
                "A StatelessWidget's render object is the proxy owned by its StatelessElement."
            );

        /// <inheritdoc/>
        [CanBeNull]
        public virtual string GetDiagnosticInfo() => null;

        [CanBeNull]
        public State CreateState(StateProvider provider) => null;

        [NotNull]
        public State CreateState()
        {
            return new StatelessElement(this);
        }
    }


    internal sealed class StatelessElement : State, ISingleChildLayoutState
    {
        private readonly StateHolder _stateHolder;
        private readonly MutableAtom<StatelessWidget> _widget = Atom.Value(default(StatelessWidget));

        public override IViewState InnerViewState => _stateHolder.Value.InnerViewState;
        public IState Child => _stateHolder.Value;

        // Owns a proxy over the widget it builds, rather than exposing that widget's own render
        // object. See HocState.CreateOwnRenderObject for why forwarding had to go.
        internal sealed override RenderObject CreateOwnRenderObject() => new RenderProxy(this);

        public StatelessElement(StatelessWidget widget)
        {
            _widget.Value = widget;
            _stateHolder = Create<Widget, IState>(StateLifetime, new BuildContext(this, Context), BuildChild);
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
                throw new InvalidOperationException("StatelessElement can only update with a StatelessWidget");
            }
        }
    }
}
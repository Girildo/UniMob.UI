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


        public RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return state.InnerViewState.RenderObject;
        }

        public virtual string? GetDiagnosticsInfo() => null;

        [CanBeNull]
        public State CreateState(StateProvider provider) => null;

        [NotNull]
        public State CreateState()
        {
            return new StatelessElement(this);
        }
    }


    internal sealed class StatelessElement : State
    {
        private readonly StateHolder _stateHolder;
        private readonly MutableAtom<StatelessWidget> _widget = Atom.Value(default(StatelessWidget));

        public override IViewState InnerViewState => _stateHolder.Value.InnerViewState;
        public override WidgetSize Size => _stateHolder.Value.Size;
        internal sealed override void InitRenderObject()
        {
            
        }

        public sealed override RenderObject RenderObject => _stateHolder.Value?.RenderObject;

        public StatelessElement(StatelessWidget widget)
        {
            _widget.Value = widget;
            _stateHolder = Create<Widget, IState>(StateLifetime, new BuildContext(this, Context), BuildChild);
        }

        public override string GetDiagnosticInfo()
        {
            return this._widget.Value?.GetDiagnosticsInfo();
        }

        private Widget BuildChild(BuildContext context)
        {
            // Reads _widget.Value, establishing a reactive dependency.
            // When the widget is replaced in Update(), this computed re-runs.
            return _widget.Value.Build(context);
        }

        internal sealed override void UpdateConstraints(LayoutConstraints constraints)
        {
            base.UpdateConstraints(constraints);
            _stateHolder.Value.UpdateConstraints(constraints);
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
using System;
using UniMob.UI;
using UniMob.UI.Rendering;
using UnityEngine.Assertions;

namespace UniMob.UI
{
    public interface IHocState : IState { }

    public abstract class HocState<TWidget> : State, IHocState, ISingleChildLayoutState
        where TWidget : Widget
    {
        private readonly StateHolder _child;
        private readonly MutableAtom<TWidget?> _widget = Atom.Value(default(TWidget));

        // Written by Update, which runs before anything can build; the atom starts empty only so
        // that first write registers as a change.
        protected TWidget Widget => _widget.Value!;

        // Build is declared to return a widget, so the holder always has a state to forward to.
        public sealed override IViewState InnerViewState => _child.Value!.InnerViewState;

        public IState? Child => _child.Value;

        // A higher order state builds a child but paints nothing itself, so it owns a proxy over that
        // child rather than exposing the child's render object. Exposing it would make one render
        // object reachable from two states, and both would drive it. The proxy also fixes the
        // ordering: it is a plain field set at InitRenderObject, so a parent can write constraints
        // without forcing a build, and the build happens afterwards when the proxy's sizing pass pulls
        // Child. Resolving a forwarded render object would itself be the build, inverting that.
        internal sealed override RenderObject CreateOwnRenderObject() => new RenderProxy(this);

        protected HocState()
        {
            _child = Create<Widget, IState>(StateLifetime, new BuildContext(this, Context), Build);
        }

        internal sealed override void Update(Widget widget)
        {
            if (widget is not TWidget typedWidget)
            {
                throw new WrongStateTypeException(GetType(), typeof(TWidget), widget.GetType());
            }

            var oldWidget = _widget.Value;

            _widget.Value = typedWidget;

            base.Update(widget);

            if (oldWidget != null)
            {
                DidUpdateWidget(oldWidget);
            }
        }

        public abstract Widget Build(BuildContext context);

        public virtual void DidUpdateWidget(TWidget oldWidget)
        {
            Assert.IsNull(Atom.CurrentScope);
        }
    }
}

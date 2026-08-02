using System;
using JetBrains.Annotations;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine.Assertions;

namespace UniMob.UI
{
    public interface IHocState : IState
    {
    }

    public abstract class HocState<TWidget> : State, IHocState, ISingleChildLayoutState
        where TWidget : Widget
    {
        private readonly StateHolder _child;
        private readonly MutableAtom<TWidget> _widget = Atom.Value(default(TWidget));

        protected TWidget Widget => _widget.Value;

        public sealed override WidgetSize Size => InnerViewState.Size;

        public sealed override IViewState InnerViewState => _child.Value.InnerViewState;

        public IState Child => _child.Value;

        // A higher order state builds a child but paints nothing itself, so it owns a proxy over that
        // child rather than exposing the child's own render object. Exposing it made one render object
        // reachable from two states, both of which then drove it. A proxy also fixes the ordering:
        // resolving a forwarded RenderObject *was* the build, so constraints could only be written
        // after it, whereas a proxy is a plain field set at InitRenderObject and the build happens
        // later, when the proxy's sizing pass pulls Child.
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

            var oldWidget = Widget;

            _widget.Value = typedWidget;

            base.Update(widget);

            if (oldWidget != null)
            {
                DidUpdateWidget(oldWidget);
            }
        }

        

        public abstract Widget Build(BuildContext context);

        public virtual void DidUpdateWidget([NotNull] TWidget oldWidget)
        {
            Assert.IsNull(Atom.CurrentScope);
        }
    }
}
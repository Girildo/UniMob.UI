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

    public abstract class HocState<TWidget> : State, IHocState
        where TWidget : Widget
    {
        private readonly StateHolder _child;
        private readonly MutableAtom<TWidget> _widget = Atom.Value(default(TWidget));

        protected TWidget Widget => _widget.Value;

        public sealed override WidgetSize Size => InnerViewState.Size;

        public sealed override IViewState InnerViewState => _child.Value.InnerViewState;

        public IState Child => _child.Value;

        internal sealed override void InitRenderObject()
        {
            // A higher order state does not have a render object -- it forwards the accessor to its child.
        }
        public sealed override RenderObject RenderObject => _child.Value?.RenderObject;

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

        

        internal sealed override void UpdateConstraints(LayoutConstraints constraints)
        {
            base.UpdateConstraints(constraints); // Keep HocState's own atoms happy
            Child?.UpdateConstraints(constraints);
        }

        public abstract Widget Build(BuildContext context);

        public virtual void DidUpdateWidget([NotNull] TWidget oldWidget)
        {
            Assert.IsNull(Atom.CurrentScope);
        }
    }
}
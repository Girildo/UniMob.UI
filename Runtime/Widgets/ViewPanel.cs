using UniMob.UI.Internal;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Widgets
{
    [AddComponentMenu("UniMob/Views/ViewPanel")]
    public sealed class ViewPanel : View<IViewState>
    {
        private Atom<LayoutConstraints> _layoutConstraints;

        // The state this panel roots its layout chain at. Distinct from State, which is that state's
        // InnerViewState: a panel wrapping a build-only widget has an outer state that owns the chain
        // and an inner one that owns the view, and they are not the same object.
        //
        // A plain field read from the reactive Render() below, which is safe only because of the
        // order the two Renders run in: the caller sets this and pushes constraints in Render(state)
        // before base.Render reaches Render(). A reactive re-run in between reuses the last state,
        // which is the same one -- the caller passes a fresh state by calling Render(state) again,
        // which reassigns this first. The null fallback below covers the one case the ordering does
        // not: a render before any caller has passed a state at all.
        private IState _layoutRoot;

        private ViewMapperBase _mapper;

        internal override bool TriggerViewMountEvents => false;

        public void Render(IState state, bool link = false)
        {
            _layoutConstraints ??= CreateLayoutConstraints();
            _layoutRoot = state;

            // Lay out from the outer state, which owns the top of this panel's layout chain. Pushing
            // to one state and driving from another only lines up when something forwards constraints
            // between them; each state owns its own render object, so the push belongs where the chain
            // starts. InnerViewState is passed on purely for view mapping.
            state.RenderObject.Layout(_layoutConstraints.Get());

            base.Render(state.InnerViewState, link);
        }

        protected override void Activate()
        {
            base.Activate();

            if (_mapper == null)
                _mapper = new PooledViewMapper(transform);
        }

        protected override void Render()
        {
            using (var render = _mapper.CreateRender())
            {
                var child = State;

                var finalSize = (_layoutRoot ?? child).RenderObject.WatchLayout();

                var childView = render.RenderItem(child);


                LayoutData layout;
                layout.Size = finalSize;
                if (State.RenderObject is RenderLegacy)
                {
                    layout.Alignment = Alignment.Center;
                    layout.Corner = Alignment.Center;
                }
                else
                {
                    layout.Alignment = Alignment.TopLeft;
                    layout.Corner = Alignment.TopLeft;
                }
                layout.CornerPosition = Vector2.zero;
                ViewLayoutUtility.SetLayout(childView.rectTransform, layout);

            }
        }

        private Atom<LayoutConstraints> CreateLayoutConstraints()
        {
            // In the Unity UI we cannot calculate the size immediately (it will be broken sometimes),
            // therefore we use an atom for delayed computation and invalidate it when necessary.
            return Atom.Computed(ViewLifetime, () =>
            {
                var panelSize = rectTransform.rect.size;
                var panelConstraints = LayoutConstraints.Tight(panelSize.x, panelSize.y);
                return panelConstraints;
            });
        }

        private void InvalidateLayoutConstraints()
        {
            using (Atom.NoWatch)
            {
                _layoutConstraints?.Invalidate();
            }
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();

            InvalidateLayoutConstraints();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();

            InvalidateLayoutConstraints();
        }
    }
}
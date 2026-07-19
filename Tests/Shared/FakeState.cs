using System;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Base for hand-rolled fake <see cref="IState"/> implementations used to unit-test a RenderObject
    ///     against a specific state interface (e.g. <c>IFlexContainerState</c>, <c>IZStackState</c>)
    ///     without mounting a real widget. Every member defaults to throwing: a RenderObject under test
    ///     should only ever touch the handful of members its own state interface actually adds, and a fake
    ///     that silently returned a default for something it was never supposed to read would hide a bug
    ///     instead of failing the test. Subclasses override only what their target RenderObject reads.
    /// </summary>
    public abstract class FakeState : IState
    {
        public virtual Lifetime StateLifetime => Lifetime.Eternal;

        public virtual Key Key => throw new NotImplementedException();
        public virtual Widget RawWidget => throw new NotImplementedException();
        public virtual RenderObject RenderObject => throw new NotImplementedException();
        public virtual LayoutConstraints Constraints => throw new NotImplementedException();
        public virtual BuildContext Context => throw new NotImplementedException();
        public virtual IViewState InnerViewState => throw new NotImplementedException();
        public virtual WidgetSize Size => throw new NotImplementedException();

        public virtual void UpdateConstraints(LayoutConstraints constraints) => throw new NotImplementedException();
        public virtual Vector2 WatchedPerformLayout() => throw new NotImplementedException();
        public virtual Vector2 WatchedSize() => throw new NotImplementedException();
        public virtual string GetDiagnosticInfo() => null;

        // ILayoutMetricsState members (the single-/multi-child layout state interfaces now extend it).
        // A RenderObject under test never reads a widget's rendered geometry, so -- like the rest of this
        // fake -- these throw unless a subclass deliberately overrides them.
        public virtual Vector2 LocalSize => throw new NotImplementedException();
        public virtual Rect LocalRect => throw new NotImplementedException();
        public virtual bool TryGetGlobalGeometry(out WidgetGeometry geometry) => throw new NotImplementedException();
        public virtual WidgetGeometry GlobalGeometry => throw new NotImplementedException();
    }
}

using System;
using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
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
        public virtual BuildContext Context => throw new NotImplementedException();
        public virtual IViewState InnerViewState => throw new NotImplementedException();

        public virtual string GetDiagnosticInfo() => null;

        // A state fake owes nothing about layout beyond naming the render object it owns. Pushing
        // constraints, observing size and reading constraints back all belong to RenderObject, and
        // geometry to either the render object (local) or IViewState (on-screen).
    }
}

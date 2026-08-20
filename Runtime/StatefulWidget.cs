using System;
using UniMob.UI.Rendering;

namespace UniMob.UI
{
    public abstract class StatefulWidget : Widget
    {
        private Type? _type;

        public Type Type => _type ?? (_type = GetType());

        public Key? Key { get; set; }

        public virtual State? CreateState(StateProvider provider)
        {
            return provider.Of(this);
        }

        public virtual State? CreateState()
        {
            return null;
        }

        /// <summary>
        /// Creates the lightweight RenderObject responsible for layout calculations.
        /// </summary>
        /// <remarks>
        /// The fallback is a throw rather than the inner view state's render object. Forwarding one
        /// would make a single render object reachable from two states, and both would drive it --
        /// the aliasing every build-only state was given its own proxy to avoid. Unreachable in
        /// practice: a state that is not an <see cref="IViewState"/> is a build-only wrapper, and
        /// both of those (HocState, StatelessElement) seal CreateOwnRenderObject and never ask a
        /// widget for one.
        /// </remarks>
        public virtual RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            throw new NotSupportedException(
                $"{GetType().Name} has no view to render and no render object of its own. A state "
                    + "that builds rather than paints must own a proxy over its child (see "
                    + "HocState.CreateOwnRenderObject), not borrow its child's."
            );
        }

        /// <inheritdoc/>
        public virtual string? GetDiagnosticInfo() => null;
    }
}

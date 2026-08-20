using UniMob.UI;
using UniMob.UI.Internal;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Mounts and lays out widgets with no Unity View/GameObject involved, for RenderObjects whose
    ///     construction doesn't eagerly resolve a real View (composite/pure-layout widgets like
    ///     <c>Column</c>/<c>Row</c>/<c>ZStack</c>/<c>Positioned</c>, plus <see cref="FixedSizeBox"/>).
    ///     Confirmed via <see cref="StateUtilities.InflateWidget"/>: mounting only calls
    ///     <c>CreateState</c>/<c>Mount</c>/<c>Update</c>/<c>InitState</c>/<c>InitRenderObject</c> -- none of
    ///     which touch a GameObject unless the specific RenderObject's own constructor does (e.g. RenderText).
    /// </summary>
    public static class TestHarness
    {
        // BuildContext.Parent == null is a valid root (see BuildContext.cs) -- no DI/app bootstrapping
        // is needed to mount a widget tree purely for layout testing.
        private static readonly BuildContext Root = new BuildContext(null, null);

        // Atom.NoWatch mirrors StateCollectionHolder.ComputeStates: reconciling a widget into a state
        // is a mutation, not a dependency, so it must not register against whatever computation happens
        // to be running. Load-bearing when a test mounts children from inside a sizing pass, as the
        // virtualized sliver fixtures do -- sizing now runs inside a tracked scope, and StateUtilities
        // asserts it is never reconciling within one.
        public static State Mount(Widget widget)
        {
            using (Atom.NoWatch)
            {
                return StateUtilities.UpdateChild(Root, null, widget);
            }
        }

        // State.Update(Widget) is internal, so tests can't call it directly even on a State subclass
        // they got back from Mount(). StateUtilities.UpdateChild is the public equivalent: when
        // `existing`'s widget can be updated in place (same Key/Type as newWidget), it just calls
        // Update(newWidget) and returns the same instance -- the passed-in context is irrelevant on
        // that path (see StateUtilities.UpdateChild), so reusing Root here is safe.
        public static State Update(State existing, Widget newWidget)
        {
            using (Atom.NoWatch)
            {
                return StateUtilities.UpdateChild(Root, existing, newWidget);
            }
        }

        public static Vector2 Layout(State state, LayoutConstraints constraints)
        {
            return state.RenderObject.Layout(constraints);
        }

        public static Vector2 MountAndLayout(Widget widget, LayoutConstraints constraints)
        {
            return Layout(Mount(widget), constraints);
        }

        // -- Reactive drivers ----------------------------------------------------------------------
        //
        // Layout/MountAndLayout above call the render object directly, which is what the render-object
        // fixtures want: they assert sizing arithmetic and have no interest in who scheduled the pass.
        // That path bypasses the reactive layer entirely, so it cannot see a render object being driven
        // twice, or constraints arriving by the wrong route.
        //
        // The three below go through the reactive layer instead, applying the pulls the runtime
        // applies. They are the only place that knows how layout is driven, so when ownership of the
        // layout atom moves, this is the sole thing that changes and every assertion built on it keeps
        // its meaning.
        //
        // None of them advances time. A frame belongs to the clock -- TestZone.Pump and its relatives --
        // and these are the layout pulls a frame happens to contain.

        /// <summary>
        ///     Applies a parent's layout push, as <c>RenderObject.LayoutChild</c> does.
        /// </summary>
        internal static Vector2 DriveLayout(State state, LayoutConstraints constraints)
        {
            return state.RenderObject.Layout(constraints);
        }

        /// <summary>
        ///     Applies the view pass, as <c>View.DoRender</c> does on the state backing a view.
        /// </summary>
        internal static void DriveViewPass(State state)
        {
            state.InnerViewState.RenderObject.WatchLayout();
        }

        /// <summary>
        ///     Applies both layout pulls, in runtime order. Where a build-only wrapper is involved these
        ///     reach the same render object, which is what makes double-driving visible.
        /// </summary>
        internal static void DriveLayoutAndView(State state, LayoutConstraints constraints)
        {
            DriveLayout(state, constraints);
            DriveViewPass(state);
        }
    }
}

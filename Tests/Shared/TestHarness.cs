using UniMob.UI.Internal;
using UniMob.UI.Layout;
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

        public static State Mount(Widget widget)
        {
            return StateUtilities.UpdateChild(Root, null, widget);
        }

        // State.Update(Widget) is internal, so tests can't call it directly even on a State subclass
        // they got back from Mount(). StateUtilities.UpdateChild is the public equivalent: when
        // `existing`'s widget can be updated in place (same Key/Type as newWidget), it just calls
        // Update(newWidget) and returns the same instance -- the passed-in context is irrelevant on
        // that path (see StateUtilities.UpdateChild), so reusing Root here is safe.
        public static State Update(State existing, Widget newWidget)
        {
            return StateUtilities.UpdateChild(Root, existing, newWidget);
        }

        public static Vector2 Layout(State state, LayoutConstraints constraints)
        {
            state.RenderObject.PerformLayoutImmediate(constraints);
            return state.RenderObject.Size;
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
        // The three below go through the reactive layer instead, reproducing what the runtime actually
        // does in a frame. They are the only place that knows how layout is driven, so when ownership of
        // the layout atom moves, this is the sole thing that changes and every assertion built on it
        // keeps its meaning.

        /// <summary>
        ///     Applies a parent's layout push, as <c>RenderObject.LayoutChild</c> does.
        /// </summary>
        public static Vector2 DriveLayout(State state, LayoutConstraints constraints)
        {
            var target = (IState)state;
            target.UpdateConstraints(constraints);
            return target.WatchedSize();
        }

        /// <summary>
        ///     Applies the view pass, as <c>View.DoRender</c> does on the state backing a view.
        /// </summary>
        public static void DriveViewPass(State state)
        {
            state.InnerViewState.WatchedPerformLayout();
        }

        /// <summary>
        ///     Applies both pulls a real frame makes, in runtime order. Where a build-only wrapper is
        ///     involved these reach the same render object, which is what makes double-driving visible.
        /// </summary>
        public static void DriveFrame(State state, LayoutConstraints constraints)
        {
            DriveLayout(state, constraints);
            DriveViewPass(state);
        }
    }
}

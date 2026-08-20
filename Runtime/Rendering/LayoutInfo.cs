#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNIMOB_UI_FORCE_DIAGNOSTICS
#define UNIMOB_UI_DIAGNOSTICS
#endif

using UniMob.UI.Diagnostics;
using UnityEngine;

namespace UniMob.UI.Rendering
{
    /// <summary>
    ///     A pure C# data class that holds all the necessary information for performing layout for a widget.
    ///     It is decoupled from MonoBehaviour and the Unity rendering pipeline.
    /// </summary>
    public struct LayoutInfo
    {
        public Vector2 Size;
        public Vector2 Position;

#if UNIMOB_UI_DIAGNOSTICS
        /// <summary>
        ///     Set while this child is implicated in a layout fault, for the in-scene stripe.
        /// </summary>
        /// <remarks>
        ///     A code rather than a message, and written by the reporting facade rather than by an
        ///     algorithm. It was a string that two sites pipe-appended to in place, which made the
        ///     buffer a second place layout diagnostics were composed -- and the reason this struct is
        ///     not pure geometry is the repair policy, not the wording: something has to say that a
        ///     zero-sized widget was zeroed rather than empty.
        /// </remarks>
        public LayoutIssueCode? Issue;
#endif
    }
}

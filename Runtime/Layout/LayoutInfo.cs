using UnityEngine;

namespace UniMob.UI.Layout
{
    /// <summary>
    ///     A pure C# data class that holds all the necessary information for performing layout for a widget.
    ///     It is decoupled from MonoBehaviour and the Unity rendering pipeline.
    /// </summary>
    public struct LayoutInfo
    {
        public Vector2 Size;
        public Vector2 Position;

#if UNITY_EDITOR
        public string? DebugWarning;
#endif
    }
}
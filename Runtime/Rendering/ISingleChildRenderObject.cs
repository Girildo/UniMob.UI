using UnityEngine;

namespace UniMob.UI.Rendering
{
    public interface ISingleChildRenderObject
    {
        Vector2 ChildSize { get; }
        Vector2 ChildPosition { get; }

        LayoutInfo ChildLayout { get; }
    }
}

using System.Collections.Generic;
using UniMob.UI.Widgets;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public interface IMultiChildrenRenderObject
    {
        IReadOnlyList<LayoutData> ChildrenLayout { get; }
    }
}
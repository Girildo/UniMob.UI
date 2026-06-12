using System.Collections.Generic;


namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public interface IMultiChildrenRenderObject
    {
        IReadOnlyList<LayoutInfo> ChildrenLayout { get; }
    }
}
using UnityEngine.UI;

namespace UniMob.UI.Layout.Internal.Utilities
{
    /// <summary>
    /// A graphic component that registers Unity Pointer events but draws absolutely nothing.
    /// Prevents GPU overdraw while allowing the GestureDetector to catch rays.
    /// </summary>
    public class InvisibleRaycastTarget : Graphic
    {
        public override void SetMaterialDirty() { }

        public override void SetVerticesDirty() { }

        // Tells the GPU to draw zero vertices for this object.
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
        }
    }
}

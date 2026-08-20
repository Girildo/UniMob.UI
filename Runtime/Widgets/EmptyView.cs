using UniMob.UI;
using UniMob.UI.Internal;
using UniMob.UI.Widgets;
using UnityEngine;

[assembly: RegisterComponentViewFactory(
    "UniMob.EmptyView",
    typeof(RectTransform),
    typeof(EmptyView)
)]

namespace UniMob.UI.Widgets
{
    internal class EmptyView : View<IEmptyState>
    {
        protected override void Render() { }
    }
}

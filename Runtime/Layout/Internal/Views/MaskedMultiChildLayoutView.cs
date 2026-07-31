using UniMob.UI.Internal;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine;
using UnityEngine.UI;

// The plain multi-child layout view with a RectMask2D in front of it, and nothing else: clipping is
// PAINTING, not layout, so it needs no render object and no view class of its own -- only one more
// component on the object the factory builds.
//
// It is a second registration rather than a flag on the first because the factory builds from a fixed
// component set. A widget that wants the mask chooses it by switching its View reference, which is a
// reactive read, so the choice can still change at runtime.
[assembly: RegisterComponentViewFactory("$$_Layout.MaskedMultiChildLayoutView",
    typeof(RectTransform),
    typeof(RectMask2D),
    typeof(MultiChildLayoutView))]

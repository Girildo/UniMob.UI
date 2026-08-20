using UniMob.UI;
using UniMob.UI.Internal;
using UniMob.UI.Internal.Views;
using UnityEngine;
using UnityEngine.UI;

// The plain multi-child layout view with a RectMask2D in front of it, and nothing else: clipping is
// PAINTING, not layout, so it needs no render object and no view class of its own -- only one more
// component on the object the factory builds.
//
// It has to be a second registration rather than a toggle on the first, because a factory builds from
// a fixed component set, and MultiChildLayoutView is shared with Column/Row/ZStack -- putting a mask
// there would hang one off every column in the app. A widget asks for whichever of the two it needs.
// Tabs is currently the only one that needs this one, and needs it unconditionally.
[assembly: RegisterComponentViewFactory(
    "UniMob.MaskedMultiChildLayoutView",
    typeof(RectTransform),
    typeof(RectMask2D),
    typeof(MultiChildLayoutView)
)]

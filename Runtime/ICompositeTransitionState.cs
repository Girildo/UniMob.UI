using UnityEngine;

namespace UniMob.UI
{
    internal interface ICompositeTransitionState : ISingleChildLayoutState
    {
        IAnimation<float> Opacity { get; }
        IAnimation<Vector2> Position { get; }
        IAnimation<Vector3> Scale { get; }
        IAnimation<Quaternion> Rotation { get; }
    }
}

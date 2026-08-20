using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Shared base for fakes of the many state interfaces that extend <see cref="ISingleChildLayoutState"/>
    ///     with exactly one or two extra members (<c>IConstrainedBoxState</c>, <c>IPaddingState</c>,
    ///     <c>IPositionedBoxState</c>, <c>IAspectRatioState</c>, <c>IIntrinsicSizeState</c>, ...). Subclasses
    ///     just add their own extra property/properties.
    /// </summary>
    public class FakeSingleChildLayoutState : FakeState, ISingleChildLayoutState
    {
        public IState Child { get; set; }
    }
}

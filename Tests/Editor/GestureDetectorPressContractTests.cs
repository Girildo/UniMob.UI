using System;
using NUnit.Framework;
using UniMob.UI.Internal.Views;

namespace UniMob.UI.Tests
{
    /// <summary>
    /// Which pointer receivers a <c>GestureDetector</c> installs is a contract rather than an
    /// implementation detail: a detector that handles taps has to own the press as well, or the
    /// nearest button above it takes the press instead -- pressing for a gesture that is not its
    /// own, and swallowing the tap under an input module that requires one object to own both ends.
    /// Drag stays out of it, because the drag target is resolved separately and claiming it would
    /// starve any scroll view above.
    /// </summary>
    public class GestureDetectorPressContractTests
    {
        private class FakeGestureDetectorState : FakeState, IGestureDetectorState
        {
            public Action<TapDetails> OnTap { get; set; }
            public Action<PointerDetails> OnPointerDown { get; set; }
            public Action<PointerDetails> OnPointerUp { get; set; }
            public Action<DragDetails> OnDragUpdate { get; set; }
            public Action<PointerDetails> OnPointerMove { get; set; }

            public IState Child => throw new NotImplementedException();
        }

        [Test]
        public void TapHandlerTakesThePress()
        {
            var state = new FakeGestureDetectorState { OnTap = _ => { } };

            Assert.IsTrue(
                GestureDetectorView.HandlesPress(state),
                "a detector that handles the tap must take the press, or an ancestor button takes it"
            );
        }

        [Test]
        public void PressHandlerTakesThePress()
        {
            var state = new FakeGestureDetectorState { OnPointerDown = _ => { } };

            Assert.IsTrue(GestureDetectorView.HandlesPress(state));
        }

        [Test]
        public void ReleaseHandlerTakesThePress()
        {
            var state = new FakeGestureDetectorState { OnPointerUp = _ => { } };

            Assert.IsTrue(GestureDetectorView.HandlesPress(state));
        }

        [Test]
        public void DragOnlyDetectorLeavesThePress()
        {
            var state = new FakeGestureDetectorState { OnDragUpdate = _ => { } };

            Assert.IsFalse(
                GestureDetectorView.HandlesPress(state),
                "a drag-only detector must leave the press to the button it sits inside"
            );
        }

        [Test]
        public void MoveOnlyDetectorLeavesThePress()
        {
            var state = new FakeGestureDetectorState { OnPointerMove = _ => { } };

            Assert.IsFalse(GestureDetectorView.HandlesPress(state));
        }

        [Test]
        public void IdleDetectorTakesNothing()
        {
            Assert.IsFalse(GestureDetectorView.HandlesPress(new FakeGestureDetectorState()));
        }
    }
}

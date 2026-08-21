using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UniMob.UI.Internal.Views;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    /// A receiver is added to a view that gets pooled and reparented, so the canvas above it when it
    /// is created is not the canvas above it when it is tapped -- and at creation there may be no
    /// canvas at all. Resolving one once and keeping it therefore costs a whole gesture to a null.
    /// </summary>
    public class GestureDetectorReceiverCanvasTests
    {
        private GameObject canvasObject;
        private GameObject receiverObject;

        [TearDown]
        public void TearDown()
        {
            if (receiverObject != null)
                Object.DestroyImmediate(receiverObject);
            if (canvasObject != null)
                Object.DestroyImmediate(canvasObject);
        }

        [Test]
        public void ReportsATapWhenTheCanvasArrivedAfterTheReceiver()
        {
            // The pooled order: the component exists before the view is put under a canvas.
            this.receiverObject = new GameObject("detector", typeof(RectTransform));
            var receiver = this.receiverObject.AddComponent<GestureDetectorTapReceiver>();

            this.canvasObject = MakeCanvas();
            this.receiverObject.transform.SetParent(this.canvasObject.transform, false);

            TapDetails captured = null;
            receiver.OnTap = details => captured = details;
            receiver.OnPointerClick(Click(new Vector2(120, 240)));

            Assert.IsNotNull(
                captured,
                "a tap must be reported once the receiver is under a canvas"
            );
        }

        [Test]
        public void MeasuresTapsAgainstTheCanvasItEndsUpUnder()
        {
            this.canvasObject = MakeCanvas();
            this.receiverObject = new GameObject("detector", typeof(RectTransform));
            this.receiverObject.transform.SetParent(this.canvasObject.transform, false);
            var receiver = this.receiverObject.AddComponent<GestureDetectorTapReceiver>();

            var taps = new List<TapDetails>();
            receiver.OnTap = taps.Add;
            receiver.OnPointerClick(Click(new Vector2(100, 100)));
            receiver.OnPointerClick(Click(new Vector2(140, 100)));

            // Where the origin sits depends on where the canvas is; the SPAN between two taps does
            // not, at scale 1 -- so this asserts a real conversion happened without pinning the box.
            Assert.That(
                taps[1].LocalPosition.x - taps[0].LocalPosition.x,
                Is.EqualTo(40f).Within(0.01f)
            );
        }

        [Test]
        public void SaysSoWhenThereIsNoCanvasAtAll()
        {
            this.receiverObject = new GameObject("orphan", typeof(RectTransform));
            var receiver = this.receiverObject.AddComponent<GestureDetectorTapReceiver>();

            LogAssert.Expect(LogType.Error, new Regex("no Canvas"));

            TapDetails captured = null;
            receiver.OnTap = details => captured = details;
            receiver.OnPointerClick(Click(new Vector2(10, 10)));

            Assert.IsNotNull(captured, "the gesture still happened; only its position is unknown");
            Assert.AreEqual(Vector2.zero, captured.LocalPosition);
        }

        private static GameObject MakeCanvas() =>
            new GameObject("canvas", typeof(RectTransform), typeof(Canvas));

        private static PointerEventData Click(Vector2 screenPoint) =>
            new PointerEventData(EventSystem.current) { position = screenPoint };
    }
}

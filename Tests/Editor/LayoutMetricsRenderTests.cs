using NUnit.Framework;
using UniMob.UI.Layout;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // Coverage for the view-capture + rendered-geometry path added to ViewState and exposed via
    // ILayoutMetricsState. A real RectTransform under a real (scaleFactor-1 overlay) Canvas is driven
    // into the mounted state through DidViewMount, so TryGetGlobalGeometry reads a genuine on-screen
    // box. The reactive GlobalGeometry/frame-tick path is exercised by the manual smoke test in the
    // feature plan (it spins up a persistent ticker object, which we keep out of the automated suite).
    public class LayoutMetricsRenderTests
    {
        private GameObject _canvasGo;

        [SetUp]
        public void SetUp()
        {
            _canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            _canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_canvasGo);
        }

        private RectTransform MakeRect(Vector2 size)
        {
            var go = new GameObject("Rect", typeof(RectTransform));
            var rt = (RectTransform) go.transform;
            rt.SetParent(_canvasGo.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0);
            rt.sizeDelta = size;
            rt.anchoredPosition = new Vector2(30, 40);
            return rt;
        }

        [Test]
        public void TryGetGlobalGeometry_TracksMountAndUnmount()
        {
            var state = (ILayoutMetricsState) TestHarness.Mount(
                new SizedBox { Width = 100, Height = 50, Child = new FixedSizeBox { Size = new Vector2(10, 10) } });

            Assert.IsFalse(state.TryGetGlobalGeometry(out _), "an unmounted state reports no geometry");

            var rt = MakeRect(new Vector2(100, 50));
            var view = new FakeView(rt);
            ((IViewState) state).DidViewMount(view);

            Assert.IsTrue(state.TryGetGlobalGeometry(out var geo));
            Assert.That(geo.Size.x, Is.EqualTo(100f).Within(0.01f));
            Assert.That(geo.Size.y, Is.EqualTo(50f).Within(0.01f));

            // scaleFactor-1 overlay canvas: canvas-space corners equal the rect's raw world corners,
            // and their order must match GetWorldCorners (BL, TL, TR, BR).
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Assert.That(geo.CanvasSpace.BottomLeft.x, Is.EqualTo(corners[0].x).Within(0.01f));
            Assert.That(geo.CanvasSpace.BottomLeft.y, Is.EqualTo(corners[0].y).Within(0.01f));
            Assert.That(geo.CanvasSpace.TopRight.x, Is.EqualTo(corners[2].x).Within(0.01f));
            Assert.That(geo.CanvasSpace.TopRight.y, Is.EqualTo(corners[2].y).Within(0.01f));

            ((IViewState) state).DidViewUnmount(view);
            Assert.IsFalse(state.TryGetGlobalGeometry(out _), "geometry is gone once the view unmounts");
        }

        [Test]
        public void LocalSize_ReflectsLaidOutSize()
        {
            var state = (ILayoutMetricsState) TestHarness.Mount(
                new SizedBox { Width = 120, Height = 80, Child = new FixedSizeBox { Size = new Vector2(10, 10) } });

            // The root harness supplies no constraints (they default to zero), so set loose ones the
            // SizedBox can size itself within.
            ((IState) state).UpdateConstraints(LayoutConstraints.Loose(1000, 1000));

            Assert.That(state.LocalSize.x, Is.EqualTo(120f).Within(0.01f));
            Assert.That(state.LocalSize.y, Is.EqualTo(80f).Within(0.01f));
            Assert.AreEqual(Vector2.zero, state.LocalRect.position);
            Assert.That(state.LocalRect.width, Is.EqualTo(120f).Within(0.01f));
            Assert.That(state.LocalRect.height, Is.EqualTo(80f).Within(0.01f));
        }

        // Minimal IView carrying a real RectTransform, so we can drive DidViewMount without the full
        // render pipeline (mirrors how tests fake collaborators around the framework's view seam).
        private sealed class FakeView : IView
        {
            private readonly RectTransform _rectTransform;

            public FakeView(RectTransform rectTransform) => _rectTransform = rectTransform;

            public GameObject gameObject => _rectTransform.gameObject;
            public RectTransform rectTransform => _rectTransform;
            public bool IsDestroyed => _rectTransform == null;
            public void SetSource(IState source, bool link) { }
            public void ResetSource() { }
        }
    }
}

using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class RenderLegacyTests
    {
        // IViewState isn't ISingleChildLayoutState-derived, so this doesn't build on
        // FakeSingleChildLayoutState. Size/StateLifetime need to be configurable per test, but they're
        // only-a-getter virtuals on FakeState -- an override can't add a setter to those, so this exposes
        // differently-named settable properties that the overrides read from instead.
        private class FakeLegacyState : FakeState, IViewState
        {
            public WidgetSize FakeSize { get; set; }
            public Lifetime? FakeLifetime { get; set; }

            public override WidgetSize Size => FakeSize;
            public override Lifetime StateLifetime => FakeLifetime ?? Lifetime.Eternal;

            public WidgetViewReference View => throw new System.NotImplementedException();

            public void DidViewMount(IView view) => throw new System.NotImplementedException();

            public void DidViewUnmount(IView view) => throw new System.NotImplementedException();

            public IView MountedView => throw new System.NotImplementedException();

            // On-screen geometry is a Unity-boundary query owned by IViewState. A render object under
            // test never asks for it, so it throws like the rest of this fake.
            public bool TryGetGlobalGeometry(out WidgetGeometry geometry) =>
                throw new System.NotImplementedException();

            public WidgetGeometry GlobalGeometry => throw new System.NotImplementedException();
        }

        [Test]
        public void ConstrainsLegacySize_ToIncomingConstraints()
        {
            var state = new FakeLegacyState { FakeSize = WidgetSize.Fixed(50, 30) };
            var legacy = new RenderLegacy(state);

            legacy.Layout(LayoutConstraints.Loose(1000, 1000));
            Assert.AreEqual(new Vector2(50, 30), legacy.Size);

            legacy.Layout(new LayoutConstraints(0, 0, 10, 10));
            Assert.AreEqual(new Vector2(10, 10), legacy.Size);
        }

        [Test]
        public void IntrinsicSize_ReadsLegacySizeMaximums()
        {
            var state = new FakeLegacyState { FakeSize = new WidgetSize(0, 0, 80, 60) };
            var legacy = new RenderLegacy(state);

            Assert.AreEqual(80f, legacy.GetIntrinsicWidth(0f));
            Assert.AreEqual(60f, legacy.GetIntrinsicHeight(0f));
        }

        [Test]
        public void DisposedLifetime_ShortCircuitsToZero()
        {
            var controller = new LifetimeController();

            var state = new FakeLegacyState
            {
                FakeSize = WidgetSize.Fixed(50, 30),
                FakeLifetime = controller.Lifetime,
            };
            var legacy = new RenderLegacy(state);

            // Disposed after construction, not before: a render object registers atoms on its lifetime,
            // and is only ever built from InflateWidget on a freshly mounted state.
            controller.Dispose();

            legacy.Layout(LayoutConstraints.Loose(1000, 1000));

            Assert.AreEqual(Vector2.zero, legacy.Size);
        }
    }
}

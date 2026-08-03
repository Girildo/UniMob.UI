using System;
using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class RenderImageTests
    {
        private class FakeImageState : FakeState, IImageState
        {
            public Texture Texture { get; set; }
            public ImageFit Fit { get; set; } = ImageFit.Contain;

            public Color Color => throw new NotImplementedException();
            public Alignment Alignment => throw new NotImplementedException();
            public bool IsRaycastTarget => throw new NotImplementedException();
            public WidgetViewReference View => throw new NotImplementedException();

            public void DidViewMount(IView view) => throw new NotImplementedException();

            public void DidViewUnmount(IView view) => throw new NotImplementedException();

            public IView MountedView => throw new NotImplementedException();

            // On-screen geometry is a Unity-boundary query owned by IViewState. A render object under
            // test never asks for it, so it throws like the rest of this fake.
            public bool TryGetGlobalGeometry(out WidgetGeometry geometry) =>
                throw new NotImplementedException();

            public WidgetGeometry GlobalGeometry => throw new NotImplementedException();
        }

        // 2:1 aspect ratio texture, reused read-only across cases.
        private static Texture MakeTexture() => new Texture2D(100, 50);

        private static RenderImage Build(ImageFit fit, Texture texture) =>
            new RenderImage(new FakeImageState { Fit = fit, Texture = texture });

        [Test]
        public void NullTexture_CollapsesToZero()
        {
            var render = Build(ImageFit.Contain, texture: null);
            render.Layout(LayoutConstraints.Loose(200, 200));

            Assert.AreEqual(Vector2.zero, render.Size);
            Assert.AreEqual(0f, render.GetIntrinsicWidth(100f));
            Assert.AreEqual(0f, render.GetIntrinsicHeight(100f));
        }

        [Test]
        public void TightConstraints_BypassesFitMode_SizesToConstraints()
        {
            var render = Build(ImageFit.Contain, MakeTexture());
            render.Layout(LayoutConstraints.Tight(30, 30));

            Assert.AreEqual(new Vector2(30, 30), render.Size);
        }

        [Test]
        public void Fill_StretchesToFillAvailableSpace()
        {
            var render = Build(ImageFit.Fill, MakeTexture());
            render.Layout(LayoutConstraints.Loose(200, 200));

            Assert.AreEqual(new Vector2(200, 200), render.Size);
        }

        [Test]
        public void Cover_CurrentlyBehavesIdenticallyToFill()
        {
            // Documents current behavior: Fill and Cover share the same switch case, so Cover also
            // stretches to fill exactly rather than cropping while preserving aspect ratio.
            var render = Build(ImageFit.Cover, MakeTexture());
            render.Layout(LayoutConstraints.Loose(200, 200));

            Assert.AreEqual(new Vector2(200, 200), render.Size);
        }

        [Test]
        public void Contain_ScalesUniformly_ToFitInsideConstraints()
        {
            var render = Build(ImageFit.Contain, MakeTexture());
            render.Layout(LayoutConstraints.Loose(200, 200));

            // scale = min(200/100, 200/50) = 2 => (100,50) * 2
            Assert.AreEqual(new Vector2(200, 100), render.Size);
        }

        [Test]
        public void ScaleDown_DoesNotUpscale_WhenConstraintsAreLarger()
        {
            var render = Build(ImageFit.ScaleDown, MakeTexture());
            render.Layout(LayoutConstraints.Loose(200, 200));

            Assert.AreEqual(new Vector2(100, 50), render.Size);
        }

        [Test]
        public void ScaleDown_DoesScaleDown_WhenConstraintsAreSmaller()
        {
            var render = Build(ImageFit.ScaleDown, MakeTexture());
            render.Layout(LayoutConstraints.Loose(40, 40));

            // scale = min(40/100, 40/50) = 0.4 => (100,50) * 0.4
            Assert.AreEqual(new Vector2(40, 20), render.Size);
        }

        [Test]
        public void FitWidth_MatchesMaxWidth_DerivesHeightFromAspectRatio()
        {
            var render = Build(ImageFit.FitWidth, MakeTexture());
            render.Layout(LayoutConstraints.Loose(200, 200));

            Assert.AreEqual(new Vector2(200, 100), render.Size);
        }

        [Test]
        public void None_UsesIntrinsicTextureSize()
        {
            var render = Build(ImageFit.None, MakeTexture());
            render.Layout(LayoutConstraints.Loose(200, 200));

            Assert.AreEqual(new Vector2(100, 50), render.Size);
        }

        [Test]
        public void IntrinsicWidth_DerivesFromAspectRatio_WhenHeightIsFinite()
        {
            var render = Build(ImageFit.Contain, MakeTexture());

            Assert.AreEqual(200f, render.GetIntrinsicWidth(100f));
        }

        [Test]
        public void IntrinsicWidth_ReturnsTextureWidth_WhenHeightIsInfiniteOrFitIsNone()
        {
            var render = Build(ImageFit.Contain, MakeTexture());
            Assert.AreEqual(100f, render.GetIntrinsicWidth(float.PositiveInfinity));

            var noneRender = Build(ImageFit.None, MakeTexture());
            Assert.AreEqual(100f, noneRender.GetIntrinsicWidth(999f));
        }
    }
}

using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Diagnostics;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     The labels the package's own widgets answer with. Every one of these is channel 2, so it is
    ///     a plain call on an unbuilt widget -- which is also the guarantee being tested: a label is
    ///     read at the worst possible moment, and needing a mounted state is a way to fail that.
    /// </summary>
    public class WidgetLabelTests
    {
        [Test]
        public void Text_LabelsItselfWithItsValue()
        {
            Assert.AreEqual("Add to cart", new Text { Value = "Add to cart" }.GetDiagnosticInfo());
        }

        // A label is printed up to twelve deep on one line, so a paragraph in a Text is cut here rather
        // than by the backstop that exists for labels nobody thought about.
        [Test]
        public void Text_CutsAValueTooLongForAChain()
        {
            var widget = new Text { Value = "the quick brown fox jumps over the lazy dog" };

            Assert.AreEqual("the quick brown fox jump...", widget.GetDiagnosticInfo());
        }

        [Test]
        public void Image_LabelsItselfWithItsTexture()
        {
            var texture = new Texture2D(1, 1) { name = "room_thumb" };
            try
            {
                Assert.AreEqual("room_thumb", new Image { Texture = texture }.GetDiagnosticInfo());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        // An unnamed texture is the norm for one built at runtime, and "" would render as an empty
        // pair of quotes -- worse than no label, because it looks like the author had something to say.
        [Test]
        public void Image_SaysNothing_WithoutANamedTexture()
        {
            Assert.IsNull(new Image().GetDiagnosticInfo());

            var texture = new Texture2D(1, 1);
            try
            {
                Assert.IsNull(new Image { Texture = texture }.GetDiagnosticInfo());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        // The reason the override uses Unity's != null rather than ?.: a destroyed texture still has a
        // live CLR reference, so ?. would sail past the null check and throw on .name. Describing a
        // torn-down tree is exactly when this happens.
        [Test]
        public void Image_SurvivesADestroyedTexture()
        {
            var texture = new Texture2D(1, 1) { name = "gone" };
            var widget = new Image { Texture = texture };

            Object.DestroyImmediate(texture);

            Assert.IsNull(widget.GetDiagnosticInfo());
        }

        [Test]
        public void ScrollList_CountsItsChildren_WhenEager()
        {
            var widget = new ScrollList
            {
                Children = { new FixedSizeBox(), new FixedSizeBox(), new FixedSizeBox() },
            };

            Assert.AreEqual("3 items", widget.GetDiagnosticInfo());
        }

        // The lazy list holds no children at all, so the count has to come from ItemCount -- and never
        // from the state's built window, which is a computed atom that builds states when read.
        [Test]
        public void ScrollList_CountsItsItems_WhenLazy()
        {
            var widget = new ScrollList
            {
                ItemCount = 120,
                ItemBuilder = (_, _) => new FixedSizeBox(),
            };

            Assert.AreEqual("120 items", widget.GetDiagnosticInfo());
        }

        [Test]
        public void ScrollList_SaysSo_WhenEmpty()
        {
            Assert.AreEqual("0 items", new ScrollList().GetDiagnosticInfo());
            Assert.AreEqual("1 item", new ScrollList { ItemCount = 1 }.GetDiagnosticInfo());
        }

        [Test]
        public void ScrollGrid_CountsItsItems()
        {
            var widget = new ScrollGrid
            {
                ItemCount = 42,
                ItemBuilder = (_, _) => new FixedSizeBox(),
            };

            Assert.AreEqual("42 items", widget.GetDiagnosticInfo());
        }

        // Which page is showing. Every tab is laid out, the neighbours just sit outside the viewport,
        // so the tree itself never answers this.
        [Test]
        public void Tabs_LabelsThePageItIsShowing()
        {
            var controller = new TabController(Lifetime.Eternal, tabCount: 5, duration: 0f);
            var widget = new Tabs(controller);

            Assert.AreEqual("1/5", widget.GetDiagnosticInfo());

            controller.AnimateTo(2, immediate: true);

            Assert.AreEqual("3/5", widget.GetDiagnosticInfo());
        }

        [Test]
        public void AnimatedCrossFade_LabelsTheChildItIsShowing()
        {
            Assert.AreEqual("first", new AnimatedCrossFade().GetDiagnosticInfo());

            var second = new AnimatedCrossFade { CrossFadeState = CrossFadeState.ShowSecond };

            Assert.AreEqual("second", second.GetDiagnosticInfo());
        }

        [Test]
        public void AnchoredBox_NamesTheWidgetItIsAnchoredTo()
        {
            var anchor = new WidgetGeometryKey();
            TestHarness.Mount(new LabelledBox { Key = anchor });

            Assert.AreEqual(
                "-> LabelledBox",
                new AnchoredBox { Anchor = anchor }.GetDiagnosticInfo()
            );
        }

        // The ordinary first frame, and the state you are staring at when an anchored box renders
        // nothing at all -- so it is worth saying, rather than reading as "no anchor configured".
        [Test]
        public void AnchoredBox_SaysWhenItsAnchorIsNotBoundYet()
        {
            var widget = new AnchoredBox { Anchor = new WidgetGeometryKey() };

            Assert.AreEqual("-> unbound", widget.GetDiagnosticInfo());
        }

        [Test]
        public void AnchoredBox_SaysNothing_WithoutAnAnchor()
        {
            Assert.IsNull(new AnchoredBox().GetDiagnosticInfo());
        }

        // One end-to-end case, so the wiring is covered and not just the expressions: a package
        // widget's own label reaches DiagnosticNode through the state mounted for it, alongside its
        // key. A ScrollList rather than the more obvious Text -- RenderText's constructor instantiates
        // a TMP prefab, which is why its own tests live in Tests/Runtime.
        [Test]
        public void ALabelReachesTheRenderedNode()
        {
            var widget = new ScrollList
            {
                Key = Key.Of("body"),
                Children = { new FixedSizeBox(), new FixedSizeBox() },
            };

            Assert.AreEqual(
                "ScrollList#body \"2 items\"",
                DiagnosticNode.Describe(TestHarness.Mount(widget))
            );
        }
    }
}

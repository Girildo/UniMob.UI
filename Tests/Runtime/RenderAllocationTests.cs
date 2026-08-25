using System.Collections;
using NUnit.Framework;
using UniMob.Core;
using UniMob.UI.Internal.Views;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.TestTools;
// Both, and neither can go: the namespace carries the AllocatingGCMemory extension on NUnit's
// ConstraintExpression, and its own Is would otherwise collide with NUnit.Framework.Is.
using UnityEngine.TestTools.Constraints;
using ConstraintIs = UnityEngine.TestTools.Constraints.Is;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     What a repaint costs the garbage collector.
    /// </summary>
    /// <remarks>
    ///     A layout pass invalidates <c>WatchLayout</c> whether or not anything moved, so every view
    ///     under anything animating re-renders every frame. At that rate a single string per view per
    ///     render is a frame-budget item, which is what these fixtures exist to keep at zero.
    ///     <para>
    ///         Measured around <c>InvalidateLayout</c> plus a scheduler drain rather than around real
    ///         frames: that is a layout pass with no widget rebuild behind it, which is the steady
    ///         state being claimed. A rebuild allocates by design, because
    ///         <c>StateCollectionHolder</c> must hand out a fresh array for the children atom to
    ///         propagate at all.
    ///     </para>
    /// </remarks>
    public class RenderAllocationTests
    {
        private const int ChildCount = 8;
        private const int WarmupPasses = 8;

        /// <summary>Drains per repaint. An atom actualized during a drain queues work for the next.</summary>
        private const int MaxDrains = 8;

        /// <summary>Written into a child rect to prove the repaint actually reached it.</summary>
        private const float NeverPainted = -1234f;

        private LifetimeController lifetime = null!;
        private GameObject canvasGo = null!;

        [SetUp]
        public void SetUp()
        {
            this.lifetime = new LifetimeController();

            this.canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            this.canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [TearDown]
        public void TearDown()
        {
            this.lifetime.Dispose();
            Object.DestroyImmediate(this.canvasGo);
        }

        [UnityTest]
        public IEnumerator AMultiChildLayoutRepaint_AllocatesNothing()
        {
            this.Host();
            yield return this.Settle();

            var view = this.canvasGo.GetComponentInChildren<MultiChildLayoutView>(true);
            Assert.NotNull(view, "no MultiChildLayoutView mounted, so the measurement is vacuous");

            AssertRepaintAllocatesNothing(view!, nameof(MultiChildLayoutView));
        }

        [UnityTest]
        public IEnumerator ASingleChildLayoutRepaint_AllocatesNothing()
        {
            this.Host();
            yield return this.Settle();

            // The first one that actually has a child: a SizedBox.Shrink leaf renders through this
            // view too, and its Render takes the childless early return that paints nothing.
            SingleChildLayoutView? view = null;
            foreach (
                var candidate in this.canvasGo.GetComponentsInChildren<SingleChildLayoutView>(true)
            )
            {
                if (candidate.transform.childCount > 0)
                {
                    view = candidate;
                    break;
                }
            }

            Assert.NotNull(view, "no SingleChildLayoutView mounted with a child to paint");

            AssertRepaintAllocatesNothing(view!, nameof(SingleChildLayoutView));
        }

        private static void AssertRepaintAllocatesNothing(Component view, string what)
        {
            var source = ((IView)view).Source;
            Assert.NotNull(source, $"{what} is not bound to a state");

            var renderObject = source!.RenderObject;
            var painted = (RectTransform)view.transform.GetChild(0);

            // Steady state is the claim, so everything that is paid once is paid before measuring:
            // the pooled atom dependency arrays, the layout buffers, this view's profiler sampler
            // and its Editor name.
            for (var i = 0; i < WarmupPasses; i++)
            {
                Repaint(renderObject);
            }

            // The control. A render writes every child's sizeDelta unconditionally, so a rect still
            // holding the marker means nothing rendered and the zero below would be vacuous.
            painted.sizeDelta = new Vector2(NeverPainted, NeverPainted);
            Repaint(renderObject);

            Assert.AreNotEqual(
                NeverPainted,
                painted.sizeDelta.x,
                $"{what} did not repaint on an invalidated layout, so this fixture measures nothing"
            );

            Assert.That(
                () => Repaint(renderObject),
                ConstraintIs.Not.AllocatingGCMemory(),
                $"a steady-state {what}.Render must not allocate: it runs on every layout pass, for "
                    + "every view under anything that is moving"
            );
        }

        /// <summary>A layout pass with no widget rebuild behind it, run to completion.</summary>
        private static void Repaint(RenderObject renderObject)
        {
            renderObject.InvalidateLayout();

            for (var i = 0; i < MaxDrains && AtomScheduler.HasPendingWork; i++)
            {
                AtomScheduler.Sync();
            }
        }

        private IEnumerator Settle()
        {
            for (var i = 0; i < 4; i++)
            {
                yield return null;
            }
        }

        /// <summary>
        ///     A column of fixed boxes: one <see cref="MultiChildLayoutView"/> over a
        ///     <see cref="SingleChildLayoutView"/> per child, which is the pair under test.
        /// </summary>
        private void Host()
        {
            var panelGo = new GameObject("ViewPanel", typeof(RectTransform));
            panelGo.transform.SetParent(this.canvasGo.transform, false);

            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.sizeDelta = Vector2.zero;

            var panel = panelGo.AddComponent<ViewPanel>();

            UniMobUI.RunApp(
                this.lifetime.Lifetime,
                panel,
                _ =>
                {
                    var column = new Column();

                    for (var i = 0; i < ChildCount; i++)
                    {
                        column.Children.Add(new Container(width: 10, height: 10 + i));
                    }

                    return column;
                }
            );
        }
    }
}

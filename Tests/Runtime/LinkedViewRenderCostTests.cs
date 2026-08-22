using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Internal;
using UniMob.UI.Internal.Views;
using UniMob.UI.Navigation;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.TestTools;

[assembly: RegisterComponentViewFactory(
    "UniMob.Tests.ProbeLayerView",
    typeof(RectTransform),
    typeof(UniMob.UI.Tests.ProbeLayerView)
)]

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     What a view mapper's <c>link</c> flag actually costs its parent.
    /// </summary>
    /// <remarks>
    ///     <c>ViewMapperBase.EndRender</c> ends with <c>SetSource(child, link)</c>, and link decides
    ///     between a tracked <c>_doRebind.Get()</c> and an untracked <c>Actualize()</c>. So with link
    ///     the parent's render subscribes to every child's rebind, and <c>NavigatorView</c> passes
    ///     false on purpose while <c>MultiChildLayoutView</c> -- which is what an <c>Overlay</c>
    ///     renders through -- passes true.
    ///     <para>
    ///         The question this fixture answers with numbers rather than reading: does a rebuild deep
    ///         inside a linked child re-run the parent's <c>Render()</c>, and does the answer get worse
    ///         with depth or with a deeper navigator stack. Counted off a probe layer that is an
    ///         <c>Overlay</c> in every respect but two: its view counts its renders, and its link is a
    ///         knob.
    ///     </para>
    ///     <para>
    ///         The answer is no, and the reason is <c>DoRebind</c>: while the child stays bound to the
    ///         same state it returns that same state, and an atom whose value does not change does not
    ///         invalidate its subscribers. <c>RenderObject.PerformLayout</c> carries a version field
    ///         precisely to escape that rule; the rebind atom has no such escape. So the link makes the
    ///         parent pull its children's rebinds, not re-render behind them.
    ///     </para>
    ///     <para>
    ///         What is counted is <c>Render()</c> calls, which is where the RectTransform writes and
    ///         the mapper churn live. Atom re-actualization along the linked chain is not counted, and
    ///         is not free -- it is just not what a frame budget is spent on.
    ///     </para>
    /// </remarks>
    public class LinkedViewRenderCostTests
    {
        private LifetimeController lifetime = null!;
        private GameObject canvasGo = null!;

        [SetUp]
        public void SetUp()
        {
            this.lifetime = new LifetimeController();

            this.canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = this.canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            ProbeLayerView.Renders = 0;
            ProbeLayerView.Link = true;
        }

        [TearDown]
        public void TearDown()
        {
            this.lifetime.Dispose();
            Object.DestroyImmediate(this.canvasGo);
        }

        [UnityTest]
        public IEnumerator ADeepRebuildUnderALinkedChild_CostsTheParentNothing()
        {
            var source = Atom.Value(0);

            this.Host(_ => new ProbeLayer { Children = { Deep(8, source) } });

            yield return this.Settle();

            var before = ProbeLayerView.Renders;

            Debug.Log($"[link cost] probe renders after mount: {before}");
            Assert.Greater(
                before,
                0,
                "the probe layer never rendered, so every count below would be a vacuous zero"
            );

            source.Value += 1;
            yield return this.Settle();

            var linked = ProbeLayerView.Renders - before;

            // Same tree, same change, without the link.
            this.Rehost(linked: false, out var unlinkedSource);
            yield return this.Settle();

            var unlinkedBefore = ProbeLayerView.Renders;

            unlinkedSource.Value += 1;
            yield return this.Settle();

            var unlinked = ProbeLayerView.Renders - unlinkedBefore;

            Debug.Log(
                $"[link cost] deep rebuild, depth 8: linked={linked} parent renders, "
                    + $"unlinked={unlinked}"
            );

            Assert.AreEqual(
                unlinked,
                linked,
                "a rebuild that leaves the child bound to the same state must not reach the parent, "
                    + "linked or not: _doRebind returns _nextState, and an unchanged value does not "
                    + "propagate"
            );
        }

        [UnityTest]
        public IEnumerator TheCostDoesNotGrowWithDepth()
        {
            var counts = new List<(int Depth, int Renders)>();

            foreach (var depth in new[] { 1, 8, 32 })
            {
                this.RehostAtDepth(depth, out var source);
                yield return this.Settle();

                var before = ProbeLayerView.Renders;

                source.Value += 1;
                yield return this.Settle();

                counts.Add((depth, ProbeLayerView.Renders - before));
            }

            Debug.Log(
                "[link cost] parent renders per deep change, by depth: "
                    + string.Join(", ", counts.ConvertAll(c => $"{c.Depth}->{c.Renders}"))
            );

            Assert.AreEqual(
                counts[0].Renders,
                counts[counts.Count - 1].Renders,
                "depth must not multiply the parent's render count"
            );
        }

        [UnityTest]
        public IEnumerator ADeepNavigatorStack_CostsTheLayerNothingPerRebuild()
        {
            var counts = new List<(int Depth, int Renders)>();

            foreach (var stackDepth in new[] { 1, 10 })
            {
                var source = Atom.Value(0);
                var navigatorKey = new GlobalKey<NavigatorState>();

                this.Rehost(() =>
                    new ProbeLayer
                    {
                        Children =
                        {
                            new Navigator(
                                "screen",
                                new Dictionary<string, System.Func<Route>>
                                {
                                    ["screen"] = () => new ProbeRoute(Deep(4, source)),
                                }
                            )
                            {
                                Key = navigatorKey,
                            },
                        },
                    }
                );

                yield return this.Settle();

                for (var i = 1; i < stackDepth; i++)
                {
                    navigatorKey.CurrentState!.Push(new ProbeRoute(Deep(4, source)));
                    yield return this.Settle();
                }

                var before = ProbeLayerView.Renders;

                source.Value += 1;
                yield return this.Settle();

                counts.Add((stackDepth, ProbeLayerView.Renders - before));
            }

            Debug.Log(
                "[link cost] layer renders per deep change, by navigator stack depth: "
                    + string.Join(", ", counts.ConvertAll(c => $"{c.Depth}->{c.Renders}"))
            );

            Assert.AreEqual(
                counts[0].Renders,
                counts[1].Renders,
                "a deeper stack must not cost the layer more per rebuild"
            );
        }

        [UnityTest]
        public IEnumerator APushReachesTheLayer()
        {
            var navigatorKey = new GlobalKey<NavigatorState>();
            var source = Atom.Value(0);

            this.Rehost(() =>
                new ProbeLayer
                {
                    Children =
                    {
                        new Navigator(
                            "screen",
                            new Dictionary<string, System.Func<Route>>
                            {
                                ["screen"] = () => new ProbeRoute(Deep(4, source)),
                            }
                        )
                        {
                            Key = navigatorKey,
                        },
                    },
                }
            );

            yield return this.Settle();

            var before = ProbeLayerView.Renders;

            navigatorKey.CurrentState!.Push(new ProbeRoute(Deep(4, source)));
            yield return this.Settle();

            var pushCost = ProbeLayerView.Renders - before;

            Debug.Log($"[link cost] layer renders per navigator push: {pushCost}");

            // Measured as zero: a push replaces what the navigator renders, not which state the
            // layer is bound to. One would be harmless; a number that grows with the stack would
            // mean the link had started cascading.
            Assert.LessOrEqual(pushCost, 1, "a push must not cascade into the layer above it");
        }

        /// <summary>
        /// The control. Something has to move this counter, or every zero above is a broken probe
        /// rather than a finding -- and this is also the cost that is actually paid, since inserting
        /// or removing an entry is exactly a change to the layer's own child list.
        /// </summary>
        [UnityTest]
        public IEnumerator ChangingTheLayersOwnChildren_DoesReRenderIt()
        {
            var source = Atom.Value(0);
            var entries = Atom.Value(0);

            this.Rehost(() =>
            {
                var layer = new ProbeLayer { Children = { Deep(4, source) } };

                for (var i = 0; i < entries.Value; i++)
                {
                    layer.Children.Add(new Container(width: 10, height: 10));
                }

                return layer;
            });

            yield return this.Settle();

            var before = ProbeLayerView.Renders;

            entries.Value += 1;
            yield return this.Settle();

            var cost = ProbeLayerView.Renders - before;

            Debug.Log($"[link cost] layer renders per entry insert: {cost}");

            Assert.Greater(cost, 0, "an entry insert must reach the layer, or the probe is broken");
        }

        // -- harness ---------------------------------------------------------------------------

        private IEnumerator Settle()
        {
            for (var i = 0; i < 4; i++)
            {
                yield return null;
            }
        }

        private void Rehost(bool linked, out MutableAtom<int> source)
        {
            var seed = Atom.Value(0);
            source = seed;
            ProbeLayerView.Link = linked;
            this.Rehost(() => new ProbeLayer { Children = { Deep(8, seed) } });
        }

        private void RehostAtDepth(int depth, out MutableAtom<int> source)
        {
            var seed = Atom.Value(0);
            source = seed;
            this.Rehost(() => new ProbeLayer { Children = { Deep(depth, seed) } });
        }

        private void Rehost(System.Func<Widget> build)
        {
            this.lifetime.Dispose();
            this.lifetime = new LifetimeController();
            this.Host(_ => build());
        }

        private void Host(WidgetBuilder<Widget> builder)
        {
            var panelGo = new GameObject("ViewPanel", typeof(RectTransform));
            panelGo.transform.SetParent(this.canvasGo.transform, false);

            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.sizeDelta = Vector2.zero;

            var panel = panelGo.AddComponent<ViewPanel>();

            UniMobUI.RunApp(this.lifetime.Lifetime, new StateProvider(), panel, builder);
        }

        /// <summary>
        ///     A leaf that reads <paramref name="source"/>, buried under <paramref name="depth"/> layers
        ///     of ordinary layout, so a change to it is as far from the probe as a page's contents are
        ///     from the overlay above them.
        /// </summary>
        private static Widget Deep(int depth, MutableAtom<int> source)
        {
            Widget widget = new Builder(_ =>
            {
                source.Get();
                return new Container(width: 10, height: 10);
            });

            for (var i = 0; i < depth; i++)
            {
                widget = new ZStack { Children = { widget } };
            }

            return widget;
        }

        private sealed class ProbeRoute : Route
        {
            private readonly Widget content;

            public ProbeRoute(Widget content)
                : base(new RouteSettings("probe", RouteModalType.Fullscreen))
            {
                this.content = content;
            }

            public override Widget Build(BuildContext context) => this.content;
        }
    }

    /// <summary>
    ///     An <see cref="Overlay"/> in everything that bears on rendering: same render object, same
    ///     mapper, same reconciliation. Its view counts its renders and takes its link from a static,
    ///     which is the whole reason it exists.
    /// </summary>
    public class ProbeLayer : StatefulWidget, IMultiChildLayoutWidget
    {
        public List<Widget> Children { get; init; } = new List<Widget>();

        public override State CreateState() => new ProbeLayerState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state) =>
            new RenderOverlay((IOverlayState)state);
    }

    public class ProbeLayerState : ViewState<ProbeLayer>, IOverlayState
    {
        private readonly StateCollectionHolder children;

        public ProbeLayerState()
        {
            this.children = CreateChildren(_ => Widget.Children);
        }

        public IState[] Children => this.children.Value;

        public override WidgetViewReference View =>
            WidgetViewReference.Registered("UniMob.Tests.ProbeLayerView");
    }

    public class ProbeLayerView : View<IOverlayState>
    {
        /// <summary>How many times any probe layer has rendered since a test reset it.</summary>
        public static int Renders;

        /// <summary>Read once per view, at Awake, as the real views read their own literal.</summary>
        public static bool Link = true;

        private ViewMapperBase mapper = null!;

        protected override void Awake()
        {
            base.Awake();
            this.mapper = new PooledViewMapper(transform, link: Link);
        }

        protected override void Render()
        {
            Renders++;

            if (State.RenderObject is not IMultiChildrenRenderObject renderObject)
                return;

            var childrenLayout = renderObject.ChildrenLayout;
            var children = State.Children;

            using var render = this.mapper.CreateRender();

            for (var i = 0; i < children.Length; i++)
            {
                var layout = childrenLayout[i];
                var childView = render.RenderItem(children[i]);
                var rt = childView.rectTransform;

                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(0, 1);

                var pivotOffset = new Vector2(
                    layout.Size.x * rt.pivot.x,
                    -layout.Size.y * (1.0f - rt.pivot.y)
                );

                rt.sizeDelta = layout.Size;
                rt.anchoredPosition =
                    new Vector2(layout.Position.x, -layout.Position.y) + pivotOffset;

                if (rt.GetSiblingIndex() != i)
                    rt.SetSiblingIndex(i);
            }
        }
    }
}

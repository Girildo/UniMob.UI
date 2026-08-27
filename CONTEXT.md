# Vocabulary

Words this package uses in a specific sense, where the ordinary reading would mislead.

| Term | Meaning |
| --- | --- |
| **zone** | The frame clock: the tickers plus the next-frame queue. Not Dart's error-interception zone, despite the name. |
| **fault** | An exception that escaped a lifecycle callback, reported to `UniMobError`. |
| **layout issue** | A layout-protocol violation, reported to `UniMobDiagnostics`. A different thing from a fault, with a different audience. |
| **tick** | One ticker invocation, given a delta. Produces invalidations and consumes nothing. |
| **animation ticker** | A ticker registered through `AddAnimationTicker`, so it exists only while something is moving, and is counted by `Zone.RunningAnimations`. A plain `AddTicker` is a polling ticker and is not counted. Both are `Action<float>`, so the distinction lives in the registry, not in the type. |
| **pump** | One whole frame: tickers, the next-frame queue, the scheduler, continuations, the geometry ticker. |
| **settle** | Pump until nothing is left to do, or fail at a deadline. Not a frame count. |
| **pull** | A render object being driven, either by its parent with constraints or by its view through `WatchLayout`. |
| **reaction root** | An `AutoActualize` atom with no subscribers. The only kind `AtomScheduler.Sync()` drives. |

## Trees

There are two, not three. Flutter is Widget -> Element -> RenderObject; here it is Widget -> State,
with the render object welded onto the state.

**Every state owns exactly one render object.** Always: a build-only state owns a `RenderProxy` over
its child rather than none, which is what makes it an ordinary link in the layout chain instead of a
second driver of somebody else's render object. The layout links are not edges of their own either --
they are `ISingleChildLayoutState.Child` and `IMultiChildLayoutState.Children`, properties on the
state, reached as `child.RenderObject`. Rendering is 1:1 with the state tree, so who owns a state and
who lays it out are the same edge.

**What the collapse buys.** One reconciliation keeps everything consistent: `UpdateChildren`
maintains ownership and layout structure in a single act, and the view pass derives GameObject
parentage straight from `State.Children`. Nothing adopts or drops a render object, and no two
structures can disagree about where a child belongs.

**What it costs.** A state's layout position is not separable from its ownership, so there is no way
to own a child you do not render. That rules out rendering a subtree away from its owner, relayout
boundaries, and render-object reuse across rebuilds. It is also why `RenderObject`'s memo is
defensive rather than incidental: it lives on the render object "no matter how many states can see
it" because a build-only wrapper can make one object reachable from two. That tolerance holds only
while those states push the same constraints. Two drivers pushing different constraints invalidate
each other's memo on every pass.

**The 1:1 is not a consequence of the reactivity.** Layout is a push -- a child's constraints are an
output of the parent's algorithm, so there is no per-child constraints getter to pull, and the atoms
memoize that push without reversing it. The two directions have different causes, and only one of
them constrains the architecture:

- **render -> state** is the reactive link: a render object reads its children through its owning
  state and holds its atoms on that state's lifetime.
- **state -> render** is traversal, because `LayoutChild` takes an `IState` and resolves
  `child.RenderObject`. This is the half that welds layout position to ownership.

**The context chain is a mirror, not a third tree.** `BuildContext.Parent` is written once, at
`Mount`, from the creating state's context, and `InflateWidget` is the only caller. There are two
context nodes per state -- the state's own, plus one slot node per child holder it creates -- so an
ancestor walk visits every state twice, and a walker must know which node it wants: skipping
duplicates steps `Parent.Parent`, skipping only itself steps `Parent` once. Nothing forces the chain
to mirror the state tree. It does because of what that single `Mount` call is handed.

**The view tree is derived output.** A view links into its parent as a side effect of the render
pass, through `ViewRenderScope` and the ambient `UniMobViewContext.CurrentElement`, so a view cannot
be rendered without being linked. It is not isomorphic to the state tree: `ScrollListView` parents
its items to a prefab-internal `contentRoot`, and pooling reparents a recycled view to a
`DontDestroyOnLoad` pool root. This is the tree that owns z-order, clipping, raycasting, opacity and
transitions, and the other two model none of them -- an effect applied through a `CanvasGroup` or a
`RectMask2D` reaches exactly the GameObject subtree beneath it and nothing else.

**If a third tree is ever wanted.** Give `RenderObject` its own parent and child links so layout
stops traversing through states, keeping `Owner` as the reactive link only. That is Flutter's
arrangement, and it removes the two-driver problem outright, since one render parent means one
constraint source.

Changing every render object to take a child render object instead of an `IState` is the easy part.
The cost is in three places that are structural rather than mechanical:

- **The render tree becomes something that must be maintained.** Today it is not maintained at all:
  it *is* the state tree, so reconciliation produces it as a side effect and it cannot drift. Give it
  its own links and every insert, removal and reorder needs an explicit adopt and drop. That is a
  standing class of bug rather than a one-time edit, and it is why frameworks that work this way
  carry so many assertions about it.
- **The view layer has to follow the render tree instead of `State.Children`.** GameObject parentage
  is what supplies z-order, clipping and raycasting, and it is currently read off the state's
  children during the render pass. Once render position can differ from state position, the mappers,
  the sibling-index ordering and the ambient unmount linking all have to be re-pointed at the new
  tree.
- **Lifetime gains two states it does not have.** Attached-but-not-owned, and owned-but-detached.
  Both need handling, and neither is expressible today.

In exchange it buys relayout boundaries, render-object reuse across rebuilds, and the ability to
render a subtree away from its owner. The collapse is a deliberate trade of that ceiling for the
guarantee that the three structures cannot disagree.

## Frames

Three drivers advance a frame, and they run in this order:

1. `Zone` — tickers, then the next-frame queue.
2. `AtomScheduler` — actualizes what those invalidated.
3. `WidgetGeometryTicker` — bumps the geometry atom.

The order is **measured, not assumed**. `ZoneDriverTests` asserts it against the real player loop, and
`ZoneDriver` pins it with `[DefaultExecutionOrder]` so it cannot drift: both Update-phase components
otherwise sat at the default order, and `AtomScheduler`'s GameObject is created lazily on first
actualize, so a component registered mid-frame could reorder them.

A tick produces invalidations and consumes none, so the dependency runs one way: tick, then sync.
Driver order is therefore a latency question rather than a correctness one -- but the navigator's
golden traces encode that latency, which is why it is pinned.

`TestZone.Pump` reproduces the same order, with one addition. Asynchronous continuations run between
the scheduler and the geometry ticker, because that is where the player loop runs them:
`ScriptRunDelayedTasks` follows every `Update` and precedes every `LateUpdate`. Several completers in
`Navigation` are built with `RunContinuationsAsynchronously`, so without draining them a fixture
would block forever on a task waiting for a frame the fixture was itself preventing.

## Testing

`Testing/` is a consumable assembly (`UniMob.UI.Testing`, namespace `UniMob.UI.Tests`), not a test assembly. It is constrained to
`UNITY_INCLUDE_TESTS`, so nothing in it reaches a player build, and an app can build widget tests
against it.

Almost everything runs in EditMode. `Tests/Runtime` holds only what needs the real player loop:
`ZoneDriverTests`, which proves the real frame driver drives -- the one thing a fake clock cannot
establish about itself -- plus the allocation, view-render-cost and `ScrollRect` offset fixtures,
which measure Unity objects a `TestHarness` mount never creates.

`WidgetSnapshotter`, in `Testing/`, is the consumable that needs PlayMode: it renders a mounted tree
through a camera, which needs the real player loop. It settles on real frames rather
than through `TestZone`, and on a narrower condition -- the scheduler is idle and the next-frame
queue is empty, with tickers excluded. `UniMobUI.RunApp` always mounts a `UniMobDeviceWidget`, whose
ticker polls the screen for as long as the tree is mounted, so `IsSettled` would never come true for
anything hosted the way the app hosts it.

# Vocabulary

Words this package uses in a specific sense, where the ordinary reading would mislead.

| Term | Meaning |
| --- | --- |
| **zone** | The frame clock: the tickers plus the next-frame queue. Not Dart's error-interception zone, despite the name. |
| **fault** | An exception that escaped a lifecycle callback, reported to `UniMobError`. |
| **layout issue** | A layout-protocol violation, reported to `UniMobDiagnostics`. A different thing from a fault, with a different audience. |
| **tick** | One ticker invocation, given a delta. Produces invalidations and consumes nothing. |
| **pump** | One whole frame: tickers, the next-frame queue, the scheduler, continuations, the geometry ticker. |
| **settle** | Pump until nothing is left to do, or fail at a deadline. Not a frame count. |
| **pull** | A render object being driven, either by its parent with constraints or by its view through `WatchLayout`. |
| **reaction root** | An `AutoActualize` atom with no subscribers. The only kind `AtomScheduler.Sync()` drives. |

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

`Testing/` is a consumable assembly (`UniMob.UI.Testing`), not a test assembly. It is constrained to
`UNITY_INCLUDE_TESTS`, so nothing in it reaches a player build, and an app can build widget tests
against it.

Almost everything runs in EditMode. `Tests/Runtime` holds exactly one fixture, and it exists to prove
the real frame driver drives -- the one thing a fake clock cannot establish about itself.

`WidgetSnapshotter` is the exception that keeps the PlayMode assembly earning its place: it renders a
mounted tree through a camera, which needs the real player loop. It settles on real frames rather
than through `TestZone`, and on a narrower condition -- the scheduler is idle and the next-frame
queue is empty, with tickers excluded. `UniMobUI.RunApp` always mounts a `UniMobDeviceWidget`, whose
ticker polls the screen for as long as the tree is mounted, so `IsSettled` would never come true for
anything hosted the way the app hosts it.

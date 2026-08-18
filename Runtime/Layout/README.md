# UniMob.UI modern layout layer — conventions

UniMob.UI has **two coexisting layout systems**, migrated one widget at a time:

- **Legacy** — `Runtime/Widgets/`, namespace `UniMob.UI.Widgets`. Bottom-up sizing via `ViewState<T>.CalculateSize() : WidgetSize`.
- **Modern** — `Runtime/Layout/`, namespace `UniMob.UI.Layout`. Flutter-style: the parent passes `LayoutConstraints` down, the child returns a size up, and a pure-C# `RenderObject` does the math.

This document governs the **modern layer only**. Every new modern widget must follow it. The legacy layer is frozen and untouched; the two are told apart by namespace (`using UniMob.UI.Layout` vs `using UniMob.UI.Widgets`).

## Namespaces

| Namespace | Holds |
|---|---|
| `UniMob.UI.Layout` | Public API: widgets, their `State` classes, base interfaces (`ISingleChildLayoutWidget`, `IMultiChildLayoutWidget`, `ISingleChildLayoutState`, `IMultiChildLayoutState`), public value/config types (`LayoutConstraints`, `LayoutInfo`, `SliverGridDelegate`), and widget-specific enums. |
| `UniMob.UI.Layout.Internal` | Shared internal helpers (`VirtualizedChildren`, `LayoutConstants`). |
| `UniMob.UI.Layout.Internal.RenderObjects` | `RenderObject` subclasses **and** the `I<X>State` contract interfaces they consume. |
| `UniMob.UI.Layout.Internal.Views` | Unity `MonoBehaviour` views (`*View`) plus the shared `SingleChildLayoutView` / `MultiChildLayoutView`. |
| `UniMob.UI.Layout.Internal.Diagnostics` / `.Utilities` | One private painting helper (`LayoutWarningOverlay`) and small utilities. |
| `UniMob.UI.Diagnostics` | **Public.** Describing the tree (`DiagnosticNode`, `WidgetPath`, `LayoutTree`) and reporting a fault (`LayoutIssue`, `IDiagnosticsReporter`, `UniMobDiagnostics`). Deliberately not under `Layout/Internal`: tests cannot see `internal`, and a diagnostics layer nobody outside can call is not one. |

## Where each type lives, and what it's called

| Piece | Name | File | Visibility |
|---|---|---|---|
| Widget | `<Widget>` | `Layout/<Widget>.cs` | `public` |
| State | `<Widget>State` | same file as the widget | `internal` (default) |
| State contract | `I<X>State` | in its consumer's file (render object or bespoke view) | `internal`/`public` |
| Render object | `Render<Behavior>` | `Layout/Internal/RenderObjects/` | `public` — all of them are, the `Internal` in the namespace notwithstanding |
| Bespoke view | `<Widget>View` | `Layout/Internal/Views/` | `internal` |

Rules:

- **One widget per file.** `Layout/<Widget>.cs` holds `public class <Widget>` plus its `<Widget>State`. A tightly-coupled variant family may share a single file **named after the family** (e.g. `Flexible.cs` holds `Flexible` + `Expanded` + `FlexFit` + `FlexibleState`) — but only deliberately, and never leave an orphan stub file for the absorbed variant.
- **States are `internal` by default.** Consumers construct the widget, not the state. Make a state `public` only when something outside the assembly must reference it.
- **Render objects are named by layout behavior and shared**, not 1:1 with the widget: `RenderFlex` (Row/Column), `RenderPositionedBox` (Align/Positioned), `RenderProxy` (Expanded/Flexible). Use `Render<Widget>` only when the behavior is unique to that widget (`RenderConstrainedBox`).
- **The consumer owns its required-state contract.** The `I<X>State` interface is declared in the file of whatever reads it — the render object (`IConstrainedBoxState` in `RenderConstrainedBox.cs`) or, for view-driven effects, the bespoke view (`IOpacityState` in `OpacityView.cs`). The widget's `State` implements it. This keeps the state→consumer coupling explicit and one-directional.
- **Add a bespoke view only when needed.** Widgets that need custom Unity rendering or interaction get a `<Widget>View` (see `OpacityView`, `GestureDetectorView`, `ImageView`, `ColoredImageBoxView`). Everything else lets the `State.View` fall through to the shared `$$_Layout.SingleChildLayoutView` / `$$_Layout.MultiChildLayoutView`.
- **Widget-specific enums live in the widget file** (`FlexFit`, `CrossFadeState`); value types used by more than one widget get their own top-level file.
- **A render object takes its owner state, not a lifetime.** `base(state)`, never `base(state.StateLifetime)` — the lifetime comes off the state, and `Owner` is what lets a report say which widget it is about.

## Skeleton — single-child widget

Model on `ConstrainedBox.cs`. `Child` comes from the `SingleChildLayoutWidget` base.

```csharp
using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
    public class Foo : SingleChildLayoutWidget
    {
        public float Bar { get; set; }

        public override State CreateState() => new FooState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
            => new RenderFoo((FooState) state);
    }

    internal class FooState : SingleChildLayoutState<Foo>, IFooState
    {
        public float Bar => Widget.Bar;
    }
}
```

The contract + render object live together under `Internal/RenderObjects/RenderFoo.cs`:

```csharp
namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public interface IFooState : ISingleChildLayoutState { float Bar { get; } }

    internal class RenderFoo : SingleChildRenderObject
    {
        // PerformSizing(constraints) -> Vector2 ; PerformPositioning(size) ; intrinsics
    }
}
```

## Skeleton — multi-child widget

Model on `Column.cs`. Children come via `CreateChildren(...)`; the shared `MultiChildLayoutView` renders them; a shared, behavior-named render object does the layout.

```csharp
using System.Collections.Generic;
using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
    public class Foo : StatefulWidget, IMultiChildLayoutWidget
    {
        public List<Widget> Children { get; set; } = new();

        public override State CreateState() => new FooState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
            => new RenderFooLayout((FooState) state);
    }

    internal class FooState : ViewState<Foo>, IFooLayoutState
    {
        private readonly StateCollectionHolder _children;
        public FooState() => _children = CreateChildren(context => Widget.Children);

        public IState[] Children => _children.Value;

        public override WidgetViewReference View
            => WidgetViewReference.Resource("$$_Layout.MultiChildLayoutView");
    }
}
```

## Composed & facade widgets (`HocState`)

Not every widget needs a render object. A widget defined purely in terms of other widgets uses `HocState<TWidget>` and returns a composed subtree from `Build(context)` — no render object, no view. The child builds lazily, so any `AnimationController` is created in `InitState()` (ready before the first `Build`) and reacted to in `DidUpdateWidget(old)`.

- **Composition** — build from modern widgets: `Container` → `Align`/`ColoredImageBox`/`SizedBox`; `AnimatedCrossFade` → a `ZStack` of two `CompositeTransition`s.
- **Facade over legacy** — when a legacy widget's behaviour lives in a Unity-prefab, interactive view that isn't worth reimplementing yet (`AnimatedSwitcher`, `Tabs`), wrap the legacy widget in an `HocState`; it renders under modern parents via the `RenderLegacy` bridge. Keep the public surface modern (no `WidgetSize` in the API).

## Vocabulary

**Push** — a parent handing constraints down to one child. The only way a render object is ever laid out; there is no ambient size and no pull. A child a parent's sizing pass never pushed to is not laid out small, it is not laid out at all.

**Unbounded** — of a constraint, having no finite maximum on an axis. Legal and routine: a scroll list gives its children an unbounded main axis on purpose, and a flex measures its inflexible children that way to learn their natural size.

**Materialise** — to turn an unbounded axis into a concrete number, which a render object can only do once something bounds it.

**Hug / fill** — the two answers a box that could legally be either size gives when asked to size itself. Which one it gives is decided by the constraints it was handed, never by anything at the call site: `Align` fills a bounded axis and hugs an unbounded one, so the same widget fills as a flex child (a flex hands its own cross axis down) and hugs under a scroll list. A non-null `WidthFactor`/`HeightFactor` forces hugging on that axis whatever its value — the rule is `factor != null || !bounded` — which is why `WidthFactor = 1` is the idiom for "hug" rather than the no-op multiply it reads as, and why a hugged axis has no space left for `Alignment` to move the child in. Flutter's `RenderPositionedBox` rule, kept verbatim so that its documentation applies here.

**Build-only state** — a state with no view, and therefore no `GameObject`. `HocState`, `StatelessElement`, and every state owning a `RenderProxy` (`Flexible`, `Positioned`, `Opacity`, `Clickable`, and the rest). Invisible in Unity's Hierarchy window, and disproportionately the cause of layout faults.

**Owner** — the single state a render object belongs to. A structural relationship, held by the render object, true whether or not anything is being reported.

**Subject** — the state that *noticed* a fault, in a report. A role, not a relationship. It is the reporting render object's Owner at every site that exists today; the words are kept apart because Subject is meaningful only inside a report and Owner is meaningful always.

**Culprit** — the state a report *blames*, when that differs from the Subject. A `Row` is the Subject of an overflow; the child that did not fit is the Culprit.

**Remedy** — the sentence telling a developer what to change, written next to the algorithm that knows. Belongs to the *site*, not to the code: an unbounded constraint is fixed differently in a flex, an anchored box and a pan surface.

**Edge-triggered / level-triggered** — the console is edge-triggered, emitting when a fault starts and not again while it persists. The in-scene stripe is level-triggered, true for exactly as long as the fault is happening. The stripe is the live picture and the console is the history, so a stale number in the log is not a defect.

## The materialise rule

**An infinite size out is legal exactly when the constraint in was infinite on that axis.**

Returning infinity under a finite maximum is always the render object's own fault, and `ValidateLayout` checks it at the end of every pass. Passing infinity along under infinity is passing the buck upward, and stays legal right up until it reaches a `RectTransform`, where it is fatal — which is why `MultiChildLayoutView` keeps a check at paint: that is the only place the fault is decidable.

**A non-finite axis is clamped to zero**, on that axis alone, at the point of detection, **in every build**. Zero rather than the available extent, because the available extent is exactly what is missing in the case that causes this: the fault almost always originates in an unbounded constraint, where the axis maximum *is* infinity.

Zero is only honest when something says so — a zero-sized widget is indistinguishable from an intentionally empty one — so the clamp is never adopted alone. It comes with a reported issue naming the widget, the axis and the remedy, and with the in-scene stripe. Use `NonFiniteAxes()` / `ZeroOn()` rather than hand-rolling it; four hand-rolled copies is how the four sites came to disagree in the first place.

## Labelling a widget for diagnostics

A layout error that says `Column` identifies nothing in an app with forty of them. Four channels, in increasing order of author effort, all resolved in one place — `DiagnosticNode.Describe`.

**Channel 0 — free.** The widget's type name with generic arguments expanded, its position among its siblings, and the ancestor chain: `App > HomeScreen > Padding > Row`. Enough for most errors, because the failure is usually structural rather than instance-specific.

**Channel 1 — `Key`, for a widget you did not author.** Works on a `Column` you are merely composing:

```csharp
new Column { Key = Key.Of("body"), Children = { ... } }   // renders as  Column#body
```

Two caveats, and both bite.

*It is identity, not decoration.* `CanUpdateWidget` compares `Key` and type, so adding a `Key` to a previously unkeyed widget makes the old state fail to match once: it is disposed and recreated. That is a one-time cost on the frame you add it, which is a source edit and therefore a domain reload anyway. Inside a `Children` list it switches that child from positional to keyed matching, which is usually a fix rather than a regression.

*It may already be spent.* A widget carrying a `GlobalKey` for functional identity cannot be labelled this way — adding a label would destroy identity the app depends on. Such a widget renders `#global<T>`, and must use channel 2 or 3, which it can, because a widget that needs a `GlobalKey` is one you authored.

**Channel 2 — `Widget.GetDiagnosticInfo()`, for config-derived labels.** For widgets you author, where the disambiguating information is the widget's own configuration:

```csharp
public override string? GetDiagnosticInfo() => $"{this.Icon} {this.Text}";
```

Renders as `AppButton "Cart Add to cart"`. Available on every widget, stateful included — it used to exist only on `StatelessWidget`, which is a large part of why nothing in this package ever implemented it.

**Channel 3 — `State.GetDiagnosticInfo()`, for runtime-state labels.** Defaults to the widget's, so channel 2 needs no state involvement at all. Override when runtime state is the useful thing, and compose with `base` to keep both:

```csharp
public override string GetDiagnosticInfo() =>
    $"{base.GetDiagnosticInfo()} items={_children.Value.Length} window={_first}..{_last}";
```

**The state wins**, because it is the only one of the two that can see both.

**Channel 4 — free.** Constraints in, size out, and one level of child sizes. The reporting render object already holds all of it; nothing is walked.

The contract on a label lives on `Widget.GetDiagnosticInfo`: one line, describes the instance rather than the type, may read reactive state freely (it is invoked under `Atom.NoWatch`), and must tolerate being called mid-layout, before anything has been laid out, and on a disposed state.

## Inspecting a running app

`Window > UniMob > Widget Hierarchy` shows the tree as you wrote it. Unity's own Hierarchy cannot: a
GameObject exists only where a state has a view, so every build-only widget — `HocState`, and
everything owning a `RenderProxy` (`Flexible`, `Positioned`, `Opacity`, `Clickable`,
`GestureDetector`, …) — is invisible in it, and those are disproportionately the ones that cause
layout faults. Rows come from `DiagnosticNode` and fault badges from `RenderObject.HasLayoutIssue`,
so the window agrees with the console by construction.

**Select** works like a browser's element picker, and deliberately so — it is the interaction people
already have in their fingers. `Ctrl/Cmd+Shift+C` toggles it from anywhere and opens the window if it
is closed, the same chord DevTools uses; it is listed in Edit > Shortcuts as *UniMob/Toggle Widget
Picker* and can be rebound there, and the Select button's tooltip always names the current binding.

- Arming it lays a faint wash over the app — the only persistent sign that your clicks are being
  swallowed. The widget under the pointer is marked and outlined, and **the row follows the pointer**,
  so you read the tree while sweeping rather than clicking blind.
- Clicking picks and *ends the hunt*: the dim goes, the app gets its input back, and the outline
  stays on what you found. The toggle turns itself off.
- The outline outlives picking, so selecting a row still lights that widget in the app afterwards.
  That is the other half of the loop and the reason a pick does not tear the overlay down.

**A pick asks uGUI what a click would hit**, which is the only thing that knows about masks, canvas
sorting and what actually paints. Two consequences worth knowing before they confuse you:

- **A widget that paints nothing is never the direct answer.** You land on a painted descendant and
  walk up — the same move as in a browser, and the reason this window exists.
- **A widget that takes no input cannot be picked at all**: anything below an `IgnorePointer` (whose
  view drops `blocksRaycasts`), a `CustomPaint` (whose image sets `raycastTarget = false`), or any
  Graphic with raycasting off. The raycast lands on whatever is *behind* it rather than failing, so
  nothing announces this.

That second case is what the **Boxes** toggle beside Select is for: it hit-tests layout boxes
instead, reaching widgets no click can. **Alt** does the same while you keep hovering. The toggle
exists because a held modifier is not something anyone discovers — and holding Alt presses it, which
is how the shortcut teaches itself to whoever found the button first.

**The outline turns violet whenever boxes are deciding**, by either route. A mode with different
answers and no indicator is how a tool earns a reputation for being random, and while you are picking
you are looking at the app rather than at a toolbar — so the mode has to be legible on the outline
itself.

## Reporting a fault from a render object

Never `Debug.Log` at the call site. Call a facade — `ReportOverflow`, `ReportContentOverflow`, `ReportUnboundedConstraint`, `ReportNonFiniteChildSize`, `ReportNonFiniteSize` — and pass a `const string` remedy declared next to the algorithm that knows the fix.

### Content that does not fit

`Constrain` is `Mathf.Clamp`, so the moment your desired size exceeds the maximum, the shortfall is gone: it exists for the lifetime of one expression, and nothing downstream can recover it. **If your sizing pass computes a size and then constrains it, call `ReportContentOverflow` immediately before the clamp.**

Two things keep it meaningful, and both are load-bearing:

- **An infinite desire is not a fault.** Asking for infinity and being clamped to the maximum is how a render object says "fill", and it is the most common thing they do. Only a *finite* desire beyond a finite maximum is content that will be drawn outside its box — this layer does not clip.
- **Narrow the axes where one has a legitimate reason to come up short**, via the `considered` mask. A text told not to wrap is *meant* to be wider than its box; reporting that would put one console entry on every row of a list.

Do not add the call where it can never fire. If children were laid out under `Loosen()` or `Tighten()`, they are already inside the maximum and the `Constrain` is defensive, not lossy.

### The two checks you do not have to call

Both run from `ValidateLayout` on every pass, so no render object opts into them.

**`SizeExceedsConstraints`** is the contract: *a render object answers with a size its constraints allow*. Flutter enforces this in the `RenderBox.size` setter and treats a violation as fatal; here it is reported. It matters because code all over this layer assumes it holds — `RenderConstrainedBox` returns its child's size verbatim, and several render objects skip constraining a result they know came from a child they bounded themselves.

**`ChildOutOfBounds`** is the geometry: *a child lies inside its parent's box*. It is the only check made of the finished result rather than of an algorithm's intentions, which is exactly why it catches what the others cannot. Every other code asks whether one render object's own reasoning held up, and they all trust the measurements the pass trusted; a pass whose numbers are wrong satisfies all of them while positioning a child outside the box, and this layer does not clip, so that child is then drawn there.

Two properties of it are load-bearing:

- **Overhang is counted at both edges.** Content taller than its box under a centred main axis escapes at the near edge as well as the far one.
- **It is silent whenever anything else reported in the same pass.** A flex that overflows always has children outside it too, so without that it would repeat, in vaguer words, a fault already named precisely — and `MarkCulprit` would overwrite the specific code on the child the stripe points at. It is the net under the other checks, not a second opinion on what they caught.

Override `ChildrenMayOverhang` to opt out, and only for a render object whose purpose involves painting outside itself: the slivers (positioning children beyond the viewport is what scrolling is), `RenderZStack`, `RenderAnchoredBox`, `RenderTabs`.

They are `void` and `[Conditional]`, so a release player deletes the call **and its argument expressions**: an argument that scans every child to find the largest costs nothing unless something is already wrong. They are also the one place `Atom.NoWatch` is applied on behalf of every site, which matters because a report describes the tree, the tree is atoms, and all of this runs inside a layout computation. **Reporting observes; it never participates.**

Each report is emitted **once per render object per fault**, and re-arms when that fault clears. `HasLayoutIssue` is the level-triggered counterpart, true for as long as the fault is happening — that is what the stripe is drawn from.

Severity is derived from the code, not chosen at the site: overflow warns, everything else errors. `Remedy` goes the other way and stays per-site, because the same code has genuinely different fixes in a flex, an anchored box and a pan surface.

To capture reports instead of logging them — in a test, or to route them into an application logger — install a reporter with `UniMobDiagnostics.Override(...)`, which restores the previous one when its scope is disposed.

## Known cruft (tracked, not yet removed)

These predate the conventions and are left in place for now (deletion is a later, separate pass):

- `Internal/SingleChildLayoutState.cs` — an empty non-generic `SingleChildLayoutState` class that shadows the real generic `SingleChildLayoutState<T>` base; dead.
- `Expanded.cs` — an orphan stub with a commented-out class; the real `Expanded` lives in `Flexible.cs`.

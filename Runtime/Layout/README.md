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
| `UniMob.UI.Layout.Internal.Diagnostics` / `.Utilities` | Diagnostics and small utilities. |

## Where each type lives, and what it's called

| Piece | Name | File | Visibility |
|---|---|---|---|
| Widget | `<Widget>` | `Layout/<Widget>.cs` | `public` |
| State | `<Widget>State` | same file as the widget | `internal` (default) |
| State contract | `I<X>State` | in its consumer's file (render object or bespoke view) | `internal`/`public` |
| Render object | `Render<Behavior>` | `Layout/Internal/RenderObjects/` | `internal` |
| Bespoke view | `<Widget>View` | `Layout/Internal/Views/` | `internal` |

Rules:

- **One widget per file.** `Layout/<Widget>.cs` holds `public class <Widget>` plus its `<Widget>State`. A tightly-coupled variant family may share a single file **named after the family** (e.g. `Flexible.cs` holds `Flexible` + `Expanded` + `FlexFit` + `FlexibleState`) — but only deliberately, and never leave an orphan stub file for the absorbed variant.
- **States are `internal` by default.** Consumers construct the widget, not the state. Make a state `public` only when something outside the assembly must reference it.
- **Render objects are named by layout behavior and shared**, not 1:1 with the widget: `RenderFlex` (Row/Column), `RenderPositionedBox` (Align/Positioned), `RenderProxy` (Expanded/Flexible). Use `Render<Widget>` only when the behavior is unique to that widget (`RenderConstrainedBox`).
- **The consumer owns its required-state contract.** The `I<X>State` interface is declared in the file of whatever reads it — the render object (`IConstrainedBoxState` in `RenderConstrainedBox.cs`) or, for view-driven effects, the bespoke view (`IOpacityState` in `OpacityView.cs`). The widget's `State` implements it. This keeps the state→consumer coupling explicit and one-directional.
- **Add a bespoke view only when needed.** Widgets that need custom Unity rendering or interaction get a `<Widget>View` (see `OpacityView`, `GestureDetectorView`, `ImageView`, `ColoredImageBoxView`). Everything else lets the `State.View` fall through to the shared `$$_Layout.SingleChildLayoutView` / `$$_Layout.MultiChildLayoutView`.
- **Widget-specific enums live in the widget file** (`FlexFit`, `CrossFadeState`); value types used by more than one widget get their own top-level file.

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
        // PerformSizing(constraints) -> Vector2 ; PerformPositioning() ; intrinsics
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

## Known cruft (tracked, not yet removed)

These predate the conventions and are left in place for now (deletion is a later, separate pass):

- `Internal/SingleChildLayoutState.cs` — an empty non-generic `SingleChildLayoutState` class that shadows the real generic `SingleChildLayoutState<T>` base; dead.
- `Expanded.cs` — an orphan stub with a commented-out class; the real `Expanded` lives in `Flexible.cs`.

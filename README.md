# UniMob.UI [![Github license](https://img.shields.io/github/license/Girildo/UniMob.UI.svg?style=flat-square)](#) [![Unity 6000.3](https://img.shields.io/badge/Unity-6000.3+-2296F3.svg?style=flat-square)](#)

A declarative library for building reactive user interfaces, built over [UniMob](https://github.com/codewriter-packages/UniMob).

## About this fork

This is a fork of [codewriter-packages/UniMob.UI](https://github.com/codewriter-packages/UniMob.UI) that has diverged
far enough not to be mergeable with it. The layout system was replaced with a Flutter-style one -- a parent passes
`LayoutConstraints` down, a child returns a size up, and a pure-C# `RenderObject` does the arithmetic -- and the
original bottom-up `WidgetSize` layer has been removed rather than kept alongside it.

Upstream is kept as a read-only remote for archaeology. Changes are not taken from it and are not offered back to it.

The `UniMob` core package it depends on is consumed from a fork as well
([Girildo/UniMob](https://github.com/Girildo/UniMob), version 2.6.0, which adds `AtomScheduler.HasPendingWork`).
The namespaces and the package name still say `UniMob` because both packages keep upstream's identity.

## Getting Started

#### 1. Create a view

A view is a regular MonoBehaviour attached to a GameObject.

```csharp
using UniMob.UI;

public class CounterView : View<ICounterState>
{
    public UnityEngine.UI.Text counterText;
    public UnityEngine.UI.Button incrementButton;

    protected override void Awake()
    {
        base.Awake();

        incrementButton.Click(() => State.Increment);
    }

    // Called automatically whenever the data it reads changes, so the view is always in sync.
    protected override void Render()
    {
        counterText.text = "Counter: " + State.Counter;
    }
}

// The data the view needs and the actions it performs.
public interface ICounterState : IViewState
{
    int Counter { get; }

    void Increment();
}
```

#### 2. Create a widget

A widget is an immutable description of an interface element. Its properties are `init`-only: a widget is
constructed, handed to the framework, and replaced rather than edited.

A widget that paints must also say how it measures, by returning a `RenderObject`. Forgetting to is an
exception rather than a default.

```csharp
using UniMob.UI;
using UniMob.UI.Rendering;

public class CounterWidget : StatefulWidget
{
    public int IncrementStep { get; init; } = 1;

    public override State CreateState() => new CounterState();

    public override RenderObject CreateRenderObject(BuildContext context, IState state) =>
        new RenderCounter((CounterState) state);
}
```

A widget that is defined purely in terms of other widgets needs no render object at all: derive its state from
`HocState<TWidget>` and return a subtree from `Build(context)`.

#### 3. Create a state

The state provides data to the view and owns whatever mutable state that part of the interface has.

```csharp
using UniMob;
using UniMob.UI;

public class CounterState : ViewState<CounterWidget>, ICounterState
{
    // Where the view comes from: a direct prefab link, Resources, Addressables, or a view
    // registered in source with WidgetViewReference.Registered(name).
    public override WidgetViewReference View => WidgetViewReference.Resource("Prefabs/Counter View");

    [Atom] public int Counter { get; private set; }

    public void Increment()
    {
        // Writing an atom updates the UI on its own.
        Counter += Widget.IncrementStep;
    }
}
```

#### 4. Run the app

```csharp
using UniMob;
using UniMob.UI;

public class CounterApp : UniMobUIApp
{
    protected override Widget Build(BuildContext context)
    {
        return new CounterWidget { IncrementStep = 1 };
    }
}
```

## Widget composition

Widgets are the building blocks of an interface and form a hierarchy by composition, which is what lets a complex
custom interface update itself only where it needs to.

```csharp
private Widget Build(BuildContext context)
{
    return new ScrollGrid
    {
        MaxCrossAxisExtent = 750f,
        Children =
        {
            this.BuildDailyOffers(),

            this.BuildGemsHeader(),
            this.BuildGemsSlots(),
        },
    };
}

private IEnumerable<Widget> BuildDailyOffers()
{
    return this.DailyOffersModel
        .GetAllOffers()
        .Where(offer => offer.Visible) // subscribes to the 'Visible' atom
        .Select(offer => this.BuildDailyOffer(offer.Id));
}

private Widget BuildDailyOffer(string offerId)
{
    return new DailyOfferWidget(offerId)
    {
        // List elements need keys.
        Key = Key.Of($"store_item_{offerId}"),
    };
}
```

## Built-in widgets

**Layout**

> **[Row](./Runtime/Widgets/Row.cs), [Column](./Runtime/Widgets/Column.cs)** -- lay children out along the horizontal or vertical axis.
>
> **[ZStack](./Runtime/Widgets/ZStack.cs)** -- place children on top of each other in paint order.
>
> **[Wrap](./Runtime/Widgets/Wrap.cs)** -- lay children out in a line that wraps onto the next one when it runs out of room.
>
> **[Expanded](./Runtime/Widgets/Flexible.cs), [Flexible](./Runtime/Widgets/Flexible.cs), [Spacer](./Runtime/Widgets/Spacer.cs)** -- divide a Row or Column's remaining space between its children.
>
> **[Align](./Runtime/Widgets/Align.cs), [Positioned](./Runtime/Widgets/Positioned.cs), [AnchoredBox](./Runtime/Widgets/AnchoredBox.cs)** -- place a child within the space its parent gave it.
>
> **[PaddingBox](./Runtime/Widgets/PaddingBox.cs)** -- inset a child.
>
> **[SizedBox](./Runtime/Widgets/SizedBox.cs), [ConstrainedBox](./Runtime/Widgets/ConstrainedBox.cs), [AspectRatio](./Runtime/Widgets/AspectRatio.cs), [IntrinsicWidth/IntrinsicHeight](./Runtime/Widgets/IntrinsicSize.cs)** -- impose a size or a shape on a child.
>
> **[ConstrainedBuilder](./Runtime/Widgets/ConstrainedBuilder.cs)** -- build a subtree from the constraints the parent handed down.
>
> **[Container](./Runtime/Widgets/Container.cs)** -- a rectangle with a background colour, a size and alignment.
>
> **[Empty](./Runtime/Widgets/Empty.cs)** -- occupies nothing, whatever the parent offers.

**Scrolling**

> **[ScrollList](./Runtime/Widgets/ScrollList.cs), [ScrollGrid](./Runtime/Widgets/ScrollGrid.cs)** -- virtualized scrollable list and grid, on either axis.

**Painting and input**

> **[Text](./Runtime/Widgets/Text.cs)** -- display and style text.
>
> **[Image](./Runtime/Widgets/Image.cs), [ColoredImageBox](./Runtime/Widgets/ColoredImageBox.cs)** -- display a texture, or a sprite over a colour.
>
> **[GestureDetector](./Runtime/Widgets/GestureDetector.cs)** -- recognize taps, presses, drags and pointer movement, with an invisible raycast target of its own.
>
> **[Clickable](./Runtime/Widgets/Clickable.cs)** -- make a painted child activatable through a Unity Button; it carries no raycast target of its own.
>
> **[IgnorePointer](./Runtime/Widgets/IgnorePointer.cs)** -- make a subtree invisible to hit testing.
>
> **[Opacity](./Runtime/Widgets/Opacity.cs)** -- fade a subtree.

**Animation and navigation**

> **[CompositeTransition](./Runtime/Widgets/CompositeTransition.cs)** -- animate a subtree's opacity, position, rotation and scale together.
>
> **[AnimatedCrossFade](./Runtime/Widgets/AnimatedCrossFade.cs)** -- cross-fade between two children.
>
> **[AnimatedSwitcher](./Runtime/Widgets/AnimatedSwitcher.cs)** -- animate a child out and its replacement in.
>
> **[Tabs](./Runtime/Widgets/Tabs.cs)** -- horizontally paged tabs driven by a `TabController`; swiping is not built in.
>
> **[Navigator](./Runtime/Navigation/Navigator.cs)** -- a stack of routes, with push, pop and transitions.
>
> **[Builder](./Runtime/Widgets/Builder.cs)** -- build a subtree inline from a delegate.
>
> **[StatefulBuilder](./Runtime/Widgets/StatefulBuilder.cs)** -- build a subtree that owns one piece of local state.
>
> **[Overlay](./Runtime/Widgets/Overlay.cs)** -- a layer of independently-lived entries drawn over a child, reached through `Overlay.Of(context)`.

Conventions for adding a widget are in [Runtime/README.md](./Runtime/README.md), and the words this
package uses in a specific sense are in [CONTEXT.md](./CONTEXT.md).

## Testing

`UniMob.UI.Testing` is a consumable assembly, constrained to `UNITY_INCLUDE_TESTS` so nothing in it
reaches a player build. It carries `TestZone`, a frame clock a test drives by hand, so a test that
needs frames does not need a play-mode transition:

```csharp
using UniMob.UI.Tests; // the UniMob.UI.Testing assembly's namespace

using var zone = TestZone.Install();

var state = TestHarness.Mount(new CounterWidget());
TestHarness.Layout(state, LayoutConstraints.Loose(1000, 1000));

zone.PumpFrames(30);       // 30 frames at 1/60
zone.Settle();             // or: pump until nothing is left to do
```

`RecordingReporter` and `RecordingErrors` capture layout issues and faults for the length of a
`using` block, so a test asserts on what went wrong rather than on Unity's console.

## How to install

Minimum Unity version is 6000.3.

Distributed as a git package
([how to install a package from a git URL](https://docs.unity3d.com/Manual/upm-ui-giturl.html)).

<br>Git URL (UniMob.UI): `https://github.com/Girildo/UniMob.UI.git`
<br>Git URL (UniMob): `https://github.com/Girildo/UniMob.git` (2.6.0 or later; `UniMob.UI.Testing` needs `AtomScheduler.HasPendingWork`, which upstream does not ship)

## License

UniMob.UI is [MIT licensed](./LICENSE.md).

## Credits

This fork would not have been possible without the original amazing work of [codewriter-packages](https://github.com/codewriter-packages).
UniMob.UI inspired by [Flutter](https://github.com/flutter/flutter).


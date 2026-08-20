# UniMob.UI [![Github license](https://img.shields.io/github/license/Girildo/UniMob.UI.svg?style=flat-square)](#) [![Unity 6000.3](https://img.shields.io/badge/Unity-6000.3+-2296F3.svg?style=flat-square)](#)

A declarative library for building reactive user interfaces, built over [UniMob](https://github.com/codewriter-packages/UniMob).

## About this fork

This is a fork of [codewriter-packages/UniMob.UI](https://github.com/codewriter-packages/UniMob.UI) that has diverged
far enough not to be mergeable with it. The layout system was replaced with a Flutter-style one -- a parent passes
`LayoutConstraints` down, a child returns a size up, and a pure-C# `RenderObject` does the arithmetic -- and the
original bottom-up `WidgetSize` layer has been removed rather than kept alongside it.

Upstream is kept as a read-only remote for archaeology. Changes are not taken from it and are not offered back to it.

The `UniMob` core package it depends on is *not* forked, which is why the namespaces and the package name still say
`UniMob`.

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
using UniMob.UI.Layout.Internal.RenderObjects;

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
    // Where to load the view from; supports a direct prefab link, Resources and Addressables.
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
        CrossAxisAlignment = CrossAxisAlignment.Center,
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

> **[Row](./Runtime/Layout/Row.cs), [Column](./Runtime/Layout/Column.cs)** -- lay children out along the horizontal or vertical axis.
>
> **[ZStack](./Runtime/Layout/ZStack.cs)** -- place children on top of each other in paint order.
>
> **[Wrap](./Runtime/Layout/Wrap.cs)** -- lay children out in a line that wraps onto the next one when it runs out of room.
>
> **[Expanded](./Runtime/Layout/Flexible.cs), [Flexible](./Runtime/Layout/Flexible.cs), [Spacer](./Runtime/Layout/Spacer.cs)** -- divide a Row or Column's remaining space between its children.
>
> **[Align](./Runtime/Layout/Align.cs), [Positioned](./Runtime/Layout/Positioned.cs), [AnchoredBox](./Runtime/Layout/AnchoredBox.cs)** -- place a child within the space its parent gave it.
>
> **[PaddingBox](./Runtime/Layout/PaddingBox.cs)** -- inset a child.
>
> **[SizedBox](./Runtime/Layout/SizedBox.cs), [ConstrainedBox](./Runtime/Layout/ConstrainedBox.cs), [AspectRatio](./Runtime/Layout/AspectRatio.cs), [IntrinsicWidth/IntrinsicHeight](./Runtime/Layout/IntrinsicSize.cs)** -- impose a size or a shape on a child.
>
> **[ConstrainedBuilder](./Runtime/Layout/ConstrainedBuilder.cs)** -- build a subtree from the constraints the parent handed down.
>
> **[Container](./Runtime/Layout/Container.cs)** -- a rectangle with a background colour, a size and alignment.
>
> **[Empty](./Runtime/Widgets/Empty.cs)** -- occupies nothing, whatever the parent offers.

**Scrolling**

> **[ScrollList](./Runtime/Layout/ScrollList.cs), [ScrollGrid](./Runtime/Layout/ScrollGrid.cs)** -- virtualized scrollable list and grid, on either axis.

**Painting and input**

> **[Text](./Runtime/Layout/Text.cs)** -- display and style text.
>
> **[Image](./Runtime/Layout/Image.cs), [ColoredImageBox](./Runtime/Layout/ColoredImageBox.cs)** -- display a sprite.
>
> **[GestureDetector](./Runtime/Layout/GestureDetector.cs), [Clickable](./Runtime/Layout/Clickable.cs)** -- recognize taps, presses, drags and pointer movement.
>
> **[IgnorePointer](./Runtime/Layout/IgnorePointer.cs)** -- make a subtree invisible to hit testing.
>
> **[Opacity](./Runtime/Layout/Opacity.cs)** -- fade a subtree.

**Animation and navigation**

> **[CompositeTransition](./Runtime/Layout/CompositeTransition.cs)** -- animate a subtree's opacity, position, rotation and scale together.
>
> **[AnimatedCrossFade](./Runtime/Layout/AnimatedCrossFade.cs)** -- cross-fade between two children.
>
> **[AnimatedSwitcher](./Runtime/Layout/AnimatedSwitcher.cs)** -- animate a child out and its replacement in.
>
> **[Tabs](./Runtime/Layout/Tabs.cs)** -- a horizontally scrollable, draggable list of tabs.
>
> **[Navigator](./Runtime/Widgets/Navigator.cs)** -- a stack of routes, with push, pop and transitions.
>
> **[Builder](./Runtime/Widgets/Builder.cs)** -- build a subtree inline from a delegate.

Conventions for adding a widget, and the vocabulary the layout system uses, are in
[Runtime/Layout/README.md](./Runtime/Layout/README.md).

## How to install

Minimum Unity version is 6000.3.

Distributed as a git package
([how to install a package from a git URL](https://docs.unity3d.com/Manual/upm-ui-giturl.html)).

<br>Git URL (UniMob.UI): `https://github.com/Girildo/UniMob.UI.git`
<br>Git URL (UniMob): `https://github.com/codewriter-packages/UniMob.git`

## License

UniMob.UI is [MIT licensed](./LICENSE.md).

## Credits

UniMob.UI inspired by [Flutter](https://github.com/flutter/flutter).

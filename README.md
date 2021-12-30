# UniMob.UI [![Github license](https://img.shields.io/github/license/codewriter-packages/UniMob.UI.svg?style=flat-square)](#) [![Unity 2019.3](https://img.shields.io/badge/Unity-2019.3+-2296F3.svg?style=flat-square)](#) ![GitHub package.json version](https://img.shields.io/github/package-json/v/codewriter-packages/UniMob.UI?style=flat-square)

A declarative library for building reactive user interface. Built over [UniMob](https://github.com/codewriter-packages/UniMob).

## Getting Started

#### 1. Create view

View are regular MonoBehaviour that should be attached to gameObject.

```csharp
using UniMob.UI;

public class CounterView : View<ICounterState>
{
    public UnityEngine.UI.Text counterText;
    public UnityEngine.UI.Button incrementButton;

    // initial setup
    protected override void Awake()
    {
        base.Awake();

        incrementButton.Click(() => State.Increment);
    }

    // update view with data provided by State
    // Render() is called automatically when data changes
    // so view always be synchronized with state
    protected override void Render()
    {
        counterText.text = "Conter: " + State.Counter;
    }
}

// describes the data required for the view 
// and the actions performed by the view 
public interface IConterState : IViewState {
  int Counter { get; }
  
  void Increment();
}
```

#### 2. Create widget

A widget is an immutable description of an interface element. May contain additional data if necessary.

```csharp
using UniMob.UI;

public class CounterWidget : StatefulWidget
{
    public int IncrementStep { get; set; }

    public override State CreateState() => new CounterState();
}
```

#### 3. Create state

The State provides data for the View and optionally contains mutable state of this interface part.

```csharp
using UniMob;
using UniMob.UI;

public class CounterState : ViewState<CounterWidget>, ICounterState
{
    // where to load the view from? 
    public override WidgetViewReference View {
        // supports direct prefab link, Resources and Addressables
        get => WidgetViewReference.Resource("Prefabs/Counter View");
    }
    
    [Atom] public int Counter { get; private set; }

    public void Increment()
    {
        // atom modification will automatically update UI
        Counter += Widget.IncrementStep;
    }
}
```

#### 3. Run app

```csharp
using UniMob;
using UniMob.UI;

public class CounterApp : UniMobUIApp
{
    protected override Widget Build(BuildContext context)
    {
        return new ConterWidget() {
            IncrementStep = 1
        };
    }
}
```

## Widget Composition

Widgets are the building blocks of a app’s user interface.
Widgets form a hierarchy based on composition.

UniMob.UI comes with a suite of powerful basic widgets, of which the following are commonly used:

> **[Row](./Runtime/Widgets/Row.cs), [Column](./Runtime/Widgets/Column.cs)**<br/>
> These widgets let you create layouts in both the horizontal (Row) and vertical (Column) directions. 
>
>**[ZStack](./Runtime/Widgets/ZStack.cs)**<br/>
>A Stack widget lets you place widgets on top of each other in paint order.
>
>**[Container](./Runtime/Widgets/Container.cs)**<br/>
>The Container widget lets you create a rectangular visual element that has background color and custom size.
>
>**[Scroll Grid Flow](./Runtime/Widgets/ScrollGridFlow.cs)**<br/>
>The ScrollGridFlow widget lets you create a virtualized scrollable grid of elements.

Composition of widgets allows you to build complex custom interfaces that will automatically update when needed.

```csharp
private Widget Build(BuildContext context) {
    return new ScrollGridFlow {
        CrossAxisAlignment = CrossAxisAlignment.Center,
        MaxCrossAxisExtent = 750.0f,
        Children = {
            this.BuildDailyOffers(),
            
            this.BuildGemsHeader(),
            this.BuildGemsSlots(),

            this.BuildGoldHeader(),
            this.BuildGoldSlots(),
        }
    };
}

private IEnumerable<StoreItem> BuildDailyOffers() {
    return this.DailyOffersModel
        .GetAllOffers()
        .Where(offer => offer.Visible) // subscribe to 'Visible' atom
        .Select(offer => this.BuildDailyOffer(offer.Id);
}

private Widget BuildDailyOffer(string offerId) {
    return new DailyOfferWidget(offerId) {
        // keys must be set for list elements
        Key = Key.Of($"store_item_{offerId}")
    };
}

// ...
```

More code samples are located in  [UniMob.UI Samples](https://github.com/codewriter-packages/UniMob.UI-Samples) repository.

## How to Install
Minimal Unity Version is 2019.3.

Library distributed as git package ([How to install package from git URL](https://docs.unity3d.com/Manual/upm-ui-giturl.html))
<br>Git URL (UniMob.UI): `https://github.com/codewriter-packages/UniMob.UI.git`
<br>Git URL (UniMob): `https://github.com/codewriter-packages/UniMob.git`

## License

UniMob.UI is [MIT licensed](./LICENSE.md).

## Credits

UniMob.UI inspired by [Flutter](https://github.com/flutter/flutter).

# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). From 1.0.0 onward
this package follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

To be released as 1.0.0. Give this section its version and its date in the same commit that sets
`version` in `package.json`, and not before.

The first release on this fork's own version line, and the first with a public API worth promising
to keep.

The numbers before it are not a sequence this package owns. `0.7.0` was the last version the fork
set for itself; `0.13.x`, `0.14.0` and `0.15.0` arrived from upstream's `package.json` through a
merge and were never releases of this code. History before 1.0.0 lives in git, not here.

### Stability

`UniMob.UI`, `UniMob.UI.Widgets`, `UniMob.UI.Rendering` and `UniMob.UI.Navigation` are the public
API and follow semantic versioning.

`UniMob.UI.Testing` is a consumable assembly rather than a test assembly, and follows semantic
versioning with the rest of the public API.

`UniMob.UI.Internal`, `UniMob.UI.Internal.Views`, `UniMob.UI.Diagnostics` and `UniMob.UI.DevTools`
are the extension surface. They are public on purpose, so that a consumer can write its own views
and diagnostics, but they are not covered by the version promise and may change in a minor release.

### Added

- View registration is public, so a view can be declared in source from any assembly instead of
  shipping a prefab under `Resources/`: `IViewFactory`, `RegisterViewFactoryAttribute`,
  `RegisterComponentViewFactoryAttribute`, `RegisterCustomViewFactoryAttribute`, and
  `WidgetViewReference.Registered(name)` to point a widget at one.
- `IPositionedState` and `IFlexibleState`. Taking part in a `ZStack` or a flex is a contract a state
  answers, so a widget declared outside this package can do both. `RenderZStack` and `RenderFlex`
  previously matched on the framework's own concrete state types and nothing else could join.
- `Navigator.OfOrNull`, which answers `null` where `Navigator.Of` throws.
- `Runtime/csc.rsp`, so the package compiles with its own `-langversion:preview -nullable` rather
  than inheriting whatever the consuming project happens to set. Cloned on its own it now builds the
  same way it does inside a project.
- A csharpier check in CI, and a `.config/dotnet-tools.json` pinning the version it runs.
- **`UniMob.UI.Testing`**, so an app can build widget tests against the same machinery this package
  tests itself with. `TestZone` is a frame clock a test drives by hand -- `Pump`, `PumpFrames`,
  `PumpFor`, and a `Settle` that asks whether anything is left to do rather than counting quiet
  frames. `TestHarness` mounts and lays out a widget with no GameObject involved.
  `RecordingReporter` and `RecordingErrors` capture layout issues and faults for the length of a
  scope. The assembly is constrained to `UNITY_INCLUDE_TESTS`, so none of it reaches a player build.
- **`UniMobError`**, the one place an exception caught on a caller's behalf is reported, mirroring
  `UniMobDiagnostics`: `IErrorReporter`, `UniMobFault`, and an `Override` that restores the previous
  reporter when its scope is disposed. An app can route faults to its own crash reporting.
- `Zone` is public, and a ticker registered with it receives the seconds since the last frame.
- `CONTEXT.md`, which defines the words this package uses in a specific sense and records the order
  the frame drivers run in.

### Changed

Every entry in this section is a breaking change.

- **One layout system.** Ten widget names existed in both `UniMob.UI.Widgets` and `UniMob.UI.Layout`
  with different sizing behaviour, so a stray `using` picked the wrong one and failed as a
  wrong-sized box rather than a compile error. `UniMob.UI.Layout` is gone and each name now resolves
  to exactly one type.
- **Namespaces name what they hold.** Widgets are in `UniMob.UI.Widgets`, render objects and
  `LayoutConstraints` in `UniMob.UI.Rendering`, `Navigator`/`Route`/`ViewPanel` in
  `UniMob.UI.Navigation`, views and pooling in `UniMob.UI.Internal`. Render objects are no longer
  reached through a path containing `Internal`, which they never were.
- **State contracts live in `UniMob.UI`.** Every `I*State` is in the root namespace, which every
  sub-namespace reaches without a using directive. The enums and payload types those contracts
  expose moved with them: `FlexFit`, `ImageFit`, `HorizontalTextAlignment`, and `GestureDetails`,
  `TapDetails`, `PointerDetails`, `DragDetails`.
- **Widget properties are `init`-only**, `Key` included. Object initializers are unaffected;
  assigning to a widget after construction no longer compiles.
- **Null contracts are in the type system.** Nullable reference types replace 101 JetBrains
  annotations, which also removes an undeclared dependency on `UnityEngine.CoreModule` for the
  attributes.
- **`NavigatorStack.Pop()` returns void.** Every caller popped for the effect.
- **A ticker takes its delta.** `Zone.AddTicker` and `RemoveTicker` take `Action<float>` rather than
  `Action`, as Flutter's `Ticker` does, so a ticker no longer reads the clock itself.
- **The frame drivers have a pinned order.** `ZoneDriver` carries `[DefaultExecutionOrder(-1000)]`,
  which pins the order the zone and the atom scheduler already ran in rather than leaving it to
  component registration.
- **`Navigator.Of` lost its `nullOk` parameter.** Use `OfOrNull`.
- **Registered views are named, not prefixed.** The `$$_` sigil on `WidgetViewReference.Resource`
  paths is retired in favour of `WidgetViewReference.Registered`, so registered names and
  `Resources/` paths no longer share one string space.
- **`Text` builds its view in source** instead of loading `Resources/Layout/UniMob.Text`.
- **Minimum Unity is 6000.3**, corrected from a `2019.3` floor the package had long since left.
  Dependencies move with it: `com.codewriter.unimob` 2.6.0, `com.unity.addressables` 2.9.0,
  `com.unity.textmeshpro` 5.0.0. 2.6.0 adds `AtomScheduler.HasPendingWork`, which is what lets a
  test ask the reactive graph whether it still has queued work.

### Removed

- The bottom-up layer the Flutter-style system replaced: `WidgetSize`, `IState.Size`,
  `ViewState.CalculateSize()` and `RenderLegacy`. A leaf widget that does not override
  `CreateRenderObject` now throws instead of silently becoming a legacy one, which is what made the
  old system the default.
- Widgets with a modern equivalent or no working implementation: `UnPositionedStack`, `GridFlow`,
  `ScrollGridFlow`, `HorizontalScrollGridFlow` (use `ScrollGrid`, which is axis-aware),
  `VerticalSplitBox` (`Column` with `Expanded`), `UniMobText`, `UniMobButton`, and
  `DismissibleDialog`, which asked `Resources` for a path that did not match the asset on disk and
  so had never loaded.
- `Route.GetAwaiter()`, deprecated in favour of `PopTask` and `PushTask`. Awaiting a route directly
  no longer compiles.
- `Resources/Layout/UniMob.Text.prefab`. `Resources/Layout/UniMob.ScrollList.prefab` remains; its
  template is an eleven-object hierarchy wired into `ScrollRect.content`.

### Fixed

- `ScrollListView.AnimateScrollTo` invoked an easing that every caller down the chain defaults to
  null, so a plain `ScrollTo` threw on the first frame of the animation. It now eases linearly.
- `ScrollListView.Awake` built its view mapper against `contentRoot` before resolving it, so the
  fallback path parented every child to whatever the prefab left null.
- `PrefabViewLoader` looked a prefab up in its cache before the null check written for it, so a null
  prefab threw out of the dictionary instead of reaching the message.
- `PooledViewMapper` and `RenderText` turned a loader that answered null into a null dereference a
  line later, with the reference no longer in hand to name.
- `RenderIntrinsicSize` dereferenced a child it is not guaranteed, and re-declared the two intrinsic
  hooks its base already answers with a null guard.
- `RouteOfT` read the type of a null value while reporting a value it could not cast.
- `LayoutTextView.Awake` was private and hid `UIBehaviour.Awake` rather than overriding it.
- `RenderZStack` cast a positioned child's widget to `Positioned` after matching on its state type.
  The cast is gone along with the type test.
- Measuring text, resolving a registered view and mounting a navigator all required play mode, for
  three separate reasons: `Object.DontDestroyOnLoad`, which throws outside it; a view loader built
  only by a `[RuntimeInitializeOnLoadMethod]`, so it was null in the editor; and a frame clock that
  existed only while playing. The package's own suite is EditMode now, and an app's widget tests can
  be.
- The registered-view cache returned a view whose GameObject had been destroyed, which fails
  silently and permanently because nothing rebuilds it.
- `Tests/Shared` shipped into player builds, carrying no define constraint while documenting that no
  test helper does.

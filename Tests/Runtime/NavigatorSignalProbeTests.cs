using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UniMob.UI.Widgets;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Pins how much of what the navigator does is visible through its public atoms alone, which is
    ///     what decides whether a notification channel earns its place beside them.
    /// </summary>
    /// <remarks>
    ///     Characterization, in the sense of <see cref="NavigatorBaselineTests"/>: it asserts what the
    ///     atoms report today, including where that is less than a reader would expect.
    /// </remarks>
    public class NavigatorSignalProbeTests
    {
        /// <summary>
        ///     <see cref="NavigatorState.NavigationStack"/> and <see cref="NavigatorState.TopmostRoute"/> both
        ///     report only the per-frame net of what happened.
        /// </summary>
        /// <remarks>
        ///     Worth knowing before reaching for either as a navigation signal.
        ///     <para>
        ///         <b>NavigationStack notifies per mutation.</b> <c>NavigatorStack.Routes</c> hands out a
        ///         fresh snapshot after every push, pop and replace, so the computed atom sees a new value
        ///         and obsoletes its subscribers -- even when the contents came back the same within the
        ///         frame, since a new instance is a new value. It used not to: one collection instance for
        ///         its whole life, so <c>ComputedAtom.Evaluate</c> compared equal every time and the change
        ///         never propagated -- correct to read during a build, useless to react to.
        ///     </para>
        ///     <para>
        ///         <b>Both are lossy.</b> Reactions are actualized once per frame by <c>AtomScheduler</c>.
        ///         Anything that happens and unhappens inside one frame is invisible, and a batch that
        ///         moves the stack several times reports only where it ended up.
        ///     </para>
        /// </remarks>
        [UnityTest]
        public IEnumerator TheAtomsReportTheNetResult_NotWhatHappened()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var stackRuns = 0;
            var seenTopmost = new List<string>();
            var lifetime = new LifetimeController();

            try
            {
                Atom.Reaction(
                    lifetime.Lifetime,
                    () => host.Navigator.NavigationStack,
                    _ => stackRuns++
                );
                Atom.Reaction(
                    lifetime.Lifetime,
                    () => host.Navigator.TopmostRoute,
                    route => seenTopmost.Add(route == null ? "null" : route.Key)
                );

                host.Navigator.Push(
                    host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain)
                );
                yield return host.Settle();

                // Pushed and popped within one frame. A whole route was created, focused, destroyed and
                // disposed here.
                host.Navigator.Push(
                    host.Create("T", RouteModalType.Fullscreen, RouteFlavour.Plain)
                );
                host.Navigator.TopmostRoute.Pop();
                yield return host.Settle();

                host.Navigator.Push(
                    host.Create("C", RouteModalType.Fullscreen, RouteFlavour.Plain)
                );
                yield return host.Settle();

                // PopTo(null) + Replace as one batch: C and B are destroyed and A is replaced.
                host.Navigator.NewRoot(
                    host.Create("D", RouteModalType.Fullscreen, RouteFlavour.Plain)
                );
                yield return host.Settle();
            }
            finally
            {
                lifetime.Dispose();
            }

            Assert.AreEqual(
                5,
                stackRuns,
                "NavigationStack fired its initial run and once per frame in which the stack was mutated: "
                    + "B pushed, T pushed and popped, C pushed, and the NewRoot. The frame that only pushed and "
                    + "popped T counts, because the snapshot is new even though its contents came back the same; "
                    + "and the NewRoot's three removals and one replace collapse into a single run. Whether the "
                    + "contents actually changed is for a computation over them to decide"
            );

            CollectionAssert.AreEqual(
                new[] { "A", "B", "C", "D" },
                seenTopmost,
                "T is absent entirely -- it lived and died inside one frame -- and the NewRoot reports "
                    + "C -> D, never revealing that B and C were destroyed and A was replaced on the way"
            );

            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }
    }
}

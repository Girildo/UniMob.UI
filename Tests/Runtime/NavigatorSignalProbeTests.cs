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
        ///     <see cref="NavigatorState.NavigationStack"/> never notifies, and
        ///     <see cref="NavigatorState.TopmostRoute"/> reports only the per-frame net.
        /// </summary>
        /// <remarks>
        ///     Two separate causes, both worth knowing before reaching for either as a navigation signal.
        ///     <para>
        ///         <b>NavigationStack is inert.</b> <c>NavigatorStack.Routes</c> returns the same
        ///         <c>Stack&lt;Route&gt;</c> instance on every read, and <c>ComputedAtom.Evaluate</c>
        ///         returns early without obsoleting its subscribers when the new value equals the cached
        ///         one. The collection's identity never changes, so the comparison always succeeds and the
        ///         change never propagates. The atom is correct to read during a build, which re-reads its
        ///         contents, and silently useless to react to. That is a defect, not a design.
        ///     </para>
        ///     <para>
        ///         <b>TopmostRoute is lossy.</b> Its identity does change, so it does propagate, but
        ///         reactions are actualized once per frame by <c>AtomScheduler</c>. Anything that happens
        ///         and unhappens inside one frame is invisible, and a batch that moves the stack several
        ///         times reports only where it ended up.
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
                Atom.Reaction(lifetime.Lifetime, () => host.Navigator.NavigationStack, _ => stackRuns++);
                Atom.Reaction(lifetime.Lifetime, () => host.Navigator.TopmostRoute,
                    route => seenTopmost.Add(route == null ? "null" : route.Key));

                host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
                yield return host.Settle();

                // Pushed and popped within one frame. A whole route was created, focused, destroyed and
                // disposed here.
                host.Navigator.Push(host.Create("T", RouteModalType.Fullscreen, RouteFlavour.Plain));
                host.Navigator.Pop();
                yield return host.Settle();

                host.Navigator.Push(host.Create("C", RouteModalType.Fullscreen, RouteFlavour.Plain));
                yield return host.Settle();

                // PopTo(null) + Replace as one batch: C and B are destroyed and A is replaced.
                host.Navigator.NewRoot(host.Create("D", RouteModalType.Fullscreen, RouteFlavour.Plain));
                yield return host.Settle();
            }
            finally
            {
                lifetime.Dispose();
            }

            Assert.AreEqual(1, stackRuns,
                "NavigationStack fired only its initial run, across five stack mutations: the collection " +
                "identity never changes, so the computed never obsoletes its subscribers");

            CollectionAssert.AreEqual(
                new[] { "A", "B", "C", "D" },
                seenTopmost,
                "T is absent entirely -- it lived and died inside one frame -- and the NewRoot reports " +
                "C -> D, never revealing that B and C were destroyed and A was replaced on the way");

            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }
    }
}

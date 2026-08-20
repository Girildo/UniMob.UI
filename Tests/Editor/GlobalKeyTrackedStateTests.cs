using System.Collections.Generic;
using NUnit.Framework;
using UniMob.Core;
using UniMob.UI.Internal;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     <see cref="GlobalKey{T}"/> serves its binding through two accessors, and the split is the
    ///     point: <c>TrackedState</c> is read reactively, so an observer created <em>before</em> the
    ///     key is bound wakes when binding happens, while <c>CurrentState</c> stays untracked so
    ///     identity reads gain no dependency as a side effect.
    /// </summary>
    /// <remarks>
    ///     The untracked read is the trap these tests exist to mark. A reader that takes it while the
    ///     key is unbound touches no atom, registers zero dependencies and never runs again -- so it
    ///     sleeps through the widget mounting, and keeps reporting the null it saw first. Both halves
    ///     are asserted here, because a test of the reactive one that would also pass against the
    ///     untracked one is testing nothing.
    /// </remarks>
    public class GlobalKeyTrackedStateTests
    {
        [Test]
        public void ObserverCreatedBeforeBinding_WakesWhenTheKeyBinds()
        {
            var lifetime = new LifetimeController();
            var key = new GlobalKey<CountingBoxState>();
            var observed = new List<CountingBoxState>();

            Atom.Reaction(lifetime.Lifetime, () => key.TrackedState, state => observed.Add(state));

            Assert.AreEqual(1, observed.Count, "the reaction fires once on creation");
            Assert.IsNull(observed[0], "unbound: there is nothing to resolve yet");

            var root = TestHarness.Mount(
                new CountingBox { BoxSize = new Vector2(30, 40), Key = key }
            );
            AtomScheduler.Sync();

            Assert.AreSame(
                root,
                observed[observed.Count - 1],
                "binding the key must wake the observer and hand it the bound state"
            );

            StateUtilities.DeactivateChild(root);
            AtomScheduler.Sync();

            Assert.IsNull(
                observed[observed.Count - 1],
                "unbinding must be observed the same way binding was"
            );

            lifetime.Dispose();
        }

        /// <summary>
        ///     The negative control, and the reason the tracked accessor has to exist at all.
        /// </summary>
        [Test]
        public void ObserverOverCurrentState_NeverWakes()
        {
            var lifetime = new LifetimeController();
            var key = new GlobalKey<CountingBoxState>();
            var runs = 0;
            CountingBoxState seen = null;

            Atom.Reaction(
                lifetime.Lifetime,
                () =>
                {
                    runs++;
                    seen = key.CurrentState;
                }
            );

            Assert.AreEqual(1, runs, "the reaction runs once on creation");

            var root = TestHarness.Mount(new CountingBox { Key = key });
            AtomScheduler.Sync();

            Assert.AreEqual(1, runs, "CurrentState is untracked: binding must not wake its reader");
            Assert.IsNull(seen, "and so the reader never learns the key bound");

            StateUtilities.DeactivateChild(root);
            AtomScheduler.Sync();

            Assert.AreEqual(1, runs, "nor must unbinding");

            lifetime.Dispose();
        }

        /// <summary>
        ///     The same promise in the shape that can actually be violated: an untracked read taken
        ///     inside a computation that has real dependencies of its own must not quietly add one.
        /// </summary>
        [Test]
        public void CurrentState_AddsNoDependency_ToATrackedComputation()
        {
            var lifetime = new LifetimeController();
            var key = new GlobalKey<CountingBoxState>();
            var trigger = Atom.Value(1);
            var runs = 0;

            Atom.Reaction(
                lifetime.Lifetime,
                () =>
                {
                    runs++;
                    _ = trigger.Value;
                    _ = key.CurrentState;
                }
            );

            Assert.AreEqual(1, runs, "the reaction runs once on creation");

            var root = TestHarness.Mount(new CountingBox { Key = key });
            AtomScheduler.Sync();

            Assert.AreEqual(
                1,
                runs,
                "the untracked read must not have subscribed the computation to the binding"
            );

            trigger.Value = 2;
            AtomScheduler.Sync();

            Assert.AreEqual(
                2,
                runs,
                "while the computation's own dependency still wakes it -- otherwise the assertion "
                    + "above passes because nothing was ever observing"
            );

            StateUtilities.DeactivateChild(root);
            lifetime.Dispose();
        }

        /// <summary>
        ///     The dependency is on the binding itself, not on the value projected out of it: a key
        ///     pointed at a state of another type reads <c>null</c> both before and after binding,
        ///     and must still wake its observer.
        /// </summary>
        [Test]
        public void TrackedState_WakesOnBinding_EvenWhenTheStateTypeDoesNotMatch()
        {
            var lifetime = new LifetimeController();
            var key = new GlobalKey<FixedSizeBoxState>();
            var runs = 0;
            FixedSizeBoxState seen = null;

            Atom.Reaction(
                lifetime.Lifetime,
                () =>
                {
                    runs++;
                    seen = key.TrackedState;
                }
            );

            Assert.AreEqual(1, runs, "the reaction runs once on creation");
            Assert.IsNull(seen);

            var root = TestHarness.Mount(new CountingBox { Key = key });
            AtomScheduler.Sync();

            Assert.AreEqual(
                2,
                runs,
                "binding must wake the observer even though the projected value did not change"
            );
            Assert.IsNull(
                seen,
                "a state of the wrong type reads as default, matching CurrentState"
            );

            StateUtilities.DeactivateChild(root);
            lifetime.Dispose();
        }
    }
}

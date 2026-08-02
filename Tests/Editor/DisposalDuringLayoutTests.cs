using System;
using System.Collections.Generic;
using NUnit.Framework;
using UniMob.Core;
using UniMob.UI.Internal;
using UniMob.UI.Layout;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     What happens when a layout pull is already queued and the subtree it targets dies before the
    ///     pull runs.
    /// </summary>
    /// <remarks>
    ///     Layout invalidation only marks; the scheduler drains the queue on a later tick, so a state
    ///     can be disposed in between. Several disposal guards exist across the layout path with no
    ///     recorded rationale for any of them. These tests pin the <em>observable</em> contract -- a
    ///     dead subtree is not laid out and nothing throws -- rather than any particular guard, so that
    ///     a guard can be removed and the contract re-checked without rewriting the test.
    ///     <para>
    ///         The reaction is load-bearing: <c>AtomScheduler.Sync</c> only actualizes atoms that are
    ///         <c>Active</c>, so without a live subscriber holding the layout atom the queue drains
    ///         without touching it and the test would pass vacuously.
    ///     </para>
    /// </remarks>
    public class DisposalDuringLayoutTests
    {
        private static readonly LayoutConstraints Constraints = LayoutConstraints.Loose(100, 100);

        private static RenderCountingBox RenderOf(State root) =>
            (RenderCountingBox)root.RenderObject;

        /// <summary>
        ///     Runs <paramref name="action"/> and returns any errors Unity logged during it.
        /// </summary>
        /// <remarks>
        ///     Reaching a dead subtree does not throw -- <c>AtomBase</c> logs "Actualization of disposed
        ///     atom" and then recomputes anyway, which is the failure worth catching: it means the pull
        ///     ran over a disposed tree rather than being refused. Asserting on the captured log states
        ///     that contract directly, instead of leaning on the test framework's implicit
        ///     unexpected-error-log failure, which reports it as a framework complaint rather than as
        ///     the layout defect it is.
        /// </remarks>
        private static string[] CaptureErrors(Action action)
        {
            var errors = new List<string>();

            void Handler(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    errors.Add(message);
                }
            }

            var previouslyIgnoring = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += Handler;
            try
            {
                action();
            }
            finally
            {
                Application.logMessageReceived -= Handler;
                LogAssert.ignoreFailingMessages = previouslyIgnoring;
            }

            return errors.ToArray();
        }

        private static void AssertLayoutRefused(string[] errors)
        {
            Assert.IsEmpty(
                errors,
                "A layout pull reached a disposed subtree instead of being refused:\n  "
                    + string.Join("\n  ", errors)
            );
        }

        [Test]
        public void QueuedLayoutPull_DoesNotRunAgainstADisposedSubtree()
        {
            var reactionLifetime = new LifetimeController();
            var boxSize = Atom.Value(new Vector2(30, 40));
            var root = TestHarness.Mount(
                new Builder(_ => new CountingBox { BoxSize = boxSize.Value })
            );

            Atom.Reaction(
                reactionLifetime.Lifetime,
                () => TestHarness.DriveLayout(root, Constraints)
            );

            var render = RenderOf(root);
            var passesWhileAlive = render.SizingPasses;
            Assert.AreEqual(
                1,
                passesWhileAlive,
                "The reaction should have laid the tree out once."
            );

            // Invalidate so a pull is queued, then kill the subtree before the scheduler drains it.
            boxSize.Value = new Vector2(55, 65);
            StateUtilities.DeactivateChild(root);

            AssertLayoutRefused(CaptureErrors(() => AtomScheduler.Sync()));
            Assert.AreEqual(
                passesWhileAlive,
                render.SizingPasses,
                "A disposed subtree must not be laid out by a pull that was queued before it died."
            );

            reactionLifetime.Dispose();
        }

        [Test]
        public void QueuedLayoutPull_UnderBuildOnlyWrappers_DoesNotRunAgainstADisposedSubtree()
        {
            var reactionLifetime = new LifetimeController();
            var boxSize = Atom.Value(new Vector2(30, 40));
            var root = TestHarness.Mount(
                new Builder(_ => new Builder(__ => new CountingBox { BoxSize = boxSize.Value }))
            );

            Atom.Reaction(
                reactionLifetime.Lifetime,
                () => TestHarness.DriveLayout(root, Constraints)
            );

            var render = RenderOf(root);
            var passesWhileAlive = render.SizingPasses;

            boxSize.Value = new Vector2(55, 65);
            StateUtilities.DeactivateChild(root);

            AssertLayoutRefused(CaptureErrors(() => AtomScheduler.Sync()));
            Assert.AreEqual(passesWhileAlive, render.SizingPasses);

            reactionLifetime.Dispose();
        }

        /// <summary>
        ///     Reading geometry off a subtree that has already been disposed must be answerable rather
        ///     than throwing. Intrinsics are the one layout entry point that can trigger a build, so
        ///     they are the most exposed to a dead subtree.
        /// </summary>
        [Test]
        public void IntrinsicsOnADisposedSubtree_DoNotThrow()
        {
            var root = TestHarness.Mount(
                new Builder(_ => new CountingBox { BoxSize = new Vector2(30, 40) })
            );
            TestHarness.DriveFrame(root, Constraints);

            var render = root.RenderObject;
            StateUtilities.DeactivateChild(root);

            AssertLayoutRefused(
                CaptureErrors(() =>
                {
                    render.GetIntrinsicWidth(100f);
                    render.GetIntrinsicHeight(100f);
                })
            );
        }

        /// <summary>
        ///     Laying out a subtree that is already dead must be a no-op rather than a throw: the
        ///     scheduler can reach one, and a partly torn-down tree is not a crash-worthy condition.
        /// </summary>
        [Test]
        public void ExplicitLayoutOfADisposedSubtree_DoesNotThrow()
        {
            var root = TestHarness.Mount(
                new Builder(_ => new CountingBox { BoxSize = new Vector2(30, 40) })
            );
            TestHarness.DriveFrame(root, Constraints);

            var render = root.RenderObject;
            StateUtilities.DeactivateChild(root);

            AssertLayoutRefused(
                CaptureErrors(() =>
                {
                    TestHarness.Layout(root, Constraints);
                    render.GetIntrinsicWidth(50f);
                })
            );
        }
    }
}

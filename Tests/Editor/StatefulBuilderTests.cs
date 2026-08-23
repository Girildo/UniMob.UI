using System;
using System.Collections.Generic;
using NUnit.Framework;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     <see cref="StatefulBuilder{TState}"/> holds a value for a position in the tree rather than
    ///     for the widget describing that position, which is the whole reason it exists and the one
    ///     thing a caller can get wrong about it.
    /// </summary>
    public class StatefulBuilderTests
    {
        /// <summary>Records every value the builder is handed, and the setter it was last given.</summary>
        private sealed class Recorder
        {
            public readonly List<int> Seen = new List<int>();

            public Action<int> Set = _ => { };

            public int Last => this.Seen[this.Seen.Count - 1];
        }

        private static Widget Build(Recorder recorder, int initialValue, Key? key = null)
        {
            return new StatefulBuilder<int>(
                initialValue,
                (_, value, set) =>
                {
                    recorder.Seen.Add(value);
                    recorder.Set = set;
                    return new CountingBox { BoxSize = new Vector2(10, 10) };
                }
            )
            {
                Key = key,
            };
        }

        [Test]
        public void BuilderIsHandedTheInitialValueOnTheFirstBuild()
        {
            var recorder = new Recorder();
            var root = TestHarness.Mount(Build(recorder, 1));

            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Tight(80, 60));

            Assert.AreEqual(1, recorder.Last);
        }

        [Test]
        public void TheSetterReplacesTheValueTheBuilderSees()
        {
            var recorder = new Recorder();
            var root = TestHarness.Mount(Build(recorder, 1));
            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Tight(80, 60));

            recorder.Set(5);
            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Tight(80, 60));

            Assert.AreEqual(5, recorder.Last, "the setter's value should reach the next build");
        }

        /// <summary>
        ///     The state survives an update to the widget that declared it. A parent that rebuilds
        ///     names an initial value again on every pass, and honouring it would silently discard
        ///     whatever the user had done since -- the failure this widget exists to avoid.
        /// </summary>
        [Test]
        public void RebuildingWithADifferentInitialValueLeavesTheStoredValueAlone()
        {
            var recorder = new Recorder();
            var root = TestHarness.Mount(Build(recorder, 1));
            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Tight(80, 60));

            recorder.Set(5);
            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Tight(80, 60));

            var buildsBefore = recorder.Seen.Count;
            var updated = TestHarness.Update(root, Build(recorder, 99));
            TestHarness.DriveLayoutAndView(updated, LayoutConstraints.Tight(80, 60));

            Assert.AreSame(root, updated, "the widget should update in place, not remount");
            Assert.Greater(
                recorder.Seen.Count,
                buildsBefore,
                "the update must actually rebuild, or the assertion below proves nothing"
            );
            Assert.AreEqual(
                5,
                recorder.Last,
                "a new InitialValue seeds nothing once the state exists"
            );
        }

        /// <summary>
        ///     The documented way to start over. Since InitialValue is ignored while the state
        ///     lives, replacing the state is the only thing that can re-seed it.
        /// </summary>
        [Test]
        public void ADifferentKeyReplacesTheStateAndSeedsItAgain()
        {
            var recorder = new Recorder();
            var root = TestHarness.Mount(Build(recorder, 1, Key.Of("a")));
            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Tight(80, 60));

            recorder.Set(5);
            TestHarness.DriveLayoutAndView(root, LayoutConstraints.Tight(80, 60));

            var remounted = TestHarness.Update(root, Build(recorder, 99, Key.Of("b")));
            TestHarness.DriveLayoutAndView(remounted, LayoutConstraints.Tight(80, 60));

            Assert.AreNotSame(root, remounted, "a different key replaces the state");
            Assert.AreEqual(99, recorder.Last, "a fresh state seeds from the new InitialValue");
        }
    }
}

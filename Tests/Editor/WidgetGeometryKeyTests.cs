using System.Collections.Generic;
using NUnit.Framework;
using UniMob.Core;
using UniMob.UI.Internal;
using UniMob.UI.Layout;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     The binding behind <see cref="WidgetGeometryKey"/> is observable: a reaction over the
    ///     key's geometry created <em>before</em> the key is bound wakes when binding happens.
    /// </summary>
    /// <remarks>
    ///     Pins the shape that silently died in the MeasuredBox migration: a reaction created in
    ///     <c>InitState</c> over an unbound key's <c>LocalSize</c> read null through a plain
    ///     property, registered zero dependencies, and never ran again -- so the observer slept
    ///     through the widget mounting, laying out, and resizing. The reaction here is created
    ///     before anything is mounted at all, which is the worst legal moment, and must still end
    ///     up observing the real size.
    /// </remarks>
    public class WidgetGeometryKeyTests
    {
        [Test]
        public void ObserverCreatedBeforeBinding_WakesWhenTheKeyBinds()
        {
            var lifetime = new LifetimeController();
            var key = new WidgetGeometryKey();
            var observed = new List<Vector2?>();

            Atom.Reaction(lifetime.Lifetime, () => key.LocalSize, size => observed.Add(size));

            Assert.AreEqual(1, observed.Count, "the reaction fires once on creation");
            Assert.IsNull(observed[0], "unbound: there is nothing to measure yet");

            var root = TestHarness.Mount(
                new CountingBox { BoxSize = new Vector2(30, 40), Key = key }
            );
            TestHarness.DriveLayout(root, LayoutConstraints.Loose(100, 100));
            AtomScheduler.Sync();

            Assert.AreEqual(
                new Vector2(30, 40),
                observed[observed.Count - 1],
                "binding the key must wake the observer and hand it the measured size"
            );

            StateUtilities.DeactivateChild(root);
            AtomScheduler.Sync();

            Assert.IsNull(
                observed[observed.Count - 1],
                "unbinding must be observed the same way binding was"
            );

            lifetime.Dispose();
        }

        [Test]
        public void ObserverCreatedAfterBinding_StillTracksTheSize()
        {
            var lifetime = new LifetimeController();
            var key = new WidgetGeometryKey();
            var boxSize = Atom.Value(new Vector2(30, 40));
            var observed = new List<Vector2?>();

            var root = TestHarness.Mount(new Builder(_ => new CountingBox
            {
                BoxSize = boxSize.Value,
                Key = key,
            }));
            TestHarness.DriveLayout(root, LayoutConstraints.Loose(100, 100));

            Atom.Reaction(lifetime.Lifetime, () => key.LocalSize, size => observed.Add(size));

            Assert.AreEqual(new Vector2(30, 40), observed[observed.Count - 1]);

            boxSize.Value = new Vector2(55, 65);
            TestHarness.DriveLayout(root, LayoutConstraints.Loose(100, 100));
            AtomScheduler.Sync();

            Assert.AreEqual(
                new Vector2(55, 65),
                observed[observed.Count - 1],
                "the pre-existing path -- observe after binding -- must keep working"
            );

            lifetime.Dispose();
        }
    }
}

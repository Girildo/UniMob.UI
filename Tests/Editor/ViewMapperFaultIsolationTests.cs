using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniMob.UI.Internal;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // A render pass over many children ends by recycling every view no child claimed. A child that
    // throws part way through must cost its own view and nothing else: the children after it were
    // never reached, which is not the same as no longer being wanted.
    public class ViewMapperFaultIsolationTests
    {
        private sealed class FakeView : IView
        {
            public FakeView() => gameObject = new GameObject("FakeView", typeof(RectTransform));

            public GameObject gameObject { get; }
            public RectTransform rectTransform => (RectTransform)gameObject.transform;
            public bool IsDestroyed => gameObject == null;
            public IState Source { get; private set; }

            public void SetSource(IState source, bool link) => Source = source;

            public void ResetSource() => Source = null;
        }

        private sealed class FakeMapper : ViewMapperBase
        {
            public readonly List<IView> Resolved = new();
            public readonly List<IView> Recycled = new();
            public bool FailToResolve;

            public FakeMapper()
                : base(link: false) { }

            protected override IView ResolveView(WidgetViewReference state)
            {
                if (FailToResolve)
                    throw new InvalidOperationException("No view could be loaded");

                var view = new FakeView();
                Resolved.Add(view);
                return view;
            }

            protected override void RecycleView(IView view) => Recycled.Add(view);
        }

        private FakeMapper _mapper;

        [SetUp]
        public void SetUp() => _mapper = new FakeMapper();

        [TearDown]
        public void TearDown()
        {
            foreach (var view in _mapper.Resolved)
                UnityEngine.Object.DestroyImmediate(view.gameObject);
        }

        private static IState[] MountRows(int count) =>
            Enumerable
                .Range(0, count)
                .Select(i =>
                    (IState)
                        TestHarness.Mount(
                            new FixedSizeBox { Key = Key.Of(i), Size = new Vector2(10, 10) }
                        )
                )
                .ToArray();

        [Test]
        public void ChildThatCannotBeRendered_IsReportedAndSkipped_AndTheChildrenAfterItKeepTheirViews()
        {
            var rows = MountRows(5);
            var viewOf = new Dictionary<IState, IView>();

            using (var render = _mapper.CreateRender())
            {
                foreach (var row in rows)
                    viewOf[row] = render.RenderItem(row);
            }

            // A new row scrolls in at position 2, and its view cannot be loaded.
            var incoming = (IState)
                TestHarness.Mount(
                    new FixedSizeBox { Key = Key.Of("incoming"), Size = new Vector2(10, 10) }
                );
            var nextPass = new[] { rows[0], rows[1], incoming, rows[3], rows[4] };
            var rendered = new List<IState>();

            using var faults = RecordingErrors.Capture();
            using (var render = _mapper.CreateRender())
            {
                foreach (var row in nextPass)
                {
                    _mapper.FailToResolve = ReferenceEquals(row, incoming);
                    if (render.TryRenderItem(row, out var view))
                    {
                        rendered.Add(row);
                        Assert.AreSame(
                            viewOf[row],
                            view,
                            $"row {row.Key} must keep the view it already had"
                        );
                    }
                }
            }

            CollectionAssert.AreEqual(
                new[] { rows[0], rows[1], rows[3], rows[4] },
                rendered,
                "every row but the faulty one must render, the ones after it included"
            );
            CollectionAssert.AreEquivalent(
                new[] { viewOf[rows[2]] },
                _mapper.Recycled,
                "only the row that left the pass gives its view back; recycled "
                    + string.Join(", ", _mapper.Recycled.Select(v => v.gameObject.name))
            );

            Assert.AreEqual(1, faults.Count, "the faulty row is reported exactly once");
            Assert.AreSame(incoming, faults[0].Subject, "the fault names the row that caused it");
            StringAssert.Contains("No view could be loaded", faults[0].Exception.Message);
        }

        [Test]
        public void FaultyChild_IsRenderedAgainOnceItsViewResolves()
        {
            var rows = MountRows(3);

            using var faults = RecordingErrors.Capture();
            using (var render = _mapper.CreateRender())
            {
                for (var i = 0; i < rows.Length; i++)
                {
                    _mapper.FailToResolve = i == 1;
                    render.TryRenderItem(rows[i], out _);
                }
            }

            _mapper.FailToResolve = false;
            using (var render = _mapper.CreateRender())
            {
                foreach (var row in rows)
                    Assert.IsTrue(
                        render.TryRenderItem(row, out _),
                        $"row {row.Key} must render once nothing throws"
                    );
            }

            Assert.AreEqual(3, _mapper.Resolved.Count, "one view per row, none resolved twice");
            Assert.IsEmpty(_mapper.Recycled, "no row left the pass, so no view is given back");
        }
    }
}

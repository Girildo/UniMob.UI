using System;

namespace UniMob.UI.Rendering
{
    /// <summary>
    ///     The main-axis model of a lazily measured run of slots (the items of a list, the rows of a
    ///     grid): the exact extent of every slot ever measured, and an average standing in for the
    ///     rest. It answers both directions of the same question, where a slot starts and which slot
    ///     an offset falls in, so a window selected through it is a window positioned where it was
    ///     selected for.
    /// </summary>
    internal sealed class MeasuredExtents
    {
        private const float Unmeasured = -1f;

        // Fenwick trees over the measured slots, one-based: the sum of their extents and how many
        // there are. A leading edge is a prefix query instead of a walk over every measurement.
        private float[] _extents = Array.Empty<float>();
        private double[] _sumTree = new double[1];
        private int[] _countTree = new int[1];

        /// <summary>How many slots have a measured extent.</summary>
        public int Count { get; private set; }

        public void Clear()
        {
            Array.Fill(_extents, Unmeasured);
            Array.Clear(_sumTree, 0, _sumTree.Length);
            Array.Clear(_countTree, 0, _countTree.Length);
            Count = 0;
        }

        public bool TryGet(int index, out float extent)
        {
            extent = index >= 0 && index < _extents.Length ? _extents[index] : Unmeasured;
            return extent >= 0f;
        }

        public void Set(int index, float extent)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            extent = Math.Max(0f, extent);
            EnsureCapacity(index + 1);

            var previous = _extents[index];
            var wasMeasured = previous >= 0f;
            _extents[index] = extent;

            if (!wasMeasured)
                Count++;

            Add(index, extent - (wasMeasured ? previous : 0f), wasMeasured ? 0 : 1);
        }

        /// <summary>
        ///     The offset at which slot <paramref name="index" /> starts: the measured extents below it,
        ///     <paramref name="average" /> for each unmeasured one, and a <paramref name="spacing" />
        ///     gap after every slot below it. Never decreases as the index grows.
        /// </summary>
        public float LeadingEdge(int index, float average, float spacing)
        {
            if (index <= 0)
                return 0f;

            var measuredSum = 0d;
            var measuredCount = 0;
            for (var i = Math.Min(index, _extents.Length); i > 0; i -= i & -i)
            {
                measuredSum += _sumTree[i];
                measuredCount += _countTree[i];
            }

            return (float)(
                measuredSum + (double)average * (index - measuredCount) + (double)spacing * index
            );
        }

        /// <summary>
        ///     The last slot in [0, <paramref name="slotCount" />) that starts at or before
        ///     <paramref name="offset" />, which is the slot the offset falls in (or the gap after
        ///     it). Answers 0 for an offset before the first slot.
        /// </summary>
        public int IndexAt(float offset, int slotCount, float average, float spacing)
        {
            var low = 0;
            var high = slotCount - 1;
            while (low < high)
            {
                var middle = low + (high - low + 1) / 2;
                if (LeadingEdge(middle, average, spacing) <= offset)
                    low = middle;
                else
                    high = middle - 1;
            }

            return Math.Max(0, low);
        }

        private void Add(int index, double extentDelta, int countDelta)
        {
            for (var i = index + 1; i < _sumTree.Length; i += i & -i)
            {
                _sumTree[i] += extentDelta;
                _countTree[i] += countDelta;
            }
        }

        private void EnsureCapacity(int capacity)
        {
            if (capacity <= _extents.Length)
                return;

            var newCapacity = Math.Max(capacity, Math.Max(64, _extents.Length * 2));
            var extents = new float[newCapacity];
            Array.Copy(_extents, extents, _extents.Length);
            Array.Fill(extents, Unmeasured, _extents.Length, newCapacity - _extents.Length);

            _extents = extents;
            _sumTree = new double[newCapacity + 1];
            _countTree = new int[newCapacity + 1];

            // The new nodes span slots that are already measured, so the trees are rebuilt.
            for (var i = 0; i < _extents.Length; i++)
            {
                if (_extents[i] >= 0f)
                    Add(i, _extents[i], 1);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SpriteFit
{
    /// <summary>
    /// Accumulates pixels into a <see cref="FitInput"/>. Keeps only the moments and each row's leftmost and
    /// rightmost pixel, which is all the convex hull needs.
    /// </summary>
    public sealed class FitInputBuilder
    {
        readonly int _minY;
        readonly int[] _rowMin;
        readonly int[] _rowMax;
        PixelMoments _moments;

        /// <param name="minY">Lowest row any added pixel can be on.</param>
        /// <param name="maxY">Highest row any added pixel can be on.</param>
        public FitInputBuilder(int minY, int maxY)
        {
            if (maxY < minY) throw new ArgumentException("maxY must be at least minY.");

            _minY = minY;
            int rows = maxY - minY + 1;
            _rowMin = new int[rows];
            _rowMax = new int[rows];
            Clear();
        }

        public int Count => _moments.Count;

        public void Clear()
        {
            for (int i = 0; i < _rowMin.Length; i++)
            {
                _rowMin[i] = int.MaxValue;
                _rowMax[i] = int.MinValue;
            }

            _moments = default;
        }

        public void Add(int x, int y)
        {
            int row = y - _minY;
            if ((uint)row >= (uint)_rowMin.Length) throw new ArgumentOutOfRangeException(nameof(y));

            if (x < _rowMin[row]) _rowMin[row] = x;
            if (x > _rowMax[row]) _rowMax[row] = x;
            _moments.Add(x, y);
        }

        public FitInput Build()
        {
            if (_moments.Count == 0) throw new InvalidOperationException("No pixels were added.");

            var candidates = new List<double2>(_rowMin.Length * 6);
            AppendHullCandidates(candidates);
            return new FitInput(_moments, Hull2D.Build(candidates));
        }

        /// <summary>
        /// Adds the edge midpoints of each row's leftmost and rightmost pixel. Midpoints of the pixels in
        /// between lie on the segments joining these, so they can't be hull vertices.
        /// </summary>
        public void AppendHullCandidates(List<double2> candidates)
        {
            for (int row = 0; row < _rowMin.Length; row++)
            {
                if (_rowMin[row] > _rowMax[row]) continue;

                double y = _minY + row;
                double left = _rowMin[row];
                double right = _rowMax[row];

                candidates.Add(new double2(left, y + 0.5));
                candidates.Add(new double2(left + 0.5, y));
                candidates.Add(new double2(left + 0.5, y + 1.0));
                candidates.Add(new double2(right + 1.0, y + 0.5));
                candidates.Add(new double2(right + 0.5, y));
                candidates.Add(new double2(right + 0.5, y + 1.0));
            }
        }

        public PixelMoments Moments => _moments;
    }
}

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace SpriteFit
{
    /// <summary>
    /// Binary opacity mask of a sprite. Pixel (x, y) covers [x, x + 1] × [y, y + 1] in pixel space,
    /// with row 0 at the bottom. Immutable.
    /// </summary>
    public sealed class PixelMask
    {
        // 1 for opaque, 0 for transparent; bytes so the Burst evaluator can copy them straight in.
        readonly byte[] _opaque;
        int[] _labels;
        Island[] _islands;

        public PixelMask(int width, int height, bool[] opaque, float pixelsPerUnit = 100f, float2 pivot = default)
        {
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (opaque == null) throw new ArgumentNullException(nameof(opaque));
            if (opaque.Length != width * height) throw new ArgumentException("Length must be width × height.", nameof(opaque));
            if (!(pixelsPerUnit > 0f)) throw new ArgumentOutOfRangeException(nameof(pixelsPerUnit));

            Width = width;
            Height = height;
            PixelsPerUnit = pixelsPerUnit;
            Pivot = pivot;
            _opaque = new byte[opaque.Length];
            int count = 0;
            for (int i = 0; i < opaque.Length; i++)
            {
                if (!opaque[i]) continue;

                _opaque[i] = 1;
                count++;
            }

            OpaqueCount = count;
        }

        public int Width { get; }

        public int Height { get; }

        public float PixelsPerUnit { get; }

        /// <summary>Pivot in pixel space, measured from the bottom-left corner.</summary>
        public float2 Pivot { get; }

        public int OpaqueCount { get; }

        /// <summary>The 8-connected islands of opaque pixels, labelled in scan order from the bottom row.</summary>
        public IReadOnlyList<Island> Islands
        {
            get
            {
                EnsureIslands();
                return _islands;
            }
        }

        /// <summary>
        /// Builds a mask from row-major alpha values (row 0 at the bottom). A pixel is opaque when its
        /// alpha is at least <paramref name="threshold"/> (0–1).
        /// </summary>
        public static PixelMask FromAlpha(byte[] alpha, int width, int height, float threshold, float pixelsPerUnit, float2 pivot)
        {
            if (alpha == null) throw new ArgumentNullException(nameof(alpha));

            byte cutoff = AlphaCutoff(threshold);
            var opaque = new bool[alpha.Length];
            for (int i = 0; i < alpha.Length; i++) opaque[i] = alpha[i] >= cutoff;
            return new PixelMask(width, height, opaque, pixelsPerUnit, pivot);
        }

        /// <summary>The smallest 8-bit alpha counted as opaque for a 0–1 threshold. Never 0.</summary>
        public static byte AlphaCutoff(float threshold)
        {
            return (byte)math.clamp((int)math.round(threshold * 255f), 1, 255);
        }

        /// <summary>False outside the mask.</summary>
        public bool IsOpaque(int x, int y)
        {
            return (uint)x < (uint)Width && (uint)y < (uint)Height && _opaque[y * Width + x] != 0;
        }

        /// <summary>0 for transparent pixels and pixels outside the mask, otherwise the island's label.</summary>
        public int GetLabel(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0;

            EnsureIslands();
            return _labels[y * Width + x];
        }

        /// <summary>Copies the 0/1 opacity bytes, row-major, into <paramref name="destination"/>.</summary>
        internal void CopyTo(NativeArray<byte> destination)
        {
            destination.CopyFrom(_opaque);
        }

        /// <summary>Island label of the pixel at a row-major index; 0 for transparent pixels.</summary>
        internal int LabelAt(int index)
        {
            EnsureIslands();
            return _labels[index];
        }

        void EnsureIslands()
        {
            if (_islands != null) return;

            _labels = new int[_opaque.Length];
            var mins = new List<int2>();
            var maxs = new List<int2>();
            var stack = new List<int>();

            for (int start = 0; start < _opaque.Length; start++)
            {
                if (_opaque[start] == 0 || _labels[start] != 0) continue;

                int label = mins.Count + 1;
                var min = new int2(start % Width, start / Width);
                int2 max = min;
                _labels[start] = label;
                stack.Add(start);

                while (stack.Count > 0)
                {
                    int index = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    var p = new int2(index % Width, index / Width);
                    min = math.min(min, p);
                    max = math.max(max, p);

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = p.y + dy;
                        if ((uint)ny >= (uint)Height) continue;

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = p.x + dx;
                            if ((uint)nx >= (uint)Width) continue;

                            int neighbour = ny * Width + nx;
                            if (_opaque[neighbour] != 0 && _labels[neighbour] == 0)
                            {
                                _labels[neighbour] = label;
                                stack.Add(neighbour);
                            }
                        }
                    }
                }

                mins.Add(min);
                maxs.Add(max);
            }

            var builders = new FitInputBuilder[mins.Count];
            for (int i = 0; i < builders.Length; i++) builders[i] = new FitInputBuilder(mins[i].y, maxs[i].y);

            for (int y = 0, index = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++, index++)
                {
                    int label = _labels[index];
                    if (label != 0) builders[label - 1].Add(x, y);
                }
            }

            _islands = new Island[builders.Length];
            for (int i = 0; i < builders.Length; i++) _islands[i] = new Island(i + 1, mins[i], maxs[i], builders[i].Build());
        }
    }
}

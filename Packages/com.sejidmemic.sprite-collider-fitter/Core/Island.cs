using Unity.Mathematics;

namespace SpriteFit
{
    /// <summary>One 8-connected group of opaque pixels in a <see cref="PixelMask"/>.</summary>
    public sealed class Island
    {
        internal Island(int label, int2 min, int2 max, FitInput input)
        {
            Label = label;
            Min = min;
            Max = max;
            Input = input;
        }

        /// <summary>1-based label, as returned by <see cref="PixelMask.GetLabel"/>.</summary>
        public int Label { get; }

        /// <summary>Lowest pixel coordinates in the island (inclusive).</summary>
        public int2 Min { get; }

        /// <summary>Highest pixel coordinates in the island (inclusive).</summary>
        public int2 Max { get; }

        public int PixelCount => Input.PixelCount;

        public FitInput Input { get; }
    }
}

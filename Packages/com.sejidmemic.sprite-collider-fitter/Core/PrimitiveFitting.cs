using System;
using System.Collections.Generic;

namespace SpriteFit
{
    /// <summary>Picks the best allowed single primitive for a set of pixels, and fits one per island.</summary>
    public static class PrimitiveFitting
    {
        /// <summary>
        /// A cheaper shape wins when its area is within this fraction of the smallest candidate's, so a
        /// round sprite gets a circle rather than a capsule with a near-zero segment.
        /// </summary>
        public const float AreaTieTolerance = 0.005f;

        // Cheapest shape first; see PrimitiveShape.
        static readonly IPrimitiveFitter[] Fitters = { new CircleFitter(), new CapsuleFitter(), new BoxFitter() };

        /// <summary>
        /// Fits every allowed shape and keeps the one with the least area. All candidates enclose the
        /// pixels, so least area means least overfill.
        /// </summary>
        public static bool TryFitBest(FitInput input, AllowedShapes allowed, out Primitive best)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            best = default;
            if (input.PixelCount == 0) return false;

            var candidates = new List<Primitive>(Fitters.Length);
            float minArea = float.PositiveInfinity;
            foreach (IPrimitiveFitter fitter in Fitters)
            {
                if (!allowed.Allows(fitter.Shape)) continue;

                Primitive candidate = fitter.Fit(input);
                candidates.Add(candidate);
                minArea = Math.Min(minArea, candidate.Area);
            }

            foreach (Primitive candidate in candidates)
            {
                if (candidate.Area <= minArea * (1f + AreaTieTolerance))
                {
                    best = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>One primitive per island, in pixel space. Empty when the mask has no opaque pixels.</summary>
        public static List<Primitive> FitIslands(PixelMask mask, AllowedShapes allowed)
        {
            if (mask == null) throw new ArgumentNullException(nameof(mask));

            var result = new List<Primitive>(mask.Islands.Count);
            foreach (Island island in mask.Islands)
            {
                if (TryFitBest(island.Input, allowed, out Primitive primitive)) result.Add(primitive);
            }

            return result;
        }
    }
}

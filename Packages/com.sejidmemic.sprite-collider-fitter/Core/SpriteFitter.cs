using System;
using System.Collections.Generic;

namespace SpriteFit
{
    /// <summary>Entry point: fits a mask according to <see cref="FitSettings"/> and scores the result.</summary>
    public static class SpriteFitter
    {
        /// <param name="classes">Optional Width × Height buffer for the heatmap; see <see cref="ErrorEvaluator"/>.</param>
        public static FitResult Fit(PixelMask mask, FitSettings settings, byte[] classes = null)
        {
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            List<Primitive> primitives = settings.Mode == FitMode.Single
                ? PrimitiveFitting.FitIslands(mask, settings.Allowed)
                : Decomposer.Decompose(mask, settings);

            FitError error = ErrorEvaluator.Evaluate(mask, primitives, classes);
            return new FitResult(primitives, error, mask.Islands.Count);
        }
    }
}

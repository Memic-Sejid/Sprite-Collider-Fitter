using System.Collections.Generic;

namespace SpriteFit
{
    public sealed class FitResult
    {
        public FitResult(IReadOnlyList<Primitive> primitives, FitError error, int islandCount)
        {
            Primitives = primitives;
            Error = error;
            IslandCount = islandCount;
        }

        /// <summary>The fitted primitives, in the mask's pixel space.</summary>
        public IReadOnlyList<Primitive> Primitives { get; }

        public FitError Error { get; }

        public int IslandCount { get; }
    }
}

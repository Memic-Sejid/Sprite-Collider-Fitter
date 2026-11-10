using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SpriteFit
{
    /// <summary>
    /// Splits islands into several primitives (FR-03). Starts with one cluster per island, then repeatedly
    /// splits the cluster with the most overfill along the best straight cut, until the weighted error is
    /// within tolerance or the primitive limit is reached. A merge pass then joins touching primitives
    /// whose union costs little extra.
    /// </summary>
    /// <remarks>
    /// Cuts are tried perpendicular to the x axis, the y axis, and the cluster's principal and secondary
    /// axes, at every pixel step (up to <see cref="MaxBins"/> positions per direction). Each side's hull
    /// and moments are built incrementally, so a whole sweep costs about one fit per position.
    /// While searching, a cluster's error is its primitive's area minus its pixel count: every fit
    /// encloses its pixels, so that is its overfill if nothing else overlaps it.
    /// </remarks>
    public static class Decomposer
    {
        public const int MaxBins = 256;
        public const double MergeRiseLimit = 0.1;
        const int MaxMergeIterations = 32;
        const double MinImprovement = 0.5;
        static readonly double AxisAlignedCos = math.cos(math.radians(3.0));

        sealed class Cluster
        {
            public int Island;
            public int[] Pixels;
            public FitInput Input;
            public Primitive Primitive;
            public double Error;
            public bool Unsplittable;
        }

        public static List<Primitive> Decompose(PixelMask mask, FitSettings settings)
        {
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            List<Cluster> clusters = InitialClusters(mask, settings.Allowed);
            if (clusters.Count == 0) return new List<Primitive>();

            int maxPrimitives = math.max(1, settings.MaxPrimitives);
            double current = TotalError(mask, clusters, settings);
            var search = new SplitSearch(mask, settings.Allowed);

            // Concave shapes often get worse before they get better: two halves of a ring need more area
            // than one circle, but eight arcs need much less. So the best split is always taken, even when
            // it doesn't help yet, and the best configuration seen is what's returned.
            var best = new List<Cluster>(clusters);
            double bestError = current;

            while (current > settings.Tolerance && clusters.Count < maxPrimitives)
            {
                Cluster worst = null;
                foreach (Cluster cluster in clusters)
                {
                    if (!cluster.Unsplittable && (worst == null || cluster.Error > worst.Error)) worst = cluster;
                }

                if (worst == null || worst.Error < MinImprovement) break;

                if (!search.TrySplit(worst, out Cluster a, out Cluster b))
                {
                    worst.Unsplittable = true;
                    continue;
                }

                int index = clusters.IndexOf(worst);
                clusters[index] = a;
                clusters.Insert(index + 1, b);
                current = TotalError(mask, clusters, settings);

                if (current < bestError)
                {
                    best = new List<Cluster>(clusters);
                    bestError = current;
                }
            }

            clusters = best;
            current = bestError;
            if (settings.MergePass) Merge(mask, clusters, settings, current);

            var result = new List<Primitive>(clusters.Count);
            foreach (Cluster cluster in clusters) result.Add(cluster.Primitive);
            return result;
        }

        static List<Cluster> InitialClusters(PixelMask mask, AllowedShapes allowed)
        {
            IReadOnlyList<Island> islands = mask.Islands;
            var pixels = new int[islands.Count][];
            var filled = new int[islands.Count];
            for (int i = 0; i < islands.Count; i++) pixels[i] = new int[islands[i].PixelCount];

            int total = mask.Width * mask.Height;
            for (int index = 0; index < total; index++)
            {
                int label = mask.LabelAt(index);
                if (label != 0) pixels[label - 1][filled[label - 1]++] = index;
            }

            var clusters = new List<Cluster>(islands.Count);
            for (int i = 0; i < islands.Count; i++)
            {
                Cluster cluster = MakeCluster(islands[i].Label, pixels[i], islands[i].Input, allowed);
                if (cluster == null) return new List<Cluster>();

                clusters.Add(cluster);
            }

            return clusters;
        }

        static Cluster MakeCluster(int island, int[] pixels, FitInput input, AllowedShapes allowed)
        {
            if (!PrimitiveFitting.TryFitBest(input, allowed, out Primitive primitive)) return null;

            return new Cluster { Island = island, Pixels = pixels, Input = input, Primitive = primitive, Error = Estimate(primitive, input) };
        }

        static double Estimate(Primitive primitive, FitInput input)
        {
            return math.max(0.0, primitive.Area - input.PixelCount);
        }

        static double TotalError(PixelMask mask, List<Cluster> clusters, FitSettings settings)
        {
            var primitives = new List<Primitive>(clusters.Count);
            foreach (Cluster cluster in clusters) primitives.Add(cluster.Primitive);
            return ErrorEvaluator.Evaluate(mask, primitives).Weighted(settings.OverWeight, settings.UnderWeight);
        }

        static void Merge(PixelMask mask, List<Cluster> clusters, FitSettings settings, double current)
        {
            var rejected = new HashSet<(Cluster, Cluster)>();

            for (int iteration = 0; iteration < MaxMergeIterations; iteration++)
            {
                Cluster bestA = null, bestB = null, bestUnion = null;
                double bestRise = double.PositiveInfinity;

                for (int i = 0; i < clusters.Count; i++)
                {
                    for (int j = i + 1; j < clusters.Count; j++)
                    {
                        Cluster a = clusters[i], b = clusters[j];
                        if (a.Island != b.Island || rejected.Contains((a, b)) || !Touching(a.Primitive, b.Primitive)) continue;

                        FitInput input = FitInput.Combine(a.Input, b.Input);
                        if (!PrimitiveFitting.TryFitBest(input, settings.Allowed, out Primitive primitive)) continue;

                        double pairError = a.Error + b.Error;
                        double unionError = Estimate(primitive, input);
                        double rise = unionError - pairError;
                        if (rise >= MergeRiseLimit * pairError || rise >= bestRise) continue;

                        bestRise = rise;
                        bestA = a;
                        bestB = b;
                        bestUnion = new Cluster { Island = a.Island, Input = input, Primitive = primitive, Error = unionError };
                    }
                }

                if (bestUnion == null) return;

                var merged = new List<Cluster>(clusters);
                int index = merged.IndexOf(bestA);
                merged.Remove(bestB);
                merged[index] = bestUnion;

                // Only keep a merge that stays within tolerance or doesn't make things worse.
                double total = TotalError(mask, merged, settings);
                if (total > math.max(settings.Tolerance, current))
                {
                    rejected.Add((bestA, bestB));
                    continue;
                }

                bestUnion.Pixels = new int[bestA.Pixels.Length + bestB.Pixels.Length];
                bestA.Pixels.CopyTo(bestUnion.Pixels, 0);
                bestB.Pixels.CopyTo(bestUnion.Pixels, bestA.Pixels.Length);
                clusters.Clear();
                clusters.AddRange(merged);
                current = total;
            }
        }

        static bool Touching(Primitive a, Primitive b)
        {
            a.GetBounds(out float2 minA, out float2 maxA);
            b.GetBounds(out float2 minB, out float2 maxB);
            return math.all(minA <= maxB + 1f & minB <= maxA + 1f);
        }

        /// <summary>Reusable buffers for sweeping cut positions over one cluster at a time.</summary>
        sealed class SplitSearch
        {
            readonly PixelMask _mask;
            readonly AllowedShapes _allowed;
            readonly List<double2> _candidates = new List<double2>();
            double[] _projections = new double[0];
            int[] _binRowMin = new int[0];
            int[] _binRowMax = new int[0];

            // The lowest-error cut, and the lowest-error cut through the middle (by pixel count).
            Cut _best, _median;

            sealed class Cut
            {
                public double Error = double.PositiveInfinity;
                public FitInput InputA, InputB;
                public Primitive A, B;
                public double2 Direction;
                public double Origin, BinWidth;
                public int BinCount, Index;
            }

            public SplitSearch(PixelMask mask, AllowedShapes allowed)
            {
                _mask = mask;
                _allowed = allowed;
            }

            /// <summary>
            /// Splits along the cut that lowers the cluster's error most. When no cut lowers it (as with the
            /// first cut of a ring), splits through the middle instead so later cuts can make progress.
            /// </summary>
            public bool TrySplit(Cluster cluster, out Cluster a, out Cluster b)
            {
                a = b = null;
                int[] pixels = cluster.Pixels;
                if (pixels.Length < 2) return false;

                int width = _mask.Width;
                int minY = int.MaxValue, maxY = int.MinValue;
                foreach (int index in pixels)
                {
                    int y = index / width;
                    minY = math.min(minY, y);
                    maxY = math.max(maxY, y);
                }

                _best = new Cut();
                _median = new Cut();

                Sweep(pixels, minY, maxY, new double2(1, 0));
                Sweep(pixels, minY, maxY, new double2(0, 1));

                double theta = cluster.Input.PrincipalAngle;
                var principal = new double2(math.cos(theta), math.sin(theta));
                if (math.abs(principal.x) < AxisAlignedCos && math.abs(principal.y) < AxisAlignedCos)
                {
                    Sweep(pixels, minY, maxY, principal);
                    Sweep(pixels, minY, maxY, new double2(-principal.y, principal.x));
                }

                Cut cut = _best.Error < cluster.Error - MinImprovement || _median.InputA == null ? _best : _median;
                if (cut.InputA == null) return false;

                // Partition with exactly the binning the sweep used.
                var left = new List<int>(cut.InputA.PixelCount);
                var right = new List<int>(cut.InputB.PixelCount);
                foreach (int index in pixels)
                {
                    if (BinOf(Projection(index, cut.Direction), cut.Origin, cut.BinWidth, cut.BinCount) <= cut.Index) left.Add(index);
                    else right.Add(index);
                }

                a = new Cluster { Island = cluster.Island, Pixels = left.ToArray(), Input = cut.InputA, Primitive = cut.A, Error = Estimate(cut.A, cut.InputA) };
                b = new Cluster { Island = cluster.Island, Pixels = right.ToArray(), Input = cut.InputB, Primitive = cut.B, Error = Estimate(cut.B, cut.InputB) };
                return true;
            }

            void Sweep(int[] pixels, int minY, int maxY, double2 direction)
            {
                int n = pixels.Length;
                if (_projections.Length < n) _projections = new double[n];

                double origin = double.PositiveInfinity, end = double.NegativeInfinity;
                for (int i = 0; i < n; i++)
                {
                    double s = Projection(pixels[i], direction);
                    _projections[i] = s;
                    origin = math.min(origin, s);
                    end = math.max(end, s);
                }

                double extent = end - origin;
                if (extent < 0.5) return;

                double binWidth = math.max(1.0, extent / (MaxBins - 1));
                int binCount = (int)math.floor(extent / binWidth + 1e-9) + 1;
                int rows = maxY - minY + 1;
                int cells = binCount * rows;
                if (_binRowMin.Length < cells)
                {
                    _binRowMin = new int[cells];
                    _binRowMax = new int[cells];
                }

                for (int i = 0; i < cells; i++)
                {
                    _binRowMin[i] = int.MaxValue;
                    _binRowMax[i] = int.MinValue;
                }

                var moments = new PixelMoments[binCount];
                int width = _mask.Width;
                for (int i = 0; i < n; i++)
                {
                    int index = pixels[i];
                    int x = index % width, y = index / width;
                    int bin = BinOf(_projections[i], origin, binWidth, binCount);
                    int cell = bin * rows + (y - minY);
                    _binRowMin[cell] = math.min(_binRowMin[cell], x);
                    _binRowMax[cell] = math.max(_binRowMax[cell], x);
                    moments[bin].Add(x, y);
                }

                var binHulls = new double2[binCount][];
                for (int bin = 0; bin < binCount; bin++)
                {
                    if (moments[bin].Count == 0) continue;

                    _candidates.Clear();
                    for (int row = 0; row < rows; row++)
                    {
                        int cell = bin * rows + row;
                        if (_binRowMin[cell] > _binRowMax[cell]) continue;

                        double y = minY + row;
                        double left = _binRowMin[cell], right = _binRowMax[cell];
                        _candidates.Add(new double2(left, y + 0.5));
                        _candidates.Add(new double2(left + 0.5, y));
                        _candidates.Add(new double2(left + 0.5, y + 1.0));
                        _candidates.Add(new double2(right + 1.0, y + 0.5));
                        _candidates.Add(new double2(right + 0.5, y));
                        _candidates.Add(new double2(right + 0.5, y + 1.0));
                    }

                    binHulls[bin] = Hull2D.Build(_candidates);
                }

                // Hull and moments of everything up to and including each bin, and from each bin on.
                var prefixHulls = new double2[binCount][];
                var prefixMoments = new PixelMoments[binCount];
                double2[] hull = null;
                PixelMoments sum = default;
                for (int bin = 0; bin < binCount; bin++)
                {
                    if (moments[bin].Count > 0)
                    {
                        hull = hull == null ? binHulls[bin] : Union(hull, binHulls[bin]);
                        sum += moments[bin];
                    }

                    prefixHulls[bin] = hull;
                    prefixMoments[bin] = sum;
                }

                var suffixHulls = new double2[binCount][];
                hull = null;
                for (int bin = binCount - 1; bin >= 0; bin--)
                {
                    if (moments[bin].Count > 0) hull = hull == null ? binHulls[bin] : Union(hull, binHulls[bin]);
                    suffixHulls[bin] = hull;
                }

                PixelMoments all = prefixMoments[binCount - 1];
                for (int cut = 0; cut < binCount - 1; cut++)
                {
                    // An empty bin gives the same split as the cut before it.
                    if (moments[cut].Count == 0 || prefixMoments[cut].Count == all.Count) continue;

                    var inputA = new FitInput(prefixMoments[cut], prefixHulls[cut]);
                    var inputB = new FitInput(all - prefixMoments[cut], suffixHulls[cut + 1]);
                    if (!PrimitiveFitting.TryFitBest(inputA, _allowed, out Primitive a)) continue;
                    if (!PrimitiveFitting.TryFitBest(inputB, _allowed, out Primitive b)) continue;

                    double error = Estimate(a, inputA) + Estimate(b, inputB);
                    bool isMedian = 2 * prefixMoments[cut].Count >= all.Count && (cut == 0 || 2 * prefixMoments[cut - 1].Count < all.Count);

                    if (error < _best.Error) Record(_best);
                    if (isMedian && error < _median.Error) Record(_median);

                    void Record(Cut target)
                    {
                        target.Error = error;
                        target.InputA = inputA;
                        target.InputB = inputB;
                        target.A = a;
                        target.B = b;
                        target.Direction = direction;
                        target.Origin = origin;
                        target.BinWidth = binWidth;
                        target.BinCount = binCount;
                        target.Index = cut;
                    }
                }
            }

            double Projection(int index, double2 direction)
            {
                int width = _mask.Width;
                return (index % width + 0.5) * direction.x + (index / width + 0.5) * direction.y;
            }

            static int BinOf(double projection, double origin, double binWidth, int binCount)
            {
                return math.clamp((int)math.floor((projection - origin) / binWidth + 1e-9), 0, binCount - 1);
            }

            static double2[] Union(double2[] a, double2[] b)
            {
                var points = new double2[a.Length + b.Length];
                a.CopyTo(points, 0);
                b.CopyTo(points, a.Length);
                return Hull2D.Build(points);
            }
        }
    }
}

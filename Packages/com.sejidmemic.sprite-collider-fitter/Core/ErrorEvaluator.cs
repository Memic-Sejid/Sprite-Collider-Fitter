using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SpriteFit
{
    /// <summary>
    /// Counts overfill and underfill by testing every pixel centre against the primitives, as a Burst job
    /// over rows. Optionally classifies each mask pixel for the heatmap.
    /// </summary>
    public static class ErrorEvaluator
    {
        /// <summary>Pixel class: transparent pixel inside a primitive.</summary>
        public const byte Over = 1;

        /// <summary>Pixel class: opaque pixel outside every primitive.</summary>
        public const byte Under = 2;

        /// <param name="primitives">Primitives in the mask's pixel space.</param>
        public static FitError Evaluate(PixelMask mask, IReadOnlyList<Primitive> primitives)
        {
            return Evaluate(mask, primitives, null);
        }

        /// <param name="primitives">Primitives in the mask's pixel space.</param>
        /// <param name="classes">
        /// Optional, Width × Height row-major. Receives <see cref="Over"/>, <see cref="Under"/> or 0 for each
        /// mask pixel. Overfill beyond the mask's edges is counted but has no pixel to mark.
        /// </param>
        public static FitError Evaluate(PixelMask mask, IReadOnlyList<Primitive> primitives, byte[] classes)
        {
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            if (primitives == null) throw new ArgumentNullException(nameof(primitives));

            int pixelCount = mask.Width * mask.Height;
            if (classes != null && classes.Length != pixelCount) throw new ArgumentException("Length must be Width × Height.", nameof(classes));

            int count = primitives.Count;

            // Primitives can reach past the mask, and transparent pixels out there count as overfill.
            // Never look further than one mask size beyond any edge.
            var regionMin = new int2(0, 0);
            var regionMax = new int2(mask.Width - 1, mask.Height - 1);
            for (int i = 0; i < count; i++)
            {
                primitives[i].GetBounds(out float2 min, out float2 max);
                regionMin = math.min(regionMin, (int2)math.floor(min));
                regionMax = math.max(regionMax, (int2)math.ceil(max));
            }

            regionMin = math.max(regionMin, new int2(-mask.Width, -mask.Height));
            regionMax = math.min(regionMax, new int2(2 * mask.Width, 2 * mask.Height));
            int regionWidth = regionMax.x - regionMin.x + 1;
            int regionHeight = regionMax.y - regionMin.y + 1;
            if (regionWidth <= 0 || regionHeight <= 0) return new FitError(mask.OpaqueCount, 0, mask.OpaqueCount);

            var maskData = new NativeArray<byte>(pixelCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var primitiveData = new NativeArray<Primitive>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var bounds = new NativeArray<float4>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var rowCounts = new NativeArray<int2>(regionHeight, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var classData = new NativeArray<byte>(classes != null ? pixelCount : 0, Allocator.TempJob);

            try
            {
                mask.CopyTo(maskData);
                for (int i = 0; i < count; i++)
                {
                    primitiveData[i] = primitives[i];
                    primitives[i].GetBounds(out float2 min, out float2 max);
                    bounds[i] = new float4(min, max);
                }

                var job = new CoverageJob
                {
                    Mask = maskData,
                    Width = mask.Width,
                    Height = mask.Height,
                    Primitives = primitiveData,
                    Bounds = bounds,
                    RegionMin = regionMin,
                    RegionWidth = regionWidth,
                    WriteClasses = classes != null,
                    Classes = classData,
                    RowCounts = rowCounts,
                };
                job.Schedule(regionHeight, 8).Complete();

                int over = 0, under = 0;
                for (int i = 0; i < regionHeight; i++)
                {
                    over += rowCounts[i].x;
                    under += rowCounts[i].y;
                }

                if (classes != null) classData.CopyTo(classes);
                return new FitError(mask.OpaqueCount, over, under);
            }
            finally
            {
                maskData.Dispose();
                primitiveData.Dispose();
                bounds.Dispose();
                rowCounts.Dispose();
                classData.Dispose();
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct CoverageJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> Mask;
            public int Width;
            public int Height;
            [ReadOnly] public NativeArray<Primitive> Primitives;
            [ReadOnly] public NativeArray<float4> Bounds;
            public int2 RegionMin;
            public int RegionWidth;
            public bool WriteClasses;

            // Each row writes only its own pixels.
            [NativeDisableParallelForRestriction] public NativeArray<byte> Classes;

            [WriteOnly] public NativeArray<int2> RowCounts;

            public void Execute(int row)
            {
                int y = RegionMin.y + row;
                bool rowInMask = (uint)y < (uint)Height;
                int over = 0, under = 0;

                for (int i = 0; i < RegionWidth; i++)
                {
                    int x = RegionMin.x + i;
                    var p = new float2(x + 0.5f, y + 0.5f);

                    bool inside = false;
                    for (int k = 0; k < Primitives.Length && !inside; k++)
                    {
                        float4 b = Bounds[k];
                        if (p.x < b.x || p.y < b.y || p.x > b.z || p.y > b.w) continue;

                        inside = Primitives[k].Contains(p);
                    }

                    bool inMask = rowInMask && (uint)x < (uint)Width;
                    bool opaque = inMask && Mask[y * Width + x] != 0;
                    byte pixelClass = 0;
                    if (inside && !opaque)
                    {
                        over++;
                        pixelClass = Over;
                    }
                    else if (!inside && opaque)
                    {
                        under++;
                        pixelClass = Under;
                    }

                    if (WriteClasses && inMask) Classes[y * Width + x] = pixelClass;
                }

                RowCounts[row] = new int2(over, under);
            }
        }
    }
}

using System;
using UnityEngine;

namespace SpriteFit
{
    /// <summary>Everything that controls a fit. Defaults follow the spec.</summary>
    [Serializable]
    public sealed class FitSettings
    {
        public const int MaxPrimitivesLimit = 16;

        public FitMode Mode = FitMode.Compound;

        [Range(1, MaxPrimitivesLimit)]
        public int MaxPrimitives = 4;

        public AllowedShapes Allowed = AllowedShapes.All;

        /// <summary>Target weighted error E as a fraction of the opaque area (0.03 = 3%).</summary>
        [Range(0f, 1f)]
        public float Tolerance = 0.03f;

        /// <summary>Alpha (0–1) at or above which a pixel counts as opaque.</summary>
        [Range(0.01f, 1f)]
        public float AlphaThreshold = 0.5f;

        /// <summary>w_o: weight of overfill (objects hit empty air).</summary>
        [Min(0f)]
        public float OverWeight = 1f;

        /// <summary>w_u: weight of underfill (objects clip through).</summary>
        [Min(0f)]
        public float UnderWeight = 2f;

        /// <summary>After splitting, merge touching primitives when that raises their error by less than 10%.</summary>
        public bool MergePass = true;

        public FitSettings Clone()
        {
            return (FitSettings)MemberwiseClone();
        }
    }
}

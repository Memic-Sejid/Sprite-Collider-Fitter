using System;
using Unity.Mathematics;

namespace SpriteFit
{
    /// <summary>
    /// What a fitter needs to know about a set of opaque pixels: its convex hull and its moments.
    /// Coordinates are in pixel space; pixel (x, y) has its centre at (x + 0.5, y + 0.5).
    /// </summary>
    public sealed class FitInput
    {
        public FitInput(PixelMoments moments, double2[] hull)
        {
            Moments = moments;
            Hull = hull ?? throw new ArgumentNullException(nameof(hull));
        }

        public PixelMoments Moments { get; }

        public int PixelCount => Moments.Count;

        /// <summary>Mean of the pixel centres.</summary>
        public double2 Centroid => Moments.Centroid;

        /// <summary>Population covariance of the pixel centres as (xx, xy, yy).</summary>
        public double3 Covariance => Moments.Covariance;

        /// <summary>
        /// Counter-clockwise convex hull of the pixels' edge midpoints. A shape that contains it contains
        /// every pixel centre, and reaches the outer edge of pixels on horizontal and vertical boundaries.
        /// </summary>
        public double2[] Hull { get; }

        /// <summary>Angle of the principal axis of the pixel covariance, in radians.</summary>
        public double PrincipalAngle
        {
            get
            {
                double3 c = Covariance;
                return 0.5 * math.atan2(2.0 * c.y, c.x - c.z);
            }
        }

        /// <summary>The input for the union of two disjoint pixel sets: hull of both hulls, summed moments.</summary>
        public static FitInput Combine(FitInput a, FitInput b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            var points = new double2[a.Hull.Length + b.Hull.Length];
            a.Hull.CopyTo(points, 0);
            b.Hull.CopyTo(points, a.Hull.Length);
            return new FitInput(a.Moments + b.Moments, Hull2D.Build(points));
        }
    }
}

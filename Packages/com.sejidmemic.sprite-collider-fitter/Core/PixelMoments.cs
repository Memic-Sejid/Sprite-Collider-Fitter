using Unity.Mathematics;

namespace SpriteFit
{
    /// <summary>
    /// Count and raw sums of pixel centres. Sums add, so the moments of a union are the sum of the parts.
    /// </summary>
    public struct PixelMoments
    {
        public int Count;
        public double SumX, SumY, SumXX, SumXY, SumYY;

        public void Add(int x, int y)
        {
            double cx = x + 0.5;
            double cy = y + 0.5;
            Count++;
            SumX += cx;
            SumY += cy;
            SumXX += cx * cx;
            SumXY += cx * cy;
            SumYY += cy * cy;
        }

        public static PixelMoments operator +(PixelMoments a, PixelMoments b)
        {
            return new PixelMoments
            {
                Count = a.Count + b.Count,
                SumX = a.SumX + b.SumX,
                SumY = a.SumY + b.SumY,
                SumXX = a.SumXX + b.SumXX,
                SumXY = a.SumXY + b.SumXY,
                SumYY = a.SumYY + b.SumYY,
            };
        }

        public static PixelMoments operator -(PixelMoments a, PixelMoments b)
        {
            return new PixelMoments
            {
                Count = a.Count - b.Count,
                SumX = a.SumX - b.SumX,
                SumY = a.SumY - b.SumY,
                SumXX = a.SumXX - b.SumXX,
                SumXY = a.SumXY - b.SumXY,
                SumYY = a.SumYY - b.SumYY,
            };
        }

        /// <summary>Mean of the pixel centres; zero when empty.</summary>
        public double2 Centroid => Count > 0 ? new double2(SumX / Count, SumY / Count) : double2.zero;

        /// <summary>Population covariance of the pixel centres as (xx, xy, yy); zero when empty.</summary>
        public double3 Covariance
        {
            get
            {
                if (Count == 0) return double3.zero;

                double n = Count;
                double2 c = Centroid;
                // Variances can come out a hair below zero from rounding.
                return new double3(
                    math.max(SumXX / n - c.x * c.x, 0.0),
                    SumXY / n - c.x * c.y,
                    math.max(SumYY / n - c.y * c.y, 0.0));
            }
        }
    }
}

using System;
using Unity.Mathematics;

namespace SpriteFit.Tests
{
    /// <summary>Masks with known shapes. A pixel is opaque when its centre is inside the shape.</summary>
    internal static class SyntheticMasks
    {
        public static PixelMask FromPredicate(int width, int height, Func<double2, bool> inside)
        {
            var opaque = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    opaque[y * width + x] = inside(new double2(x + 0.5, y + 0.5));
                }
            }

            return new PixelMask(width, height, opaque, 100f, new float2(width * 0.5f, height * 0.5f));
        }

        public static PixelMask Circle(int size, double2 center, double radius)
        {
            return FromPredicate(size, size, p => math.distancesq(p, center) <= radius * radius);
        }

        public static PixelMask Box(int size, double2 center, double2 halfSize, double angleDegrees)
        {
            return FromPredicate(size, size, p => math.all(math.abs(ToShapeFrame(p, center, angleDegrees)) <= halfSize));
        }

        public static PixelMask Capsule(int size, double2 center, double angleDegrees, double halfLength, double radius)
        {
            return FromPredicate(size, size, p =>
            {
                double2 q = ToShapeFrame(p, center, angleDegrees);
                q.x = math.max(math.abs(q.x) - halfLength, 0.0);
                return math.lengthsq(q) <= radius * radius;
            });
        }

        public static PixelMask Empty(int width, int height)
        {
            return new PixelMask(width, height, new bool[width * height]);
        }

        public static PixelMask Full(int width, int height)
        {
            var opaque = new bool[width * height];
            for (int i = 0; i < opaque.Length; i++) opaque[i] = true;
            return new PixelMask(width, height, opaque);
        }

        static double2 ToShapeFrame(double2 p, double2 center, double angleDegrees)
        {
            double a = math.radians(angleDegrees);
            double c = math.cos(a), s = math.sin(a);
            double2 d = p - center;
            return new double2(c * d.x + s * d.y, -s * d.x + c * d.y);
        }
    }
}

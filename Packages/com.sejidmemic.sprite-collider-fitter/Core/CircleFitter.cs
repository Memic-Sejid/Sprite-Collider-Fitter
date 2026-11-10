using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SpriteFit
{
    /// <summary>Minimal enclosing circle by Welzl's algorithm (iterative form), expected O(n).</summary>
    public sealed class CircleFitter : IPrimitiveFitter
    {
        // Fixed so the same input always gives the same result.
        const int ShuffleSeed = 0x5EED;

        public PrimitiveShape Shape => PrimitiveShape.Circle;

        public Primitive Fit(FitInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            MinimumEnclosingCircle(input.Hull, out double2 center, out double radius);
            return Primitive.Circle((float2)center, (float)radius);
        }

        public static void MinimumEnclosingCircle(IReadOnlyList<double2> points, out double2 center, out double radius)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (points.Count == 0) throw new ArgumentException("At least one point is required.", nameof(points));

            int n = points.Count;
            var p = new double2[n];
            for (int i = 0; i < n; i++) p[i] = points[i];

            var random = new System.Random(ShuffleSeed);
            for (int i = n - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (p[i], p[j]) = (p[j], p[i]);
            }

            double2 c = p[0];
            double r2 = 0;

            for (int i = 1; i < n; i++)
            {
                if (Inside(p[i], c, r2)) continue;

                c = p[i];
                r2 = 0;
                for (int j = 0; j < i; j++)
                {
                    if (Inside(p[j], c, r2)) continue;

                    c = (p[i] + p[j]) * 0.5;
                    r2 = math.distancesq(p[i], c);
                    for (int k = 0; k < j; k++)
                    {
                        if (Inside(p[k], c, r2)) continue;

                        Circumcircle(p[i], p[j], p[k], out c, out r2);
                    }
                }
            }

            center = c;
            radius = math.sqrt(r2);
        }

        static bool Inside(double2 p, double2 c, double r2)
        {
            return math.distancesq(p, c) <= r2 + 1e-9 * (1.0 + r2);
        }

        static void Circumcircle(double2 a, double2 b, double2 c, out double2 center, out double r2)
        {
            double2 ab = b - a;
            double2 ac = c - a;
            double ab2 = math.dot(ab, ab);
            double ac2 = math.dot(ac, ac);
            double d = 2.0 * (ab.x * ac.y - ab.y * ac.x);

            if (math.abs(d) <= 1e-12 * math.max(1.0, ab2 + ac2))
            {
                // Collinear: the circle on the farthest pair covers the third point.
                double bc2 = math.distancesq(b, c);
                if (ab2 >= ac2 && ab2 >= bc2) center = (a + b) * 0.5;
                else if (ac2 >= bc2) center = (a + c) * 0.5;
                else center = (b + c) * 0.5;

                r2 = math.max(math.distancesq(a, center), math.max(math.distancesq(b, center), math.distancesq(c, center)));
                return;
            }

            var offset = new double2((ac.y * ab2 - ab.y * ac2) / d, (ab.x * ac2 - ac.x * ab2) / d);
            center = a + offset;
            r2 = math.dot(offset, offset);
        }
    }
}

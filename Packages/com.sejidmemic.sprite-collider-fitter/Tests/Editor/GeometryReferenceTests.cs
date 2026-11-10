using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace SpriteFit.Tests
{
    /// <summary>Hull, Welzl and rotating calipers against brute-force references on 1,000 random point sets each.</summary>
    public class GeometryReferenceTests
    {
        const int SetCount = 1000;

        [Test]
        public void Hull_MatchesDefinitionOnRandomSets()
        {
            var random = new System.Random(1);
            for (int set = 0; set < SetCount; set++)
            {
                // Integer points on a small grid: lots of duplicates and collinear runs.
                List<double2> points = RandomPoints(random, random.Next(1, 40), 12, integer: true);
                double2[] hull = Hull2D.Build(points);
                string context = $"set {set}";

                foreach (double2 vertex in hull) Assert.That(points.Contains(vertex), Is.True, context);

                if (hull.Length >= 3)
                {
                    for (int i = 0; i < hull.Length; i++)
                    {
                        double2 a = hull[i], b = hull[(i + 1) % hull.Length], c = hull[(i + 2) % hull.Length];
                        Assert.That(Hull2D.Cross(a, b, c), Is.GreaterThan(0), $"{context}: not strictly convex CCW at {i}");
                        foreach (double2 p in points) Assert.That(Hull2D.Cross(a, b, p), Is.GreaterThanOrEqualTo(0), $"{context}: {p} outside edge {i}");
                    }
                }
                else
                {
                    // Fewer than 3 vertices only when every point lies on one segment.
                    foreach (double2 p in points) Assert.That(Hull2D.Cross(hull[0], hull[hull.Length - 1], p), Is.EqualTo(0), context);
                }
            }
        }

        [Test]
        public void Welzl_MatchesBruteForceRadius()
        {
            var random = new System.Random(2);
            for (int set = 0; set < SetCount; set++)
            {
                List<double2> points = RandomPoints(random, random.Next(1, 13), 100, integer: false);

                CircleFitter.MinimumEnclosingCircle(points, out double2 center, out double radius);
                double expected = BruteForceEnclosingRadius(points);

                Assert.That(radius, Is.EqualTo(expected).Within(1e-6 * (1 + expected)), $"set {set}");
                foreach (double2 p in points) Assert.That(math.distance(p, center), Is.LessThanOrEqualTo(radius + 1e-6), $"set {set}");
            }
        }

        [Test]
        public void Calipers_MatchesBruteForceArea()
        {
            var random = new System.Random(3);
            for (int set = 0; set < SetCount; set++)
            {
                double2[] hull = Hull2D.Build(RandomPoints(random, random.Next(3, 60), 100, integer: false));
                if (hull.Length < 3) continue;

                BoxFitter.MinimumAreaRectangle(hull, out double2 center, out double2 halfSize, out double angle);
                double area = 4 * halfSize.x * halfSize.y;
                double expected = BruteForceRectangleArea(hull);

                Assert.That(area, Is.EqualTo(expected).Within(1e-6 * (1 + expected)), $"set {set}");

                var box = new Primitive { Shape = PrimitiveShape.Box, Center = (float2)center, Angle = (float)angle, Extents = (float2)halfSize };
                foreach (double2 p in hull) Assert.That(box.SignedDistance((float2)p), Is.LessThanOrEqualTo(1e-3f), $"set {set}");
            }
        }

        static List<double2> RandomPoints(System.Random random, int count, double range, bool integer)
        {
            var points = new List<double2>(count);
            for (int i = 0; i < count; i++)
            {
                double x = random.NextDouble() * range, y = random.NextDouble() * range;
                points.Add(integer ? math.floor(new double2(x, y)) : new double2(x, y));
            }

            return points;
        }

        // Smallest circle among those through 2 (diametral) or 3 points that contain every point.
        static double BruteForceEnclosingRadius(List<double2> p)
        {
            int n = p.Count;
            if (n == 1) return 0;

            double best = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double2 c = (p[i] + p[j]) * 0.5;
                    best = Math.Min(best, RadiusIfEnclosing(p, c, math.distance(p[i], c)));

                    for (int k = j + 1; k < n; k++)
                    {
                        double2 ab = p[j] - p[i], ac = p[k] - p[i];
                        double d = 2 * (ab.x * ac.y - ab.y * ac.x);
                        if (math.abs(d) < 1e-9) continue;

                        double ab2 = math.dot(ab, ab), ac2 = math.dot(ac, ac);
                        var offset = new double2((ac.y * ab2 - ab.y * ac2) / d, (ab.x * ac2 - ac.x * ab2) / d);
                        best = Math.Min(best, RadiusIfEnclosing(p, p[i] + offset, math.length(offset)));
                    }
                }
            }

            return best;
        }

        static double RadiusIfEnclosing(List<double2> points, double2 center, double radius)
        {
            foreach (double2 q in points)
            {
                if (math.distance(q, center) > radius * (1 + 1e-9) + 1e-9) return double.PositiveInfinity;
            }

            return radius;
        }

        // Freeman–Shapira: try every hull edge as a rectangle side, O(h²).
        static double BruteForceRectangleArea(double2[] hull)
        {
            double best = double.PositiveInfinity;
            for (int i = 0; i < hull.Length; i++)
            {
                double2 u = math.normalize(hull[(i + 1) % hull.Length] - hull[i]);
                var v = new double2(-u.y, u.x);
                double minU = double.PositiveInfinity, maxU = double.NegativeInfinity, minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
                foreach (double2 p in hull)
                {
                    minU = Math.Min(minU, math.dot(p, u));
                    maxU = Math.Max(maxU, math.dot(p, u));
                    minV = Math.Min(minV, math.dot(p, v));
                    maxV = Math.Max(maxV, math.dot(p, v));
                }

                best = Math.Min(best, (maxU - minU) * (maxV - minV));
            }

            return best;
        }
    }
}

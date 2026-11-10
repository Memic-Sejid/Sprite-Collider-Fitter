using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SpriteFit
{
    /// <summary>
    /// Minimum-area enclosing rectangle by rotating calipers, O(h) on h hull points. By the
    /// Freeman–Shapira theorem one side of the optimal rectangle lies along a hull edge.
    /// </summary>
    public sealed class BoxFitter : IPrimitiveFitter
    {
        const double Epsilon = 1e-9;

        public PrimitiveShape Shape => PrimitiveShape.Box;

        public Primitive Fit(FitInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            MinimumAreaRectangle(input.Hull, out double2 center, out double2 halfSize, out double angle);
            return Primitive.Box((float2)center, (float2)halfSize, (float)angle);
        }

        /// <param name="hull">Convex polygon, counter-clockwise, no collinear or repeated points.</param>
        /// <param name="angle">Direction of the rectangle's x axis, in radians.</param>
        public static void MinimumAreaRectangle(IReadOnlyList<double2> hull, out double2 center, out double2 halfSize, out double angle)
        {
            if (hull == null) throw new ArgumentNullException(nameof(hull));

            int n = hull.Count;
            if (n == 0) throw new ArgumentException("At least one point is required.", nameof(hull));

            if (n == 1)
            {
                center = hull[0];
                halfSize = double2.zero;
                angle = 0;
                return;
            }

            if (n == 2)
            {
                double2 d = hull[1] - hull[0];
                center = (hull[0] + hull[1]) * 0.5;
                halfSize = new double2(math.length(d) * 0.5, 0);
                angle = math.atan2(d.y, d.x);
                return;
            }

            double bestArea = double.PositiveInfinity;
            center = double2.zero;
            halfSize = double2.zero;
            angle = 0;

            // Extreme points along the edge direction (right, left) and its normal (top). Each only moves
            // forward around the hull as the edge direction turns, which is what makes this linear.
            int right = 0, top = 0, left = 0;

            for (int i = 0; i < n; i++)
            {
                double2 a = hull[i];
                double2 u = math.normalize(hull[(i + 1) % n] - a);
                var v = new double2(-u.y, u.x);

                if (i == 0)
                {
                    for (int k = 1; k < n; k++)
                    {
                        if (math.dot(hull[k], u) > math.dot(hull[right], u)) right = k;
                        if (math.dot(hull[k], v) > math.dot(hull[top], v)) top = k;
                        if (math.dot(hull[k], u) < math.dot(hull[left], u)) left = k;
                    }
                }
                else
                {
                    right = Advance(hull, right, u);
                    top = Advance(hull, top, v);
                    left = Advance(hull, left, -u);
                }

                double minU = math.dot(hull[left] - a, u);
                double maxU = math.dot(hull[right] - a, u);
                double height = math.dot(hull[top] - a, v);
                double width = maxU - minU;
                double area = width * height;

                if (area < bestArea)
                {
                    bestArea = area;
                    center = a + u * (minU + width * 0.5) + v * (height * 0.5);
                    halfSize = new double2(width * 0.5, height * 0.5);
                    angle = math.atan2(u.y, u.x);
                }
            }
        }

        static int Advance(IReadOnlyList<double2> hull, int index, double2 direction)
        {
            int n = hull.Count;
            for (int steps = 0; steps < n; steps++)
            {
                int next = (index + 1) % n;
                if (math.dot(hull[next], direction) <= math.dot(hull[index], direction) + Epsilon) break;
                index = next;
            }

            return index;
        }
    }
}

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SpriteFit
{
    /// <summary>Convex hull by Andrew's monotone chain, O(n log n).</summary>
    public static class Hull2D
    {
        /// <summary>
        /// Returns the hull vertices counter-clockwise, without collinear or duplicate points
        /// and without repeating the first point.
        /// </summary>
        public static double2[] Build(IReadOnlyList<double2> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));

            int n = points.Count;
            var sorted = new double2[n];
            for (int i = 0; i < n; i++) sorted[i] = points[i];
            Array.Sort(sorted, (a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            if (n < 3) return Deduplicate(sorted);

            var hull = new double2[2 * n];
            int k = 0;

            for (int i = 0; i < n; i++)
            {
                while (k >= 2 && Cross(hull[k - 2], hull[k - 1], sorted[i]) <= 0) k--;
                hull[k++] = sorted[i];
            }

            for (int i = n - 2, lower = k + 1; i >= 0; i--)
            {
                while (k >= lower && Cross(hull[k - 2], hull[k - 1], sorted[i]) <= 0) k--;
                hull[k++] = sorted[i];
            }

            // The last point repeats the first.
            var result = new double2[math.max(k - 1, 1)];
            Array.Copy(hull, result, result.Length);
            return result.Length == 2 && result[0].Equals(result[1]) ? new[] { result[0] } : result;
        }

        /// <summary>Z component of (a - o) × (b - o); positive when o → a → b turns left.</summary>
        public static double Cross(double2 o, double2 a, double2 b)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }

        static double2[] Deduplicate(double2[] sorted)
        {
            var unique = new List<double2>(sorted.Length);
            foreach (double2 p in sorted)
            {
                if (unique.Count == 0 || !unique[unique.Count - 1].Equals(p)) unique.Add(p);
            }

            return unique.ToArray();
        }
    }
}

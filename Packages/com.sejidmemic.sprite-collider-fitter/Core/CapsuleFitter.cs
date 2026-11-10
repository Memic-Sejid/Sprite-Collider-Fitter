using System;
using Unity.Mathematics;

namespace SpriteFit
{
    /// <summary>
    /// Enclosing capsule along the pixels' principal axis. For a radius r, the shortest core segment that
    /// covers every hull point is [min(t + s), max(t - s)] with s = √(r² − d²), where t and d are a point's
    /// position along and distance from the axis. A golden-section search over r minimises 2rL + πr².
    /// </summary>
    public sealed class CapsuleFitter : IPrimitiveFitter
    {
        const int MaxIterations = 64;
        const double InvPhi = 0.6180339887498949;

        public PrimitiveShape Shape => PrimitiveShape.Capsule;

        public Primitive Fit(FitInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            double2[] hull = input.Hull;
            int n = hull.Length;
            if (n == 0) throw new ArgumentException("The input has no pixels.", nameof(input));

            double theta = input.PrincipalAngle;
            var u = new double2(math.cos(theta), math.sin(theta));
            var v = new double2(-u.y, u.x);

            var t = new double[n];
            var w = new double[n];
            double tMin = double.PositiveInfinity, tMax = double.NegativeInfinity;
            double wMin = double.PositiveInfinity, wMax = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                t[i] = math.dot(hull[i], u);
                w[i] = math.dot(hull[i], v);
                tMin = math.min(tMin, t[i]);
                tMax = math.max(tMax, t[i]);
                wMin = math.min(wMin, w[i]);
                wMax = math.max(wMax, w[i]);
            }

            // Centre the axis between the extreme points across it, which minimises the smallest usable radius.
            double axisOffset = (wMin + wMax) * 0.5;
            double rMin = (wMax - wMin) * 0.5;
            var d2 = new double[n];
            for (int i = 0; i < n; i++) d2[i] = (w[i] - axisOffset) * (w[i] - axisOffset);

            // At this radius a circle at the middle of the axis already covers everything.
            double halfSpan = (tMax - tMin) * 0.5;
            double rMax = math.sqrt(halfSpan * halfSpan + rMin * rMin);

            double lo = rMin, hi = rMax;
            double x1 = hi - InvPhi * (hi - lo);
            double x2 = lo + InvPhi * (hi - lo);
            double f1 = Area(x1), f2 = Area(x2);
            for (int iteration = 0; iteration < MaxIterations && hi - lo > 1e-6; iteration++)
            {
                if (f1 < f2)
                {
                    hi = x2;
                    x2 = x1;
                    f2 = f1;
                    x1 = hi - InvPhi * (hi - lo);
                    f1 = Area(x1);
                }
                else
                {
                    lo = x1;
                    x1 = x2;
                    f1 = f2;
                    x2 = lo + InvPhi * (hi - lo);
                    f2 = Area(x2);
                }
            }

            // Any r >= rMin encloses the hull; take the upper end of the bracket so rounding can't undercut it.
            double r = hi;
            Segment(r, out double top, out double bottom);
            double middle = (top + bottom) * 0.5;
            double halfLength = math.max(0.0, (top - bottom) * 0.5);
            double2 center = u * middle + v * axisOffset;

            return Primitive.Capsule((float2)center, (float)theta, (float)halfLength, (float)r);

            double Area(double radius)
            {
                Segment(radius, out double segmentTop, out double segmentBottom);
                double length = math.max(0.0, segmentTop - segmentBottom);
                return 2.0 * radius * length + math.PI_DBL * radius * radius;
            }

            void Segment(double radius, out double segmentTop, out double segmentBottom)
            {
                double r2 = radius * radius;
                segmentTop = double.NegativeInfinity;
                segmentBottom = double.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    double s = math.sqrt(math.max(0.0, r2 - d2[i]));
                    segmentTop = math.max(segmentTop, t[i] - s);
                    segmentBottom = math.min(segmentBottom, t[i] + s);
                }
            }
        }
    }
}

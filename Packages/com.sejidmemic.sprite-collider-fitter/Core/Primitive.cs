using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SpriteFit
{
    /// <summary>
    /// A fitted collider shape. Units follow the input: pixels while fitting, local units after <see cref="ToLocal"/>.
    /// </summary>
    [Serializable]
    public struct Primitive
    {
        public PrimitiveShape Shape;

        public float2 Center;

        /// <summary>Counter-clockwise rotation of the shape's x axis, in radians.</summary>
        public float Angle;

        /// <summary>
        /// Circle: (radius, radius). Box: half width along the x axis, half height.
        /// Capsule: half length of the core segment along the x axis, radius.
        /// </summary>
        public float2 Extents;

        public static Primitive Circle(float2 center, float radius)
        {
            return new Primitive { Shape = PrimitiveShape.Circle, Center = center, Angle = 0f, Extents = new float2(radius, radius) };
        }

        public static Primitive Box(float2 center, float2 halfSize, float angle)
        {
            return new Primitive { Shape = PrimitiveShape.Box, Center = center, Angle = angle, Extents = halfSize }.Normalized();
        }

        public static Primitive Capsule(float2 center, float angle, float halfLength, float radius)
        {
            return new Primitive { Shape = PrimitiveShape.Capsule, Center = center, Angle = angle, Extents = new float2(halfLength, radius) }.Normalized();
        }

        /// <summary>Radius of a circle or capsule; 0 for a box.</summary>
        public float Radius => Shape == PrimitiveShape.Box ? 0f : Extents.y;

        /// <summary>Unit vector along the shape's x axis.</summary>
        public float2 Axis => new float2(math.cos(Angle), math.sin(Angle));

        public float Area
        {
            get
            {
                switch (Shape)
                {
                    case PrimitiveShape.Circle: return math.PI * Extents.x * Extents.x;
                    case PrimitiveShape.Box: return 4f * Extents.x * Extents.y;
                    case PrimitiveShape.Capsule: return 4f * Extents.x * Extents.y + math.PI * Extents.y * Extents.y;
                    default: return 0f;
                }
            }
        }

        /// <summary>Signed distance from <paramref name="point"/> to the boundary; negative inside.</summary>
        public float SignedDistance(float2 point)
        {
            float2 d = point - Center;
            float c = math.cos(Angle);
            float s = math.sin(Angle);
            var q = new float2(c * d.x + s * d.y, -s * d.x + c * d.y);

            switch (Shape)
            {
                case PrimitiveShape.Circle:
                    return math.length(q) - Extents.x;
                case PrimitiveShape.Box:
                {
                    float2 e = math.abs(q) - Extents;
                    return math.length(math.max(e, 0f)) + math.min(math.cmax(e), 0f);
                }
                case PrimitiveShape.Capsule:
                    q.x = math.max(math.abs(q.x) - Extents.x, 0f);
                    return math.length(q) - Extents.y;
                default:
                    return float.PositiveInfinity;
            }
        }

        public bool Contains(float2 point)
        {
            return SignedDistance(point) <= 0f;
        }

        /// <summary>Axis-aligned bounds of the shape.</summary>
        public void GetBounds(out float2 min, out float2 max)
        {
            float c = math.abs(math.cos(Angle));
            float s = math.abs(math.sin(Angle));
            float2 half;
            switch (Shape)
            {
                case PrimitiveShape.Box:
                    half = new float2(c * Extents.x + s * Extents.y, s * Extents.x + c * Extents.y);
                    break;
                case PrimitiveShape.Capsule:
                    half = new float2(c * Extents.x, s * Extents.x) + Extents.y;
                    break;
                default:
                    half = new float2(Extents.x);
                    break;
            }

            min = Center - half;
            max = Center + half;
        }

        /// <summary>
        /// Puts the angle in a canonical range without changing the shape: 0 for circles,
        /// (-90°, 90°] for capsules, and (-45°, 45°] for boxes (swapping width and height when needed).
        /// </summary>
        public Primitive Normalized()
        {
            Primitive p = this;
            if (Shape == PrimitiveShape.Circle)
            {
                p.Angle = 0f;
                return p;
            }

            const float halfPi = math.PI * 0.5f;
            const float quarterPi = math.PI * 0.25f;

            float a = p.Angle - math.PI * math.round(p.Angle / math.PI);
            if (a <= -halfPi) a += math.PI;

            if (Shape == PrimitiveShape.Box)
            {
                if (a > quarterPi)
                {
                    a -= halfPi;
                    p.Extents = p.Extents.yx;
                }
                else if (a <= -quarterPi)
                {
                    a += halfPi;
                    p.Extents = p.Extents.yx;
                }
            }

            p.Angle = a;
            return p;
        }

        /// <summary>Converts from sprite pixel space to the sprite's local space.</summary>
        public Primitive ToLocal(float2 pivot, float pixelsPerUnit)
        {
            Primitive p = this;
            p.Center = (Center - pivot) / pixelsPerUnit;
            p.Extents = Extents / pixelsPerUnit;
            return p;
        }

        /// <summary>Mirrors the shape about the origin's axes, as SpriteRenderer.flipX / flipY do.</summary>
        public Primitive Mirrored(bool flipX, bool flipY)
        {
            Primitive p = this;
            if (flipX)
            {
                p.Center.x = -p.Center.x;
                p.Angle = math.PI - p.Angle;
            }

            if (flipY)
            {
                p.Center.y = -p.Center.y;
                p.Angle = -p.Angle;
            }

            return p.Normalized();
        }

        /// <summary>Appends the closed outline (first point not repeated) to <paramref name="points"/>.</summary>
        public void GetOutline(List<float2> points, int circleSegments = 64)
        {
            float2 axis = Axis;
            var normal = new float2(-axis.y, axis.x);

            switch (Shape)
            {
                case PrimitiveShape.Circle:
                    for (int i = 0; i < circleSegments; i++)
                    {
                        float t = 2f * math.PI * i / circleSegments;
                        points.Add(Center + Extents.x * new float2(math.cos(t), math.sin(t)));
                    }
                    break;

                case PrimitiveShape.Box:
                    points.Add(Center + axis * Extents.x + normal * Extents.y);
                    points.Add(Center - axis * Extents.x + normal * Extents.y);
                    points.Add(Center - axis * Extents.x - normal * Extents.y);
                    points.Add(Center + axis * Extents.x - normal * Extents.y);
                    break;

                case PrimitiveShape.Capsule:
                {
                    int half = math.max(2, circleSegments / 2);
                    float2 end = Center + axis * Extents.x;
                    float2 start = Center - axis * Extents.x;
                    for (int i = 0; i <= half; i++)
                    {
                        float t = -0.5f * math.PI + math.PI * i / half;
                        points.Add(end + Extents.y * (axis * math.cos(t) + normal * math.sin(t)));
                    }

                    for (int i = 0; i <= half; i++)
                    {
                        float t = 0.5f * math.PI + math.PI * i / half;
                        points.Add(start + Extents.y * (axis * math.cos(t) + normal * math.sin(t)));
                    }
                    break;
                }
            }
        }

        public override string ToString()
        {
            return $"{Shape} center {Center} angle {math.degrees(Angle):F2}° extents {Extents}";
        }
    }
}

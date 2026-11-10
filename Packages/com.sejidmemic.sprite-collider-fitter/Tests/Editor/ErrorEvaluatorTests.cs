using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace SpriteFit.Tests
{
    /// <summary>The Burst evaluator against a plain per-pixel loop.</summary>
    public class ErrorEvaluatorTests
    {
        [Test]
        public void MatchesReferenceOnRandomMasksAndPrimitives()
        {
            var random = new System.Random(4);
            for (int set = 0; set < 50; set++)
            {
                int width = random.Next(1, 80), height = random.Next(1, 80);
                var opaque = new bool[width * height];
                for (int i = 0; i < opaque.Length; i++) opaque[i] = random.NextDouble() < 0.5;
                var mask = new PixelMask(width, height, opaque);

                var primitives = new List<Primitive>();
                int count = random.Next(0, 5);
                for (int i = 0; i < count; i++)
                {
                    // Some reach well past the mask edges.
                    var center = new float2((float)(random.NextDouble() * 1.4 - 0.2) * width, (float)(random.NextDouble() * 1.4 - 0.2) * height);
                    float size = (float)random.NextDouble() * 30f + 0.5f;
                    float angle = (float)(random.NextDouble() * math.PI_DBL);
                    switch (random.Next(3))
                    {
                        case 0: primitives.Add(Primitive.Circle(center, size)); break;
                        case 1: primitives.Add(Primitive.Box(center, new float2(size, size * 0.4f), angle)); break;
                        default: primitives.Add(Primitive.Capsule(center, angle, size, size * 0.3f + 0.5f)); break;
                    }
                }

                var classes = new byte[width * height];
                FitError actual = ErrorEvaluator.Evaluate(mask, primitives, classes);
                Reference(mask, primitives, out int over, out int under, out byte[] expectedClasses);

                Assert.That(actual.Over, Is.EqualTo(over), $"set {set}");
                Assert.That(actual.Under, Is.EqualTo(under), $"set {set}");
                Assert.That(classes, Is.EqualTo(expectedClasses), $"set {set}");
            }
        }

        static void Reference(PixelMask mask, List<Primitive> primitives, out int over, out int under, out byte[] classes)
        {
            over = under = 0;
            classes = new byte[mask.Width * mask.Height];
            // The evaluator looks at most one mask size beyond each edge.
            for (int y = -mask.Height; y <= 2 * mask.Height; y++)
            {
                for (int x = -mask.Width; x <= 2 * mask.Width; x++)
                {
                    var p = new float2(x + 0.5f, y + 0.5f);
                    bool inside = primitives.Exists(q => q.Contains(p));
                    bool opaque = mask.IsOpaque(x, y);
                    byte c = 0;
                    if (inside && !opaque)
                    {
                        over++;
                        c = ErrorEvaluator.Over;
                    }
                    else if (!inside && opaque)
                    {
                        under++;
                        c = ErrorEvaluator.Under;
                    }

                    if (x >= 0 && y >= 0 && x < mask.Width && y < mask.Height) classes[y * mask.Width + x] = c;
                }
            }
        }
    }
}

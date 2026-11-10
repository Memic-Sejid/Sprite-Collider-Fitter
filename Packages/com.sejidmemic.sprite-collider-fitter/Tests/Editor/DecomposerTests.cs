using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace SpriteFit.Tests
{
    /// <summary>Compound fits (FR-03, FR-04, FR-09) on synthetic sprites.</summary>
    public class DecomposerTests
    {
        static PixelMask LShape()
        {
            // A 160 × 40 foot and a 40 × 160 upright sharing a 40 × 40 corner.
            return SyntheticMasks.FromPredicate(200, 200, p =>
                (p.x > 20 && p.x < 180 && p.y > 20 && p.y < 60) ||
                (p.x > 20 && p.x < 60 && p.y > 20 && p.y < 180));
        }

        [Test]
        public void LShape_TwoBoxes_OverfillUnderTwoPercent()
        {
            PixelMask mask = LShape();

            FitResult result = SpriteFitter.Fit(mask, new FitSettings());

            Assert.That(result.Primitives.Count, Is.EqualTo(2), Describe(result));
            Assert.That(result.Primitives, Has.All.Matches<Primitive>(p => p.Shape == PrimitiveShape.Box), Describe(result));
            Assert.That(result.Error.OverRatio, Is.LessThan(0.02), Describe(result));
            Assert.That(result.Error.Under, Is.Zero, Describe(result));
        }

        [Test]
        public void Ring_AtFivePercent_ManyPrimitivesAndHoleCentreUncovered()
        {
            var center = new double2(128, 128);
            PixelMask mask = SyntheticMasks.FromPredicate(256, 256, p =>
            {
                double d = math.distance(p, center);
                return d >= 60 && d <= 100;
            });

            FitResult result = SpriteFitter.Fit(mask, new FitSettings { Tolerance = 0.05f, MaxPrimitives = FitSettings.MaxPrimitivesLimit });

            Assert.That(result.Primitives.Count, Is.GreaterThanOrEqualTo(4), Describe(result));
            foreach (Primitive p in result.Primitives)
            {
                Assert.That(p.Contains((float2)center), Is.False, $"{p} covers the hole centre. {Describe(result)}");
            }

            Assert.That(result.Error.Under, Is.Zero, Describe(result));
        }

        [Test]
        public void Circle_AlreadyWithinTolerance_StaysOneCircle()
        {
            PixelMask mask = SyntheticMasks.Circle(256, new double2(128, 128), 100);

            FitResult result = SpriteFitter.Fit(mask, new FitSettings());

            Assert.That(result.Primitives.Count, Is.EqualTo(1), Describe(result));
            Assert.That(result.Primitives[0].Shape, Is.EqualTo(PrimitiveShape.Circle));
        }

        [Test]
        public void CirclesOnly_Compound_OutputIsOnlyCircles()
        {
            FitResult result = SpriteFitter.Fit(LShape(), new FitSettings { Allowed = AllowedShapes.Circle, MaxPrimitives = 8 });

            Assert.That(result.Primitives.Count, Is.GreaterThan(1), Describe(result));
            Assert.That(result.Primitives, Has.All.Matches<Primitive>(p => p.Shape == PrimitiveShape.Circle), Describe(result));
        }

        [Test]
        public void NeverExceedsMaxPrimitives()
        {
            var center = new double2(128, 128);
            PixelMask mask = SyntheticMasks.FromPredicate(256, 256, p =>
            {
                double d = math.distance(p, center);
                return d >= 60 && d <= 100;
            });

            for (int max = 1; max <= 6; max++)
            {
                FitResult result = SpriteFitter.Fit(mask, new FitSettings { Tolerance = 0f, MaxPrimitives = max });
                Assert.That(result.Primitives.Count, Is.InRange(1, max), $"max {max}: {Describe(result)}");
            }
        }

        [Test]
        public void TwoBlobs_Compound_AtLeastOnePrimitivePerBlob()
        {
            var a = new double2(50, 50);
            var b = new double2(150, 140);
            PixelMask mask = SyntheticMasks.FromPredicate(200, 200, p => math.distance(p, a) <= 30 || math.distance(p, b) <= 20);

            FitResult result = SpriteFitter.Fit(mask, new FitSettings());

            Assert.That(result.IslandCount, Is.EqualTo(2));
            Assert.That(CountCovering(result.Primitives, a), Is.GreaterThanOrEqualTo(1), Describe(result));
            Assert.That(CountCovering(result.Primitives, b), Is.GreaterThanOrEqualTo(1), Describe(result));
            Assert.That(result.Error.Under, Is.Zero);
        }

        [Test]
        public void SingleMode_OnePrimitivePerIsland()
        {
            FitResult result = SpriteFitter.Fit(LShape(), new FitSettings { Mode = FitMode.Single });

            Assert.That(result.Primitives.Count, Is.EqualTo(1));
        }

        [Test]
        public void NoShapesAllowed_NoPrimitives()
        {
            FitResult result = SpriteFitter.Fit(LShape(), new FitSettings { Allowed = AllowedShapes.None });

            Assert.That(result.Primitives, Is.Empty);
            Assert.That(result.Error.UnderRatio, Is.EqualTo(1.0));
        }

        [Test]
        public void FitInputCombine_MatchesBuildingTheUnionDirectly()
        {
            PixelMask mask = LShape();
            var lower = new FitInputBuilder(0, 199);
            var upper = new FitInputBuilder(0, 199);
            var both = new FitInputBuilder(0, 199);
            for (int y = 0; y < 200; y++)
            {
                for (int x = 0; x < 200; x++)
                {
                    if (!mask.IsOpaque(x, y)) continue;

                    (y < 60 ? lower : upper).Add(x, y);
                    both.Add(x, y);
                }
            }

            FitInput combined = FitInput.Combine(lower.Build(), upper.Build());
            FitInput direct = both.Build();

            Assert.That(combined.PixelCount, Is.EqualTo(direct.PixelCount));
            Assert.That(math.distance(combined.Centroid, direct.Centroid), Is.LessThan(1e-9));
            Assert.That(math.distance(combined.Covariance, direct.Covariance), Is.LessThan(1e-6));
            Assert.That(combined.Hull, Is.EquivalentTo(direct.Hull));
        }

        static int CountCovering(IReadOnlyList<Primitive> primitives, double2 point)
        {
            int count = 0;
            foreach (Primitive p in primitives)
            {
                if (p.Contains((float2)point)) count++;
            }

            return count;
        }

        static string Describe(FitResult result)
        {
            return $"{result.Primitives.Count} primitives, {result.Error}: {string.Join(" | ", result.Primitives)}";
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace SpriteFit.Tests
{
    /// <summary>Single-primitive fits on synthetic sprites with known answers (spec validation plan, FR-02/04/09).</summary>
    public class FitterFixtureTests
    {
        [Test]
        public void Circle_R100_RadiusAndCentreWithinOnePixel()
        {
            var center = new double2(128.3, 127.6);
            PixelMask mask = SyntheticMasks.Circle(256, center, 100);

            Primitive fit = FitSingle(mask, AllowedShapes.All);

            Assert.That(fit.Shape, Is.EqualTo(PrimitiveShape.Circle), fit.ToString());
            Assert.That(math.abs(fit.Radius - 100f), Is.LessThan(1f), fit.ToString());
            Assert.That(math.distance((double2)fit.Center, center), Is.LessThan(1.0), fit.ToString());
            AssertEncloses(mask, fit);
        }

        [Test]
        public void Box_Rotated30_AngleWithinOneDegree_SizeWithinOnePixel()
        {
            PixelMask mask = SyntheticMasks.Box(300, new double2(150.25, 149.75), new double2(90, 45), 30);

            Primitive fit = FitSingle(mask, AllowedShapes.All);

            Assert.That(fit.Shape, Is.EqualTo(PrimitiveShape.Box), fit.ToString());
            Assert.That(math.abs(math.degrees(fit.Angle) - 30f), Is.LessThan(1f), fit.ToString());
            Assert.That(math.abs(2f * fit.Extents.x - 180f), Is.LessThan(1f), fit.ToString());
            Assert.That(math.abs(2f * fit.Extents.y - 90f), Is.LessThan(1f), fit.ToString());
            AssertEncloses(mask, fit);
        }

        [Test]
        public void Capsule_Rotated45_OverfillUnderOnePercent()
        {
            PixelMask mask = SyntheticMasks.Capsule(512, new double2(256, 256), 45, 160, 80);

            Primitive fit = FitSingle(mask, AllowedShapes.All);
            FitError error = ErrorEvaluator.Evaluate(mask, new[] { fit });

            Assert.That(fit.Shape, Is.EqualTo(PrimitiveShape.Capsule), fit.ToString());
            Assert.That(error.OverRatio, Is.LessThan(0.01), error.ToString());
            Assert.That(error.Under, Is.Zero, error.ToString());
        }

        [Test]
        public void FullyOpaque_OneBoxMatchingTheRect()
        {
            PixelMask mask = SyntheticMasks.Full(64, 32);

            List<Primitive> fits = PrimitiveFitting.FitIslands(mask, AllowedShapes.All);

            Assert.That(fits, Has.Count.EqualTo(1));
            Primitive box = fits[0];
            Assert.That(box.Shape, Is.EqualTo(PrimitiveShape.Box), box.ToString());
            Assert.That(math.abs(box.Angle), Is.LessThan(1e-4f), box.ToString());
            Assert.That(box.Center.x, Is.EqualTo(32f).Within(1e-3f));
            Assert.That(box.Center.y, Is.EqualTo(16f).Within(1e-3f));
            Assert.That(box.Extents.x, Is.EqualTo(32f).Within(1e-3f));
            Assert.That(box.Extents.y, Is.EqualTo(16f).Within(1e-3f));
            Assert.That(ErrorEvaluator.Evaluate(mask, fits).Over, Is.Zero);
        }

        [Test]
        public void FullyTransparent_NoPrimitives()
        {
            PixelMask mask = SyntheticMasks.Empty(32, 32);

            Assert.That(mask.Islands, Is.Empty);
            Assert.That(PrimitiveFitting.FitIslands(mask, AllowedShapes.All), Is.Empty);
        }

        [Test]
        public void CirclesOnly_OnABox_OutputIsOnlyCircles()
        {
            PixelMask mask = SyntheticMasks.Box(200, new double2(100, 100), new double2(60, 30), 10);

            List<Primitive> fits = PrimitiveFitting.FitIslands(mask, AllowedShapes.Circle);

            Assert.That(fits, Is.Not.Empty);
            Assert.That(fits, Has.All.Matches<Primitive>(p => p.Shape == PrimitiveShape.Circle));
            Assert.That(ErrorEvaluator.Evaluate(mask, fits).Under, Is.Zero);
        }

        [Test]
        public void NoShapesAllowed_NoPrimitives()
        {
            PixelMask mask = SyntheticMasks.Full(8, 8);

            Assert.That(PrimitiveFitting.FitIslands(mask, AllowedShapes.None), Is.Empty);
        }

        [Test]
        public void TwoSeparateBlobs_OnePrimitivePerBlob()
        {
            var a = new double2(50, 50);
            var b = new double2(150, 140);
            PixelMask mask = SyntheticMasks.FromPredicate(200, 200, p => math.distance(p, a) <= 30 || math.distance(p, b) <= 20);

            List<Primitive> fits = PrimitiveFitting.FitIslands(mask, AllowedShapes.All);

            Assert.That(mask.Islands.Count, Is.EqualTo(2));
            Assert.That(fits, Has.Count.EqualTo(2));
            Assert.That(fits.Exists(p => math.distance((double2)p.Center, a) < 1), Is.True);
            Assert.That(fits.Exists(p => math.distance((double2)p.Center, b) < 1), Is.True);
            Assert.That(ErrorEvaluator.Evaluate(mask, fits).Under, Is.Zero);
        }

        [Test]
        public void SinglePixel_EveryShapeHasNonZeroSize()
        {
            var opaque = new bool[9];
            opaque[4] = true;
            var mask = new PixelMask(3, 3, opaque);
            FitInput input = mask.Islands[0].Input;

            foreach (IPrimitiveFitter fitter in new IPrimitiveFitter[] { new CircleFitter(), new CapsuleFitter(), new BoxFitter() })
            {
                Primitive fit = fitter.Fit(input);
                Assert.That(fit.Area, Is.GreaterThan(0.1f), fit.ToString());
                Assert.That(fit.Contains(new float2(1.5f, 1.5f)), Is.True, fit.ToString());
            }
        }

        static Primitive FitSingle(PixelMask mask, AllowedShapes allowed)
        {
            Assert.That(mask.Islands.Count, Is.EqualTo(1));
            Assert.That(PrimitiveFitting.TryFitBest(mask.Islands[0].Input, allowed, out Primitive fit), Is.True);
            return fit;
        }

        static void AssertEncloses(PixelMask mask, Primitive primitive)
        {
            Assert.That(ErrorEvaluator.Evaluate(mask, new[] { primitive }).Under, Is.Zero, "Enclosing fits must have no underfill.");
        }
    }
}

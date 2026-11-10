using NUnit.Framework;
using Unity.Mathematics;

namespace SpriteFit.Tests
{
    public class PrimitiveTests
    {
        [Test]
        public void Box_Normalized_KeepsShapeAndPutsAngleInQuarterTurn()
        {
            // A 10 × 4 box at 80° is the same shape as a 4 × 10 box at -10°.
            Primitive box = Primitive.Box(new float2(1, 2), new float2(5, 2), math.radians(80f));

            Assert.That(math.degrees(box.Angle), Is.EqualTo(-10f).Within(1e-3f));
            Assert.That(box.Extents.x, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(box.Extents.y, Is.EqualTo(5f).Within(1e-5f));
        }

        [Test]
        public void Capsule_SignedDistance_CapAndSide()
        {
            Primitive capsule = Primitive.Capsule(float2.zero, 0f, 3f, 1f);

            Assert.That(capsule.SignedDistance(new float2(5f, 0f)), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(capsule.SignedDistance(new float2(0f, 2f)), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(capsule.Contains(new float2(3.5f, 0.5f)), Is.True);
            Assert.That(capsule.Area, Is.EqualTo(12f + math.PI).Within(1e-4f));
        }

        [Test]
        public void Bounds_RotatedBox()
        {
            Primitive box = Primitive.Box(float2.zero, new float2(1f, 1f), math.radians(45f));

            box.GetBounds(out float2 min, out float2 max);

            Assert.That(max.x, Is.EqualTo(math.SQRT2).Within(1e-5f));
            Assert.That(min.y, Is.EqualTo(-math.SQRT2).Within(1e-5f));
        }

        [Test]
        public void ToLocal_UsesPivotAndPixelsPerUnit()
        {
            Primitive circle = Primitive.Circle(new float2(150f, 100f), 50f);

            Primitive local = circle.ToLocal(new float2(100f, 100f), 100f);

            Assert.That(local.Center.x, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(local.Center.y, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(local.Radius, Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void Mirrored_FlipX_MirrorsCentreAndAngle()
        {
            Primitive box = Primitive.Box(new float2(2f, 1f), new float2(3f, 1f), math.radians(20f));

            Primitive mirrored = box.Mirrored(true, false);

            Assert.That(mirrored.Center.x, Is.EqualTo(-2f).Within(1e-6f));
            Assert.That(mirrored.Center.y, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(math.degrees(mirrored.Angle), Is.EqualTo(-20f).Within(1e-3f));
        }
    }
}

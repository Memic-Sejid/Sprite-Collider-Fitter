using NUnit.Framework;
using Unity.Mathematics;

namespace SpriteFit.Tests
{
    public class PixelMaskTests
    {
        [Test]
        public void FromAlpha_ThresholdHalf_CutsAt128()
        {
            byte[] alpha = { 0, 127, 128, 255 };

            PixelMask mask = PixelMask.FromAlpha(alpha, 4, 1, 0.5f, 100f, float2.zero);

            Assert.That(mask.IsOpaque(0, 0), Is.False);
            Assert.That(mask.IsOpaque(1, 0), Is.False);
            Assert.That(mask.IsOpaque(2, 0), Is.True);
            Assert.That(mask.IsOpaque(3, 0), Is.True);
            Assert.That(mask.OpaqueCount, Is.EqualTo(2));
        }

        [Test]
        public void AlphaCutoff_ZeroThreshold_NeverCountsFullyTransparentPixels()
        {
            Assert.That(PixelMask.AlphaCutoff(0f), Is.EqualTo(1));
        }

        [Test]
        public void Islands_DiagonalNeighboursAreConnected()
        {
            // X .
            // . X
            var mask = new PixelMask(2, 2, new[] { false, true, true, false });

            Assert.That(mask.Islands.Count, Is.EqualTo(1));
            Assert.That(mask.Islands[0].PixelCount, Is.EqualTo(2));
        }

        [Test]
        public void Islands_SeparatedPixels_GetTheirOwnLabelsAndBounds()
        {
            // X . X
            var mask = new PixelMask(3, 1, new[] { true, false, true });

            Assert.That(mask.Islands.Count, Is.EqualTo(2));
            Assert.That(mask.GetLabel(0, 0), Is.EqualTo(1));
            Assert.That(mask.GetLabel(1, 0), Is.EqualTo(0));
            Assert.That(mask.GetLabel(2, 0), Is.EqualTo(2));
            Assert.That(mask.Islands[1].Min, Is.EqualTo(new int2(2, 0)));
            Assert.That(mask.Islands[1].Max, Is.EqualTo(new int2(2, 0)));
        }

        [Test]
        public void IslandMoments_HorizontalBar_PrincipalAxisIsHorizontal()
        {
            PixelMask mask = SyntheticMasks.FromPredicate(20, 5, p => p.y > 2 && p.y < 3);
            FitInput input = mask.Islands[0].Input;

            Assert.That(input.PixelCount, Is.EqualTo(20));
            Assert.That(input.Centroid.x, Is.EqualTo(10.0).Within(1e-9));
            Assert.That(input.Centroid.y, Is.EqualTo(2.5).Within(1e-9));
            Assert.That(input.PrincipalAngle, Is.EqualTo(0.0).Within(1e-9));
        }
    }
}

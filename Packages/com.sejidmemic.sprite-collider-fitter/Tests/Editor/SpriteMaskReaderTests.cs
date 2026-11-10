using System;
using NUnit.Framework;
using SpriteFit.Editor;
using UnityEngine;

namespace SpriteFit.Tests
{
    /// <summary>FR-01: the mask comes from the GPU, so compression and Read/Write don't matter.</summary>
    public class SpriteMaskReaderTests
    {
        const int Size = 64;

        Texture2D _texture;
        Sprite _sprite;

        [SetUp]
        public void SetUp()
        {
            if (!SpriteMaskReader.HasGraphicsDevice) Assert.Ignore("Needs a graphics device to read sprites.");
        }

        [TearDown]
        public void TearDown()
        {
            if (_sprite != null) UnityEngine.Object.DestroyImmediate(_sprite);
            if (_texture != null) UnityEngine.Object.DestroyImmediate(_texture);
        }

        // Two blocks aligned to 4 × 4 so block compression keeps their alpha exact.
        // Asymmetric, so a flip in either axis would fail.
        static bool Pattern(int x, int y)
        {
            return (x < 32 && y < 16) || (x >= 40 && x < 60 && y >= 44 && y < 60);
        }

        [Test]
        public void CompressedNonReadableTexture_MaskMatchesSource()
        {
            _texture = MakeTexture(Pattern);
            _texture.Compress(false);
            _texture.Apply(false, true);
            Assume.That(_texture.isReadable, Is.False);
            Assume.That(_texture.format, Is.Not.EqualTo(TextureFormat.RGBA32), "Compression is not supported on this platform.");

            _sprite = Sprite.Create(_texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            PixelMask mask = SpriteMaskReader.ReadMask(_sprite, 0.5f);

            Assert.That(mask.Width, Is.EqualTo(Size));
            Assert.That(mask.Height, Is.EqualTo(Size));
            AssertMaskMatches(mask, Pattern, 0, 0);
            Assert.That(mask.Pivot.x, Is.EqualTo(32f));
            Assert.That(mask.PixelsPerUnit, Is.EqualTo(100f));
        }

        [Test]
        public void SubRectSprite_ReadsOnlyItsRect()
        {
            _texture = MakeTexture(Pattern);
            _texture.Apply(false, true);

            // The top-right quarter, as an atlas sprite would be.
            _sprite = Sprite.Create(_texture, new Rect(32, 32, 32, 32), Vector2.zero, 100f, 0, SpriteMeshType.FullRect);
            PixelMask mask = SpriteMaskReader.ReadMask(_sprite, 0.5f);

            Assert.That(mask.Width, Is.EqualTo(32));
            Assert.That(mask.Height, Is.EqualTo(32));
            AssertMaskMatches(mask, Pattern, 32, 32);
        }

        [Test]
        public void TightMesh_MatchesSource()
        {
            _texture = MakeTexture(Pattern);
            _sprite = Sprite.Create(_texture, new Rect(0, 0, Size, Size), new Vector2(0.25f, 0.75f), 50f, 0, SpriteMeshType.Tight);
            _texture.Apply(false, true);

            PixelMask mask = SpriteMaskReader.ReadMask(_sprite, 0.5f);

            AssertMaskMatches(mask, Pattern, 0, 0);
        }

        static Texture2D MakeTexture(Func<int, int, bool> opaque)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    pixels[y * Size + x] = opaque(x, y) ? new Color32(200, 100, 50, 255) : new Color32(0, 0, 0, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        static void AssertMaskMatches(PixelMask mask, Func<int, int, bool> expected, int offsetX, int offsetY)
        {
            int mismatches = 0;
            string first = null;
            for (int y = 0; y < mask.Height; y++)
            {
                for (int x = 0; x < mask.Width; x++)
                {
                    if (mask.IsOpaque(x, y) == expected(x + offsetX, y + offsetY)) continue;

                    mismatches++;
                    if (first == null) first = $"({x}, {y})";
                }
            }

            Assert.That(mismatches, Is.Zero, $"{mismatches} pixels differ, first at {first}");
        }
    }
}

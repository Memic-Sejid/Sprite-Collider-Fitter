using System;
using UnityEditor;
using UnityEngine;

namespace SpriteFit.Editor
{
    /// <summary>
    /// Scene view overlay of the error classes: red where colliders cover empty air, blue where they miss
    /// the sprite. Covers the sprite's rect only; overfill beyond it is counted in the stats but not drawn.
    /// </summary>
    public sealed class HeatmapOverlay : IDisposable
    {
        const string ShaderPath = "Packages/com.sejidmemic.sprite-collider-fitter/Editor/Shaders/Overlay.shader";
        const string ShaderName = "Hidden/SpriteFit/Overlay";

        static readonly Color32 OverColor = new Color32(255, 40, 40, 150);
        static readonly Color32 UnderColor = new Color32(40, 110, 255, 170);
        static readonly Color32 NoColor = new Color32(0, 0, 0, 0);

        Texture2D _texture;
        Material _material;
        Color32[] _pixels;

        public void Update(int width, int height, byte[] classes)
        {
            if (classes == null) throw new ArgumentNullException(nameof(classes));
            if (classes.Length != width * height) throw new ArgumentException("Length must be width × height.", nameof(classes));

            if (_texture == null || _texture.width != width || _texture.height != height)
            {
                DestroyTexture();
                _texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                _pixels = new Color32[width * height];
            }

            for (int i = 0; i < classes.Length; i++)
            {
                switch (classes[i])
                {
                    case ErrorEvaluator.Over: _pixels[i] = OverColor; break;
                    case ErrorEvaluator.Under: _pixels[i] = UnderColor; break;
                    default: _pixels[i] = NoColor; break;
                }
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        /// <summary>Draws the overlay over the sprite's rect. Call during a Scene view Repaint.</summary>
        /// <param name="localToWorld">The sprite's local space, including any flip.</param>
        public void Draw(Matrix4x4 localToWorld, PixelMask mask)
        {
            if (_texture == null || mask == null) return;
            if (_material == null) _material = CreateMaterial();

            Vector2 min = -(Vector2)mask.Pivot / mask.PixelsPerUnit;
            Vector2 max = (new Vector2(mask.Width, mask.Height) - (Vector2)mask.Pivot) / mask.PixelsPerUnit;

            _material.mainTexture = _texture;
            _material.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(localToWorld);
            GL.Begin(GL.QUADS);
            GL.TexCoord2(0f, 0f);
            GL.Vertex3(min.x, min.y, 0f);
            GL.TexCoord2(0f, 1f);
            GL.Vertex3(min.x, max.y, 0f);
            GL.TexCoord2(1f, 1f);
            GL.Vertex3(max.x, max.y, 0f);
            GL.TexCoord2(1f, 0f);
            GL.Vertex3(max.x, min.y, 0f);
            GL.End();
            GL.PopMatrix();
        }

        public void Dispose()
        {
            DestroyTexture();
            if (_material != null) UnityEngine.Object.DestroyImmediate(_material);
            _material = null;
        }

        void DestroyTexture()
        {
            if (_texture != null) UnityEngine.Object.DestroyImmediate(_texture);
            _texture = null;
        }

        static Material CreateMaterial()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException($"Shader '{ShaderName}' not found at {ShaderPath}.");

            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
    }
}

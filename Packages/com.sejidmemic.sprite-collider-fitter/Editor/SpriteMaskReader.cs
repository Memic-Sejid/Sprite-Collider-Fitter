using System;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace SpriteFit.Editor
{
    /// <summary>
    /// Reads a sprite's alpha by drawing the sprite's own mesh into a render texture the size of
    /// <see cref="Sprite.rect"/> and reading it back (FR-01). Going through the GPU handles non-readable,
    /// compressed, tightly packed and rotated atlas sprites the same way.
    /// </summary>
    public static class SpriteMaskReader
    {
        const string ShaderPath = "Packages/com.sejidmemic.sprite-collider-fitter/Editor/Shaders/SpriteAlpha.shader";
        const string ShaderName = "Hidden/SpriteFit/Alpha";

        /// <summary>False under -nographics (as in most CI runners), where sprites can't be drawn.</summary>
        public static bool HasGraphicsDevice => SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

        public static PixelMask ReadMask(Sprite sprite, float alphaThreshold)
        {
            byte[] alpha = ReadAlpha(sprite, out int width, out int height);
            return PixelMask.FromAlpha(alpha, width, height, alphaThreshold, sprite.pixelsPerUnit, (float2)sprite.pivot);
        }

        /// <summary>Row-major alpha of the sprite's rect, row 0 at the bottom.</summary>
        public static byte[] ReadAlpha(Sprite sprite, out int width, out int height)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));
            if (sprite.texture == null) throw new ArgumentException($"Sprite '{sprite.name}' has no texture.", nameof(sprite));
            if (!HasGraphicsDevice) throw new InvalidOperationException("Reading a sprite needs a graphics device, which -nographics mode doesn't have.");

            Rect rect = sprite.rect;
            width = Mathf.Max(1, Mathf.CeilToInt(rect.width));
            height = Mathf.Max(1, Mathf.CeilToInt(rect.height));

            Shader shader = LoadShader();
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, mainTexture = sprite.texture };
            var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0) { sRGB = false };
            RenderTexture target = RenderTexture.GetTemporary(descriptor);
            var readback = new Texture2D(width, height, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture previous = RenderTexture.active;

            try
            {
                RenderTexture.active = target;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, width, 0, height);
                GL.Clear(true, true, Color.clear);

                if (!material.SetPass(0)) throw new InvalidOperationException($"Shader '{ShaderName}' failed to set up its pass.");

                Vector2[] vertices = sprite.vertices;
                Vector2[] uvs = sprite.uv;
                ushort[] triangles = sprite.triangles;
                float pixelsPerUnit = sprite.pixelsPerUnit;
                Vector2 pivot = sprite.pivot;

                // Sprite vertices are in local units around the pivot; the rect's pixel space puts the
                // pivot at sprite.pivot.
                GL.Begin(GL.TRIANGLES);
                foreach (ushort index in triangles)
                {
                    Vector2 uv = uvs[index];
                    Vector2 position = vertices[index] * pixelsPerUnit + pivot;
                    GL.TexCoord2(uv.x, uv.y);
                    GL.Vertex3(position.x, position.y, 0f);
                }

                GL.End();
                GL.PopMatrix();

                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                Color32[] pixels = readback.GetPixels32();
                var alpha = new byte[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) alpha[i] = pixels[i].a;
                return alpha;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        static Shader LoadShader()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException($"Shader '{ShaderName}' not found at {ShaderPath}.");
            return shader;
        }
    }
}

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace SpriteFit.Editor
{
    /// <summary>Draws primitives with <see cref="Handles"/>, in the current <see cref="Handles.matrix"/>.</summary>
    public static class PrimitiveHandles
    {
        const float LineWidth = 3f;

        static readonly List<float2> Outline = new List<float2>(160);

        public static void Draw(Primitive primitive)
        {
            Outline.Clear();
            primitive.GetOutline(Outline);
            if (Outline.Count < 2) return;

            var points = new Vector3[Outline.Count + 1];
            for (int i = 0; i < Outline.Count; i++) points[i] = new Vector3(Outline[i].x, Outline[i].y, 0f);
            points[Outline.Count] = points[0];
            Handles.DrawAAPolyLine(LineWidth, points);
        }
    }
}

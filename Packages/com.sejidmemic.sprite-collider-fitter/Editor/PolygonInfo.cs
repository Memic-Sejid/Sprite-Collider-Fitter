using UnityEngine;

namespace SpriteFit.Editor
{
    /// <summary>The polygon the generated colliders replace, for the stats (FR-08).</summary>
    public readonly struct PolygonInfo
    {
        PolygonInfo(string source, int paths, int vertices)
        {
            Source = source;
            Paths = paths;
            Vertices = vertices;
        }

        /// <summary>Where the numbers come from, or null when there's no polygon to compare against.</summary>
        public string Source { get; }

        public int Paths { get; }

        public int Vertices { get; }

        /// <summary>
        /// The target's PolygonCollider2D if it has one; otherwise the sprite's physics shape, which is what
        /// a new PolygonCollider2D would be generated from.
        /// </summary>
        public static PolygonInfo For(SpriteRenderer target)
        {
            if (target == null) return default;

            var polygon = target.GetComponent<PolygonCollider2D>();
            if (polygon != null) return new PolygonInfo("PolygonCollider2D", polygon.pathCount, polygon.GetTotalPointCount());

            Sprite sprite = target.sprite;
            if (sprite == null) return default;

            int paths = sprite.GetPhysicsShapeCount();
            int vertices = 0;
            for (int i = 0; i < paths; i++) vertices += sprite.GetPhysicsShapePointCount(i);
            return paths > 0 ? new PolygonInfo("sprite physics shape", paths, vertices) : default;
        }
    }
}

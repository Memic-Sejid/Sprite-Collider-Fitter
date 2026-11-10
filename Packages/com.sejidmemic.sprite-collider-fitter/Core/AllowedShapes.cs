using System;

namespace SpriteFit
{
    /// <summary>Which primitive shapes a fit may use (FR-04).</summary>
    [Flags]
    public enum AllowedShapes
    {
        None = 0,
        Circle = 1 << 0,
        Capsule = 1 << 1,
        Box = 1 << 2,
        All = Circle | Capsule | Box,
    }

    public static class AllowedShapesExtensions
    {
        public static AllowedShapes ToFlag(this PrimitiveShape shape)
        {
            switch (shape)
            {
                case PrimitiveShape.Circle: return AllowedShapes.Circle;
                case PrimitiveShape.Capsule: return AllowedShapes.Capsule;
                case PrimitiveShape.Box: return AllowedShapes.Box;
                default: return AllowedShapes.None;
            }
        }

        public static bool Allows(this AllowedShapes allowed, PrimitiveShape shape)
        {
            return (allowed & shape.ToFlag()) != 0;
        }
    }
}

namespace SpriteFit
{
    /// <summary>Fits one shape of primitive to a set of pixels. One implementation per shape.</summary>
    public interface IPrimitiveFitter
    {
        PrimitiveShape Shape { get; }

        /// <summary>
        /// Returns the primitive of this shape that encloses the input's hull with the least area,
        /// in pixel space. The input must contain at least one pixel.
        /// </summary>
        Primitive Fit(FitInput input);
    }
}

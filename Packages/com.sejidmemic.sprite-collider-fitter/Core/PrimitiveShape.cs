namespace SpriteFit
{
    /// <summary>
    /// The collider shapes the fitter can produce, ordered from cheapest to most expensive for 2D physics.
    /// </summary>
    public enum PrimitiveShape
    {
        Circle = 0,
        Capsule = 1,
        Box = 2,
    }
}

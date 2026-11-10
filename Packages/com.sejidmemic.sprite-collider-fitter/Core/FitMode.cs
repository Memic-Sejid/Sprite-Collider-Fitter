namespace SpriteFit
{
    public enum FitMode
    {
        /// <summary>One primitive per island.</summary>
        Single = 0,

        /// <summary>Split islands into several primitives until the error tolerance or the primitive limit is met.</summary>
        Compound = 1,
    }
}

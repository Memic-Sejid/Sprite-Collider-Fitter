namespace SpriteFit
{
    /// <summary>Pixel counts that score a set of primitives against a mask.</summary>
    public readonly struct FitError
    {
        public FitError(int opaque, int over, int under)
        {
            Opaque = opaque;
            Over = over;
            Under = under;
        }

        /// <summary>Opaque pixels in the mask (A).</summary>
        public int Opaque { get; }

        /// <summary>Transparent pixels inside any primitive: objects hit empty air.</summary>
        public int Over { get; }

        /// <summary>Opaque pixels inside no primitive: objects clip through.</summary>
        public int Under { get; }

        public double OverRatio => Opaque > 0 ? (double)Over / Opaque : 0.0;

        public double UnderRatio => Opaque > 0 ? (double)Under / Opaque : 0.0;

        /// <summary>E = w_o · Over / A + w_u · Under / A.</summary>
        public double Weighted(double overWeight, double underWeight)
        {
            return overWeight * OverRatio + underWeight * UnderRatio;
        }

        public override string ToString()
        {
            return $"over {Over} ({OverRatio:P2}), under {Under} ({UnderRatio:P2}) of {Opaque}";
        }
    }
}

using System.Collections.Generic;

namespace SpriteFit.Editor
{
    /// <summary>Outcome of <see cref="BatchFitter.Apply"/>.</summary>
    public sealed class BatchReport
    {
        public int Total;
        public int Applied;
        public bool Cancelled;
        public double Seconds;

        /// <summary>Distinct sprites fitted; targets sharing a sprite reuse its fit.</summary>
        public int SpritesFitted;

        /// <summary>One line per target that was skipped, with the reason.</summary>
        public readonly List<string> Problems = new List<string>();

        public override string ToString()
        {
            string summary = $"Applied colliders to {Applied} of {Total} objects ({SpritesFitted} sprites) in {Seconds:0.0} s.";
            if (Cancelled) summary += " Cancelled.";
            if (Problems.Count > 0) summary += $" {Problems.Count} skipped:\n" + string.Join("\n", Problems);
            return summary;
        }
    }
}

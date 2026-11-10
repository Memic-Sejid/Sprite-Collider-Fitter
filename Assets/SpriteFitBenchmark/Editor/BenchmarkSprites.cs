using System;
using UnityEditor;
using UnityEngine;

namespace SpriteFit.Benchmark
{
    /// <summary>
    /// The sample sprites the benchmark and the sample scene use: a barrel (convex prop), a shuriken (thin
    /// blades), a spaceship (soft glow, thin wings) and a sword (long and narrow).
    /// </summary>
    public static class BenchmarkSprites
    {
        public const string Folder = "Assets/SpriteFitBenchmark/Sprites";

        public static readonly string[] Names = { "Barrel", "Shuriken", "Spaceship", "Sword" };

        public static Sprite[] Load()
        {
            var sprites = new Sprite[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/{Names[i]}.png");
                if (sprites[i] == null)
                {
                    throw new InvalidOperationException($"Missing sprite {Folder}/{Names[i]}.png.");
                }
            }

            return sprites;
        }
    }
}

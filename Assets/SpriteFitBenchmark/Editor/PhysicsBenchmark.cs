using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using SpriteFit.Editor;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SpriteFit.Benchmark
{
    /// <summary>
    /// The spec's physics benchmark: bodies of the sample sprites fall into a container, once with each
    /// collider variant, and the 2D physics step is timed. Runs in Edit mode inside a preview scene, which has
    /// its own physics scene, so it touches neither the open scenes nor the project's physics settings.
    /// </summary>
    public static class PhysicsBenchmark
    {
        public const string ResultsPath = "Assets/SpriteFitBenchmark/BenchmarkResults.md";

        const float TimeStep = 1f / 50f;
        const float Spacing = 1.6f;
        const int Columns = 25;

        // Compound fits are run at the default limit and a looser one, to show the cost/accuracy trade-off.
        static readonly int[] CompoundLimits = { 4, 8 };

        enum Kind
        {
            Polygon,
            Compound,
            BoundingBox,
        }

        sealed class Variant
        {
            public Kind Kind;
            public int CompoundIndex;
            public string Label;
        }

        sealed class SpriteData
        {
            public Sprite Sprite;
            public List<Vector2[]> Paths;
            public int PolygonVertices;
            public FitError PolygonError;
            public List<Primitive>[] Compound;
            public FitError[] CompoundError;
            public Primitive Box;
            public FitError BoxError;
        }

        sealed class Timing
        {
            public Variant Variant;
            public int Shapes;
            public double Average, P95, Max;
        }

        [MenuItem("Tools/Sprite Collider Fitter/Benchmark/Run", priority = 101)]
        static void RunFromMenu()
        {
            try
            {
                UnityEngine.Debug.Log(Run());
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <returns>The results as Markdown, also written to <see cref="ResultsPath"/>.</returns>
        public static string Run(int bodiesPerSprite = 250, int steps = 600)
        {
            Sprite[] sprites = BenchmarkSprites.Load();
            var settings = new FitSettings();

            var data = new List<SpriteData>();
            foreach (Sprite sprite in sprites) data.Add(Prepare(sprite, settings));

            var variants = new List<Variant> { new Variant { Kind = Kind.Polygon, Label = "PolygonCollider2D (sprite physics shape)" } };
            for (int i = 0; i < CompoundLimits.Length; i++)
            {
                variants.Add(new Variant { Kind = Kind.Compound, CompoundIndex = i, Label = $"Fitted primitives, up to {CompoundLimits[i]} (this tool)" });
            }

            variants.Add(new Variant { Kind = Kind.BoundingBox, Label = "Single bounding box" });

            var timings = new List<Timing>();
            foreach (Variant variant in variants)
            {
                EditorUtility.DisplayProgressBar("Physics benchmark", variant.Label, (float)timings.Count / variants.Count);
                timings.Add(Simulate(variant, data, bodiesPerSprite, steps));
            }

            EditorUtility.ClearProgressBar();
            string markdown = Format(data, timings, settings, bodiesPerSprite * data.Count, steps);
            File.WriteAllText(ResultsPath, markdown);
            AssetDatabase.ImportAsset(ResultsPath);
            return markdown;
        }

        static SpriteData Prepare(Sprite sprite, FitSettings settings)
        {
            PixelMask mask = SpriteMaskReader.ReadMask(sprite, settings.AlphaThreshold);
            var data = new SpriteData
            {
                Sprite = sprite,
                Compound = new List<Primitive>[CompoundLimits.Length],
                CompoundError = new FitError[CompoundLimits.Length],
            };

            for (int i = 0; i < CompoundLimits.Length; i++)
            {
                FitSettings limited = settings.Clone();
                limited.MaxPrimitives = CompoundLimits[i];
                FitResult result = SpriteFitter.Fit(mask, limited);
                data.CompoundError[i] = result.Error;
                data.Compound[i] = new List<Primitive>();
                foreach (Primitive p in result.Primitives) data.Compound[i].Add(p.ToLocal(mask.Pivot, mask.PixelsPerUnit));
            }

            // One axis-aligned box around all opaque pixels.
            var min = new int2(int.MaxValue);
            var max = new int2(int.MinValue);
            foreach (Island island in mask.Islands)
            {
                min = math.min(min, island.Min);
                max = math.max(max, island.Max);
            }

            Primitive box = Primitive.Box((float2)(min + max + 1) * 0.5f, (float2)(max - min + 1) * 0.5f, 0f);
            data.BoxError = ErrorEvaluator.Evaluate(mask, new[] { box });
            data.Box = box.ToLocal(mask.Pivot, mask.PixelsPerUnit);

            // The polygon a PolygonCollider2D gets: the sprite's physics shape.
            data.Paths = new List<Vector2[]>();
            var points = new List<Vector2>();
            for (int i = 0; i < sprite.GetPhysicsShapeCount(); i++)
            {
                sprite.GetPhysicsShape(i, points);
                data.Paths.Add(points.ToArray());
                data.PolygonVertices += points.Count;
            }

            data.PolygonError = PolygonError(mask, data.Paths);
            return data;
        }

        // Even-odd coverage of each pixel centre by the physics shape, in the mask's pixel space.
        static FitError PolygonError(PixelMask mask, List<Vector2[]> localPaths)
        {
            var paths = new List<Vector2[]>();
            foreach (Vector2[] local in localPaths)
            {
                var path = new Vector2[local.Length];
                for (int i = 0; i < local.Length; i++) path[i] = local[i] * mask.PixelsPerUnit + (Vector2)mask.Pivot;
                paths.Add(path);
            }

            int over = 0, under = 0;
            for (int y = 0; y < mask.Height; y++)
            {
                for (int x = 0; x < mask.Width; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    bool inside = false;
                    foreach (Vector2[] path in paths)
                    {
                        for (int i = 0, j = path.Length - 1; i < path.Length; j = i++)
                        {
                            Vector2 a = path[i], b = path[j];
                            if ((a.y > py) != (b.y > py) && px < (b.x - a.x) * (py - a.y) / (b.y - a.y) + a.x) inside = !inside;
                        }
                    }

                    bool opaque = mask.IsOpaque(x, y);
                    if (inside && !opaque) over++;
                    else if (!inside && opaque) under++;
                }
            }

            return new FitError(mask.OpaqueCount, over, under);
        }

        static Timing Simulate(Variant variant, List<SpriteData> data, int bodiesPerSprite, int steps)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                PhysicsScene2D physics = scene.GetPhysicsScene2D();
                int total = bodiesPerSprite * data.Count;
                int rows = (total + Columns - 1) / Columns;
                float width = Columns * Spacing;
                float height = rows * Spacing + 10f;

                GameObject container = NewObject(scene, "Container");
                AddWall(container, new Vector2(0f, -0.5f), new Vector2(width + 2f, 1f));
                AddWall(container, new Vector2(-width * 0.5f - 0.5f, height * 0.5f), new Vector2(1f, height));
                AddWall(container, new Vector2(width * 0.5f + 0.5f, height * 0.5f), new Vector2(1f, height));

                GameObject templateRoot = NewObject(scene, "Templates");
                templateRoot.SetActive(false);
                var templates = new GameObject[data.Count];
                for (int i = 0; i < data.Count; i++) templates[i] = BuildTemplate(variant, data[i], templateRoot.transform);

                Transform bodies = NewObject(scene, "Bodies").transform;
                var random = new System.Random(1);
                for (int i = 0; i < total; i++)
                {
                    var position = new Vector3(-width * 0.5f + (i % Columns + 0.5f) * Spacing, 2f + i / Columns * Spacing, 0f);
                    Quaternion rotation = Quaternion.Euler(0f, 0f, (float)random.NextDouble() * 360f);
                    Object.Instantiate(templates[i % data.Count], position, rotation, bodies);
                }

                // The first step creates the shapes; it isn't timed.
                physics.Simulate(TimeStep);
                int shapes = 0;
                foreach (Collider2D collider in bodies.GetComponentsInChildren<Collider2D>()) shapes += collider.shapeCount;

                var times = new double[steps];
                var stopwatch = new Stopwatch();
                for (int step = 0; step < steps; step++)
                {
                    stopwatch.Restart();
                    physics.Simulate(TimeStep);
                    times[step] = stopwatch.Elapsed.TotalMilliseconds;
                }

                double sum = 0;
                foreach (double t in times) sum += t;
                Array.Sort(times);
                return new Timing
                {
                    Variant = variant,
                    Shapes = shapes,
                    Average = sum / steps,
                    P95 = times[(int)Math.Ceiling(0.95 * steps) - 1],
                    Max = times[steps - 1],
                };
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static GameObject BuildTemplate(Variant variant, SpriteData data, Transform parent)
        {
            var template = new GameObject(data.Sprite.name) { hideFlags = HideFlags.DontSave };
            template.transform.SetParent(parent, false);
            template.AddComponent<Rigidbody2D>();

            switch (variant.Kind)
            {
                case Kind.Polygon:
                {
                    var polygon = template.AddComponent<PolygonCollider2D>();
                    polygon.pathCount = data.Paths.Count;
                    for (int i = 0; i < data.Paths.Count; i++) polygon.SetPath(i, data.Paths[i]);
                    break;
                }

                case Kind.Compound:
                    ColliderWriter.BuildContainer(template.transform, data.Compound[variant.CompoundIndex], false, false, false, null, 0);
                    break;

                case Kind.BoundingBox:
                {
                    var box = template.AddComponent<BoxCollider2D>();
                    box.offset = data.Box.Center;
                    box.size = 2f * (Vector2)data.Box.Extents;
                    break;
                }
            }

            return template;
        }

        // Created as DontSave so the open scene is never marked dirty, then moved into the preview scene.
        static GameObject NewObject(Scene scene, string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        static void AddWall(GameObject container, Vector2 center, Vector2 size)
        {
            var wall = container.AddComponent<BoxCollider2D>();
            wall.offset = center;
            wall.size = size;
        }

        static string Format(List<SpriteData> data, List<Timing> timings, FitSettings settings, int bodies, int steps)
        {
            var md = new StringBuilder();
            md.AppendLine("# Physics benchmark results");
            md.AppendLine();
            md.AppendLine($"Generated by Tools → Sprite Collider Fitter → Benchmark → Run on {DateTime.Now:yyyy-MM-dd}.");
            md.AppendLine($"Unity {Application.unityVersion}, {SystemInfo.processorType}, in the Editor.");
            md.AppendLine($"{bodies} dynamic bodies ({bodies / data.Count} of each sprite) fall into a container. " +
                          $"{steps} steps of {TimeStep * 1000f:0} ms are timed after one untimed step.");
            md.AppendLine($"Fitted primitives use the default settings ({settings.Tolerance:P0} tolerance, all shapes) with the limit shown.");
            md.AppendLine();
            md.AppendLine("| Variant | Physics shapes | Average step (ms) | 95th percentile (ms) | Slowest (ms) |");
            md.AppendLine("| --- | ---: | ---: | ---: | ---: |");
            foreach (Timing t in timings)
            {
                md.AppendLine($"| {t.Variant.Label} | {t.Shapes} | {t.Average:0.00} | {t.P95:0.00} | {t.Max:0.00} |");
            }

            md.AppendLine();
            md.AppendLine("Accuracy per sprite: overfill / underfill as a share of the sprite's opaque pixels (alpha ≥ 0.5).");
            md.AppendLine();
            var header = new StringBuilder("| Sprite | Polygon collider |");
            var rule = new StringBuilder("| --- | --- |");
            foreach (int limit in CompoundLimits)
            {
                header.Append($" Fitted, up to {limit} |");
                rule.Append(" --- |");
            }

            md.AppendLine(header + " Bounding box |");
            md.AppendLine(rule + " --- |");
            foreach (SpriteData d in data)
            {
                var row = new StringBuilder($"| {d.Sprite.name} | {Plural(d.Paths.Count, "path")}, {Plural(d.PolygonVertices, "vertex", "vertices")}: {Error(d.PolygonError)} |");
                for (int i = 0; i < CompoundLimits.Length; i++) row.Append($" {Describe(d.Compound[i])}: {Error(d.CompoundError[i])} |");
                row.Append($" {Error(d.BoxError)} |");
                md.AppendLine(row.ToString());
            }

            return md.ToString();
        }

        static string Error(FitError error)
        {
            return $"{error.OverRatio:P1} / {error.UnderRatio:P1}";
        }

        static string Plural(int count, string one, string many = null)
        {
            return $"{count} {(count == 1 ? one : many ?? one + "s")}";
        }

        static string Describe(List<Primitive> primitives)
        {
            int circles = 0, capsules = 0, boxes = 0;
            foreach (Primitive p in primitives)
            {
                if (p.Shape == PrimitiveShape.Circle) circles++;
                else if (p.Shape == PrimitiveShape.Capsule) capsules++;
                else boxes++;
            }

            var parts = new List<string>();
            if (circles > 0) parts.Add(Plural(circles, "circle"));
            if (capsules > 0) parts.Add(Plural(capsules, "capsule"));
            if (boxes > 0) parts.Add(Plural(boxes, "box", "boxes"));
            return string.Join(", ", parts);
        }
    }
}

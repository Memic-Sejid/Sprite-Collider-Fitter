using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace SpriteFit.Editor
{
    /// <summary>
    /// Fits and applies colliders to many sprite renderers with the same settings (FR-07). The whole batch
    /// is one Undo step, and each sprite is fitted once however many objects use it.
    /// </summary>
    public static class BatchFitter
    {
        const string UndoName = "Apply Sprite Colliders to Selection";

        /// <summary>
        /// Every SpriteRenderer on the given scene objects and their children, without duplicates.
        /// Assets (prefabs in the Project window) are left out; open a prefab to fit it.
        /// </summary>
        public static List<SpriteRenderer> CollectTargets(IEnumerable<GameObject> selection)
        {
            var targets = new List<SpriteRenderer>();
            if (selection == null) return targets;

            var seen = new HashSet<SpriteRenderer>();
            foreach (GameObject go in selection)
            {
                if (go == null || EditorUtility.IsPersistent(go)) continue;

                foreach (SpriteRenderer renderer in go.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (seen.Add(renderer)) targets.Add(renderer);
                }
            }

            return targets;
        }

        /// <param name="removeCustomColliders">
        /// Whether existing CustomCollider2Ds may be replaced (Root) or removed (Child); see
        /// <see cref="ColliderWriter.Apply"/>. With Root placement and false, targets that have one are skipped.
        /// </param>
        /// <param name="progress">
        /// Called before each target with (index, total, name); return true to cancel. Targets already
        /// done stay applied, and are still undone together.
        /// </param>
        public static BatchReport Apply(IReadOnlyList<SpriteRenderer> targets, FitSettings settings, OldColliderAction oldColliderAction,
            Func<int, int, string, bool> progress = null, ColliderPlacement placement = ColliderPlacement.Child, bool removeCustomColliders = false,
            bool forceTrigger = false)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var report = new BatchReport { Total = targets.Count };
            var fits = new Dictionary<Sprite, List<Primitive>>();
            Stopwatch stopwatch = Stopwatch.StartNew();

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);

            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    SpriteRenderer target = targets[i];
                    string name = target != null ? target.name : "(missing)";
                    if (progress != null && progress(i, targets.Count, name))
                    {
                        report.Cancelled = true;
                        break;
                    }

                    if (target == null)
                    {
                        report.Problems.Add("A target was deleted before it was reached.");
                        continue;
                    }

                    Sprite sprite = target.sprite;
                    if (sprite == null)
                    {
                        report.Problems.Add($"{name}: no sprite.");
                        continue;
                    }

                    if (placement == ColliderPlacement.Root && !removeCustomColliders && ColliderWriter.HasCustomCollider(target))
                    {
                        report.Problems.Add($"{name}: already has a CustomCollider2D; skipped.");
                        continue;
                    }

                    try
                    {
                        if (!fits.TryGetValue(sprite, out List<Primitive> local))
                        {
                            local = FitLocal(sprite, settings);
                            fits.Add(sprite, local);
                        }

                        if (local.Count == 0)
                        {
                            report.Problems.Add($"{name}: no primitives (no opaque pixels, or no shapes allowed).");
                            continue;
                        }

                        ColliderWriter.Write(target, local, oldColliderAction, placement, removeCustomColliders, forceTrigger);
                        report.Applied++;
                    }
                    catch (Exception exception)
                    {
                        report.Problems.Add($"{name}: {exception.Message}");
                    }
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
                report.SpritesFitted = fits.Count;
                report.Seconds = stopwatch.Elapsed.TotalSeconds;
            }

            return report;
        }

        static List<Primitive> FitLocal(Sprite sprite, FitSettings settings)
        {
            PixelMask mask = SpriteMaskReader.ReadMask(sprite, settings.AlphaThreshold);
            FitResult result = SpriteFitter.Fit(mask, settings);
            var local = new List<Primitive>(result.Primitives.Count);
            foreach (Primitive primitive in result.Primitives) local.Add(primitive.ToLocal(mask.Pivot, mask.PixelsPerUnit));
            return local;
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using SpriteFit.Editor;
using UnityEditor;
using UnityEngine;

namespace SpriteFit.Tests
{
    /// <summary>FR-07: many objects in one operation, one Undo step, cancellable.</summary>
    public class BatchFitterTests
    {
        const int Size = 64;

        readonly List<Object> _created = new List<Object>();
        readonly List<Sprite> _sprites = new List<Sprite>();
        int _undoGroup;

        [SetUp]
        public void SetUp()
        {
            if (!SpriteMaskReader.HasGraphicsDevice) Assert.Ignore("Needs a graphics device to read sprites.");

            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();

            // Five shapes, like the spec's debris set: discs of different sizes and a bar.
            for (int i = 0; i < 5; i++)
            {
                int radius = 10 + 4 * i;
                _sprites.Add(MakeSprite((x, y) => i == 4
                    ? x >= 8 && x < 56 && y >= 24 && y < 40
                    : (x - 32) * (x - 32) + (y - 32) * (y - 32) <= radius * radius));
            }
        }

        [TearDown]
        public void TearDown()
        {
            // See ColliderWriterTests: undo this test's steps before destroying its objects.
            Undo.RevertAllDownToGroup(_undoGroup);
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
            _sprites.Clear();
        }

        Sprite MakeSprite(System.Func<int, int, bool> opaque)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++) pixels[y * Size + x] = opaque(x, y) ? new Color32(255, 255, 255, 255) : default;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            _created.Add(texture);
            _created.Add(sprite);
            return sprite;
        }

        List<SpriteRenderer> MakeTargets(int count)
        {
            var targets = new List<SpriteRenderer>();
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"Debris {i}");
                _created.Insert(0, go);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = _sprites[i % _sprites.Count];
                go.AddComponent<PolygonCollider2D>();
                targets.Add(renderer);
            }

            return targets;
        }

        [Test]
        public void FiftyObjects_AllApplied_EachSpriteFittedOnce_OneUndoRemovesAll()
        {
            List<SpriteRenderer> targets = MakeTargets(50);
            int progressCalls = 0;

            BatchReport report = BatchFitter.Apply(targets, new FitSettings(), OldColliderAction.Disable, (i, n, name) =>
            {
                progressCalls++;
                return false;
            });

            Assert.That(report.Applied, Is.EqualTo(50), report.ToString());
            Assert.That(report.Problems, Is.Empty, report.ToString());
            Assert.That(report.SpritesFitted, Is.EqualTo(5));
            Assert.That(progressCalls, Is.EqualTo(50));
            Assert.That(targets.TrueForAll(t => ColliderWriter.FindContainer(t.transform) != null), Is.True);
            Assert.That(targets.TrueForAll(t => !t.GetComponent<PolygonCollider2D>().enabled), Is.True);

            Undo.PerformUndo();

            Assert.That(targets.TrueForAll(t => ColliderWriter.FindContainer(t.transform) == null), Is.True);
            Assert.That(targets.TrueForAll(t => t.GetComponent<PolygonCollider2D>().enabled), Is.True);
        }

        [Test]
        public void Cancel_StopsAndKeepsWhatWasDone()
        {
            List<SpriteRenderer> targets = MakeTargets(30);

            BatchReport report = BatchFitter.Apply(targets, new FitSettings(), OldColliderAction.Disable, (i, n, name) => i == 10);

            Assert.That(report.Cancelled, Is.True);
            Assert.That(report.Applied, Is.EqualTo(10));
            Assert.That(ColliderWriter.FindContainer(targets[9].transform), Is.Not.Null);
            Assert.That(ColliderWriter.FindContainer(targets[10].transform), Is.Null);
        }

        [Test]
        public void Root_WithoutConfirmation_SkipsObjectsThatHaveACustomCollider()
        {
            List<SpriteRenderer> targets = MakeTargets(4);
            targets[2].gameObject.AddComponent<CustomCollider2D>();

            BatchReport report = BatchFitter.Apply(targets, new FitSettings(), OldColliderAction.Disable, null, ColliderPlacement.Root, false);

            Assert.That(report.Applied, Is.EqualTo(3));
            Assert.That(report.Problems, Has.Count.EqualTo(1));
            Assert.That(report.Problems[0], Does.Contain("CustomCollider2D"));
            Assert.That(targets[2].GetComponent<CustomCollider2D>().customShapeCount, Is.Zero);
            Assert.That(targets[0].GetComponent<CustomCollider2D>().customShapeCount, Is.GreaterThan(0));
            Assert.That(targets.TrueForAll(t => t.transform.childCount == 0), Is.True);
        }

        [Test]
        public void ForceTrigger_AppliesToEveryObject()
        {
            List<SpriteRenderer> targets = MakeTargets(3);

            BatchReport report = BatchFitter.Apply(targets, new FitSettings(), OldColliderAction.Disable, null,
                ColliderPlacement.Child, false, forceTrigger: true);

            Assert.That(report.Applied, Is.EqualTo(3));
            foreach (SpriteRenderer target in targets)
            {
                Collider2D[] colliders = ColliderWriter.FindContainer(target.transform).GetComponentsInChildren<Collider2D>(true);
                Assert.That(colliders, Has.All.Matches<Collider2D>(c => c.isTrigger), target.name);
            }
        }

        [Test]
        public void TargetWithoutSprite_IsSkippedWithAReason()
        {
            List<SpriteRenderer> targets = MakeTargets(3);
            targets[1].sprite = null;

            BatchReport report = BatchFitter.Apply(targets, new FitSettings(), OldColliderAction.Disable);

            Assert.That(report.Applied, Is.EqualTo(2));
            Assert.That(report.Problems, Has.Count.EqualTo(1));
            Assert.That(report.Problems[0], Does.Contain("no sprite"));
        }

        [Test]
        public void CollectTargets_IncludesChildren_WithoutDuplicates()
        {
            List<SpriteRenderer> targets = MakeTargets(3);
            targets[1].transform.SetParent(targets[0].transform);
            targets[2].transform.SetParent(targets[1].transform);

            // Parent and a child both selected: each renderer once.
            List<SpriteRenderer> collected = BatchFitter.CollectTargets(new[] { targets[0].gameObject, targets[2].gameObject });

            Assert.That(collected, Is.EquivalentTo(targets));
        }
    }
}

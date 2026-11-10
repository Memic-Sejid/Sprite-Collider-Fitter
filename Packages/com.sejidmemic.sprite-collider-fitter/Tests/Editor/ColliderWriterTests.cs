using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpriteFit.Editor;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace SpriteFit.Tests
{
    /// <summary>FR-06: Apply writes the right components, is one Undo step, and is prefab-safe.</summary>
    public class ColliderWriterTests
    {
        const string TempFolder = "Assets/SpriteFitTestTemp";
        const int Layer = 5;

        // The test runner runs EditMode tests in its own untitled scene and restores the user's scenes after.
        readonly List<GameObject> _created = new List<GameObject>();
        PhysicsMaterial2D _material;
        int _undoGroup;

        [SetUp]
        public void SetUp()
        {
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            _material = new PhysicsMaterial2D("SpriteFit Test") { hideFlags = HideFlags.DontSave };
        }

        [TearDown]
        public void TearDown()
        {
            // Undo this test's steps while its objects still exist. Otherwise the runner's own revert after the
            // run replays them against destroyed objects and resurrects them in the user's scene.
            Undo.RevertAllDownToGroup(_undoGroup);

            foreach (GameObject go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            _created.Clear();
            if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder);
            Object.DestroyImmediate(_material);
        }

        static Primitive[] Sample()
        {
            return new[]
            {
                Primitive.Circle(new float2(0.5f, 0.25f), 0.3f),
                Primitive.Box(new float2(-0.4f, 0.1f), new float2(0.2f, 0.1f), math.radians(30f)),
                Primitive.Box(float2.zero, new float2(0.5f, 0.5f), 0f),
                Primitive.Capsule(new float2(0.2f, -0.3f), math.radians(80f), 0.3f, 0.1f),
            };
        }

        SpriteRenderer MakeTarget()
        {
            var go = new GameObject("Target") { layer = Layer };
            _created.Add(go);
            var renderer = go.AddComponent<SpriteRenderer>();
            var polygon = go.AddComponent<PolygonCollider2D>();
            polygon.isTrigger = true;
            polygon.sharedMaterial = _material;
            return renderer;
        }

        [Test]
        public void Apply_WritesEveryPrimitive_CopiesSettings_DisablesPolygon()
        {
            SpriteRenderer target = MakeTarget();

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable);

            Transform container = ColliderWriter.FindContainer(target.transform);
            Assert.That(container, Is.Not.Null);
            Collider2D[] colliders = container.GetComponentsInChildren<Collider2D>(true);
            Assert.That(colliders.Length, Is.EqualTo(4));
            Assert.That(colliders, Has.All.Matches<Collider2D>(c => c.isTrigger && c.sharedMaterial == _material && c.gameObject.layer == Layer));
            Assert.That(target.GetComponent<PolygonCollider2D>().enabled, Is.False);

            var circle = container.GetComponent<CircleCollider2D>();
            Assert.That(circle.offset, Is.EqualTo(new Vector2(0.5f, 0.25f)));
            Assert.That(circle.radius, Is.EqualTo(0.3f).Within(1e-6f));

            // The unrotated box sits on the container; the rotated one gets its own child.
            var flatBox = container.GetComponent<BoxCollider2D>();
            Assert.That(flatBox.size.x, Is.EqualTo(1f).Within(1e-6f));
            Transform rotatedBox = container.Find("Box 1");
            Assert.That(rotatedBox.localRotation.eulerAngles.z, Is.EqualTo(30f).Within(1e-3f));
            Assert.That((Vector2)rotatedBox.localPosition, Is.EqualTo(new Vector2(-0.4f, 0.1f)));
            Assert.That(rotatedBox.GetComponent<BoxCollider2D>().size.x, Is.EqualTo(0.4f).Within(1e-6f));

            // An 80° capsule becomes a vertical capsule turned -10°.
            CapsuleCollider2D capsule = container.Find("Capsule 1").GetComponent<CapsuleCollider2D>();
            Assert.That(capsule.direction, Is.EqualTo(CapsuleDirection2D.Vertical));
            Assert.That(capsule.size.x, Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(capsule.size.y, Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That(Mathf.DeltaAngle(0f, capsule.transform.localRotation.eulerAngles.z), Is.EqualTo(-10f).Within(1e-3f));
        }

        [Test]
        public void OneUndo_RemovesEverything_AndReenablesPolygon()
        {
            SpriteRenderer target = MakeTarget();

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable);
            Undo.PerformUndo();

            Assert.That(ColliderWriter.FindContainer(target.transform), Is.Null);
            Assert.That(target.GetComponent<PolygonCollider2D>().enabled, Is.True);
        }

        [Test]
        public void OneUndo_AfterRemovingPolygon_BringsItBack()
        {
            SpriteRenderer target = MakeTarget();

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Remove);
            Assert.That(target.GetComponent<PolygonCollider2D>(), Is.Null);
            Undo.PerformUndo();

            Assert.That(ColliderWriter.FindContainer(target.transform), Is.Null);
            Assert.That(target.GetComponent<PolygonCollider2D>(), Is.Not.Null);
        }

        [Test]
        public void ApplyTwice_ReplacesThePreviousColliders()
        {
            SpriteRenderer target = MakeTarget();

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Remove);
            ColliderWriter.Apply(target, new[] { Primitive.Circle(float2.zero, 1f) }, OldColliderAction.Remove);

            int containers = target.transform.Cast<Transform>().Count(t => t.name == ColliderWriter.ContainerName);
            Assert.That(containers, Is.EqualTo(1));
            Collider2D[] colliders = ColliderWriter.FindContainer(target.transform).GetComponentsInChildren<Collider2D>(true);
            Assert.That(colliders.Length, Is.EqualTo(1));
            Assert.That(colliders[0].isTrigger, Is.True, "Settings carry over from the previous colliders once the polygon is gone.");
        }

        [Test]
        public void FlipX_MirrorsTheColliders()
        {
            SpriteRenderer target = MakeTarget();
            target.flipX = true;

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable);

            Transform container = ColliderWriter.FindContainer(target.transform);
            Assert.That(container.GetComponent<CircleCollider2D>().offset, Is.EqualTo(new Vector2(-0.5f, 0.25f)));
            Transform rotatedBox = container.Find("Box 1");
            Assert.That((Vector2)rotatedBox.localPosition, Is.EqualTo(new Vector2(0.4f, 0.1f)));
            Assert.That(Mathf.DeltaAngle(0f, rotatedBox.localRotation.eulerAngles.z), Is.EqualTo(-30f).Within(1e-3f));
        }

        [Test]
        public void Revert_AfterRemove_AddsPolygonBackWithSettings()
        {
            SpriteRenderer target = MakeTarget();
            ColliderWriter.Apply(target, Sample(), OldColliderAction.Remove);

            bool reverted = ColliderWriter.Revert(target);

            Assert.That(reverted, Is.True);
            Assert.That(ColliderWriter.FindContainer(target.transform), Is.Null);
            var polygon = target.GetComponent<PolygonCollider2D>();
            Assert.That(polygon, Is.Not.Null);
            Assert.That(polygon.isTrigger, Is.True);
            Assert.That(polygon.sharedMaterial, Is.EqualTo(_material));
            Assert.That(ColliderWriter.Revert(target), Is.False, "Nothing left to revert.");
        }

        [Test]
        public void PrefabInstance_RecordsOverrides_AndUndoRestores()
        {
            AssetDatabase.CreateFolder("Assets", "SpriteFitTestTemp");
            SpriteRenderer source = MakeTarget();
            source.GetComponent<PolygonCollider2D>().sharedMaterial = null;
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(source.gameObject, TempFolder + "/Target.prefab");
            Object.DestroyImmediate(source.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            _created.Add(instance);
            var target = instance.GetComponent<SpriteRenderer>();

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable);

            GameObject container = ColliderWriter.FindContainer(target.transform).gameObject;
            Assert.That(PrefabUtility.IsAddedGameObjectOverride(container), Is.True);
            var polygon = instance.GetComponent<PolygonCollider2D>();
            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(instance);
            Assert.That(modifications.Any(m => m.propertyPath == "m_Enabled" && m.value == "0"), Is.True, "Disabling the polygon must be a recorded override.");

            Undo.PerformUndo();

            Assert.That(ColliderWriter.FindContainer(target.transform), Is.Null);
            Assert.That(polygon.enabled, Is.True);
            Assert.That(PrefabUtility.GetAddedGameObjects(instance), Is.Empty);
        }

        [TestCase(ColliderPlacement.Child)]
        [TestCase(ColliderPlacement.Root)]
        public void Colliders_MoveRotateAndScaleWithTheObject(ColliderPlacement placement)
        {
            SpriteRenderer target = MakeTarget();
            target.transform.position = new Vector3(3f, -2f, 0f);
            var circle = new[] { Primitive.Circle(new float2(0.5f, 0.25f), 0.3f) };

            ColliderWriter.Apply(target, circle, OldColliderAction.Disable, placement);
            target.transform.SetPositionAndRotation(new Vector3(10f, 5f, 0f), Quaternion.Euler(0f, 0f, 90f));
            target.transform.localScale = new Vector3(2f, 2f, 1f);
            Physics2D.SyncTransforms();

            Collider2D collider = GeneratedColliders(target).Single();
            Vector3 expected = target.transform.TransformPoint(new Vector3(0.5f, 0.25f, 0f));
            Assert.That(Vector2.Distance(collider.bounds.center, expected), Is.LessThan(1e-3f), $"centre {collider.bounds.center}, expected {expected}");
            Assert.That(collider.bounds.size.x, Is.EqualTo(1.2f).Within(1e-3f), "Scale applies too.");
            Assert.That(collider.OverlapPoint(expected), Is.True);
        }

        [Test]
        public void Root_WithoutABody_AddsAStaticOne_AndUndoRemovesIt()
        {
            SpriteRenderer target = MakeTarget();
            Assert.That(ColliderWriter.NeedsStaticBody(target, ColliderPlacement.Root), Is.True);

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable, ColliderPlacement.Root);

            var body = target.GetComponent<Rigidbody2D>();
            Assert.That(body, Is.Not.Null);
            Assert.That(body.bodyType, Is.EqualTo(RigidbodyType2D.Static));
            Assert.That(target.GetComponent<CustomCollider2D>().attachedRigidbody, Is.EqualTo(body));

            Undo.PerformUndo();

            Assert.That(target.GetComponent<Rigidbody2D>(), Is.Null);
            Assert.That(target.GetComponent<CustomCollider2D>(), Is.Null);
        }

        [Test]
        public void Root_WithItsOwnBody_UsesItAsIs()
        {
            SpriteRenderer target = MakeTarget();
            var body = target.gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable, ColliderPlacement.Root);

            Assert.That(target.GetComponents<Rigidbody2D>(), Is.EqualTo(new[] { body }));
            Assert.That(body.bodyType, Is.EqualTo(RigidbodyType2D.Dynamic));
        }

        [Test]
        public void Root_OnAChildOfABody_IsRefusedWithoutChanges()
        {
            SpriteRenderer target = MakeTarget();
            var parent = new GameObject("Body");
            _created.Add(parent);
            parent.AddComponent<Rigidbody2D>();
            target.transform.SetParent(parent.transform, false);

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable, ColliderPlacement.Root));

            Assert.That(error.Message, Does.Contain("Body"));
            Assert.That(target.GetComponent<CustomCollider2D>(), Is.Null);
            Assert.That(target.GetComponent<Rigidbody2D>(), Is.Null);
            Assert.That(target.GetComponent<PolygonCollider2D>().enabled, Is.True);

            // Child placement still works there.
            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable);
            Assert.That(ColliderWriter.FindContainer(target.transform), Is.Not.Null);
        }

        [TestCase(ColliderPlacement.Child)]
        [TestCase(ColliderPlacement.Root)]
        public void ForceTrigger_MakesEveryColliderATrigger_EvenWhenThePolygonIsnt(ColliderPlacement placement)
        {
            SpriteRenderer target = MakeTarget();
            target.GetComponent<PolygonCollider2D>().isTrigger = false;

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable, placement, forceTrigger: true);

            List<Collider2D> generated = GeneratedColliders(target);
            Assert.That(generated, Is.Not.Empty);
            Assert.That(generated, Has.All.Matches<Collider2D>(c => c.isTrigger));
            Assert.That(target.GetComponent<PolygonCollider2D>().isTrigger, Is.False, "The old polygon is left as it was.");
        }

        [TestCase(ColliderPlacement.Child)]
        [TestCase(ColliderPlacement.Root)]
        public void WithoutForceTrigger_CopiesTheSettingFromThePolygon(ColliderPlacement placement)
        {
            SpriteRenderer target = MakeTarget();
            target.GetComponent<PolygonCollider2D>().isTrigger = false;

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable, placement);

            Assert.That(GeneratedColliders(target), Has.None.Matches<Collider2D>(c => c.isTrigger));
        }

        static List<Collider2D> GeneratedColliders(SpriteRenderer target)
        {
            var colliders = new List<Collider2D>();
            Transform container = ColliderWriter.FindContainer(target.transform);
            if (container != null) colliders.AddRange(container.GetComponentsInChildren<Collider2D>(true));

            var custom = target.GetComponent<CustomCollider2D>();
            if (custom != null) colliders.Add(custom);
            return colliders;
        }

        static PhysicsShapeGroup2D ShapesOf(CustomCollider2D collider)
        {
            var shapes = new PhysicsShapeGroup2D();
            collider.GetCustomShapes(shapes);
            return shapes;
        }

        [Test]
        public void Root_OneCustomColliderWithEveryShape_NoChildren()
        {
            SpriteRenderer target = MakeTarget();

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable, ColliderPlacement.Root);

            Assert.That(ColliderWriter.FindContainer(target.transform), Is.Null);
            Assert.That(target.transform.childCount, Is.Zero);
            var custom = target.GetComponent<CustomCollider2D>();
            Assert.That(custom, Is.Not.Null);
            Assert.That(custom.isTrigger, Is.True);
            Assert.That(custom.sharedMaterial, Is.EqualTo(_material));
            Assert.That(target.GetComponent<PolygonCollider2D>().enabled, Is.False);

            PhysicsShapeGroup2D shapes = ShapesOf(custom);
            Assert.That(shapes.shapeCount, Is.EqualTo(4));

            PhysicsShape2D circle = shapes.GetShape(0);
            Assert.That(circle.shapeType, Is.EqualTo(PhysicsShapeType2D.Circle));
            Assert.That(circle.radius, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(shapes.GetShapeVertex(0, 0), Is.EqualTo(new Vector2(0.5f, 0.25f)));

            // The 0.4 × 0.2 box at 30° around (-0.4, 0.1): its first corner is (-0.2, -0.1) rotated by 30°.
            Assert.That(shapes.GetShape(1).shapeType, Is.EqualTo(PhysicsShapeType2D.Polygon));
            Vector2 corner = (Vector2)(Quaternion.Euler(0f, 0f, 30f) * new Vector2(-0.2f, -0.1f)) + new Vector2(-0.4f, 0.1f);
            Assert.That(Vector2.Distance(shapes.GetShapeVertex(1, 0), corner), Is.LessThan(1e-4f));

            // The 80° capsule keeps its exact axis: no rounding to vertical.
            PhysicsShape2D capsule = shapes.GetShape(3);
            Assert.That(capsule.shapeType, Is.EqualTo(PhysicsShapeType2D.Capsule));
            Assert.That(capsule.radius, Is.EqualTo(0.1f).Within(1e-5f));
            Vector2 axis = shapes.GetShapeVertex(3, 1) - shapes.GetShapeVertex(3, 0);
            Assert.That(Vector2.SignedAngle(Vector2.right, axis), Is.EqualTo(80f).Within(1e-3f));
            Assert.That(axis.magnitude, Is.EqualTo(0.6f).Within(1e-5f));
        }

        [Test]
        public void Root_OneUndo_RemovesTheCustomCollider()
        {
            SpriteRenderer target = MakeTarget();

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable, ColliderPlacement.Root);
            Undo.PerformUndo();

            Assert.That(target.GetComponent<CustomCollider2D>(), Is.Null);
            Assert.That(target.GetComponent<PolygonCollider2D>().enabled, Is.True);
        }

        [Test]
        public void Root_ExistingCustomCollider_NeedsConfirmation_ThenReusesIt()
        {
            SpriteRenderer target = MakeTarget();
            var existing = target.gameObject.AddComponent<CustomCollider2D>();

            Assert.Throws<System.InvalidOperationException>(() =>
                ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable, ColliderPlacement.Root));
            Assert.That(target.GetComponent<PolygonCollider2D>().enabled, Is.True, "Nothing changes without confirmation.");

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable, ColliderPlacement.Root, removeCustomCollider: true);

            Assert.That(target.GetComponents<CustomCollider2D>(), Is.EqualTo(new[] { existing }));
            Assert.That(existing.customShapeCount, Is.EqualTo(4));

            Undo.PerformUndo();

            Assert.That(existing.customShapeCount, Is.Zero, "Undo restores the shapes it had.");
        }

        [Test]
        public void SwitchingPlacement_RemovesTheOtherKind()
        {
            SpriteRenderer target = MakeTarget();

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable);
            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable, ColliderPlacement.Root);
            Assert.That(ColliderWriter.FindContainer(target.transform), Is.Null, "Root placement replaces the generated child.");

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable, ColliderPlacement.Child, removeCustomCollider: false);
            Assert.That(target.GetComponent<CustomCollider2D>(), Is.Not.Null, "Without confirmation the CustomCollider2D stays.");

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable, ColliderPlacement.Child, removeCustomCollider: true);
            Assert.That(target.GetComponent<CustomCollider2D>(), Is.Null);
            Assert.That(ColliderWriter.FindContainer(target.transform), Is.Not.Null);
        }

        [Test]
        public void Root_FlipX_MirrorsTheShapes()
        {
            SpriteRenderer target = MakeTarget();
            target.flipX = true;

            ColliderWriter.Apply(target, Sample(), OldColliderAction.Disable, ColliderPlacement.Root);

            PhysicsShapeGroup2D shapes = ShapesOf(target.GetComponent<CustomCollider2D>());
            Assert.That(shapes.GetShapeVertex(0, 0), Is.EqualTo(new Vector2(-0.5f, 0.25f)));
        }

        [Test]
        public void Revert_RemovesTheCustomColliderOnlyWhenAllowed()
        {
            SpriteRenderer target = MakeTarget();
            ColliderWriter.Apply(target, Sample(), OldColliderAction.Remove, ColliderPlacement.Root);

            // Keeping it means treating it as the user's own collider: nothing generated is left to revert.
            Assert.That(ColliderWriter.Revert(target, removeCustomCollider: false), Is.False);
            Assert.That(target.GetComponent<CustomCollider2D>(), Is.Not.Null);
            Assert.That(target.GetComponent<PolygonCollider2D>(), Is.Null);

            Assert.That(ColliderWriter.Revert(target, removeCustomCollider: true), Is.True);
            Assert.That(target.GetComponent<CustomCollider2D>(), Is.Null);
            Assert.That(target.GetComponent<PolygonCollider2D>().isTrigger, Is.True);
        }

        [Test]
        public void Root_OnPrefabInstance_RecordsAnAddedComponent_AndUndoRestores()
        {
            AssetDatabase.CreateFolder("Assets", "SpriteFitTestTemp");
            SpriteRenderer source = MakeTarget();
            source.GetComponent<PolygonCollider2D>().sharedMaterial = null;
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(source.gameObject, TempFolder + "/Target.prefab");
            Object.DestroyImmediate(source.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            _created.Add(instance);

            ColliderWriter.Apply(instance.GetComponent<SpriteRenderer>(), Sample(), OldColliderAction.Disable, ColliderPlacement.Root);

            Assert.That(PrefabUtility.GetAddedComponents(instance).Any(c => c.instanceComponent is CustomCollider2D), Is.True);

            Undo.PerformUndo();

            Assert.That(instance.GetComponent<CustomCollider2D>(), Is.Null);
            Assert.That(PrefabUtility.GetAddedComponents(instance), Is.Empty);
        }
    }
}

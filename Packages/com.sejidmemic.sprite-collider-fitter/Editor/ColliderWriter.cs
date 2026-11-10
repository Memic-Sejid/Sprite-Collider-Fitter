using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace SpriteFit.Editor
{
    /// <summary>
    /// Writes primitives as built-in 2D colliders (FR-06), either under a "Colliders (generated)" child or as
    /// one CustomCollider2D on the target (see <see cref="ColliderPlacement"/>). Each Apply or Revert is one
    /// Undo step, and prefab instances get proper overrides.
    /// </summary>
    /// <remarks>
    /// The generated child is recognised by its name. A CustomCollider2D can't be recognised, because no marker
    /// may ship in builds, so callers must ask the user before letting the writer remove or replace one.
    /// </remarks>
    public static class ColliderWriter
    {
        public const string ContainerName = "Colliders (generated)";
        const string ApplyUndoName = "Apply Sprite Colliders";
        const string RevertUndoName = "Revert Sprite Colliders";

        // Rotations below this are written as offsets on the container instead of a rotated child.
        const float RotationEpsilonDegrees = 0.01f;

        // Capsules shorter than this are written as circles.
        const float MinCapsuleHalfLength = 1e-5f;

        public static Transform FindContainer(Transform target)
        {
            if (target == null) return null;

            for (int i = 0; i < target.childCount; i++)
            {
                Transform child = target.GetChild(i);
                if (child.name == ContainerName) return child;
            }

            return null;
        }

        /// <summary>True when the target has a CustomCollider2D, which Apply and Revert only touch with permission.</summary>
        public static bool HasCustomCollider(SpriteRenderer target)
        {
            return target != null && target.GetComponent<CustomCollider2D>() != null;
        }

        /// <summary>
        /// Replaces any previous generated colliders with <paramref name="localPrimitives"/>, which are in the
        /// sprite's local space (pixel space through <see cref="Primitive.ToLocal"/>, before any flip).
        /// </summary>
        /// <param name="removeCustomCollider">
        /// Whether an existing CustomCollider2D on the target may be replaced (Root) or removed (Child). With Root
        /// placement, an existing one and false throws; with Child placement, false keeps it.
        /// </param>
        /// <param name="forceTrigger">
        /// Make every generated collider a trigger. When false, the setting is copied from the target's existing collider.
        /// </param>
        public static void Apply(SpriteRenderer target, IReadOnlyList<Primitive> localPrimitives, OldColliderAction oldColliderAction,
            ColliderPlacement placement = ColliderPlacement.Child, bool removeCustomCollider = false, bool forceTrigger = false)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (localPrimitives == null) throw new ArgumentNullException(nameof(localPrimitives));

            CheckCanWrite(target, placement, removeCustomCollider);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(ApplyUndoName);
            Write(target, localPrimitives, oldColliderAction, placement, removeCustomCollider, forceTrigger);
            Undo.CollapseUndoOperations(group);
        }

        /// <summary>
        /// Apply without its own Undo group, so a batch can collapse many targets into one step.
        /// Throws <see cref="InvalidOperationException"/> before changing anything if the target can't be written.
        /// </summary>
        internal static void Write(SpriteRenderer target, IReadOnlyList<Primitive> localPrimitives, OldColliderAction oldColliderAction,
            ColliderPlacement placement, bool removeCustomCollider, bool forceTrigger)
        {
            CheckCanWrite(target, placement, removeCustomCollider);

            GameObject owner = target.gameObject;
            Transform previous = FindContainer(owner.transform);
            var custom = owner.GetComponent<CustomCollider2D>();

            Collider2D source = FindSourceCollider(owner, previous);
            bool isTrigger = forceTrigger || (source != null && source.isTrigger);
            PhysicsMaterial2D material = source != null ? source.sharedMaterial : null;

            if (previous != null) Undo.DestroyObjectImmediate(previous.gameObject);

            if (placement == ColliderPlacement.Root)
            {
                // CustomCollider2D shapes are placed relative to the Rigidbody2D they're attached to, not to the
                // collider's own Transform. With no body they'd stay fixed in world space, so give the object a
                // static one, which behaves like no body at all apart from carrying the shapes along.
                if (NeedsStaticBody(target, placement))
                {
                    var body = Undo.AddComponent<Rigidbody2D>(owner);
                    Undo.RecordObject(body, ApplyUndoName);
                    body.bodyType = RigidbodyType2D.Static;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(body);
                }

                // Reusing an existing component keeps prefab overrides to a property change.
                if (custom == null) custom = Undo.AddComponent<CustomCollider2D>(owner);
                Undo.RecordObject(custom, ApplyUndoName);
                custom.SetCustomShapes(BuildShapes(localPrimitives, target.flipX, target.flipY));
                custom.offset = Vector2.zero;
                Configure(custom, isTrigger, material);
                PrefabUtility.RecordPrefabInstancePropertyModifications(custom);
            }
            else
            {
                int layer = source != null ? source.gameObject.layer : owner.layer;

                // Build the whole hierarchy, then register it once, so a single Undo removes all of it.
                GameObject container = BuildContainer(owner.transform, localPrimitives, target.flipX, target.flipY, isTrigger, material, layer);
                Undo.RegisterCreatedObjectUndo(container, ApplyUndoName);
                if (custom != null && removeCustomCollider) Undo.DestroyObjectImmediate(custom);
            }

            var polygon = owner.GetComponent<PolygonCollider2D>();
            if (polygon != null)
            {
                if (oldColliderAction == OldColliderAction.Remove)
                {
                    Undo.DestroyObjectImmediate(polygon);
                }
                else if (polygon.enabled)
                {
                    Undo.RecordObject(polygon, ApplyUndoName);
                    polygon.enabled = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(polygon);
                }
            }
        }

        /// <summary>
        /// True when Root placement will add a static Rigidbody2D: the target has no Rigidbody2D of its own and
        /// none on a parent. Without one, a CustomCollider2D's shapes don't move with the object.
        /// </summary>
        public static bool NeedsStaticBody(SpriteRenderer target, ColliderPlacement placement)
        {
            return placement == ColliderPlacement.Root && target != null && target.GetComponentInParent<Rigidbody2D>(true) == null;
        }

        static void CheckCanWrite(SpriteRenderer target, ColliderPlacement placement, bool removeCustomCollider)
        {
            EnsureCanReplace(FindContainer(target.transform));
            if (placement != ColliderPlacement.Root) return;

            if (!removeCustomCollider && HasCustomCollider(target))
            {
                throw new InvalidOperationException($"'{target.name}' already has a CustomCollider2D, and replacing it wasn't confirmed.");
            }

            Rigidbody2D parentBody = FindParentBody(target);
            if (parentBody != null)
            {
                throw new InvalidOperationException(
                    $"'{target.name}' is part of the Rigidbody2D on '{parentBody.name}'. Colliders on the root object only work on the " +
                    "object that has the Rigidbody2D, so turn off \"Add colliders to root object\" for this one.");
            }
        }

        /// <summary>
        /// The parent's Rigidbody2D when the target has none of its own but belongs to one higher up; otherwise null.
        /// Root placement can't be used then: the shapes would be placed relative to the parent's body and end up in
        /// the wrong place, and adding a body to the target would split it off from the parent.
        /// </summary>
        public static Rigidbody2D FindParentBody(SpriteRenderer target)
        {
            if (target == null || target.GetComponent<Rigidbody2D>() != null) return null;

            return target.GetComponentInParent<Rigidbody2D>(true);
        }

        /// <summary>The shapes for a CustomCollider2D, in the target's local space (flips applied).</summary>
        public static PhysicsShapeGroup2D BuildShapes(IReadOnlyList<Primitive> localPrimitives, bool flipX, bool flipY)
        {
            if (localPrimitives == null) throw new ArgumentNullException(nameof(localPrimitives));

            var shapes = new PhysicsShapeGroup2D();
            foreach (Primitive primitive in localPrimitives)
            {
                Primitive p = primitive.Mirrored(flipX, flipY);
                switch (p.Shape)
                {
                    case PrimitiveShape.Circle:
                        shapes.AddCircle(p.Center, p.Radius);
                        break;

                    case PrimitiveShape.Box:
                        shapes.AddBox(p.Center, 2f * (Vector2)p.Extents, math.degrees(p.Angle), 0f);
                        break;

                    case PrimitiveShape.Capsule:
                        if (p.Extents.x < MinCapsuleHalfLength)
                        {
                            shapes.AddCircle(p.Center, p.Radius);
                        }
                        else
                        {
                            float2 half = p.Axis * p.Extents.x;
                            shapes.AddCapsule(p.Center - half, p.Center + half, p.Radius);
                        }

                        break;
                }
            }

            return shapes;
        }

        /// <summary>
        /// Creates a "Colliders (generated)" child of <paramref name="parent"/> holding the colliders, without
        /// recording Undo. For tools that manage their own objects, such as benchmarks in preview scenes;
        /// use <see cref="Apply"/> for anything the user should be able to undo.
        /// </summary>
        public static GameObject BuildContainer(Transform parent, IReadOnlyList<Primitive> localPrimitives, bool flipX, bool flipY,
            bool isTrigger, PhysicsMaterial2D material, int layer)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (localPrimitives == null) throw new ArgumentNullException(nameof(localPrimitives));

            var container = new GameObject(ContainerName) { layer = layer };
            container.transform.SetParent(parent, false);

            int boxes = 0, capsules = 0;
            foreach (Primitive primitive in localPrimitives)
            {
                Primitive p = primitive.Mirrored(flipX, flipY);
                float angle = math.degrees(p.Angle);

                switch (p.Shape)
                {
                    case PrimitiveShape.Circle:
                    {
                        var circle = container.AddComponent<CircleCollider2D>();
                        circle.offset = p.Center;
                        circle.radius = p.Radius;
                        Configure(circle, isTrigger, material);
                        break;
                    }

                    case PrimitiveShape.Box:
                    {
                        GameObject host = Host(container, $"Box {++boxes}", p.Center, angle, out Vector2 offset);
                        var box = host.AddComponent<BoxCollider2D>();
                        box.offset = offset;
                        box.size = 2f * (Vector2)p.Extents;
                        Configure(box, isTrigger, material);
                        break;
                    }

                    case PrimitiveShape.Capsule:
                    {
                        // CapsuleCollider2D only points along x or y; pick the one nearer the capsule's axis.
                        float length = 2f * (p.Extents.x + p.Extents.y);
                        float thickness = 2f * p.Extents.y;
                        CapsuleDirection2D direction;
                        Vector2 size;
                        if (math.abs(angle) <= 45f)
                        {
                            direction = CapsuleDirection2D.Horizontal;
                            size = new Vector2(length, thickness);
                        }
                        else
                        {
                            direction = CapsuleDirection2D.Vertical;
                            size = new Vector2(thickness, length);
                            angle -= 90f * math.sign(angle);
                        }

                        GameObject host = Host(container, $"Capsule {++capsules}", p.Center, angle, out Vector2 offset);
                        var capsule = host.AddComponent<CapsuleCollider2D>();
                        capsule.direction = direction;
                        capsule.size = size;
                        capsule.offset = offset;
                        Configure(capsule, isTrigger, material);
                        break;
                    }
                }
            }

            return container;
        }

        /// <summary>
        /// Removes the generated colliders and brings back the polygon: re-enables a disabled one, or adds a
        /// new one (generated from the sprite's physics shape) if it was removed.
        /// </summary>
        /// <param name="removeCustomCollider">Whether a CustomCollider2D on the target may be removed too.</param>
        /// <returns>False if there was nothing to revert.</returns>
        public static bool Revert(SpriteRenderer target, bool removeCustomCollider = false)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            GameObject owner = target.gameObject;
            Transform container = FindContainer(owner.transform);
            CustomCollider2D custom = removeCustomCollider ? owner.GetComponent<CustomCollider2D>() : null;
            var polygon = owner.GetComponent<PolygonCollider2D>();
            if (container == null && custom == null && (polygon == null || polygon.enabled)) return false;

            EnsureCanReplace(container);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(RevertUndoName);

            // Keep the trigger and material settings the generated colliders carried.
            Collider2D generated = container != null ? container.GetComponentInChildren<Collider2D>(true) : null;
            if (generated == null) generated = custom;
            bool isTrigger = generated != null && generated.isTrigger;
            PhysicsMaterial2D material = generated != null ? generated.sharedMaterial : null;

            if (container != null) Undo.DestroyObjectImmediate(container.gameObject);
            if (custom != null) Undo.DestroyObjectImmediate(custom);

            if (polygon == null)
            {
                polygon = Undo.AddComponent<PolygonCollider2D>(owner);
                Configure(polygon, isTrigger, material);
            }
            else if (!polygon.enabled)
            {
                Undo.RecordObject(polygon, RevertUndoName);
                polygon.enabled = true;
                PrefabUtility.RecordPrefabInstancePropertyModifications(polygon);
            }

            Undo.CollapseUndoOperations(group);
            return true;
        }

        static void EnsureCanReplace(Transform container)
        {
            if (container == null) return;

            GameObject go = container.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsAddedGameObjectOverride(go))
            {
                throw new InvalidOperationException(
                    $"'{ContainerName}' on '{container.parent.name}' comes from the prefab asset. Open the prefab to replace it.");
            }
        }

        /// <summary>The polygon, else any collider on the owner, else one from a previous Apply.</summary>
        static Collider2D FindSourceCollider(GameObject owner, Transform previousContainer)
        {
            var polygon = owner.GetComponent<PolygonCollider2D>();
            if (polygon != null) return polygon;

            var collider = owner.GetComponent<Collider2D>();
            if (collider != null) return collider;

            return previousContainer != null ? previousContainer.GetComponentInChildren<Collider2D>(true) : null;
        }

        static GameObject Host(GameObject container, string name, float2 center, float angleDegrees, out Vector2 offset)
        {
            if (math.abs(angleDegrees) < RotationEpsilonDegrees)
            {
                offset = center;
                return container;
            }

            var child = new GameObject(name) { layer = container.layer };
            child.transform.SetParent(container.transform, false);
            child.transform.localPosition = new Vector3(center.x, center.y, 0f);
            child.transform.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
            offset = Vector2.zero;
            return child;
        }

        static void Configure(Collider2D collider, bool isTrigger, PhysicsMaterial2D material)
        {
            collider.isTrigger = isTrigger;
            collider.sharedMaterial = material;
        }
    }
}

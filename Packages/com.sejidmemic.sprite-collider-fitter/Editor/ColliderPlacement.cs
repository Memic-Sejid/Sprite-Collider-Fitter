namespace SpriteFit.Editor
{
    /// <summary>Where Apply puts the colliders.</summary>
    public enum ColliderPlacement
    {
        /// <summary>
        /// Regular collider components under a "Colliders (generated)" child, with a rotated child per rotated
        /// box or capsule. Each collider can be edited by hand.
        /// </summary>
        Child = 0,

        /// <summary>
        /// One CustomCollider2D on the target itself, holding every shape at its exact position and angle.
        /// No child objects, but the shapes can't be edited in the Inspector. A CustomCollider2D only moves with
        /// its object when the same object has the Rigidbody2D, so a static one is added if there's none, and
        /// objects that belong to a parent's Rigidbody2D can't use this placement.
        /// </summary>
        Root = 1,
    }
}

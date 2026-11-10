# Changelog

## [0.1.0] - 2026-11-09

First version.

- Fit circles, rotated boxes and capsules to a sprite's alpha, including compressed, non-readable and atlas sprites.
- Compound mode: split into up to 16 primitives within an error tolerance, then merge touching ones.
- Choose which shapes are allowed.
- Scene view preview and error heatmap; stats for primitive count, overfill, underfill and the polygon being replaced.
- Apply and Revert, each one Undo step and prefab-safe.
- Apply to a whole selection, with a progress bar and cancel.
- Option to put all shapes in one `CustomCollider2D` on the object itself instead of a child.
- Option to create the colliders as triggers.
- The window reopens where it was last closed, at the same size.
- "Sample Sprites" sample, importable from the Package Manager: four sprites with polygon colliders in a scene.

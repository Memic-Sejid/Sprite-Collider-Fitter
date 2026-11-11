# Sprite Primitive Collider Fitter

A Unity editor tool that automatically fits a collider made up of circles, rotated boxes and capsules over your 2D sprites, within an error tolerance you set.

A polygon collider generated from a sprite is accurate but expensive. A handful of primitives is cheaper, but fitting them by hand is slow and imprecise. This tool reads the sprite's alpha, fits primitives, reports how much they overfill and underfill the sprite, and adds collider components automatically. It does not run any code in your game.

## Install

Unity 2022.3 or later. In **Window → Package Manager → + → Add package from git URL**, enter:

```
https://github.com/Memic-Sejid/Sprite-Collider-Fitter.git?path=/Packages/com.sejidmemic.sprite-collider-fitter
```

It pulls in Burst and Mathematics if your project doesn't have them.

To try it on known sprites first, open the package in **Package Manager**, expand **Samples** and click **Import** next to **Sample Sprites**. It adds a barrel, a shuriken, a spaceship and a sword, each with a `PolygonCollider2D`, and a scene with all four, under `Assets/Samples/`. Delete that folder when you're done.

## Use

1. Select a GameObject with a `SpriteRenderer` and open **Tools → Sprite Collider Fitter → Open Window**. The window reopens where you last closed it.
2. Pick the settings:
   - **Mode**: *Single* gives one primitive per separate part of the sprite. *Compound* splits it further.
   - **Max primitives**: 1 to 16, 4 by default.
   - **Error tolerance**: compound mode stops splitting once the weighted error is below this; 3% by default.
   - **Merge pass**: after splitting, joins touching primitives when that barely raises the error; on by default.
   - **Allowed shapes**: what shapes your colliders will be made of.
   - **Alpha threshold**: pixels at or above it count as solid; 0.5 by default.
   - **Overfill / underfill weights**: how much empty air inside a collider costs compared with sprite left outside one; 1 and 2 by default.
3. Check the preview. With **Show preview** on, the fitted shapes are drawn in the Scene view and follow the object's position, rotation, scale and flip. **Show error heatmap** tints overfill red and underfill blue. The stats show the primitive count, overfill, underfill and the polygon being replaced. If you change the sprite's image, click **Re-read sprite** to fit the new version.
4. **Apply**. The colliders go under a `Colliders (generated)` child:
   - circles, and boxes and capsules that aren't rotated, sit on that child;
   - each rotated box or capsule gets its own rotated child, because `BoxCollider2D` and `CapsuleCollider2D` can't rotate themselves;
   - trigger, physics material and layer are copied from the old collider;
   - the old `PolygonCollider2D` is disabled, or removed if **Old polygon collider** is set to *Remove*.

   One Ctrl+Z undoes it all. On prefab instances the changes are recorded as overrides. **Revert to polygon** puts the polygon back.

   **Create as trigger** (off by default, next to Apply) makes every generated collider a trigger. When it's off, the trigger setting is copied from the old collider.

   **Add colliders to root object** (off by default, next to Apply; while it's on, the window lists its limitations and what Apply will do to the selected object) instead puts every shape into one `CustomCollider2D` on the selected object, rotated boxes and capsules included, with no child objects. Physics cost is the same, but the shapes can't be edited in the Inspector. A `CustomCollider2D` places its shapes relative to its Rigidbody2D, not its own Transform, so it only moves with the object when that object has the Rigidbody2D. If the object has none, Apply adds a **Static** Rigidbody2D, which behaves like having no body. Objects that belong to a parent's Rigidbody2D can't use this option and need the regular child placement. The tool can't tell a `CustomCollider2D` it made from one you added, so it always asks before replacing or removing one.
5. **Apply to selection** does the same for every `SpriteRenderer` in the selected objects and their children, in one Undo step. A progress bar lets you cancel. Objects that share a sprite reuse its fit.

## Benchmark

1,000 dynamic bodies of the four sample sprites fall into a container: 250 each of a barrel, a shuriken, a spaceship and a sword. 600 physics steps are timed. Unity 2022.3.62f3 and Unity 6.6 (6000.6.8f1), Intel Core Ultra 7 265K; each figure is the mean of three runs. The table shows the average step, with the 95th percentile in brackets.

| Colliders | Physics shapes | Unity 2022.3 | Unity 6.6 |
| --- | ---: | ---: | ---: |
| `PolygonCollider2D` (sprite physics shape) | 11,471 (Unity 6.6: 11,276) | 20.8 ms (38.3 ms) | 13.1 ms (25.2 ms) |
| Fitted primitives, up to 4 (default) | 3,750 | 3.9 ms (5.8 ms) | 3.7 ms (5.5 ms) |
| Fitted primitives, up to 8 | 7,250 | 9.0 ms (15.3 ms) | 8.9 ms (15.3 ms) |
| One bounding box | 1,000 | 2.2 ms (2.7 ms) | 2.1 ms (2.5 ms) |

Accuracy is the same in both versions. It is overfill, the share of empty pixels a collider covers. Underfill was 0% for every variant except the spaceship's polygon (0.2%).

| Sprite | Polygon | Fitted, up to 4 | Fitted, up to 8 | Bounding box |
| --- | ---: | ---: | ---: | ---: |
| Barrel | 6.2% | 6.6% (4 boxes) | 4.0% (8 boxes) | 13.0% |
| Shuriken | 39.6% | 82.1% (4 boxes) | 55.8% (8 boxes) | 515.0% |
| Spaceship | 9.3% | 32.9% (4 boxes) | 23.0% (2 capsules, 6 boxes) | 149.5% |
| Sword | 77.2% | 21.3% (1 capsule, 2 boxes) | 13.0% (1 circle, 4 boxes) | 284.8% |

With the default 4 primitives, physics steps are about 5× faster than with Unity's generated polygons in Unity 2022.3, and about 3.5× faster in Unity 6.6, which steps polygons faster; with up to 8, about 2.3× and 1.5× faster. On solid shapes like the barrel and the sword the fit is about as tight as the polygon or tighter; Unity's generated outline for the sword, a simplified 16-vertex polygon, covers 77% extra. On thin, curved features, the shuriken's blades and the spaceship's wings, the fit leaves more empty space than the polygon, because every primitive fully encloses its pixels (see [Limitations](#limitations)).

The benchmark isn't part of the package. It's in the development project in the [GitHub repository](https://github.com/Memic-Sejid/Sprite-Collider-Fitter), under `Assets/SpriteFitBenchmark`: clone the repository, open it in Unity and run **Tools → Sprite Collider Fitter → Benchmark → Run**. It runs in a preview scene, so it doesn't touch your open scenes or physics settings.

## How it works

The pipeline: read a pixel mask, build hulls, fit primitives, score them, split until good enough, write components. The pixel grid is also the error grid.

### 1. Mask

The sprite's own mesh (`sprite.vertices`, `uv`, `triangles`) is drawn into a temporary render texture the size of `sprite.rect`, with a shader that writes alpha unchanged. The result is read back and thresholded. Going through the GPU handles compressed, non-readable, tightly packed and atlas sprites the same way. The mask is split into 8-connected islands, and each island gets at least one primitive.

### 2. Hull

Fits enclose the convex hull of each pixel's edge midpoints, built with Andrew's monotone chain. Only the leftmost and rightmost pixel of each row can contribute, so a 512 × 512 island needs at most six candidate points per row. Covering the edge midpoints covers every pixel centre, and on horizontal and vertical edges it reaches the pixel's outer edge, so a solid 64 × 32 sprite gets exactly a 64 × 32 box.

### 3. Fitting

- **Circle**: Welzl's minimum enclosing circle, expected O(h) on h hull points.
- **Box**: rotating calipers. By the Freeman–Shapira theorem, the minimum-area enclosing rectangle has a side on a hull edge, so one pass over the edges finds it in O(h).
- **Capsule**: the axis follows the principal direction of the pixel covariance:

  ```math
  \theta = \tfrac{1}{2}\operatorname{atan2}\left(2C_{xy},\; C_{xx} - C_{yy}\right)
  ```

  The axis is shifted sideways to the middle of the hull's extent across it, which minimises the smallest usable radius. Each hull point then has a position $t_i$ along the axis and a distance $d_i$ from it.

  **Deriving the segment.** A capsule is a segment $[a, b]$ on the axis, swept by a radius $r \ge \max_i |d_i|$. A point beyond the segment's top end, with $t > b$, is inside when $(t - b)^2 + d^2 \le r^2$, that is when $b \ge t - \sqrt{r^2 - d^2}$. A point with $t < a$ is inside when $a \le t + \sqrt{r^2 - d^2}$. A point with $a \le t \le b$ is inside because $|d| \le r$. So for a given $r$, the shortest segment that covers every point has

  ```math
  b = \max_i \left(t_i - \sqrt{r^2 - d_i^2}\right), \qquad a = \min_i \left(t_i + \sqrt{r^2 - d_i^2}\right)
  ```

  and length $L = \max(0, b - a)$. If $a > b$, any centre between them works and the capsule is a circle. The area $2rL + \pi r^2$ trades radius against length. A golden-section search over $r$ minimises it, between the smallest radius $r_\min$ and the radius at which a circle on the middle of the axis already covers everything, $\sqrt{(\Delta t / 2)^2 + r_\min^2}$.

Every fit encloses its pixels, so underfill is zero by construction. When several shapes are allowed, the one with the least area wins, and a cheaper shape wins near-ties (circle, then capsule, then box).

### 4. Error

With $A$ the number of opaque pixels:

```math
E = w_o \frac{N_\text{over}}{A} + w_u \frac{N_\text{under}}{A}
```

$N_\text{over}$ counts transparent pixels inside any primitive, including any beyond the sprite's edges. $N_\text{under}$ counts opaque pixels inside none. Every pixel centre is tested against the primitives' signed distance functions in a Burst job, which also produces the heatmap.

### 5. Decomposition

1. Start with one cluster per island.
2. Repeatedly split the cluster with the most estimated overfill.
3. Cuts are tried perpendicular to the x axis, the y axis, and the cluster's principal and secondary axes, at every pixel step (up to 256 positions per direction).
   - Hulls and moments of both sides are built incrementally as the cut sweeps, so each position costs about two fits.
   - While searching, a side's error is estimated as its primitive's area minus its pixel count.
4. Concave shapes can get worse before they get better: two halves of a ring need more area than one circle. When no cut helps, the cluster is split through its middle by pixel count instead, and the best configuration seen is returned. More primitives never makes the result worse.
5. Splitting stops at the tolerance or the primitive limit.
6. A merge pass then joins touching primitives of the same island when their union's error is less than 10% above the pair's. A merge is kept only if the total error stays within tolerance or doesn't rise.

### 6. Writing

`BoxCollider2D` has no rotation and `CapsuleCollider2D` only points along x or y, so rotated shapes get rotated child objects. Each capsule is turned to whichever of the two directions is nearer its axis. Everything is created, then registered with Undo once. The old polygon is recorded with `Undo.RecordObject` and `PrefabUtility.RecordPrefabInstancePropertyModifications`. The generated colliders are recognised by the child's name, so no marker component ends up in builds.

## Assemblies

| Assembly | Contents |
| --- | --- |
| `SpriteFit.Core` | Geometry, fitting, decomposition and the Burst error job. No `UnityEditor` reference, but compiled for the Editor only, so nothing ships in player builds. |
| `SpriteFit.Editor` | Mask reading, the window, the Scene view preview and heatmap, collider writing, batch apply. |
| `SpriteFit.Tests` | EditMode tests: synthetic sprites with known answers, plus brute-force reference checks for the hull, Welzl and calipers. |

To add a shape, implement `IPrimitiveFitter`.

## Limitations

- **Every fit fully encloses its pixels.** On sprites with thin diagonal or hollow features, like the shuriken, the default 3% tolerance usually can't be met within 16 primitives. A "shrink" pass that trades a little underfill for less overfill is planned.
- **Not supported yet**: convex polygon primitives, per-frame colliders for animated sprites, tilemaps, and `CompositeCollider2D` output.
- **Sliced and tiled sprites**: `SpriteRenderer` draw modes other than Simple are fitted as the plain sprite.
- **Graphics device**: reading a sprite needs one. Under `-nographics` (most CI runners) the sprite-reading tests report as ignored.
- **Batch apply** works on objects in open scenes and prefabs open in Prefab Mode, not on prefab assets selected in the Project window.

## License

MIT. See [LICENSE.md](LICENSE.md).

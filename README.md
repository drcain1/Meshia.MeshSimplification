# Meshia Mesh Simplification


- [English](#english)
- [日本語](#日本語)

[Documents](https://drcain1.github.io/Meshia.MeshSimplification/)

## English
Mesh simplification tool/library for Unity, VRChat.

Based on Unity Job System, and Burst.
Provides fast, asynchronous mesh simplification.

Can be executed at runtime or in the editor.

### Installation

### VCC / ALCOM

Add [the drcain fork VPM repository](https://drcain1.github.io/Meshia.MeshSimplification/vpm/index.json) and install **Meshia — drcain fork**. The package ID is `io.github.drcain1.meshia.mesh-simplification`. It replaces upstream Meshia; install only one variant per project. See [migration and distribution details](docs/FORK_DISTRIBUTION.md).

Install the localization dependency `com.anatawa12.custom-localization-for-editor-extension` 1.x (at least 1.2.1) from [anatawa12's VPM repository](https://vpm.anatawa12.com/vpm.json). Avatar integration also requires NDMF; the cascading component requires Modular Avatar. Both are available from [nadena's VPM repository](https://vpm.nadena.dev/vpm.json).

For Git installation through Unity Package Manager, install these dependencies first; UPM does not resolve `vpmDependencies` automatically.

### How to use

#### NDMF integration

Attach `MeshiaMeshSimplifier` to your models.

You can preview the result in EditMode.

#### Cascading avatar simplifier

Add `Meshia Cascading Avatar Mesh Simplifier` to a child object beneath the avatar. It assigns a shared triangle budget across the avatar's eligible renderers. Use **Adjust** to distribute the current target manually, or enable **Auto Adjust** to update renderer targets automatically.

For skinned meshes, the options include an experimental **Protect joint deformation** setting. The core API keeps it disabled by default. New NDMF components also start with deformation protection off. When explicitly enabled, **Automatically protect deforming meshes** works per mesh: each mesh with positive weights on multiple valid bone transforms is protected independently, including clothing, separate limbs, and custom rigs. Rigid one-bone meshes do not need this guard. Saved legacy Auto settings keep the earlier single-body detection; the inspector identifies that mode. Explicit On/Off settings are retained. When enabled, the native Blender-style and FA-QEM candidate jobs compare endpoint skin-weight variation and the simulated discarded influence weight before accepting a collapse. `Strength` controls the graduated cost, `Max Weight Distance` limits endpoint total variation, and `Max Discarded Weight` limits influence loss caused by the fixed output influence width. The protection can reduce the achievable triangle count; the settings do not provide a pose-equivalence guarantee and should be evaluated with representative joint poses.

Each renderer's cogwheel provides an algorithm selector:

- **FA-QEM** — the default for new cascading mesh entries. Uses source geometry, boundary curvature, original normals, and a separate boundary-area penalty to rank edge collapses. Attribute and topology constraints can stop reduction above the requested count.
- **Blender Decimate** — a manually selectable alternative using the Blender-compatible collapse implementation to reach the renderer's allocated triangle count.[^blender]
- **Meshia** — uses Meshia's original simplification algorithm and exposes its existing preservation and interpolation options.
- **UV Loop Dissolve** — reconstructs quad-like topology and removes safe complete or partial edge-loop segments. UV seams, material boundaries, hard edges, borders, and non-manifold areas are protected. If loop removal cannot reach the target, Blender Decimate finishes the operation and the preview displays a fallback notification.

The inspector shows only settings used by the selected algorithm. Blender Decimate exposes bone-weight protection; UV Loop Dissolve exposes it for its Blender fallback only (loop removal has its own built-in guards). Meshia shows its original geometry options, and FA-QEM shows its border, deformation, and FA-QEM controls. Hidden settings are retained when you switch algorithms.

Existing entries retain their saved algorithms and options. FA-QEM does not automatically switch algorithms when its safety constraints prevent reaching a target; adjust the budget or choose an alternative per mesh. New standalone `MeshiaMeshSimplifier` components also select FA-QEM, initially targeting half the source triangles. Existing components and explicit C# targets are unchanged.

The **Algorithm for All Meshes** dropdown at the top of the inspector applies an algorithm to every mesh entry, including disabled entries. It displays **Mixed** when entries use different algorithms. Individual triangle targets, enabled states, and options are retained. Use Unity's **Edit → Undo** (Ctrl+Z on Windows) to undo the whole batch change. You can still override individual algorithms through each renderer's cogwheel.

Use **Preview UVs** inside a renderer's cogwheel to compare the original and simplified UV layouts. The preview updates as triangle targets and simplification settings change.

#### Geometry-only FA-QEM

FA-QEM reduces a mesh toward an absolute triangle budget using source surface planes, boundary curvature, original-normal tangent planes, and a separate boundary swept-area penalty to rank edge collapses. Select **FA-QEM** in a renderer's cogwheel, or use **Algorithm for All Meshes** at the top of the cascading inspector.

##### Initial settings and protection presets

New entries whose renderer GameObject is named **Body** (case-insensitive) start **excluded from simplification**, because many avatars use that name for the face. This is a naming convention, not face detection: `Body_base` is not excluded by this rule, and differently named faces must be excluded manually. You can explicitly enable a `Body` entry if desired. Refreshing entries preserves existing choices; the allocation **Reset** button restores this name-based default.

Each mesh row has a **protection icon beside the lock and cog**. Click to cycle **green → yellow → gray → green**. Green enables geometry and deformation protection; yellow keeps geometry protection only (or indicates a partial custom selection); gray means **No protection**. Gray bypasses boundary and selected-bone locks, coincident seams, FA-QEM material-boundary and joint guards, bone-weight limits, surface-deviation limits, and face-flip checks. It can create holes, distort shapes or textures, and damage deformation. Basic connectivity and finite-number checks remain, so an exact requested count is not guaranteed. UV Loop Dissolve uses its Blender fallback directly in this state. Saved geometry options and selected bones are retained, with their controls disabled while bypassed. Returning to green restores those settings and explicitly enables deformation protection. Undo restores each change. The control works on skinned and static meshes; excluded meshes are disabled. After changing protection, Analyze Build refreshes the measurements before further output edits. Auto Adjust never changes this state, and applying a protection preset clears the bypass. Existing entries retain their saved protections; the formerly dim deformation-off state now appears yellow when geometry protections remain.

New avatar entries and standalone components start with **deformation protection off** (`MeshSimplifierOptions.AvatarInitial`). Border and seam preservation remain on, and maximum surface deviation remains **0.0005** (0.05% of each mesh's bounds diagonal). Choose a protection preset explicitly, or use each row's protection icon.

- **Conservative** applies the previous full protection preset to every entry: per-mesh automatic bone-weight protection, strength **2**, maximum weight distance **0.10**, maximum discarded weight **0.02**, and joint-transition preservation with the default limb/hand selection.
- **Aggressive** applies that same protection to detected hand/finger and hair meshes, plus meshes named `Body` or `Face`. It switches bone-weight and joint-transition protection off on the other meshes. Border, seam, and surface settings remain conservative; it does not promise to reach the requested triangle budget.

Detection uses positive bone weights, humanoid hand/finger mappings, matching bone names for separate clothing rigs, and common English/Japanese hand and hair names. Unused bone slots do not count. Detection is approximate; unusual names can be missed. Missing/unreadable meshes or malformed weights keep conservative protection. This is a one-time, whole-mesh selection: if the body includes the hands, the whole body keeps the conservative preset. Clothing, elbows, and knees can still need additional protection. Review the row icons, check animated poses, and reapply the preset if the mesh/rig changes.

Both buttons preserve algorithms, targets, exclusions, and fixed allocations, and support Unity Undo. They do not enable an excluded face. Existing saved configurations are not changed automatically. **Reset Options** explicitly applies conservative options to one mesh while retaining its separate joint-bone selection. Core `MeshSimplifierOptions.Default` remains unchanged for API and serialization compatibility. NDMF resolves the automatic policy; direct API callers must resolve `SkinningProtection` themselves or explicitly enable it.

**Protection takes priority over reaching 70,000 triangles.** Auto Adjust allocates targets; it never relaxes safeguards or guarantees a reachable budget. The inspector lists meshes whose last preview exceeded their allocation, and build warnings report actual FA-QEM output versus requested counts. Preview counts may be stale: use **Analyze NDMF Build** to verify the complete result. A high protected count calls for revised allocations or explicit quality tradeoffs, not automatic removal of protection. Check representative poses, expressions, and layered clothing before upload.

##### Understanding the budget and reducing more

**Auto Adjust redistributes allocation in both directions.** Lowering a mesh slider or number field returns allocation to the other unlocked meshes; raising it takes allocation from them. The edited mesh, locked entries, and excluded meshes are preserved during redistribution, and recipients cannot exceed their source counts. Slider edits retain the measured build correction instead of creating a new allowance. Undo restores the edit and redistribution together. Requested allocation and actual output can differ because of geometry protection, so the asynchronous estimate uses measured mesh responses and **Analyze Build** verifies the final count. **Adjust** uses the requested triangle count minus any visible **allocation allowance** (or plus a measured **build allocation boost**), using mesh allocations plus the source counts of excluded meshes. Allocation **Reset** clears that allowance and returns to the full requested budget. AAO/preview estimates alone never change the allocation budget. Locked or excluded geometry can still make the budget unreachable. **Adjust** redistributes the budget manually across unlocked meshes. Locks and exclusions are respected. Border, seam, and surface-deviation guards still apply when deformation protection is off, so requested allocations can remain below the actual output.

The **Triangle budget** summary starts open and shows the measured avatar total from a current successful build. Mesh rows show **Output / Original**: the measured triangle count produced by Meshia, before later tools such as AAO run, followed by the original mesh count. The internal simplifier request is only shown under **Calculation details** in the row's popup. A mesh producing more than that internal request is not a warning.

Run **Analyze Build** before editing output counts. Dragging a slider or entering a number requests that output; Meshia asynchronously searches up to eight candidate requests and displays the achieved output. Enter commits number-field edits. While measuring, a blue **…** marks the pending value. If the requested change cannot be achieved, the field returns to the measured output and an amber **!** briefly explains the difference. A reduction never removes more triangles than requested; an increase never adds more than requested. Auto Adjust still redistributes the underlying allocations among other unlocked meshes after a successful edit. Superseded edits are discarded and Undo restores the edit and redistribution together. No protection is relaxed automatically.

Before the first measurement, output is shown as **—**. Stale output remains visible with a gray **?**; it is never replaced by the internal request. Changed protection, mesh inputs, or lost input caches require **Analyze Build** before output editing resumes. After edits, the avatar summary estimates the total from measured changes in all affected meshes. Later build tools may respond differently, so a full analysis verifies the final total.

Click a row's marker or **/** to open its fine-tuning panel. **Reduce output by** previews a measured saving, and **Apply reduction** retains that saving instead of redistributing it. The popup's **Calculation details** contains the internal target. Rows retain their original height and alignment; current avatar overruns are shown at the top.

**Requested Triangle Budget** is an allocation goal, not a hard limit. The inspector shows the current successful NDMF analysis and how many triangles are over budget or remain available. Old results are marked out of date; failed analyses are not treated as confirmed counts. **Find ways to reduce** opens a breakdown sorted by output count, with separate groups for excluded and unmeasured meshes. Preview counts and stale measurements are labeled explicitly. **Open mesh settings** jumps to the corresponding entry. Preview/source figures are not final per-mesh build counts, and missing previews are not presented as measured savings. The breakdown remains available after analysis, while a current successful build supersedes the preview notice.

Start with geometry that is always hidden by clothing before reducing visible surfaces more aggressively. [AAO Remove Mesh By BlendShape](https://vpm.anatawa12.com/avatar-optimizer/en/docs/reference/remove-mesh-by-blendshape/) can remove body polygons selected by a suitable hiding blendshape. [Modular Avatar Shape Changer](https://modular-avatar.nadena.dev/docs/reference/reaction/shape-changer) can do this in **Delete** mode. Merely shrinking a body part or hiding it with a shader does not remove its polygons. Animated/toggleable Shape Changer deletion hides geometry but does not reduce the reported polygon count; retain body regions needed by any outfit toggle. Review the removal in different poses and outfits, then analyze the complete build again. These tools are optional; the guide does not add components or dependencies automatically.

Automatic fitting spreads measured reductions across responsive unlocked meshes in proportion to their starting allocations. Within one Analyze Build run, it preserves at least 75% of each mesh's starting target and measured output, including across verification builds. This is a limit on automatic changes, not a guarantee of visual quality. If the budget cannot be reached within those limits, the starting allocations are restored. Larger reductions remain an explicit per-mesh choice.

For remaining overruns, review the largest entries first: enable simplification on suitable excluded meshes, reconsider unused accessories and fixed allocations, or explicitly adjust an individual mesh's protection while checking its animated appearance. **Auto Adjust changes targets**; it uses consecutive full-build measurements to estimate savings, and automatically verifies corrections within a four-build limit; it never relaxes protection. A target below the result is not proof that further reduction is safe.

##### Supported features

- **Original materials and textures:** keeps the existing material assignments and texture assets. There is no texture baking, atlas generation, UV repacking, or texture compression pass. Shader features and material animation continue to use the original materials.
- **Mesh attributes:** carries UV0–UV7, normals, tangents, vertex colors, skin weights, and blend-shape frames through the shared mesh simplification pipeline. Attributes at collapsed vertices are interpolated; retaining these channels does not mean their values or appearance remain identical. The C# option `UseBarycentricCoordinateInterpolation` selects barycentric interpolation for appearance attributes and blend shapes instead of the default edge interpolation; skin weights still use endpoint interpolation.
- **Split-vertex seam protection:** **Lock Coincident Split Vertices** is on by default. It locks coincident vertex records within a scale-dependent tolerance, including split records whose attributes match, so independently simplified face groups do not pull those copies apart. This also protects coincident splits used for UV seams and hard normals. It does not weld or repair the source mesh.
- **Material boundaries:** rejects collapses between vertices with different submesh memberships.
- **Topology and face checks:** checks the local edge-collapse link condition and rejects unsafe non-manifold configurations, degenerate surviving faces, and excessive face-normal changes. These are local safeguards, not a global self-intersection or mesh-repair guarantee.
- **Flat faces in animated meshes:** source triangles with distinct vertex indices can have zero area at rest and open under animation. On meshes with blend shapes or skin weights, FA-QEM retains these faces and locks their vertices, even if border or seam protection is disabled. Static zero-area faces can still be removed. This conservative protection may limit reduction near those faces.
- **Optional border locking:** **Preserve Border Edges** fixes vertices on topological open boundaries. Selected-bone border protection is also supported for skinned meshes when only specific regions need protection.
- **Experimental skinning protection:** **Protect joint deformation** adds a skin-weight cost and rejection limits for endpoint weight differences and discarded influences. The core API defaults to off; new NDMF entries start off, with Auto available through the presets described above. Check representative poses and blend shapes after reduction.
- **Editor and build integration:** supports per-renderer budgets, cascading budget allocation, NDMF previews/builds, and the original/simplified **Preview UVs** overlay.
- **Runtime and diagnostic APIs:** supports synchronous, asynchronous, and batch simplification, result reports, and optional collapse history with source-index mappings.

##### Border preservation defaults to on

**Preserve Border Edges defaults to on for new mesh entries and `MeshSimplifierOptions.Default`.** It can be changed per mesh in the renderer's cogwheel. Leave it enabled when open edges, such as necklines, hems, or other openings, must retain their shape. FA-QEM then rejects collapses involving those boundary vertices, keeping their positions and UVs fixed. A topological boundary is an edge used by only one triangle; this option does not freeze every camera-visible silhouette on a closed surface.

Changing algorithms, including through **Algorithm for All Meshes**, retains each entry's border setting. **Reset Options** restores the default and turns border preservation on. Existing saved entries keep their stored values, including an explicit off value; they are not automatically migrated. This is the shared options default, including for direct C# calls; disable it explicitly when unrestricted boundary reduction is intended.

**Boundary Weight** and **Swept Area Weight** discourage boundary movement but do not lock borders. Likewise, **Lock Coincident Split Vertices** protects coincident split vertices, not every open edge. Keep **Preserve Border Edges** enabled when exact open-boundary retention is required, and check this setting on older entries. Border, seam, and skinning protection can leave fewer legal collapses and prevent reaching an aggressive triangle target.

##### Preserving finger and joint shape

FA-QEM offers **Preserve Vertices Near Joints** for joints that lose their shape when bent. It keeps the original vertices where the strongest bone influence changes, together with one neighboring ring. The rest of the mesh can still simplify; this does not preserve the entire hand or finger.

The option starts **off on new avatars** and in the core API. The Conservative preset enables it on every entry; Aggressive enables it on the meshes it selects. It is independent of **Protect joint deformation** and its automatic/manual policy. In a cascading avatar entry, enable it under **FA-QEM Options**, then choose **Joint Protection Bones** below the options. New cascading entries select both hands and all fingers, upper/lower arms, upper/lower legs, and feet. Once enabled, this protects transitions around wrists, elbows, knees, and ankles where the renderer uses those humanoid bones. Saved selections are not expanded automatically. This is a separate selection from **Preserve Border Edges Bones**: it protects interior joint geometry as well as open surfaces. Clearing the selection disables joint-transition protection for that entry; missing humanoid bones are ignored. Existing border selections retain their meaning. Standalone simplifiers apply this option to all bone transitions. The core API can narrow it with the nonserialized mesh-bone indices in `SkinningProtection.JointProtectionBoneIndices` (up to its fixed-list capacity).

The guard preserves source joint/support vertices and their attributes. It may stop above the triangle target, and it does not test every pose, prevent body/clothing intersections, or guarantee unchanged geometry away from the protected rings. Compare open, partly curled, and closed hands before accepting the result, then verify the complete built-avatar count. It currently applies only to FA-QEM.

##### Hair protection and further reduction

**There is no automatic hair preset or name-based hair detection.** Hair uses the same FA-QEM safeguards as other meshes. Thin hair cards and individual strands often have many open edges and coincident split vertices, including UV and normal seams. **Preserve Border Edges** and **Lock Coincident Split Vertices** are on by default and can lock much of this geometry. Lowering the triangle target alone does not override these locks, so hair may stop well above its target.

Start with the defaults and give visually important hair a larger budget. To explore further reduction, use **that hair renderer's cogwheel** on a copy and change one option at a time:

| Option | How to reduce protection | Tradeoff |
| --- | --- | --- |
| **Preserve Border Edges** | Turn it off to allow open-boundary vertices to move or collapse. Check **Preserve Border Edges Bones** too: selected bones can keep associated boundary vertices locked even with the main toggle off. | Strand tips and card outlines can change; gaps may appear between strands. |
| **Lock Coincident Split Vertices** | Turn it off to release coincident split-vertex locks. This does not weld the copies together, and border protection may still lock the same vertices. | Copies can move apart, causing cracks, UV discontinuities, or shading changes. |
| **Maximum Surface Deviation** (`MaxSurfaceDeviation`) | If enabled, increase the positive tolerance or set it to `0` to disable the guard. New avatars and the core API default to `0.0005`. | More surface drift and possible intersections with the head or clothing. |
| **Minimum Face Normal Dot** (`MinNormalDot`) | Lower the FA-QEM value within `0`–`1` to allow larger face rotations per collapse. | Sharper folds and changed shading; other topology checks still apply. |
| **Protect joint deformation** | If active for this mesh, increase **Max Weight Distance** / **Max Discarded Weight**, or turn protection off. Turn off **Automatically protect deforming meshes** first if Auto is selected. Lowering **Strength** only reduces the cost penalty, not the rejection limits. | Check hair-bone motion and blend shapes. Per-mesh Auto protects weighted hair using multiple bones too; it does not identify hair by name or test hair physics. |

**Boundary Weight**, **Normal Weight**, and **Swept Area Weight** change collapse ranking; they do not release border or seam locks, and lowering them does not guarantee further reduction. **Smart Link** does not weld disconnected hair pieces for FA-QEM.

**Turning off border and seam protection still leaves other guards active.** These include material-membership checks and protection of distinct-index source faces that are flat at rest on meshes with skin weights or blend shapes. Those flat faces can open during animation, so FA-QEM retains them and locks their vertices. Gray **No protection** bypasses these guards too; basic connectivity and finite-number checks remain.

Compare the original and simplified hair from the front, back, and side, including strand tips and overlaps. Then test hair motion, expressions, and clothing toggles in Play Mode or VRChat. **Preview UVs** helps inspect UV changes but cannot validate animation. Verify the final budget with **Analyze NDMF Build** or the built avatar. If cracks or silhouette loss appear, restore the option and recover triangles from another mesh. These protections reduce risk; they do not guarantee hole-free hair in every pose.

##### Troubleshooting holes or distorted shapes

If a mesh develops holes, sharp dents, or stretched triangles after FA-QEM simplification:

1. Click the mesh entry to select the affected object, then open its **cog**. Make sure its protection icon is not gray (**No protection**), which bypasses the settings below.
2. In **FA-QEM settings**, check **Maximum Surface Deviation**. **`0` disables this protection.** If it is disabled, try `0.0005` as a starting point. If it is already enabled, try a smaller positive value to keep the result closer to the original surface.
3. Adjust in small steps and compare the affected area. For example, if `0.002` damages the shape, try `0.0019`, then lower it further if needed. These are examples, not universal settings: the value is relative to each mesh's size. A smaller positive value gives tighter protection; a larger value allows more movement and may permit more reduction.
4. If the problem appears when the mesh moves, also enable **bone-weight protection**. A **Maximum Skin Weight Distance** of `0.25` is a starting point; lower it if deformation still breaks. Check moving poses, especially for tails, hair, shoes, and joints.

Tighter protection can retain more triangles than requested. Give that mesh more of the budget and reduce another mesh if needed, then run **Analyze Build** again to verify the avatar total. Surface deviation limits shape changes; it does not repair existing holes or guarantee separation between clothing layers. If gaps remain, check **Preserve Border Edges** and **Lock Coincident Split Vertices**, compare against the original mesh, and inspect overlapping layers in Play Mode.

##### FA-QEM controls

The labels below match the Unity inspector. C# property names are included for API users. FA-QEM defaults come from `FaQemOptions.Default`; avatar-specific overrides and joint protection are noted separately:

| Inspector label | C# property | Default | Effect |
| --- | --- | --- | --- |
| Preserve Vertices Near Joints | `options.SkinningProtection.PreserveJointTransitions` | Off for new entries; enabled by applicable protection presets | Retains vertices at dominant-bone transitions and one neighboring ring. Separate from coincident split-vertex protection. |
| Plane Area Divisor | `options.FaQem.PlaneAreaWeight` | `1` | With inverse area weighting on, divides the source-plane weight by this value times triangle area. With it off, this value is the source-plane weight directly. Must be positive. |
| Boundary Weight | `options.FaQem.BoundaryWeight` | `500` | Strength of source boundary-curvature constraints. A soft penalty, not a border lock. |
| Normal Weight | `options.FaQem.NormalWeight` | `0.01` | Strength of tangent-plane constraints from original normals, with geometric normal fallback where needed. |
| Swept Area Weight | `options.FaQem.AreaWeight` | `100` | Strength of the separate boundary swept-area penalty used to rank collapses. |
| Use Inverse Area Weighting | `options.FaQem.UseInverseAreaWeighting` | On | Gives smaller source triangles greater plane weight. |
| Lock Coincident Split Vertices | `options.FaQem.PreserveAttributeSeams` | On | Locks coincident split vertex records, including those with matching attributes. |
| Minimum Face Normal Dot | `options.FaQem.MinNormalDot` | `0.2` | Minimum dot product between a surviving face's normals before and after each collapse. Larger values reject more changes. Range: `0`–`1`. |
| Maximum Surface Deviation | `options.FaQem.MaxSurfaceDeviation` | `0.0005` | Optional limit on sampled distance from the original surface, expressed as a fraction of the source bounds diagonal. `0.001` means 0.1%. Tighter values may stop above the requested triangle count. |

FA-QEM uses its own **Minimum Face Normal Dot** and feature weights. The legacy Meshia **Preserve Surface Curvature** and **Smart Link** controls do not configure FA-QEM's collapse metric or connect disconnected components.

With **Preserve Border Edges** enabled, collapses touching boundary vertices are rejected. The boundary-area penalty is therefore zero for accepted interior collapses, and source boundary quadrics stay attached to the locked vertices. Increasing **Swept Area Weight** or **Boundary Weight** does not protect the interior of close-fitting clothing under this policy.

**Maximum Surface Deviation** adds a sampled, one-sided envelope around the immutable original mesh. It checks the proposed vertex, surviving triangle edge midpoints, and triangle centroids. If the optimal position fails, FA-QEM tries the endpoints and midpoint and queues a valid alternative at its actual cost. The original surface is indexed once per simplification. This limits accumulated surface drift; it does not guarantee clearance from another mesh, continuous containment between samples, or preservation under every animated pose. Nearby source layers can also satisfy a nearest-surface test. New avatars and the core API enable it at `0.0005` (0.05% of the mesh bounds diagonal). Existing saved values, including an explicit `0`, are preserved. The slider covers `0–0.005` in increments of `0.00001`; the number field accepts the full `0–0.1` range without changing saved values when the inspector opens. Increasing the tolerance or disabling it is an explicit quality/budget tradeoff.

For layered clothing, inspect both sides of an overlap: an inner layer moving outward can poke through an outer layer whose own simplification is acceptable. Apply appropriate surface protection to both layers and compare against the unsimplified outfit in Play Mode, including its visibility toggles and animations. Use the actual built triangle count when checking an avatar budget; a protected mesh can stop above its requested target, and other avatar build steps can change the count.

NDMF preview and build use the same per-mesh detection for new Auto settings and retain the same single-body selection for saved legacy Auto settings. Auto preview invalidates when its rig, candidate renderers, meshes, or simplifier configuration changes. Preview still covers only participating preview passes; source geometry and final counts can differ from a complete build, so use **Analyze NDMF Build** or the actual built avatar for the final budget.

All geometric terms and this tolerance use coordinates normalized by the source bounds diagonal. The mixed error terms scale differently, so these weights describe this normalized implementation; equivalence to unnormalized paper weights is not assumed. Edges shorter than `1e-8` of that diagonal are rejected even when seam protection is disabled. Coincident split records are locked when **Lock Coincident Split Vertices** is enabled; no automatic welding or virtual edges are introduced.

##### C# usage and diagnostics

Start from `MeshSimplifierOptions.Default` and modify its initialized `FaQem` settings. This preserves intentional zero weights; an uninitialized `default(FaQemOptions)` resolves to the built-in defaults for compatibility with older serialized data.

```csharp
using Meshia.MeshSimplification;
using UnityEngine;

var options = MeshSimplifierOptions.Default;
options.PreserveBorderEdges = true; // Also the default; shown explicitly for clarity.

var target = new MeshSimplificationTarget
{
    Kind = MeshSimplificationTargetKind.FaQemTriangleCount,
    Value = 8350,
};

var simplifiedMesh = new Mesh();
var report = MeshSimplifier.SimplifyWithReport(
    originalMesh, target, options, simplifiedMesh);

Debug.Log($"Triangles: {report.InputTriangleCount} → {report.OutputTriangleCount}; " +
          $"result: {report.FaQemTermination}");
```

The same target can be passed to `Simplify`, `SimplifyAsync`, and `SimplifyBatch`. `SimplifyWithReport` exposes input, requested, and output counts, plus `TargetReached` or `ConstraintsExhausted`. A collapse can remove multiple triangles, so the output may be slightly below the requested count; constraints can also stop it above the target. FA-QEM does not fall back to another algorithm to force the budget.

`SimplifyWithHistory` additionally returns accepted collapses, affected faces, and output-to-source vertex and triangle mappings for geometry diagnostics. A source index identifies provenance; an output vertex may have moved or had its attributes interpolated. Use a separate destination mesh to keep the source intact, and release generated meshes when they are no longer needed.

##### Scope and limitations

The geometry implementation is based on [Fast and Robust Mesh Simplification for Generated and Real-World 3D Assets](https://arxiv.org/html/2605.14029v1). This is not a verified reproduction of the paper's published quality and performance results. Disconnected-component virtual edges are not enabled.

Keeping source textures avoids a texture-resampling pass, but geometry reduction can still change silhouettes, UV interpolation, shading, skinning, and blend-shape deformation. Compare the UV overlay and the rendered result at the intended budget. Open-border locking and split-seam protection address different failure modes and do not guarantee an identical rendered result.

When migrating from the experimental baking branch, restore any persistent bakes there before switching packages. Removing the baker does not reconstruct original assets from an already baked mesh. Old serialized atlas options are ignored by this branch.

#### Build-aware triangle budgeting

A spinner and **Calculating...** beside **Triangle budget** show when edits are waiting or mesh counts are being updated. The indicator disappears when those calculations finish and remains visible when the section is collapsed.

FA-QEM records the triangle counts along its normal collapse sequence during analysis. Later edits reuse those counts when the captured mesh and resolved protection settings are still valid. If a lower request is outside the recorded range, one asynchronous count-only run measures the rest of the sequence without constructing a temporary output mesh. If FA-QEM has already stopped above its request, smaller requests can reuse that result immediately. Other algorithms retain their existing measured-trial path. Changing the inputs or protections invalidates these measurements.

With reusable measurements available, **Analyze Build** can prepare an Auto Adjust correction before its first verification build, using the last verified avatar count plus measured mesh changes. This avoids an extra full build just to rediscover the starting discrepancy. The projected total is never marked verified: a complete NDMF build still checks the final count, and failed fitting restores the starting allocations. The first analysis still runs the full pipeline; plugins after Meshia may change the count or require additional verification.

When two verified builds differ in only one mesh's allocation/output, the fitter can learn how much that mesh changes the final avatar count. This bounded estimate helps avoid repeatedly adding too few triangles when later tools remove part of a mesh. It is discarded when inputs or protection settings change; ambiguous multi-mesh changes do not establish a per-mesh response. Every proposed allocation still needs full-build verification, and existing reduction limits and rollback rules still apply.

The cascading inspector distinguishes Meshia's output from the estimated final avatar count after downstream NDMF tools:

- **Meshia output** reports triangles before downstream processing.
- **AAO estimate** accounts for meshes and polygons expected to be removed by Avatar Optimizer when its compatible API is available.
- **Analyze NDMF Build** runs a temporary full NDMF build and records the exact final triangle count.

With **Auto Adjust** enabled, one **Analyze Build** click runs up to four complete builds. Between builds, a measured per-mesh fitter tries bounded target changes on copies of the exact meshes reaching Meshia, with the same resolved protection options. It changes only enabled, unlocked meshes whose trials demonstrate useful output changes. Flat or reversed responses do not earn budget, and cached trials avoid repeating identical work. Large responses are refined with at most three additional probes per mesh. Both reductions and increases are supported; increases cannot exceed the captured input triangle count. The combined proposal is always verified with a complete build. This may take longer than a single analysis. With Auto Adjust off, analysis measures once without editing allocations. Results below the requested final budget and within 0.1% of it (at least one triangle; 70 at a 70,000 goal) are accepted. Corrections in either direction aim halfway into that margin. The fitter does not relax geometry protection or treat an observed plateau as a proven minimum.

The requested final budget, exclusions, locks, and geometry protections remain unchanged. The inspector shows either an **allocation allowance** or a **build allocation boost**; **Adjust** and future Auto Adjust operations retain that correction. All corrections from one click can be undone together. **Reset build correction**, beside the correction value under **Calculation details**, clears either kind of correction without immediately changing allocations; allocation **Reset** clears it while resetting allocations. The final allowed pass measures without another correction, so the result matches the current allocations. Progress shows the build number, and cancellation is checked between builds and individual mesh probes. Individual NDMF builds run synchronously and cannot be interrupted by this control. The operation stops when it reaches the target range, stops improving, cannot adjust further, encounters a build failure, is cancelled, or reaches four builds. The inspector reports the reason. A failed or interrupted operation never claims an unverified result is current. Corrections are committed only after a build reaches the target range. Every other stop, including the limit, cancellation, or failure, restores all allocations and the allowance from before the click. The displayed baseline measurement is restored with those settings. Repeated unsuccessful clicks therefore cannot accumulate increasingly aggressive allocations. Failed analyses and changes to captured inputs or protections cannot seed a correction. Allocation-only edits can seed planning from valid cached measurements; Auto Adjust off leaves allocations unchanged. AAO/preview estimates alone never trigger it; automatic verification only runs within the explicit Analyze Build operation. Build responses are discrete and can hit protection limits, so reaching the target is not guaranteed. Automatic corrections refuse steps that would exhaust the adjustable budget or turn a positive mesh allocation into zero. A non-improving probe rolls back the entire fitting run, not only its last step. Changing the requested budget clears the learned correction; the budget field applies on Enter or focus loss so partially typed values do not redistribute meshes.

#### Use from C#

```csharp

using Meshia.MeshSimplification;

Mesh simplifiedMesh = new();

// Asynchronous API

await MeshSimplifier.SimplifyAsync(originalMesh, target, options, simplifiedMesh);

// Synchronous API

MeshSimplifier.Simplify(originalMesh, target, options, simplifiedMesh);

```

## 日本語

Unity、VRChat 向けのメッシュ軽量化ツール／ライブラリです。Unity Job System と Burst を使用し、高速な非同期処理に対応しています。ランタイムとエディターの両方で利用できます。

### インストール

#### VCC / ALCOM

[このフォークの VPM リポジトリ](https://drcain1.github.io/Meshia.MeshSimplification/vpm/index.json)を追加し、**Meshia — drcain fork** をインストールしてください。パッケージ ID は `io.github.drcain1.meshia.mesh-simplification` です。上流版 Meshia を置き換えるため、両方を同時にインストールしないでください。[移行手順](docs/FORK_DISTRIBUTION.md)も参照してください。

[anatawa12 の VPM リポジトリ](https://vpm.anatawa12.com/vpm.json)から、依存パッケージ `com.anatawa12.custom-localization-for-editor-extension` の 1.x（1.2.1 以上）を導入してください。アバター連携には NDMF、カスケード機能には Modular Avatar も必要です。どちらも [nadena の VPM リポジトリ](https://vpm.nadena.dev/vpm.json)から導入できます。

Unity Package Manager で Git URL から導入する場合は、依存パッケージを先にインストールしてください。UPM は `vpmDependencies` を自動解決しません。上流の VPM リポジトリから入手する版には、このフォークの変更は含まれません。

### 使い方

#### NDMF 連携

NDMF が導入されたプロジェクトでは、モデルに `MeshiaMeshSimplifier` を追加して使用できます。Edit Mode で軽量化結果をプレビューしながら設定を調整できます。

#### アバター全体の三角形数を配分する

レンダラーの GameObject 名が **Body** の新規項目は、大文字・小文字を区別せず、初期状態で **軽量化対象外** になります。多くのアバターで顔に使われる名前に基づく初期設定であり、顔の自動検出ではありません。`Body_base` はこの規則では除外しません。別の名前の顔メッシュは手動で除外してください。必要なら `Body` も手動で有効にできます。項目の更新は保存済みの選択を維持し、配分の **リセット（Reset）** はこの名前に基づく初期状態に戻します。

各メッシュ行の鍵と歯車の隣に **保護アイコン** があります。クリックするたびに **緑 → 黄 → 灰 → 緑** と切り替わります。緑は形状と変形の保護、黄は形状のみの保護（または一部だけ有効な設定）、灰は **保護なし** です。灰では、境界・選択ボーンの固定、同じ位置の分離頂点、FA-QEM のマテリアル境界・関節、ボーンウェイト、表面からのずれ、面の反転に関する保護を無効にします。穴、形状やテクスチャの歪み、変形の崩れが生じることがあります。基本的な接続と数値の整合性チェックは維持されるため、任意の三角形数への到達を保証するものではありません。この状態の UV Loop Dissolve は Blender フォールバックを直接使用します。保存済みの形状設定と選択ボーンは保持され、無効化中は該当する設定欄を操作できません。緑に戻すと保存済みの形状設定を復元し、変形の保護を明示的に有効にします。Undo で変更を戻せます。スキンメッシュと通常のメッシュの両方で使用でき、対象外のメッシュでは操作できません。保護を変更した後は「ビルドを解析」で計測を更新してから出力数を編集してください。自動調整は保護状態を変更しません。保護プリセットを適用すると無効化を解除します。既存の設定は維持され、従来は薄い表示だった変形保護オフの項目も、形状の保護が残っている場合は黄で表示されます。

アバター直下の子オブジェクトに **Meshia Cascading Avatar Mesh Simplifier** を追加すると、対象レンダラー全体で共有する三角形数の目標を設定できます。**Adjust** は現在の目標数を各レンダラーに手動で配分し、**Auto Adjust** は各レンダラーの目標数を自動更新します。

**初期設定と保護プリセット：** 新規メッシュ項目と単体コンポーネントでは、**変形の保護はオフ** で開始します（`MeshSimplifierOptions.AvatarInitial`）。境界・属性シームの保持と、元の表面からのずれの上限 **0.0005**（境界ボックスの対角線の 0.05%）は有効です。保護プリセットか各行のアイコンから明示的に有効にしてください。

- **保守的（Conservative）**：従来の保護設定を全項目に適用します。メッシュごとのボーンウェイト自動保護、強さ **2**、ウェイト差の上限 **0.10**、破棄するウェイトの上限 **0.02**、手足の既定ボーン選択による関節付近の頂点保持が有効です。
- **積極的（Aggressive）**：検出した手・指・髪のメッシュと、名前が `Body` または `Face` のメッシュには同じ保護を適用し、それ以外ではボーンウェイトと関節付近の頂点の保護をオフにします。境界・継ぎ目・表面の設定は保守的な値を維持します。目標三角形数への到達を保証するものではありません。

判定には正のボーンウェイト、ヒューマノイドの手・指の対応、別の衣装リグの同名ボーン、一般的な英語・日本語の手や髪の名前を使用します。未使用のボーン枠は対象にしません。特殊な名前は見落とす場合があります。メッシュを取得・読み取りできない場合やウェイトが不正な場合は保守的な保護を維持します。適用時にメッシュ単位で判定するため、手を含む身体メッシュ全体は保守的な設定になります。服・肘・膝にも追加の保護が必要な場合があります。各行のアイコンとアニメーション中の見た目を確認し、メッシュやリグの変更後は必要に応じて再適用してください。

両ボタンはアルゴリズム・目標数・対象外の設定・固定配分を維持し、元に戻す操作に対応します。対象外の顔は有効にしません。保存済み設定は自動変更しません。各メッシュの **設定をリセット（Reset Options）** は保守的なオプション値を明示的に適用しますが、別項目のボーン選択は維持します。コア API のボーンウェイト保護は引き続き明示的に有効にする方式です。元の表面からのずれの上限の初期値は `0.0005` です。自動判定は NDMF が処理するため、API を直接呼ぶ場合は `SkinningProtection` を解決するか、明示的に有効にしてください。

**7万三角形の目標より保護を優先します。** 自動調整は配分のみを変更し、保護を自動解除しません。直近のプレビューで目標を超えたメッシュをインスペクターに表示し、ビルド時にも FA-QEM の実際の出力数と目標数を警告します。プレビューが古い場合があるため、**NDMFビルド解析（Analyze NDMF Build）** で最終的な数を確認してください。目標に届かない場合は配分か品質上の妥協点を明示的に見直し、アップロード前にポーズ・表情・重なった服を確認してください。


ボーンの動きに合わせて変形するメッシュには、実験的な **ボーンによる変形の保護（Skinning Protection）** があります。**関節を動かしたときの形状を保護（Protect joint deformation）** で有効にできます。ボーンウェイトは、各ボーンが頂点の動きに与える影響の強さです。コア API の初期値はオフです。新規 NDMF コンポーネントとメッシュ項目では保護はオフです。保護プリセットまたは歯車から **変形するメッシュを自動保護** を有効にできます。複数の有効なボーンに正のウェイトを持つ各メッシュを個別に判定し、服、分割された身体、独自のボーン構成も対象にします。単一ボーンで動く剛体メッシュは対象外です。保存済みの従来の Auto は身体候補を一つに絞る以前の判定を維持し、インスペクターにその旨を表示します。既存の明示的な設定は保持されます。有効時は Blender 方式と FA-QEM の両方で、エッジの両端にあるボーンウェイトの差と、統合時に失われるボーン影響量を確認します。`Strength` は評価コストへの重み、`Max Weight Distance` は両端のウェイト分布の差の上限、`Max Discarded Weight` は出力のボーン影響数制限によって失われるウェイト量の上限です。保護により削減が途中で止まる場合があり、すべてのポーズで同じ見た目になることを保証する機能ではありません。代表的な関節ポーズで確認してください。

各レンダラーの歯車メニューでアルゴリズムを選択できます。

- **FA-QEM** — 新規のカスケードメッシュ項目の初期値です。元の形状、境界の曲率、元の法線、境界移動に伴う面積ペナルティを使ってエッジの統合を評価します。属性やトポロジーの制約により、目標数より多い状態で止まることがあります。
- **Blender Decimate** — 手動で選択できる代替アルゴリズムです。Blender 互換のエッジ統合処理で、レンダラーに配分された三角形数を目指します。[^blender-ja]
- **Meshia** — 従来の Meshia の軽量化アルゴリズムです。既存の保持設定や補間設定を利用できます。
- **UV Loop Dissolve** — 四角形に近い接続構造を再構築し、安全に除去できるエッジループの全体または一部を削減します。UV シーム、マテリアル境界、ハードエッジ、開いた境界、非多様体領域を保護します。ループ削減だけで目標に達しない場合は Blender Decimate に切り替わり、プレビューにフォールバックの通知が表示されます。

インスペクターには、選択したアルゴリズムで使用する設定だけを表示します。Blender Decimate ではボーンウェイトの保護を、UV Loop Dissolve では Blender への切り替え後に適用する同じ保護を表示します（ループ削減には専用の保護処理があります）。Meshia では従来の形状設定を、FA-QEM では境界・変形の保護と FA-QEM 専用設定を表示します。非表示になった設定値は、アルゴリズムを切り替えても保持されます。

**既存のメッシュ項目のアルゴリズムとオプションは変更されません。** FA-QEM は安全上の制約で目標に達しなくても、別のアルゴリズムへ自動で切り替わりません。必要に応じて三角形数の配分を調整するか、メッシュごとに別のアルゴリズムを選択してください。新規の単体 `MeshiaMeshSimplifier` も FA-QEM を使用し、最初の目標は元の三角形数の半分です。既存のコンポーネントと、C# で明示的に指定したターゲットは変更されません。

インスペクター上部の **Algorithm for All Meshes** は、無効な項目も含めて全メッシュのアルゴリズムを一括変更します。複数のアルゴリズムが混在している場合は **Mixed** と表示されます。個別の目標数、有効／無効の状態、オプションは保持されます。Unity の **Edit → Undo**（Windows では Ctrl+Z）で一括変更を取り消せます。一括変更後も、各レンダラーの歯車メニューで個別に選び直せます。

歯車メニューの **Preview UVs** では、元のメッシュと軽量化後の UV を重ねて比較できます。三角形数の目標や軽量化設定を変更すると、プレビューも更新されます。

#### 形状のみを軽量化する FA-QEM

FA-QEM は、元の面の平面、境界の曲率、元の法線に基づく接平面、境界移動に伴う面積ペナルティを使ってエッジ統合の優先度を決め、指定した絶対三角形数を目指して軽量化します。各レンダラーの歯車メニュー、または **Algorithm for All Meshes** から選択できます。

##### 対応機能

- **元のマテリアルとテクスチャを維持：** マテリアルの割り当てとテクスチャアセットを保持します。テクスチャベイク、アトラス生成、UV の再配置、テクスチャ圧縮は行いません。シェーダー機能やマテリアルのアニメーションは元のマテリアルを使用します。
- **メッシュ属性：** UV0～UV7、法線、接線、頂点カラー、ボーンウェイト、ブレンドシェイプの各フレームを処理します。統合された頂点の属性は補間されるため、チャンネルを保持しても値や見た目が完全に一致するとは限りません。C# の `UseBarycentricCoordinateInterpolation` を有効にすると、見た目に関わる属性とブレンドシェイプには通常のエッジ補間ではなく重心座標補間を使います。ボーンウェイトは引き続きエッジ両端の値から補間します。
- **分離頂点のシーム保護：** **同じ位置にある分離頂点の固定（Lock Coincident Split Vertices）** の初期値はオンです。メッシュの大きさに応じた許容誤差内で同じ位置にある別々の頂点を固定し、独立して軽量化される面同士が離れるのを防ぎます。属性が同じ頂点や、UV シーム・ハード法線のために分離された頂点も対象です。元のメッシュの溶接や修復は行いません。
- **マテリアル境界：** 所属するサブメッシュの組み合わせが異なる頂点同士の統合を拒否します。
- **トポロジーと面の検査：** エッジ統合の局所的なリンク条件を確認し、危険な非多様体構造、残る面の退化、過度な面法線の変化を拒否します。局所的な安全策であり、メッシュ全体の自己交差防止や修復を保証するものではありません。
- **アニメーションで開く面の保護：** 頂点インデックスが互いに異なる三角形は、初期状態では面積がゼロでも、変形によって面が開く場合があります。ブレンドシェイプまたはボーンウェイトを持つメッシュでは、FA-QEM はその面を保持し、境界・シーム保護が無効でも頂点を固定します。変形データのないメッシュの面積ゼロの面は引き続き削除できます。この安全策により、該当する面の周辺では削減が制限される場合があります。
- **境界の固定：** **Preserve Border Edges** で、トポロジー上の開いた境界の頂点を固定できます。スキンメッシュでは、特定の領域だけを保護するために選択したボーンに基づく境界保護も利用できます。
- **実験的なボーンによる変形の保護：** **関節を動かしたときの形状を保護（Protect joint deformation）** でウェイト差の評価コストと制限を追加します。コア API と新規 NDMF 項目の初期値はオフで、保護プリセットから上記の Auto を適用できます。軽量化後の代表的なポーズとブレンドシェイプを確認してください。
- **エディターとビルドの連携：** レンダラーごとの目標数、アバター全体への配分、NDMF プレビュー／ビルド、**Preview UVs** の比較表示に対応します。
- **実行・診断 API：** 同期、非同期、バッチ軽量化、結果レポート、元のインデックスとの対応を含むオプションの統合履歴に対応します。

##### 境界保持の初期値はオン

**新規メッシュ項目と `MeshSimplifierOptions.Default` では、Preserve Border Edges の初期値がオンです。** メッシュごとに歯車メニューから変更できます。襟ぐりや裾などの開いた境界の形状を保ちたい場合は、有効のまま使用してください。FA-QEM は境界頂点に触れる統合を拒否し、その位置と UV を固定します。ここでいう境界は一つの三角形だけが使用するエッジであり、閉じた面のカメラから見える輪郭すべてを固定する設定ではありません。

**Algorithm for All Meshes** を含むアルゴリズム変更では、各項目の境界設定を保持します。**Reset Options** は初期設定に戻すため、境界保持がオンになります。既存の保存済み項目は、オフに設定されている場合も含めて自動変更されません。C# から直接呼び出す場合も共通の初期値はオンです。境界を固定せずに削減したい場合は、明示的に無効にしてください。

**境界の重み（Boundary Weight）** と **境界移動面積の重み（Swept Area Weight）** は境界の移動を抑える評価項目であり、境界を固定する設定ではありません。**同じ位置にある分離頂点の固定（Lock Coincident Split Vertices）** も同じ位置にある分離頂点を保護するもので、開いた境界すべてを保護するものではありません。境界を厳密に維持したい場合は **Preserve Border Edges** を有効にし、古い項目の設定も確認してください。境界、シーム、ボーンによる変形の保護によって有効な統合候補が減り、厳しい三角形数の目標に達しない場合があります。

##### 目標数の確認と、さらに削減する方法

**目標三角形数（配分用）** は配分の目標であり、最終的な数の上限を保証するものではありません。インスペクターには現在の正常なNDMF解析結果と、超過数または残りの数を表示します。古い結果はその旨を表示し、失敗した解析は確定した数として扱いません。**削減方法を確認** では、出力数が多い順の一覧、軽量化対象外のメッシュ、未計測のメッシュを別々に表示します。プレビューの数値や古い実測値にはその旨を表示します。**メッシュ設定を開く** で対象の項目へ移動できます。プレビューや元のメッシュの数は、ビルド後の各メッシュの最終的な数ではありません。プレビューがない場合も削減量を計測済みとは表示しません。解析後も一覧は開けますが、現在の正常なビルド結果があればプレビューの案内は非表示になります。

見える部分の削減を強める前に、常に衣装で隠れている部分を見直してください。[AAO Remove Mesh By BlendShape](https://vpm.anatawa12.com/avatar-optimizer/ja/docs/reference/remove-mesh-by-blendshape/) では、素体を隠す適切なブレンドシェイプを指定してポリゴンを削除できます。[Modular Avatar Shape Changer](https://modular-avatar.nadena.dev/ja/docs/reference/reaction/shape-changer) の **Delete** モードでも削除できます。単に縮小したりシェーダーで隠したりするだけでは、ポリゴンは削除されません。Shape Changer の削除をアニメーションで切り替える場合、見えなくなっても報告されるポリゴン数は減りません。衣装の切り替えで必要になる部分は残してください。ポーズと衣装を変えて削除範囲を確認し、ビルド全体を再解析します。これらのツールは任意であり、案内からコンポーネントや依存パッケージを自動追加することはありません。

まだ超過する場合は、大きい項目から、対象外メッシュの軽量化、使わないアクセサリー、固定配分を見直してください。保護を緩める場合はメッシュごとに明示的に変更し、動かした状態を比較します。**自動調整は配分を増減の両方向に再配分します。** スライダーと数値入力は同じ動作です。目標数を下げると他の固定されていないメッシュへ配分を戻し、上げるとそれらのメッシュから配分を移します。編集中の目標、固定配分、対象外メッシュは維持し、再配分先は元の三角形数を超えません。スライダーの編集ではビルド実測による補正を維持し、新たな余裕を追加しません。Undo は編集と再配分をまとめて戻します。形状の保護により配分と実際の出力数は異なるため、非同期の推定は各メッシュの実測変化を使い、最終的な数はビルド解析で確認します。**Adjust** は手動で予算を再配分します。固定・対象外の設定は維持されます。変形の保護がオフでも境界・シーム・表面からのずれの制限は残るため、実際の出力数が目標を超える場合があります。**自動調整は目標の配分を変更します。** メッシュごとの試行で出力変化を実測し、最大4回のビルドで補正を自動検証します。保護は自動解除しません。目標数が結果より少ないからといって、安全にさらに削減できるとは限りません。

**三角形数の予算** は最初から開き、現在の正常なビルドでのアバター全体の実測値を表示します。メッシュ行は **出力 / 元の三角形数** を表示します。出力は AAO などの後続ツールで処理する前の Meshia の実測値です。内部目標値は行のポップアップの **計算の詳細** にだけ表示し、出力が内部目標を上回るだけでは警告しません。

出力数を編集する前に **ビルドを解析** を実行してください。スライダーや数値欄で希望する出力数を指定すると、最大8つの内部目標候補を非同期で測定し、達成できた出力数を表示します。数値欄は Enter で確定します。測定中は青の **…**、希望どおり変更できなかった場合は実測値に戻し、黄橙色の **!** で理由を一時表示します。指定量を超える削減や増加は適用しません。自動調整が有効なら、変更後の内部配分を他の固定されていないメッシュに再配分します。新しい編集が来た場合は古い測定を適用せず、「元に戻す」で編集と再配分を復元できます。保護を自動で緩めることはありません。

**三角形数の予算** の横にあるスピナーと **計算中...** は、編集の反映待ちやメッシュ数の更新中に表示されます。計算が終わると消え、セクションを折りたたんでいても確認できます。

FA-QEM は解析時に通常の統合処理に沿って三角形数を記録します。入力メッシュと保護設定が変わっていなければ、編集後の数値をこの記録からすぐに確認できます。記録済みの範囲より小さい数を指定した場合は、出力メッシュを生成しない非同期の計測を1回行い、残りの範囲も記録します。すでに要求値まで減らせず処理が終了したメッシュでは、さらに小さい要求にも同じ結果をすぐ返します。ほかのアルゴリズムでは従来どおり候補ごとの計測を行います。入力や保護設定が変わると、記録は無効になります。

この記録が利用できる場合、**ビルドを解析** は前回のビルド結果と各メッシュの実測変化を使い、最初の検証ビルドの前に自動調整の配分を準備します。超過量を確認するためだけにビルドを繰り返す必要が減ります。推定値を検証済みとして表示することはなく、最終的な数は必ずNDMFの完全なビルドで確認します。調整に失敗した場合は開始時の配分に戻します。初回の解析や、後続ツールの処理によって結果が変わる場合は、完全なビルドや追加の検証が必要です。

検証済みの2回のビルドで、配分・出力が変わったメッシュが1つだけの場合は、その変更がアバター全体の数にどれだけ反映されたかも次の調整に利用します。後続ツールで一部が削除されるメッシュに、少しずつ追加して何度もビルドする状況を減らすための推定です。入力や保護設定が変わると無効になり、複数のメッシュが同時に変わった結果から個別の反映率を決めることはありません。最終的な配分は必ずビルドで検証し、自動削減の上限と失敗時に配分を戻す仕組みも維持します。

未測定の出力は **—**、古い実測値は灰色の **?** とともに表示し、内部目標値には切り替えません。保護設定や入力メッシュの変更、入力キャッシュの消失後は **ビルドを解析** で更新してから編集できます。編集後の全体推定値は、影響を受けた全メッシュの実測変化を反映します。後続ツールで結果が変わる場合があるため、最終合計は再解析で確認してください。

行のマークまたは **/** をクリックすると微調整を開きます。**減らす三角形数** を測定して **削減を適用** すると、他のメッシュに再配分せず削減分を保持します。内部目標値はポップアップの **計算の詳細** にあります。行の高さと操作欄の位置は揃えたままで、アバター全体の予算超過は上部に表示します。

自動調整では、実測で削減できる固定されていないメッシュに、開始時の配分に比例して削減量を分散します。1回のビルド解析では、再検証のビルドを含め、各メッシュの開始時の目標値と実測出力数の少なくとも75%を維持します。これは自動変更の上限であり、見た目の品質を保証するものではありません。この範囲で予算に収まらない場合は開始時の配分に戻します。それ以上減らす場合は、メッシュごとに明示的に調整してください。

##### 指や関節を曲げたときの形状を保つ

FA-QEM の **関節付近の頂点を保持（Preserve Vertices Near Joints）**（`options.SkinningProtection.PreserveJointTransitions`）は、最も強く影響するボーンが切り替わる部分と、そのすぐ周囲の頂点を元の状態で保持します。他の部分は引き続き軽量化できるため、手や指の全体を固定する機能ではありません。

新規アバター設定とコア API の初期値は **オフ** です。保守的プリセットでは全項目、積極的プリセットでは検出した項目で有効にします。**関節を動かしたときの形状を保護** や自動・手動の設定とは独立しています。アバター全体の軽量化では、各メッシュの **FA-QEM設定** で有効にし、下の **関節を保護するボーン（Joint Protection Bones）** で対象を選びます。新規項目では両手と全指、両腕の上腕・前腕、両脚の太もも・すね、足を選択し、使用されているヒューマノイドボーンの手首・肘・膝・足首付近を保護します。保存済みの選択は自動で拡張しません。**境界エッジを保持するボーン** とは別の選択で、閉じた指の表面なども保護します。何も選択しなければ、その項目には関節保護を適用しません。存在しないヒューマノイドボーンは無視します。単体の軽量化では、すべてのボーンの切り替わり部分を対象にします。

保護した頂点の位置と属性を保持するため、目標三角形数より多い状態で止まる場合があります。すべてのポーズを検査する機能ではなく、体と服のめり込みや、保護範囲外の変形を完全に防ぐものではありません。手を開いた状態、途中まで曲げた状態、握った状態で比較し、ビルド後のアバター全体の三角形数を確認してください。現在は FA-QEM のみが対応しています。

##### 髪の保護と、さらに削減するための設定

**髪専用のプリセットの自動適用や、名前による髪の判別は行いません。** 髪にも他のメッシュと同じ FA-QEM の安全策が働きます。薄い板状の髪や独立した毛束には、開いた境界や、UV・法線の継ぎ目などで同じ位置に重なる別々の頂点が多いことがあります。**境界エッジを保持（Preserve Border Edges）** と **同じ位置にある分離頂点の固定（Lock Coincident Split Vertices）** の初期値はオンで、多くの頂点が固定される場合があります。目標三角形数を下げるだけでは固定を解除できないため、目標数よりかなり多い状態で削減が止まることがあります。

まずは初期設定を使い、見た目に重要な髪には多めに三角形数を配分してください。さらに削減したい場合は、コピー上で **対象の髪レンダラーの歯車メニュー** を開き、一度に一つずつ設定を変えて比較します。

| 設定 | 保護を緩める方法 | 注意する変化 |
| --- | --- | --- |
| **境界エッジを保持（Preserve Border Edges）** | オフにすると、開いた境界の頂点を移動・統合できるようになります。**境界エッジを保持するボーン（Preserve Border Edges Bones）** も確認してください。主設定がオフでも、選択したボーンに対応する境界頂点は固定される場合があります。 | 毛先や板状の髪の輪郭の変化、毛束の間の隙間。 |
| **同じ位置にある分離頂点の固定（Lock Coincident Split Vertices）** | オフにすると、同じ位置にある分離頂点の固定を解除します。頂点同士を溶接する機能ではなく、同じ頂点が境界保護で固定されている場合もあります。 | 頂点が別々に動くことによる亀裂、UV の不連続、陰影の変化。 |
| **元の表面からのずれの上限（Maximum Surface Deviation）** | 有効な場合は正の許容値を大きくするか、`0` にして無効にします。新規アバターとコア API の初期値は `0.0005` です。 | 元の形状からのずれ、頭や服へのめり込み。 |
| **面法線の内積の下限（Minimum Face Normal Dot）** | FA-QEM 側の値を `0`～`1` の範囲で下げると、統合時の面の向きの変化をより大きく許容します。 | 鋭い折れ目や陰影の変化。他のトポロジー検査は引き続き適用されます。 |
| **関節を動かしたときの形状を保護（Protect joint deformation）** | このメッシュで有効な場合は **ボーンウェイト差の上限（Maximum Skin Weight Distance）** / **破棄するボーンウェイトの上限（Maximum Discarded Skin Weight）** を大きくするか、保護をオフにします。Auto が選択されている場合は、先に **変形するメッシュを自動保護** をオフにしてください。**保護の強さ（Protection Strength）** を下げるだけでは評価コストが変わるだけで、拒否条件は解除されません。 | 髪ボーンの動きやブレンドシェイプを確認してください。メッシュごとの自動保護は複数ボーンで動く髪も対象にしますが、髪の名前による判定や物理挙動の検査は行いません。 |

**境界の重み（Boundary Weight）**、**法線の重み（Normal Weight）**、**境界移動面積の重み（Swept Area Weight）** は統合候補の優先順位に関わる重みで、境界やシームの固定を解除する設定ではありません。値を下げても、必ず三角形数が減るとは限りません。**Smart Link** も、FA-QEM で離れた髪のパーツを溶接する機能ではありません。

**境界とシームの保護をオフにしても、ほかの保護は残ります。** マテリアル所属の検査や、ボーンウェイトまたはブレンドシェイプを持つメッシュで、異なる頂点インデックスを持ち、初期状態で面積がゼロの面を保護する処理などです。このような面はアニメーション中に開く場合があるため、FA-QEM は面を保持して頂点を固定します。灰色の **保護なし** ではこれらの保護も無効になりますが、基本的な接続と数値の整合性チェックは維持されます。

元の髪と軽量化後の髪を正面・背面・側面から比較し、毛先や重なりを確認してください。その後、Play Mode または VRChat で髪の動き、表情、衣装の切り替えも確認します。**UVをプレビュー（Preview UVs）** は UV の変化を確認する補助であり、アニメーション品質の確認にはなりません。最終的な三角形数は **Analyze NDMF Build** またはビルド後のアバターで確認してください。亀裂や輪郭の崩れが出る場合は設定を戻し、他のメッシュから削減量を確保してください。これらの保護は問題を減らすためのもので、すべてのポーズで髪に穴が開かないことを保証するものではありません。

##### 穴や形状の崩れが出たとき

FA-QEM で軽量化した後に穴、鋭いへこみ、引き伸ばされた三角形が現れた場合は、次の順に確認してください。

1. メッシュ行をクリックして対象のオブジェクトを選択し、**歯車** を開きます。保護アイコンが灰色の **保護なし** になっていないことを確認してください。灰色では以下の保護設定が適用されません。
2. **FA-QEM設定** の **元の表面からのずれの上限（Maximum Surface Deviation）** を確認します。**`0` は保護を無効にする値です。** 無効になっている場合は、まず `0.0005` を試してください。すでに有効な場合は、正の値を小さくすると元の表面からのずれをより厳しく制限できます。
3. 少しずつ調整して、問題のある部分を比較します。例えば `0.002` で形状が崩れる場合は `0.0019` を試し、必要ならさらに下げてください。これは調整例であり、すべてのメッシュに適した値ではありません。メッシュの大きさに対する比率なので、正の値を小さくするほど保護が強くなり、大きくするほど形状の変化を許容して削減しやすくなる場合があります。
4. 動かしたときに崩れる場合は、**ボーンによる変形の保護** も有効にしてください。**ボーンウェイト差の上限（Maximum Skin Weight Distance）** は `0.25` を目安に試し、まだ崩れる場合は下げます。特に尻尾、髪、靴、関節付近は、動かした状態で確認してください。

保護を強めると、指定した数より多くの三角形が残ることがあります。そのメッシュへの配分を増やし、必要なら別のメッシュで削減量を確保してから、**ビルドを解析（Analyze Build）** でアバター全体の数を再確認してください。表面からのずれの制限は形状の変化を抑える機能で、元からある穴を修復したり、重なった衣装の貫通を完全に防いだりするものではありません。隙間が残る場合は **境界エッジを保持** と **同じ位置にある分離頂点の固定** も確認し、元のメッシュと比較して、Play Mode で重なる部分を確認してください。

##### FA-QEM の設定

以下の日本語名は Unity インスペクターの表示と一致します。英語表示名と C# プロパティ名も併記しているため、どの名前でも検索できます。FA-QEM の初期値は `FaQemOptions.Default` に基づき、アバター用の上書き値と関節保護は別途記載しています。

| Unity の表示名（英語表示名） | C# プロパティ | 初期値 | 効果 |
| --- | --- | --- | --- |
| 関節付近の頂点を保持（Preserve Vertices Near Joints） | `options.SkinningProtection.PreserveJointTransitions` | 新規項目ではオフ。対象の保護プリセットでオン | 最も強く影響するボーンが切り替わる部分と、そのすぐ周囲の頂点を保持します。同じ位置にある分離頂点の固定とは別の設定です。 |
| 平面評価の面積除数（Plane Area Divisor） | `options.FaQem.PlaneAreaWeight` | `1` | 逆面積重み付けがオンの場合、この値と三角形面積の積で元の面の平面重みを割ります。オフの場合は、この値を平面重みとして直接使います。正の値が必要です。 |
| 境界の重み（Boundary Weight） | `options.FaQem.BoundaryWeight` | `500` | 元の境界曲率に基づく制約の強さです。境界の固定ではなく、移動へのペナルティです。 |
| 法線の重み（Normal Weight） | `options.FaQem.NormalWeight` | `0.01` | 元の法線に基づく接平面制約の強さです。必要に応じて形状から求めた法線を使います。 |
| 境界移動面積の重み（Swept Area Weight） | `options.FaQem.AreaWeight` | `100` | 統合の優先度を決める、境界移動に伴う面積ペナルティの強さです。 |
| 面積の逆数による重み付け（Use Inverse Area Weighting） | `options.FaQem.UseInverseAreaWeighting` | オン | 元の小さい三角形ほど平面重みを大きくします。 |
| 同じ位置にある分離頂点の固定（Lock Coincident Split Vertices） | `options.FaQem.PreserveAttributeSeams` | オン | 属性が同じものも含め、同じ位置にある分離頂点を固定します。 |
| 面法線の内積の下限（Minimum Face Normal Dot） | `options.FaQem.MinNormalDot` | `0.2` | 統合前後で残る面の法線同士の内積の下限です。大きいほど多くの変化を拒否します。範囲は `0`～`1` です。 |
| 元の表面からのずれの上限（Maximum Surface Deviation） | `options.FaQem.MaxSurfaceDeviation` | `0.0005` | 元の表面からサンプル点までの距離の上限です。元のメッシュのバウンディングボックスの対角線長に対する比率で指定し、`0.001` は 0.1% を意味します。厳しくすると目標三角形数より多い状態で止まる場合があります。 |

FA-QEM は専用の **面法線の内積の下限（Minimum Face Normal Dot）** と特徴量の重みを使います。従来の Meshia の **Preserve Surface Curvature** や **Smart Link** は、FA-QEM の評価基準を変更したり、離れたメッシュ部分を接続したりする設定ではありません。

**Preserve Border Edges** が有効な場合は境界頂点に触れる統合を拒否するため、受け入れられる内部エッジの統合では境界面積ペナルティがゼロになります。元の境界の二次形式も固定された頂点に残ります。この状態で **境界移動面積の重み（Swept Area Weight）** や **境界の重み（Boundary Weight）** を上げても、身体に密着する服の内部領域は保護できません。

**元の表面からのずれの上限（Maximum Surface Deviation）** は、変更しない元のメッシュを基準に、軽量化後のサンプル点から元の表面への距離を制限します。統合先の頂点、残る三角形の各エッジ中点、三角形の重心を検査します。最適位置が条件を満たさなければ、エッジの両端と中点も試し、有効な候補を実際のコストでキューに登録します。元の表面の検索用データは軽量化ごとに一度構築します。累積する表面のずれを抑える機能ですが、別のメッシュとの隙間、サンプル点の間の連続した表面、あらゆるアニメーション中の形状を保証するものではありません。近くにある元の別の層が距離判定を満たす場合もあります。新規アバターとコア API では `0.0005`（メッシュの境界ボックスの対角線長の 0.05%）で有効です。明示的に無効にした `0` を含め、保存済みの値は維持します。スライダーは `0～0.005` を `0.00001` 刻みで調整でき、数値欄では `0～0.1` を入力できます。インスペクターを開いても保存済みの値は変更しません。許容差を緩める場合や無効にする場合は、品質と達成可能な三角形数のバランスを確認してください。

重ね着では、重なる両方の層を確認してください。外側の服の軽量化に問題がなくても、内側の層が外側へ動くと貫通する場合があります。必要な表面保護を両方に設定し、Play Mode で表示切り替えやアニメーションも含めて元の衣装と比較してください。アバター全体の目標を確認するときは、実際にビルドされた三角形数を使用してください。保護によって各メッシュが目標より多い状態で止まる場合があり、後続のビルド処理でも数が変わります。

NDMF プレビューとビルドは、メッシュごとの自動設定にはメッシュごとの判定を、保存済みの従来の Auto には身体メッシュ選択を同じ方法で適用します。Auto のプレビューはリグ、候補レンダラー、メッシュ、軽量化設定の変更に応じて再評価されます。ただし、プレビューが扱うのは参加しているプレビューパスだけです。完全なビルドとは入力形状や最終的な数が異なる場合があるため、最終確認には **Analyze NDMF Build** または実際のビルド結果を使用してください。

幾何学的な評価項目と許容距離は、元のメッシュのバウンディングボックスの対角線長で正規化した座標を使います。各評価項目のスケール依存性は異なるため、これらの重みが論文の非正規化の重みと等価とは限りません。対角線長の `1e-8` より短いエッジは、シーム保護が無効でも統合を拒否します。同じ位置にある分離頂点は **同じ位置にある分離頂点の固定（Lock Coincident Split Vertices）** が有効な場合に固定します。自動溶接や仮想エッジは導入しません。

##### C# からの利用と診断

`MeshSimplifierOptions.Default` から設定を作り、初期化済みの `FaQem` を変更してください。これにより、意図してゼロにした重みを保持できます。未初期化の `default(FaQemOptions)` は、古い保存データとの互換性のために組み込みの初期設定として扱われます。

```csharp
using Meshia.MeshSimplification;
using UnityEngine;

var options = MeshSimplifierOptions.Default;
options.PreserveBorderEdges = true; // 初期値もオンです。ここでは明示しています。

var target = new MeshSimplificationTarget
{
    Kind = MeshSimplificationTargetKind.FaQemTriangleCount,
    Value = 8350,
};

var simplifiedMesh = new Mesh();
var report = MeshSimplifier.SimplifyWithReport(
    originalMesh, target, options, simplifiedMesh);

Debug.Log($"三角形数: {report.InputTriangleCount} → {report.OutputTriangleCount}; " +
          $"結果: {report.FaQemTermination}");
```

同じターゲットを `Simplify`、`SimplifyAsync`、`SimplifyBatch` に渡せます。`SimplifyWithReport` は入力数、要求数、出力数に加えて、`TargetReached`（目標到達）または `ConstraintsExhausted`（制約により続行不可）を返します。一度の統合で複数の三角形が減るため、要求数を少し下回ることがあります。制約によって要求数より多い状態で止まることもあります。FA-QEM は目標数を強制的に達成するために別のアルゴリズムへ切り替わりません。

`SimplifyWithHistory` は、受け入れられた統合、影響を受けた面、出力から元の頂点・三角形への対応も返します。元のインデックスは由来を示すもので、出力頂点の位置や属性が変更されていないことを示すものではありません。元のメッシュを維持するには別の出力メッシュを用意し、生成したメッシュが不要になったら解放してください。

##### 対象範囲と制限

形状の軽量化処理は [Fast and Robust Mesh Simplification for Generated and Real-World 3D Assets](https://arxiv.org/html/2605.14029v1) に基づいています。論文に掲載された品質や性能を再現したことを検証済みの実装ではありません。離れたメッシュ部分を結ぶ仮想エッジは有効にしていません。

元のテクスチャを使うため再サンプリングは避けられますが、形状の軽量化によって輪郭、UV 補間、陰影、スキニング、ブレンドシェイプの変形は変わる場合があります。目的の三角形数で UV の重ね合わせと描画結果を比較してください。開いた境界の固定と分離頂点のシーム保護は、それぞれ別の問題に対処するもので、見た目の完全な一致を保証するものではありません。

実験的なテクスチャベイク版から移行する場合は、パッケージを切り替える前に、その版で保存済みのベイク結果を元に戻してください。ベイク機能を削除しても、すでにベイクされたメッシュから元のアセットは復元されません。古いアトラス設定はこの版では無視されます。

#### ビルド結果を考慮した三角形数の配分

カスケードインスペクターでは、Meshia の出力と、後続の NDMF ツールによる処理後の推定数を区別して表示します。

- **Meshia output：** 後続処理を行う前の三角形数です。
- **AAO estimate：** 対応する Avatar Optimizer API が利用できる場合に、削除される見込みのメッシュやポリゴンを考慮した推定数です。
- **Analyze NDMF Build：** 一時的な完全 NDMF ビルドを実行し、最終的な三角形数を記録します。

**自動調整（Auto Adjust）** が有効な場合、**ビルド解析** の1回のクリックで最大4回の完全なビルドを実行します。各ビルドの間に、Meshia が受け取った入力メッシュのコピーと同じ保護設定を使い、メッシュごとに目標変更の効果を実測します。有効かつ固定されていないメッシュのうち、試行で有効な出力変化が確認されたものだけを調整します。出力が変わらない、または逆方向に変わる試行は予算削減として扱わず、同じ試行の結果は再利用します。変化が大きい場合はメッシュごとに最大3回の追加試行で調整します。増減の両方に対応し、増やす場合は保存した入力の三角形数を上限とします。組み合わせた設定は必ず完全なビルドで検証するため、1回の解析より時間がかかる場合があります。自動調整が無効なら配分を変更せず1回だけ計測します。予算以下かつ目標の 0.1% 以内（最低1三角形、70,000 の場合は70三角形）を到達範囲とし、増やす場合はその中間を目指します。形状保護は自動解除せず、試行で出力が変わらなくても絶対的な最小値とは判断しません。

指定した最終予算、対象外メッシュ、固定配分、形状保護は変更しません。インスペクターには **配分の余裕** または **ビルド用の追加配分** を表示し、手動調整と以降の自動調整でも維持します。1回のクリックによる補正はまとめて Undo で取り消せます。**計算の詳細** の補正値の横にある **ビルド補正をリセット（Reset build correction）** は現在の配分を維持したまま、どちらの補正も解除します。配分の **Reset** は配分と補正をリセットします。最後のビルドでは計測後に再補正しないため、結果は現在の配分に対応します。進捗にはビルド番号を表示し、次の補正前にキャンセルを確認します。個々のNDMFビルドは同期処理のため、この操作では途中停止できません。目標範囲への到達、改善の停止、調整余地なし、ビルド失敗、キャンセル、4回の上限で停止し、その理由を表示します。失敗や中断で未検証の結果を確定扱いしません。補正はビルドが目標範囲に到達した場合のみ確定します。上限、キャンセル、失敗などその他の停止時は、配分と補正をクリック前の状態にすべて戻し、その設定の基準計測結果を表示します。失敗した実行を繰り返しても、過度な配分削減が累積することはありません。失敗・古い解析、自動調整がオフの場合、調整可能な配分がない場合は補正しません。AAO やプレビューの推定値だけでは補正せず、自動検証はビルド解析をクリックした場合のみ実行します。実際の数は離散的に変化し、保護による限界もあるため、目標への到達は保証されません。自動補正では、調整可能な配分を使い切る補正や、正のメッシュ配分をゼロにする補正を拒否します。試行で結果が改善しない場合は、最後の試行だけでなく実行全体の配分変更を取り消します。指定予算の変更時は学習した補正を解除します。予算入力は Enter またはフォーカスを外した時に反映し、入力途中の値では再配分しません。

#### C# から呼び出す

```csharp

using Meshia.MeshSimplification;

Mesh simplifiedMesh = new();

// 非同期API

await MeshSimplifier.SimplifyAsync(originalMesh, target, options, simplifiedMesh);

// 同期API

MeshSimplifier.Simplify(originalMesh, target, options, simplifiedMesh);

```

[^blender]: Blender-derived code retains its GPL-2.0-or-later terms; see the [attribution notice](BLENDER_PORT_NOTICE.md). The fork's license is in [LICENSE.md](LICENSE.md), with the original [upstream MIT notice](Licenses/UPSTREAM-MIT.txt) retained.

[^blender-ja]: Blender 由来のコードには GPL-2.0-or-later が適用されます。詳細は[帰属表示](BLENDER_PORT_NOTICE.md)、このフォークのライセンスは [LICENSE.md](LICENSE.md)、上流の MIT ライセンスは [Licenses/UPSTREAM-MIT.txt](Licenses/UPSTREAM-MIT.txt) を参照してください。

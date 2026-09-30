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

Add [the drcain fork VPM repository](https://drcain1.github.io/Meshia.MeshSimplification/vpm/index.json), enable prerelease packages, and install **Meshia — drcain fork**. The package ID is `io.github.drcain1.meshia.mesh-simplification`. It replaces upstream Meshia; install only one variant per project. See [migration and distribution details](docs/FORK_DISTRIBUTION.md).

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

Existing entries retain their saved algorithms and options. FA-QEM does not automatically switch algorithms when its safety constraints prevent reaching a target; adjust the budget or choose an alternative per mesh. New standalone `MeshiaMeshSimplifier` components also select FA-QEM, initially targeting half the source triangles. Existing components and explicit C# targets are unchanged.

The **Algorithm for All Meshes** dropdown at the top of the inspector applies an algorithm to every mesh entry, including disabled entries. It displays **Mixed** when entries use different algorithms. Individual triangle targets, enabled states, and options are retained. Use Unity's **Edit → Undo** (Ctrl+Z on Windows) to undo the whole batch change. You can still override individual algorithms through each renderer's cogwheel.

Use **Preview UVs** inside a renderer's cogwheel to compare the original and simplified UV layouts. The preview updates as triangle targets and simplification settings change.

#### Geometry-only FA-QEM

FA-QEM reduces a mesh toward an absolute triangle budget using source surface planes, boundary curvature, original-normal tangent planes, and a separate boundary swept-area penalty to rank edge collapses. Select **FA-QEM** in a renderer's cogwheel, or use **Algorithm for All Meshes** at the top of the cascading inspector.

##### Initial settings and protection presets

New entries whose renderer GameObject is named **Body** (case-insensitive) start **excluded from simplification**, because many avatars use that name for the face. This is a naming convention, not face detection: `Body_base` is not excluded by this rule, and differently named faces must be excluded manually. You can explicitly enable a `Body` entry if desired. Refreshing entries preserves existing choices; the allocation **Reset** button restores this name-based default.

Each mesh row has a **deformation-protection icon beside the lock and cog**. Its color shows your settings: green means the relevant options are enabled, amber means only some are enabled, and dim means all are off. **Auto counts as enabled.** Bone mappings do not change the color, so matching settings look the same on different meshes. Click green to disable protection; click amber or dim to enable it. A click sets bone-weight protection and joint-transition preservation to explicit On/Off while retaining numeric limits and selected bones. Only FA-QEM uses joint-transition preservation; other algorithms show the bone-weight setting alone. Excluded or non-skinned meshes cannot use this shortcut. Border, seam, and surface protection remain separate. The cog retains the individual controls and automatic policy; Unity Undo restores changes. The icon does not certify deformation quality or protection coverage on the final built mesh.

New avatar entries and standalone components start with **deformation protection off** (`MeshSimplifierOptions.AvatarInitial`). Border and seam preservation remain on, and maximum surface deviation remains **0.0005** (0.05% of each mesh's bounds diagonal). Choose a protection preset explicitly, or use each row's protection icon.

- **Conservative** applies the previous full protection preset to every entry: per-mesh automatic bone-weight protection, strength **2**, maximum weight distance **0.10**, maximum discarded weight **0.02**, and joint-transition preservation with the default limb/hand selection.
- **Aggressive** applies that same protection to detected hand/finger and hair meshes, plus meshes named `Body` or `Face`. It switches bone-weight and joint-transition protection off on the other meshes. Border, seam, and surface settings remain conservative; it does not promise to reach the requested triangle budget.

Detection uses positive bone weights, humanoid hand/finger mappings, matching bone names for separate clothing rigs, and common English/Japanese hand and hair names. Unused bone slots do not count. Detection is approximate; unusual names can be missed. Missing/unreadable meshes or malformed weights keep conservative protection. This is a one-time, whole-mesh selection: if the body includes the hands, the whole body keeps the conservative preset. Clothing, elbows, and knees can still need additional protection. Review the row icons, check animated poses, and reapply the preset if the mesh/rig changes.

Both buttons preserve algorithms, targets, exclusions, and fixed allocations, and support Unity Undo. They do not enable an excluded face. Existing saved configurations are not changed automatically. **Reset Options** explicitly applies conservative options to one mesh while retaining its separate joint-bone selection. Core `MeshSimplifierOptions.Default` remains unchanged for API and serialization compatibility. NDMF resolves the automatic policy; direct API callers must resolve `SkinningProtection` themselves or explicitly enable it.

**Protection takes priority over reaching 70,000 triangles.** Auto Adjust allocates targets; it never relaxes safeguards or guarantees a reachable budget. The inspector lists meshes whose last preview exceeded their allocation, and build warnings report actual FA-QEM output versus requested counts. Preview counts may be stale: use **Analyze NDMF Build** to verify the complete result. A high protected count calls for revised allocations or explicit quality tradeoffs, not automatic removal of protection. Check representative poses, expressions, and layered clothing before upload.

##### Understanding the budget and reducing more

**Auto Adjust keeps manual reductions.** Lowering a mesh slider or number field never raises other targets. Raising a target can lower other unlocked allocations to stay within the estimated budget. Automatic adjustment never spends spare budget; click **Adjust** explicitly to redistribute it (which can raise targets you previously lowered). Locks and exclusions are respected. Border, seam, and surface-deviation guards still apply when deformation protection is off, so requested allocations can remain below the actual output.

**Requested Triangle Budget** is an allocation goal, not a hard limit. The inspector shows the current successful NDMF analysis and how many triangles are over budget or remain available. Old results are marked out of date; failed analyses are not treated as confirmed counts. **Find ways to reduce** opens a breakdown sorted by preview triangles above allocation, a separate group of meshes excluded from simplification, and the remaining meshes. **Open mesh settings** jumps to the corresponding entry. Preview/source figures are not final per-mesh build counts, and missing previews are not presented as measured savings. The breakdown remains available after analysis, while a current successful build supersedes the preview notice.

Start with geometry that is always hidden by clothing before reducing visible surfaces more aggressively. [AAO Remove Mesh By BlendShape](https://vpm.anatawa12.com/avatar-optimizer/en/docs/reference/remove-mesh-by-blendshape/) can remove body polygons selected by a suitable hiding blendshape. [Modular Avatar Shape Changer](https://modular-avatar.nadena.dev/docs/reference/reaction/shape-changer) can do this in **Delete** mode. Merely shrinking a body part or hiding it with a shader does not remove its polygons. Animated/toggleable Shape Changer deletion hides geometry but does not reduce the reported polygon count; retain body regions needed by any outfit toggle. Review the removal in different poses and outfits, then analyze the complete build again. These tools are optional; the guide does not add components or dependencies automatically.

For remaining overruns, review the largest entries first: enable simplification on suitable excluded meshes, reconsider unused accessories and fixed allocations, or explicitly adjust an individual mesh's protection while checking its animated appearance. **Auto Adjust only distributes targets**; it does not measure achievable savings, repeatedly rebalance shortfalls, or relax protection. A target below the result is not proof that further reduction is safe.

##### Supported features

- **Original materials and textures:** keeps the existing material assignments and texture assets. There is no texture baking, atlas generation, UV repacking, or texture compression pass. Shader features and material animation continue to use the original materials.
- **Mesh attributes:** carries UV0–UV7, normals, tangents, vertex colors, skin weights, and blend-shape frames through the shared mesh simplification pipeline. Attributes at collapsed vertices are interpolated; retaining these channels does not mean their values or appearance remain identical. The C# option `UseBarycentricCoordinateInterpolation` selects barycentric interpolation for appearance attributes and blend shapes instead of the default edge interpolation; skin weights still use endpoint interpolation.
- **Split-vertex seam protection:** **Preserve Attribute Seams** is on by default. It locks coincident vertex records within a scale-dependent tolerance, including split records whose attributes match, so independently simplified face groups do not pull those copies apart. This also protects coincident splits used for UV seams and hard normals. It does not weld or repair the source mesh.
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

**Boundary Weight** and **Area Weight** discourage boundary movement but do not lock borders. Likewise, **Preserve Attribute Seams** protects coincident split vertices, not every open edge. Keep **Preserve Border Edges** enabled when exact open-boundary retention is required, and check this setting on older entries. Border, seam, and skinning protection can leave fewer legal collapses and prevent reaching an aggressive triangle target.

##### Preserving finger and joint shape

FA-QEM offers **Preserve Joint Transitions** for joints that lose their shape when bent. It keeps the original vertices where the strongest bone influence changes, together with one neighboring ring. The rest of the mesh can still simplify; this does not preserve the entire hand or finger.

The option starts **off on new avatars** and in the core API. The Conservative preset enables it on every entry; Aggressive enables it on the meshes it selects. It is independent of **Protect joint deformation** and its automatic/manual policy. In a cascading avatar entry, enable it under **FA-QEM Options**, then choose **Joint Protection Bones** below the options. New cascading entries select both hands and all fingers, upper/lower arms, upper/lower legs, and feet. Once enabled, this protects transitions around wrists, elbows, knees, and ankles where the renderer uses those humanoid bones. Saved selections are not expanded automatically. This is a separate selection from **Preserve Border Edges Bones**: it protects interior joint geometry as well as open surfaces. Clearing the selection disables joint-transition protection for that entry; missing humanoid bones are ignored. Existing border selections retain their meaning. Standalone simplifiers apply this option to all bone transitions. The core API can narrow it with the nonserialized mesh-bone indices in `SkinningProtection.JointProtectionBoneIndices` (up to its fixed-list capacity).

The guard preserves source joint/support vertices and their attributes. It may stop above the triangle target, and it does not test every pose, prevent body/clothing intersections, or guarantee unchanged geometry away from the protected rings. Compare open, partly curled, and closed hands before accepting the result, then verify the complete built-avatar count. It currently applies only to FA-QEM.

##### Hair protection and further reduction

**There is no automatic hair preset or name-based hair detection.** Hair uses the same FA-QEM safeguards as other meshes. Thin hair cards and individual strands often have many open edges and coincident split vertices, including UV and normal seams. **Preserve Border Edges** and **Preserve Attribute Seams** are on by default and can lock much of this geometry. Lowering the triangle target alone does not override these locks, so hair may stop well above its target.

Start with the defaults and give visually important hair a larger budget. To explore further reduction, use **that hair renderer's cogwheel** on a copy and change one option at a time:

| Option | How to reduce protection | Tradeoff |
| --- | --- | --- |
| **Preserve Border Edges** | Turn it off to allow open-boundary vertices to move or collapse. Check **Preserve Border Edges Bones** too: selected bones can keep associated boundary vertices locked even with the main toggle off. | Strand tips and card outlines can change; gaps may appear between strands. |
| **Preserve Attribute Seams** | Turn it off to release coincident split-vertex locks. This does not weld the copies together, and border protection may still lock the same vertices. | Copies can move apart, causing cracks, UV discontinuities, or shading changes. |
| **Maximum Surface Deviation** (`MaxSurfaceDeviation`) | If enabled, increase the positive tolerance or set it to `0` to disable the guard. New avatar defaults use `0.0005`; the core API default remains `0`. | More surface drift and possible intersections with the head or clothing. |
| **Minimum Face Normal Dot** (`MinNormalDot`) | Lower the FA-QEM value within `0`–`1` to allow larger face rotations per collapse. | Sharper folds and changed shading; other topology checks still apply. |
| **Protect joint deformation** | If active for this mesh, increase **Max Weight Distance** / **Max Discarded Weight**, or turn protection off. Turn off **Automatically protect deforming meshes** first if Auto is selected. Lowering **Strength** only reduces the cost penalty, not the rejection limits. | Check hair-bone motion and blend shapes. Per-mesh Auto protects weighted hair using multiple bones too; it does not identify hair by name or test hair physics. |

**Boundary Weight**, **Normal Weight**, and **Area Weight** change collapse ranking; they do not release border or seam locks, and lowering them does not guarantee further reduction. **Smart Link** does not weld disconnected hair pieces for FA-QEM.

**Some safeguards cannot be switched off:** material-membership and local topology checks, rejection of very short edges and degenerate surviving faces, and protection of distinct-index source faces that are flat at rest on meshes with skin weights or blend shapes. Those flat faces can open during animation, so FA-QEM retains them and locks their vertices even with border and seam protection off. There is no user-facing switch to disable that flat-face safeguard.

Compare the original and simplified hair from the front, back, and side, including strand tips and overlaps. Then test hair motion, expressions, and clothing toggles in Play Mode or VRChat. **Preview UVs** helps inspect UV changes but cannot validate animation. Verify the final budget with **Analyze NDMF Build** or the built avatar. If cracks or silhouette loss appear, restore the option and recover triangles from another mesh. These protections reduce risk; they do not guarantee hole-free hair in every pose.

##### FA-QEM controls

These defaults come from `FaQemOptions.Default`:

| Control | Default | Effect |
| --- | --- | --- |
| Plane Area Weight | `1` | With inverse area weighting on, divides the source-plane weight by this value times triangle area. With it off, this value is the source-plane weight directly. Must be positive. |
| Boundary Weight | `500` | Strength of source boundary-curvature constraints. A soft penalty, not a border lock. |
| Normal Weight | `0.01` | Strength of tangent-plane constraints from original normals, with geometric normal fallback where needed. |
| Area Weight | `100` | Strength of the separate boundary swept-area penalty used to rank collapses. |
| Use Inverse Area Weighting | On | Gives smaller source triangles greater plane weight. |
| Preserve Attribute Seams | On | Locks coincident split vertex records, including those with matching attributes. |
| Min Normal Dot | `0.2` | Minimum dot product between a surviving face's normals before and after each collapse. Larger values reject more changes. Range: `0`–`1`. |
| Max Surface Deviation | `0.0005` for new avatars; core `0` | Optional limit on sampled distance from the original surface, expressed as a fraction of the source bounds diagonal. `0.001` means 0.1%. Tighter values may stop above the requested triangle count. |

FA-QEM uses its own **Min Normal Dot** and feature weights. The legacy Meshia **Preserve Surface Curvature** and **Smart Link** controls do not configure FA-QEM's collapse metric or connect disconnected components.

With **Preserve Border Edges** enabled, collapses touching boundary vertices are rejected. The boundary-area penalty is therefore zero for accepted interior collapses, and source boundary quadrics stay attached to the locked vertices. Increasing **Area Weight** or **Boundary Weight** does not protect the interior of close-fitting clothing under this policy.

**Max Surface Deviation** adds a sampled, one-sided envelope around the immutable original mesh. It checks the proposed vertex, surviving triangle edge midpoints, and triangle centroids. If the optimal position fails, FA-QEM tries the endpoints and midpoint and queues a valid alternative at its actual cost. The original surface is indexed once per simplification. This limits accumulated surface drift; it does not guarantee clearance from another mesh, continuous containment between samples, or preservation under every animated pose. Nearby source layers can also satisfy a nearest-surface test. New avatars enable it at `0.0005`; existing saved values and core API defaults are unchanged. Increasing the tolerance or disabling it is an explicit quality/budget tradeoff.

For layered clothing, inspect both sides of an overlap: an inner layer moving outward can poke through an outer layer whose own simplification is acceptable. Apply appropriate surface protection to both layers and compare against the unsimplified outfit in Play Mode, including its visibility toggles and animations. Use the actual built triangle count when checking an avatar budget; a protected mesh can stop above its requested target, and other avatar build steps can change the count.

NDMF preview and build use the same per-mesh detection for new Auto settings and retain the same single-body selection for saved legacy Auto settings. Auto preview invalidates when its rig, candidate renderers, meshes, or simplifier configuration changes. Preview still covers only participating preview passes; source geometry and final counts can differ from a complete build, so use **Analyze NDMF Build** or the actual built avatar for the final budget.

All geometric terms and this tolerance use coordinates normalized by the source bounds diagonal. The mixed error terms scale differently, so these weights describe this normalized implementation; equivalence to unnormalized paper weights is not assumed. Edges shorter than `1e-8` of that diagonal are rejected even when seam protection is disabled. Coincident split records are locked when **Preserve Attribute Seams** is enabled; no automatic welding or virtual edges are introduced.

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

The cascading inspector distinguishes Meshia's output from the estimated final avatar count after downstream NDMF tools:

- **Meshia output** reports triangles before downstream processing.
- **AAO estimate** accounts for meshes and polygons expected to be removed by Avatar Optimizer when its compatible API is available.
- **Analyze NDMF Build** runs a temporary full NDMF build and records the exact final triangle count.

A completed analysis calibrates **Adjust** and **Auto Adjust**, allowing their targets to account for downstream removals instead of unnecessarily reducing every renderer. The inspector marks analysis data as stale after relevant avatar settings change; rerun **Analyze NDMF Build** to refresh it.

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

[このフォークの VPM リポジトリ](https://drcain1.github.io/Meshia.MeshSimplification/vpm/index.json)を追加し、プレリリースを有効にして **Meshia — drcain fork** をインストールしてください。パッケージ ID は `io.github.drcain1.meshia.mesh-simplification` です。上流版 Meshia を置き換えるため、両方を同時にインストールしないでください。[移行手順](docs/FORK_DISTRIBUTION.md)も参照してください。

[anatawa12 の VPM リポジトリ](https://vpm.anatawa12.com/vpm.json)から、依存パッケージ `com.anatawa12.custom-localization-for-editor-extension` の 1.x（1.2.1 以上）を導入してください。アバター連携には NDMF、カスケード機能には Modular Avatar も必要です。どちらも [nadena の VPM リポジトリ](https://vpm.nadena.dev/vpm.json)から導入できます。

Unity Package Manager で Git URL から導入する場合は、依存パッケージを先にインストールしてください。UPM は `vpmDependencies` を自動解決しません。上流の VPM リポジトリから入手する版には、このフォークの変更は含まれません。

### 使い方

#### NDMF 連携

NDMF が導入されたプロジェクトでは、モデルに `MeshiaMeshSimplifier` を追加して使用できます。Edit Mode で軽量化結果をプレビューしながら設定を調整できます。

#### アバター全体の三角形数を配分する

レンダラーの GameObject 名が **Body** の新規項目は、大文字・小文字を区別せず、初期状態で **軽量化対象外** になります。多くのアバターで顔に使われる名前に基づく初期設定であり、顔の自動検出ではありません。`Body_base` はこの規則では除外しません。別の名前の顔メッシュは手動で除外してください。必要なら `Body` も手動で有効にできます。項目の更新は保存済みの選択を維持し、配分の **リセット（Reset）** はこの名前に基づく初期状態に戻します。

各メッシュ行の鍵と歯車の隣に **変形の保護アイコン** があります。色は設定の状態を表し、緑は対象の保護項目が有効、黄褐色は一部だけ有効、薄い表示はすべて無効です。**自動判定も有効な設定として扱います。** ボーンの対応状況で色は変わらないため、同じ設定なら別のメッシュでも同じ色になります。緑をクリックすると無効に、黄褐色や薄い表示をクリックすると有効にします。クリックするとボーンウェイトと関節付近の頂点の保護を明示的なオン／オフに切り替え、数値の上限とボーンの選択は維持します。関節付近の頂点の保護は FA-QEM のみで使用し、他のアルゴリズムではボーンウェイトの設定だけを表示します。軽量化対象外のメッシュやスキンメッシュ以外では操作できません。境界・継ぎ目・表面の保護は変更しません。個別設定と自動判定は歯車から調整でき、変更は Unity の元に戻す操作に対応しています。このアイコンは、ビルド後のメッシュの変形品質や保護範囲を保証する表示ではありません。

アバター直下の子オブジェクトに **Meshia Cascading Avatar Mesh Simplifier** を追加すると、対象レンダラー全体で共有する三角形数の目標を設定できます。**Adjust** は現在の目標数を各レンダラーに手動で配分し、**Auto Adjust** は各レンダラーの目標数を自動更新します。

**初期設定と保護プリセット：** 新規メッシュ項目と単体コンポーネントでは、**変形の保護はオフ** で開始します（`MeshSimplifierOptions.AvatarInitial`）。境界・属性シームの保持と、元の表面からのずれの上限 **0.0005**（境界ボックスの対角線の 0.05%）は有効です。保護プリセットか各行のアイコンから明示的に有効にしてください。

- **保守的（Conservative）**：従来の保護設定を全項目に適用します。メッシュごとのボーンウェイト自動保護、強さ **2**、ウェイト差の上限 **0.10**、破棄するウェイトの上限 **0.02**、手足の既定ボーン選択による関節付近の頂点保持が有効です。
- **積極的（Aggressive）**：検出した手・指・髪のメッシュと、名前が `Body` または `Face` のメッシュには同じ保護を適用し、それ以外ではボーンウェイトと関節付近の頂点の保護をオフにします。境界・継ぎ目・表面の設定は保守的な値を維持します。目標三角形数への到達を保証するものではありません。

判定には正のボーンウェイト、ヒューマノイドの手・指の対応、別の衣装リグの同名ボーン、一般的な英語・日本語の手や髪の名前を使用します。未使用のボーン枠は対象にしません。特殊な名前は見落とす場合があります。メッシュを取得・読み取りできない場合やウェイトが不正な場合は保守的な保護を維持します。適用時にメッシュ単位で判定するため、手を含む身体メッシュ全体は保守的な設定になります。服・肘・膝にも追加の保護が必要な場合があります。各行のアイコンとアニメーション中の見た目を確認し、メッシュやリグの変更後は必要に応じて再適用してください。

両ボタンはアルゴリズム・目標数・対象外の設定・固定配分を維持し、元に戻す操作に対応します。対象外の顔は有効にしません。保存済み設定は自動変更しません。各メッシュの **設定をリセット（Reset Options）** は保守的なオプション値を明示的に適用しますが、別項目のボーン選択は維持します。コア API の `MeshSimplifierOptions.Default` は互換性のため変更していません。自動判定は NDMF が処理するため、API を直接呼ぶ場合は `SkinningProtection` を解決するか、明示的に有効にしてください。

**7万三角形の目標より保護を優先します。** 自動調整は配分のみを変更し、保護を自動解除しません。直近のプレビューで目標を超えたメッシュをインスペクターに表示し、ビルド時にも FA-QEM の実際の出力数と目標数を警告します。プレビューが古い場合があるため、**NDMFビルド解析（Analyze NDMF Build）** で最終的な数を確認してください。目標に届かない場合は配分か品質上の妥協点を明示的に見直し、アップロード前にポーズ・表情・重なった服を確認してください。


ボーンの動きに合わせて変形するメッシュには、実験的な **ボーンによる変形の保護（Skinning Protection）** があります。**関節を動かしたときの形状を保護（Protect joint deformation）** で有効にできます。ボーンウェイトは、各ボーンが頂点の動きに与える影響の強さです。コア API の初期値はオフです。新規 NDMF コンポーネントとメッシュ項目では保護はオフです。保護プリセットまたは歯車から **変形するメッシュを自動保護** を有効にできます。複数の有効なボーンに正のウェイトを持つ各メッシュを個別に判定し、服、分割された身体、独自のボーン構成も対象にします。単一ボーンで動く剛体メッシュは対象外です。保存済みの従来の Auto は身体候補を一つに絞る以前の判定を維持し、インスペクターにその旨を表示します。既存の明示的な設定は保持されます。有効時は Blender 方式と FA-QEM の両方で、エッジの両端にあるボーンウェイトの差と、統合時に失われるボーン影響量を確認します。`Strength` は評価コストへの重み、`Max Weight Distance` は両端のウェイト分布の差の上限、`Max Discarded Weight` は出力のボーン影響数制限によって失われるウェイト量の上限です。保護により削減が途中で止まる場合があり、すべてのポーズで同じ見た目になることを保証する機能ではありません。代表的な関節ポーズで確認してください。

各レンダラーの歯車メニューでアルゴリズムを選択できます。

- **FA-QEM** — 新規のカスケードメッシュ項目の初期値です。元の形状、境界の曲率、元の法線、境界移動に伴う面積ペナルティを使ってエッジの統合を評価します。属性やトポロジーの制約により、目標数より多い状態で止まることがあります。
- **Blender Decimate** — 手動で選択できる代替アルゴリズムです。Blender 互換のエッジ統合処理で、レンダラーに配分された三角形数を目指します。[^blender-ja]
- **Meshia** — 従来の Meshia の軽量化アルゴリズムです。既存の保持設定や補間設定を利用できます。
- **UV Loop Dissolve** — 四角形に近い接続構造を再構築し、安全に除去できるエッジループの全体または一部を削減します。UV シーム、マテリアル境界、ハードエッジ、開いた境界、非多様体領域を保護します。ループ削減だけで目標に達しない場合は Blender Decimate に切り替わり、プレビューにフォールバックの通知が表示されます。

**既存のメッシュ項目のアルゴリズムとオプションは変更されません。** FA-QEM は安全上の制約で目標に達しなくても、別のアルゴリズムへ自動で切り替わりません。必要に応じて三角形数の配分を調整するか、メッシュごとに別のアルゴリズムを選択してください。新規の単体 `MeshiaMeshSimplifier` も FA-QEM を使用し、最初の目標は元の三角形数の半分です。既存のコンポーネントと、C# で明示的に指定したターゲットは変更されません。

インスペクター上部の **Algorithm for All Meshes** は、無効な項目も含めて全メッシュのアルゴリズムを一括変更します。複数のアルゴリズムが混在している場合は **Mixed** と表示されます。個別の目標数、有効／無効の状態、オプションは保持されます。Unity の **Edit → Undo**（Windows では Ctrl+Z）で一括変更を取り消せます。一括変更後も、各レンダラーの歯車メニューで個別に選び直せます。

歯車メニューの **Preview UVs** では、元のメッシュと軽量化後の UV を重ねて比較できます。三角形数の目標や軽量化設定を変更すると、プレビューも更新されます。

#### 形状のみを軽量化する FA-QEM

FA-QEM は、元の面の平面、境界の曲率、元の法線に基づく接平面、境界移動に伴う面積ペナルティを使ってエッジ統合の優先度を決め、指定した絶対三角形数を目指して軽量化します。各レンダラーの歯車メニュー、または **Algorithm for All Meshes** から選択できます。

##### 対応機能

- **元のマテリアルとテクスチャを維持：** マテリアルの割り当てとテクスチャアセットを保持します。テクスチャベイク、アトラス生成、UV の再配置、テクスチャ圧縮は行いません。シェーダー機能やマテリアルのアニメーションは元のマテリアルを使用します。
- **メッシュ属性：** UV0～UV7、法線、接線、頂点カラー、ボーンウェイト、ブレンドシェイプの各フレームを処理します。統合された頂点の属性は補間されるため、チャンネルを保持しても値や見た目が完全に一致するとは限りません。C# の `UseBarycentricCoordinateInterpolation` を有効にすると、見た目に関わる属性とブレンドシェイプには通常のエッジ補間ではなく重心座標補間を使います。ボーンウェイトは引き続きエッジ両端の値から補間します。
- **分離頂点のシーム保護：** **Preserve Attribute Seams** の初期値はオンです。メッシュの大きさに応じた許容誤差内で同じ位置にある別々の頂点を固定し、独立して軽量化される面同士が離れるのを防ぎます。属性が同じ頂点や、UV シーム・ハード法線のために分離された頂点も対象です。元のメッシュの溶接や修復は行いません。
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

**Boundary Weight** と **Area Weight** は境界の移動を抑える評価項目であり、境界を固定する設定ではありません。**Preserve Attribute Seams** も同じ位置にある分離頂点を保護するもので、開いた境界すべてを保護するものではありません。境界を厳密に維持したい場合は **Preserve Border Edges** を有効にし、古い項目の設定も確認してください。境界、シーム、ボーンによる変形の保護によって有効な統合候補が減り、厳しい三角形数の目標に達しない場合があります。

##### 目標数の確認と、さらに削減する方法

**目標三角形数（配分用）** は配分の目標であり、最終的な数の上限を保証するものではありません。インスペクターには現在の正常なNDMF解析結果と、超過数または残りの数を表示します。古い結果はその旨を表示し、失敗した解析は確定した数として扱いません。**削減方法を確認** では、直近のプレビューの超過数が大きい順の一覧、軽量化対象外のメッシュ、その他のメッシュを別々に表示します。**メッシュ設定を開く** で対象の項目へ移動できます。プレビューや元のメッシュの数は、ビルド後の各メッシュの最終的な数ではありません。プレビューがない場合も削減量を計測済みとは表示しません。解析後も一覧は開けますが、現在の正常なビルド結果があればプレビューの案内は非表示になります。

見える部分の削減を強める前に、常に衣装で隠れている部分を見直してください。[AAO Remove Mesh By BlendShape](https://vpm.anatawa12.com/avatar-optimizer/ja/docs/reference/remove-mesh-by-blendshape/) では、素体を隠す適切なブレンドシェイプを指定してポリゴンを削除できます。[Modular Avatar Shape Changer](https://modular-avatar.nadena.dev/ja/docs/reference/reaction/shape-changer) の **Delete** モードでも削除できます。単に縮小したりシェーダーで隠したりするだけでは、ポリゴンは削除されません。Shape Changer の削除をアニメーションで切り替える場合、見えなくなっても報告されるポリゴン数は減りません。衣装の切り替えで必要になる部分は残してください。ポーズと衣装を変えて削除範囲を確認し、ビルド全体を再解析します。これらのツールは任意であり、案内からコンポーネントや依存パッケージを自動追加することはありません。

まだ超過する場合は、大きい項目から、対象外メッシュの軽量化、使わないアクセサリー、固定配分を見直してください。保護を緩める場合はメッシュごとに明示的に変更し、動かした状態を比較します。**自動調整は、手動で下げた目標数を他のメッシュに再配分しません。** スライダーと数値入力は同じ動作です。目標数を上げると、推定予算に収まるよう他のロックされていない配分を下げる場合があります。余った予算を使う場合は **Adjust** を明示的に押してください（手動で下げた目標数が上がる場合があります）。固定・対象外の設定は維持されます。変形の保護がオフでも境界・シーム・表面からのずれの制限は残るため、実際の出力数が目標を超える場合があります。**自動調整は目標の配分のみを行います。** 削減可能な数の計測、超過分の反復的な再配分、保護の自動解除は行いません。目標数が結果より少ないからといって、安全にさらに削減できるとは限りません。

##### 指や関節を曲げたときの形状を保つ

FA-QEM の **関節付近の頂点を保持（Preserve Joint Transitions）** は、最も強く影響するボーンが切り替わる部分と、そのすぐ周囲の頂点を元の状態で保持します。他の部分は引き続き軽量化できるため、手や指の全体を固定する機能ではありません。

新規アバター設定とコア API の初期値は **オフ** です。保守的プリセットでは全項目、積極的プリセットでは検出した項目で有効にします。**関節を動かしたときの形状を保護** や自動・手動の設定とは独立しています。アバター全体の軽量化では、各メッシュの **FA-QEM設定** で有効にし、下の **関節を保護するボーン（Joint Protection Bones）** で対象を選びます。新規項目では両手と全指、両腕の上腕・前腕、両脚の太もも・すね、足を選択し、使用されているヒューマノイドボーンの手首・肘・膝・足首付近を保護します。保存済みの選択は自動で拡張しません。**境界エッジを保持するボーン** とは別の選択で、閉じた指の表面なども保護します。何も選択しなければ、その項目には関節保護を適用しません。存在しないヒューマノイドボーンは無視します。単体の軽量化では、すべてのボーンの切り替わり部分を対象にします。

保護した頂点の位置と属性を保持するため、目標三角形数より多い状態で止まる場合があります。すべてのポーズを検査する機能ではなく、体と服のめり込みや、保護範囲外の変形を完全に防ぐものではありません。手を開いた状態、途中まで曲げた状態、握った状態で比較し、ビルド後のアバター全体の三角形数を確認してください。現在は FA-QEM のみが対応しています。

##### 髪の保護と、さらに削減するための設定

**髪専用のプリセットの自動適用や、名前による髪の判別は行いません。** 髪にも他のメッシュと同じ FA-QEM の安全策が働きます。薄い板状の髪や独立した毛束には、開いた境界や、UV・法線の継ぎ目などで同じ位置に重なる別々の頂点が多いことがあります。**境界エッジを保持（Preserve Border Edges）** と **属性の継ぎ目を保持（Preserve Attribute Seams）** の初期値はオンで、多くの頂点が固定される場合があります。目標三角形数を下げるだけでは固定を解除できないため、目標数よりかなり多い状態で削減が止まることがあります。

まずは初期設定を使い、見た目に重要な髪には多めに三角形数を配分してください。さらに削減したい場合は、コピー上で **対象の髪レンダラーの歯車メニュー** を開き、一度に一つずつ設定を変えて比較します。

| 設定 | 保護を緩める方法 | 注意する変化 |
| --- | --- | --- |
| **境界エッジを保持（Preserve Border Edges）** | オフにすると、開いた境界の頂点を移動・統合できるようになります。**境界エッジを保持するボーン（Preserve Border Edges Bones）** も確認してください。主設定がオフでも、選択したボーンに対応する境界頂点は固定される場合があります。 | 毛先や板状の髪の輪郭の変化、毛束の間の隙間。 |
| **属性の継ぎ目を保持（Preserve Attribute Seams）** | オフにすると、同じ位置にある分離頂点の固定を解除します。頂点同士を溶接する機能ではなく、同じ頂点が境界保護で固定されている場合もあります。 | 頂点が別々に動くことによる亀裂、UV の不連続、陰影の変化。 |
| **元の表面からのずれの上限（Maximum Surface Deviation）** | 有効な場合は正の許容値を大きくするか、`0` にして無効にします。新規アバターの初期値は `0.0005`、コア API は `0` です。 | 元の形状からのずれ、頭や服へのめり込み。 |
| **面法線の内積の下限（Minimum Face Normal Dot）** | FA-QEM 側の値を `0`～`1` の範囲で下げると、統合時の面の向きの変化をより大きく許容します。 | 鋭い折れ目や陰影の変化。他のトポロジー検査は引き続き適用されます。 |
| **関節を動かしたときの形状を保護（Protect joint deformation）** | このメッシュで有効な場合は **ボーンウェイト差の上限（Maximum Skin Weight Distance）** / **破棄するボーンウェイトの上限（Maximum Discarded Skin Weight）** を大きくするか、保護をオフにします。Auto が選択されている場合は、先に **変形するメッシュを自動保護** をオフにしてください。**Strength** を下げるだけでは評価コストが変わるだけで、拒否条件は解除されません。 | 髪ボーンの動きやブレンドシェイプを確認してください。メッシュごとの自動保護は複数ボーンで動く髪も対象にしますが、髪の名前による判定や物理挙動の検査は行いません。 |

**Boundary Weight**、**Normal Weight**、**Area Weight** は統合候補の優先順位に関わる重みで、境界やシームの固定を解除する設定ではありません。値を下げても、必ず三角形数が減るとは限りません。**Smart Link** も、FA-QEM で離れた髪のパーツを溶接する機能ではありません。

**オフにできない安全策もあります。** マテリアル所属と局所的なトポロジーの検査、極端に短いエッジや統合後に退化する面の拒否、アニメーションで開く可能性がある面の保護は常に有効です。ボーンウェイトまたはブレンドシェイプを持つメッシュでは、異なる頂点インデックスを持ち、初期状態で面積がゼロの面を保持して頂点を固定します。境界とシームの保護がオフでも適用され、この面の保護を無効にするユーザー向けのスイッチはありません。

元の髪と軽量化後の髪を正面・背面・側面から比較し、毛先や重なりを確認してください。その後、Play Mode または VRChat で髪の動き、表情、衣装の切り替えも確認します。**UVをプレビュー（Preview UVs）** は UV の変化を確認する補助であり、アニメーション品質の確認にはなりません。最終的な三角形数は **Analyze NDMF Build** またはビルド後のアバターで確認してください。亀裂や輪郭の崩れが出る場合は設定を戻し、他のメッシュから削減量を確保してください。これらの保護は問題を減らすためのもので、すべてのポーズで髪に穴が開かないことを保証するものではありません。

##### FA-QEM の設定

以下は `FaQemOptions.Default` の初期値です。画面上の項目と照合できるよう、設定名は英語表記を併記しています。

| 設定 | 初期値 | 効果 |
| --- | --- | --- |
| Plane Area Weight | `1` | 逆面積重み付けがオンの場合、この値と三角形面積の積で元の面の平面重みを割ります。オフの場合は、この値を平面重みとして直接使います。正の値が必要です。 |
| Boundary Weight | `500` | 元の境界曲率に基づく制約の強さです。境界の固定ではなく、移動へのペナルティです。 |
| Normal Weight | `0.01` | 元の法線に基づく接平面制約の強さです。必要に応じて形状から求めた法線を使います。 |
| Area Weight | `100` | 統合の優先度を決める、境界移動に伴う面積ペナルティの強さです。 |
| Use Inverse Area Weighting | オン | 元の小さい三角形ほど平面重みを大きくします。 |
| Preserve Attribute Seams | オン | 属性が同じものも含め、同じ位置にある分離頂点を固定します。 |
| Min Normal Dot | `0.2` | 統合前後で残る面の法線同士の内積の下限です。大きいほど多くの変化を拒否します。範囲は `0`～`1` です。 |
| Max Surface Deviation | 新規アバター `0.0005`、コア API `0` | 元の表面からサンプル点までの距離の上限です。元のメッシュのバウンディングボックスの対角線長に対する比率で指定し、`0.001` は 0.1% を意味します。厳しくすると目標三角形数より多い状態で止まる場合があります。 |

FA-QEM は専用の **Min Normal Dot** と特徴量の重みを使います。従来の Meshia の **Preserve Surface Curvature** や **Smart Link** は、FA-QEM の評価基準を変更したり、離れたメッシュ部分を接続したりする設定ではありません。

**Preserve Border Edges** が有効な場合は境界頂点に触れる統合を拒否するため、受け入れられる内部エッジの統合では境界面積ペナルティがゼロになります。元の境界の二次形式も固定された頂点に残ります。この状態で **Area Weight** や **Boundary Weight** を上げても、身体に密着する服の内部領域は保護できません。

**Max Surface Deviation** は、変更しない元のメッシュを基準に、軽量化後のサンプル点から元の表面への距離を制限します。統合先の頂点、残る三角形の各エッジ中点、三角形の重心を検査します。最適位置が条件を満たさなければ、エッジの両端と中点も試し、有効な候補を実際のコストでキューに登録します。元の表面の検索用データは軽量化ごとに一度構築します。累積する表面のずれを抑える機能ですが、別のメッシュとの隙間、サンプル点の間の連続した表面、あらゆるアニメーション中の形状を保証するものではありません。近くにある元の別の層が距離判定を満たす場合もあります。新規アバターでは `0.0005` で有効です。保存済みの値とコア API の初期値は維持します。許容差を緩める場合や無効にする場合は、品質と達成可能な三角形数のバランスを確認してください。

重ね着では、重なる両方の層を確認してください。外側の服の軽量化に問題がなくても、内側の層が外側へ動くと貫通する場合があります。必要な表面保護を両方に設定し、Play Mode で表示切り替えやアニメーションも含めて元の衣装と比較してください。アバター全体の目標を確認するときは、実際にビルドされた三角形数を使用してください。保護によって各メッシュが目標より多い状態で止まる場合があり、後続のビルド処理でも数が変わります。

NDMF プレビューとビルドは、メッシュごとの自動設定にはメッシュごとの判定を、保存済みの従来の Auto には身体メッシュ選択を同じ方法で適用します。Auto のプレビューはリグ、候補レンダラー、メッシュ、軽量化設定の変更に応じて再評価されます。ただし、プレビューが扱うのは参加しているプレビューパスだけです。完全なビルドとは入力形状や最終的な数が異なる場合があるため、最終確認には **Analyze NDMF Build** または実際のビルド結果を使用してください。

幾何学的な評価項目と許容距離は、元のメッシュのバウンディングボックスの対角線長で正規化した座標を使います。各評価項目のスケール依存性は異なるため、これらの重みが論文の非正規化の重みと等価とは限りません。対角線長の `1e-8` より短いエッジは、シーム保護が無効でも統合を拒否します。同じ位置にある分離頂点は **Preserve Attribute Seams** が有効な場合に固定します。自動溶接や仮想エッジは導入しません。

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

解析が完了すると **Adjust** と **Auto Adjust** の配分が補正され、後続処理による削除を見込んだ目標を設定できます。これにより、各レンダラーを必要以上に削減することを避けられます。関連するアバター設定を変更すると解析結果が古いことが表示されるため、**Analyze NDMF Build** を再実行してください。

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

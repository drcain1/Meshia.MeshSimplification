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

For skinned meshes, the options include an experimental **Protect joint deformation** setting. The core API keeps it disabled by default. New NDMF components use Auto, which enables protection only when a single anatomical body candidate is detected; existing explicit policies are retained. When enabled, the native Blender-style and FA-QEM candidate jobs compare endpoint skin-weight variation and the simulated discarded influence weight before accepting a collapse. `Strength` controls the graduated cost, `Max Weight Distance` limits endpoint total variation, and `Max Discarded Weight` limits influence loss caused by the fixed output influence width. The protection can reduce the achievable triangle count; the settings do not provide a pose-equivalence guarantee and should be evaluated with representative joint poses.

Each renderer's cogwheel provides an algorithm selector:

- **FA-QEM** — the default for new cascading mesh entries. Uses source geometry, boundary curvature, original normals, and a separate boundary-area penalty to rank edge collapses. Attribute and topology constraints can stop reduction above the requested count.
- **Blender Decimate** — a manually selectable alternative using the Blender-compatible collapse implementation to reach the renderer's allocated triangle count.[^blender]
- **Meshia** — uses Meshia's original simplification algorithm and exposes its existing preservation and interpolation options.
- **UV Loop Dissolve** — reconstructs quad-like topology and removes safe complete or partial edge-loop segments. UV seams, material boundaries, hard edges, borders, and non-manifold areas are protected. If loop removal cannot reach the target, Blender Decimate finishes the operation and the preview displays a fallback notification.

Existing entries retain their saved algorithms and options. FA-QEM does not automatically switch algorithms when its safety constraints prevent reaching a target; adjust the budget or choose an alternative per mesh. The standalone `MeshiaMeshSimplifier` component and explicit C# targets retain their existing defaults.

The **Algorithm for All Meshes** dropdown at the top of the inspector applies an algorithm to every mesh entry, including disabled entries. It displays **Mixed** when entries use different algorithms. Individual triangle targets, enabled states, and options are retained. Use Unity's **Edit → Undo** (Ctrl+Z on Windows) to undo the whole batch change. You can still override individual algorithms through each renderer's cogwheel.

Use **Preview UVs** inside a renderer's cogwheel to compare the original and simplified UV layouts. The preview updates as triangle targets and simplification settings change.

#### Geometry-only FA-QEM

FA-QEM reduces a mesh toward an absolute triangle budget using source surface planes, boundary curvature, original-normal tangent planes, and a separate boundary swept-area penalty to rank edge collapses. Select **FA-QEM** in a renderer's cogwheel, or use **Algorithm for All Meshes** at the top of the cascading inspector.

##### Supported features

- **Original materials and textures:** keeps the existing material assignments and texture assets. There is no texture baking, atlas generation, UV repacking, or texture compression pass. Shader features and material animation continue to use the original materials.
- **Mesh attributes:** carries UV0–UV7, normals, tangents, vertex colors, skin weights, and blend-shape frames through the shared mesh simplification pipeline. Attributes at collapsed vertices are interpolated; retaining these channels does not mean their values or appearance remain identical. The C# option `UseBarycentricCoordinateInterpolation` selects barycentric interpolation for appearance attributes and blend shapes instead of the default edge interpolation; skin weights still use endpoint interpolation.
- **Split-vertex seam protection:** **Preserve Attribute Seams** is on by default. It locks coincident vertex records within a scale-dependent tolerance, including split records whose attributes match, so independently simplified face groups do not pull those copies apart. This also protects coincident splits used for UV seams and hard normals. It does not weld or repair the source mesh.
- **Material boundaries:** rejects collapses between vertices with different submesh memberships.
- **Topology and face checks:** checks the local edge-collapse link condition and rejects unsafe non-manifold configurations, degenerate surviving faces, and excessive face-normal changes. These are local safeguards, not a global self-intersection or mesh-repair guarantee.
- **Flat faces in animated meshes:** source triangles with distinct vertex indices can have zero area at rest and open under animation. On meshes with blend shapes or skin weights, FA-QEM retains these faces and locks their vertices, even if border or seam protection is disabled. Static zero-area faces can still be removed. This conservative protection may limit reduction near those faces.
- **Optional border locking:** **Preserve Border Edges** fixes vertices on topological open boundaries. Selected-bone border protection is also supported for skinned meshes when only specific regions need protection.
- **Experimental skinning protection:** **Protect joint deformation** adds a skin-weight cost and rejection limits for endpoint weight differences and discarded influences. The core API defaults to off; new NDMF entries use the Auto policy described above. Check representative poses and blend shapes after reduction.
- **Editor and build integration:** supports per-renderer budgets, cascading budget allocation, NDMF previews/builds, and the original/simplified **Preview UVs** overlay.
- **Runtime and diagnostic APIs:** supports synchronous, asynchronous, and batch simplification, result reports, and optional collapse history with source-index mappings.

##### Border preservation defaults to on

**Preserve Border Edges defaults to on for new mesh entries and `MeshSimplifierOptions.Default`.** It can be changed per mesh in the renderer's cogwheel. Leave it enabled when open edges, such as necklines, hems, or other openings, must retain their shape. FA-QEM then rejects collapses involving those boundary vertices, keeping their positions and UVs fixed. A topological boundary is an edge used by only one triangle; this option does not freeze every camera-visible silhouette on a closed surface.

Changing algorithms, including through **Algorithm for All Meshes**, retains each entry's border setting. **Reset Options** restores the default and turns border preservation on. Existing saved entries keep their stored values, including an explicit off value; they are not automatically migrated. This is the shared options default, including for direct C# calls; disable it explicitly when unrestricted boundary reduction is intended.

**Boundary Weight** and **Area Weight** discourage boundary movement but do not lock borders. Likewise, **Preserve Attribute Seams** protects coincident split vertices, not every open edge. Keep **Preserve Border Edges** enabled when exact open-boundary retention is required, and check this setting on older entries. Border, seam, and skinning protection can leave fewer legal collapses and prevent reaching an aggressive triangle target.

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
| Max Surface Deviation | `0` (off) | Optional limit on sampled distance from the original surface, expressed as a fraction of the source bounds diagonal. `0.001` means 0.1%. Tighter values may stop above the requested triangle count. |

FA-QEM uses its own **Min Normal Dot** and feature weights. The legacy Meshia **Preserve Surface Curvature** and **Smart Link** controls do not configure FA-QEM's collapse metric or connect disconnected components.

With **Preserve Border Edges** enabled, collapses touching boundary vertices are rejected. The boundary-area penalty is therefore zero for accepted interior collapses, and source boundary quadrics stay attached to the locked vertices. Increasing **Area Weight** or **Boundary Weight** does not protect the interior of close-fitting clothing under this policy.

**Max Surface Deviation** adds a sampled, one-sided envelope around the immutable original mesh. It checks the proposed vertex, surviving triangle edge midpoints, and triangle centroids. If the optimal position fails, FA-QEM tries the endpoints and midpoint and queues a valid alternative at its actual cost. The original surface is indexed once per simplification. This limits accumulated surface drift; it does not guarantee clearance from another mesh, continuous containment between samples, or preservation under every animated pose. Nearby source layers can also satisfy a nearest-surface test. The option defaults to off, including existing serialized settings, so enabling it is an explicit quality/budget tradeoff.

For layered clothing, inspect both sides of an overlap: an inner layer moving outward can poke through an outer layer whose own simplification is acceptable. Apply appropriate surface protection to both layers and compare against the unsimplified outfit in Play Mode, including its visibility toggles and animations. Use the actual built triangle count when checking an avatar budget; a protected mesh can stop above its requested target, and other avatar build steps can change the count.

NDMF preview resolves Legacy, Auto, On, and Off joint-protection policies through the same body-selection logic as the build. Auto preview invalidates when its rig, candidate renderers, meshes, or simplifier configuration changes. Preview still covers only participating preview passes; source geometry and final counts can differ from a complete build, so use **Analyze NDMF Build** or the actual built avatar for the final budget.

All geometric terms and this tolerance use coordinates normalized by the source bounds diagonal. The mixed error terms scale differently, so these weights describe this normalized implementation; equivalence to unnormalized paper weights is not assumed. Edges shorter than `1e-8` of that diagonal are rejected even when seam protection is disabled. Coincident split records remain locked; no automatic welding or virtual edges are introduced.

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

アバター直下の子オブジェクトに **Meshia Cascading Avatar Mesh Simplifier** を追加すると、対象レンダラー全体で共有する三角形数の目標を設定できます。**Adjust** は現在の目標数を各レンダラーに手動で配分し、**Auto Adjust** は各レンダラーの目標数を自動更新します。

スキンメッシュには、実験的な **Protect joint deformation**（関節変形の保護）があります。コア API の初期値はオフです。新規 NDMF コンポーネントとメッシュ項目には **Auto** が設定され、ヒューマノイドの身体メッシュ候補が一つに絞れた場合にのみ保護を有効にします。既存の明示的な設定は保持されます。有効時は Blender 方式と FA-QEM の両方で、エッジの両端にあるスキンウェイトの差と、統合時に失われるボーン影響量を確認します。`Strength` は評価コストへの重み、`Max Weight Distance` は両端のウェイト分布の差の上限、`Max Discarded Weight` は出力のボーン影響数制限によって失われるウェイト量の上限です。保護により削減が途中で止まる場合があり、すべてのポーズで同じ見た目になることを保証する機能ではありません。代表的な関節ポーズで確認してください。

各レンダラーの歯車メニューでアルゴリズムを選択できます。

- **FA-QEM** — 新規のカスケードメッシュ項目の初期値です。元の形状、境界の曲率、元の法線、境界移動に伴う面積ペナルティを使ってエッジの統合を評価します。属性やトポロジーの制約により、目標数より多い状態で止まることがあります。
- **Blender Decimate** — 手動で選択できる代替アルゴリズムです。Blender 互換のエッジ統合処理で、レンダラーに配分された三角形数を目指します。[^blender-ja]
- **Meshia** — 従来の Meshia の軽量化アルゴリズムです。既存の保持設定や補間設定を利用できます。
- **UV Loop Dissolve** — 四角形に近い接続構造を再構築し、安全に除去できるエッジループの全体または一部を削減します。UV シーム、マテリアル境界、ハードエッジ、開いた境界、非多様体領域を保護します。ループ削減だけで目標に達しない場合は Blender Decimate に切り替わり、プレビューにフォールバックの通知が表示されます。

**既存のメッシュ項目のアルゴリズムとオプションは変更されません。** FA-QEM は安全上の制約で目標に達しなくても、別のアルゴリズムへ自動で切り替わりません。必要に応じて三角形数の配分を調整するか、メッシュごとに別のアルゴリズムを選択してください。単体の `MeshiaMeshSimplifier` コンポーネントと、C# で明示的に指定するターゲットの既存の初期設定は維持されます。

インスペクター上部の **Algorithm for All Meshes** は、無効な項目も含めて全メッシュのアルゴリズムを一括変更します。複数のアルゴリズムが混在している場合は **Mixed** と表示されます。個別の目標数、有効／無効の状態、オプションは保持されます。Unity の **Edit → Undo**（Windows では Ctrl+Z）で一括変更を取り消せます。一括変更後も、各レンダラーの歯車メニューで個別に選び直せます。

歯車メニューの **Preview UVs** では、元のメッシュと軽量化後の UV を重ねて比較できます。三角形数の目標や軽量化設定を変更すると、プレビューも更新されます。

#### 形状のみを軽量化する FA-QEM

FA-QEM は、元の面の平面、境界の曲率、元の法線に基づく接平面、境界移動に伴う面積ペナルティを使ってエッジ統合の優先度を決め、指定した絶対三角形数を目指して軽量化します。各レンダラーの歯車メニュー、または **Algorithm for All Meshes** から選択できます。

##### 対応機能

- **元のマテリアルとテクスチャを維持：** マテリアルの割り当てとテクスチャアセットを保持します。テクスチャベイク、アトラス生成、UV の再配置、テクスチャ圧縮は行いません。シェーダー機能やマテリアルのアニメーションは元のマテリアルを使用します。
- **メッシュ属性：** UV0～UV7、法線、接線、頂点カラー、スキンウェイト、ブレンドシェイプの各フレームを処理します。統合された頂点の属性は補間されるため、チャンネルを保持しても値や見た目が完全に一致するとは限りません。C# の `UseBarycentricCoordinateInterpolation` を有効にすると、見た目に関わる属性とブレンドシェイプには通常のエッジ補間ではなく重心座標補間を使います。スキンウェイトは引き続きエッジ両端の値から補間します。
- **分離頂点のシーム保護：** **Preserve Attribute Seams** の初期値はオンです。メッシュの大きさに応じた許容誤差内で同じ位置にある別々の頂点を固定し、独立して軽量化される面同士が離れるのを防ぎます。属性が同じ頂点や、UV シーム・ハード法線のために分離された頂点も対象です。元のメッシュの溶接や修復は行いません。
- **マテリアル境界：** 所属するサブメッシュの組み合わせが異なる頂点同士の統合を拒否します。
- **トポロジーと面の検査：** エッジ統合の局所的なリンク条件を確認し、危険な非多様体構造、残る面の退化、過度な面法線の変化を拒否します。局所的な安全策であり、メッシュ全体の自己交差防止や修復を保証するものではありません。
- **アニメーションで開く面の保護：** 頂点インデックスが互いに異なる三角形は、初期状態では面積がゼロでも、変形によって面が開く場合があります。ブレンドシェイプまたはスキンウェイトを持つメッシュでは、FA-QEM はその面を保持し、境界・シーム保護が無効でも頂点を固定します。変形データのないメッシュの面積ゼロの面は引き続き削除できます。この安全策により、該当する面の周辺では削減が制限される場合があります。
- **境界の固定：** **Preserve Border Edges** で、トポロジー上の開いた境界の頂点を固定できます。スキンメッシュでは、特定の領域だけを保護するために選択したボーンに基づく境界保護も利用できます。
- **実験的なスキニング保護：** **Protect joint deformation** でウェイト差の評価コストと制限を追加します。コア API の初期値はオフ、新規 NDMF 項目は上記の Auto です。軽量化後の代表的なポーズとブレンドシェイプを確認してください。
- **エディターとビルドの連携：** レンダラーごとの目標数、アバター全体への配分、NDMF プレビュー／ビルド、**Preview UVs** の比較表示に対応します。
- **実行・診断 API：** 同期、非同期、バッチ軽量化、結果レポート、元のインデックスとの対応を含むオプションの統合履歴に対応します。

##### 境界保持の初期値はオン

**新規メッシュ項目と `MeshSimplifierOptions.Default` では、Preserve Border Edges の初期値がオンです。** メッシュごとに歯車メニューから変更できます。襟ぐりや裾などの開いた境界の形状を保ちたい場合は、有効のまま使用してください。FA-QEM は境界頂点に触れる統合を拒否し、その位置と UV を固定します。ここでいう境界は一つの三角形だけが使用するエッジであり、閉じた面のカメラから見える輪郭すべてを固定する設定ではありません。

**Algorithm for All Meshes** を含むアルゴリズム変更では、各項目の境界設定を保持します。**Reset Options** は初期設定に戻すため、境界保持がオンになります。既存の保存済み項目は、オフに設定されている場合も含めて自動変更されません。C# から直接呼び出す場合も共通の初期値はオンです。境界を固定せずに削減したい場合は、明示的に無効にしてください。

**Boundary Weight** と **Area Weight** は境界の移動を抑える評価項目であり、境界を固定する設定ではありません。**Preserve Attribute Seams** も同じ位置にある分離頂点を保護するもので、開いた境界すべてを保護するものではありません。境界を厳密に維持したい場合は **Preserve Border Edges** を有効にし、古い項目の設定も確認してください。境界、シーム、スキニングの保護によって有効な統合候補が減り、厳しい三角形数の目標に達しない場合があります。

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
| Max Surface Deviation | `0`（オフ） | 元の表面からサンプル点までの距離の上限です。元のメッシュのバウンディングボックスの対角線長に対する比率で指定し、`0.001` は 0.1% を意味します。厳しくすると目標三角形数より多い状態で止まる場合があります。 |

FA-QEM は専用の **Min Normal Dot** と特徴量の重みを使います。従来の Meshia の **Preserve Surface Curvature** や **Smart Link** は、FA-QEM の評価基準を変更したり、離れたメッシュ部分を接続したりする設定ではありません。

**Preserve Border Edges** が有効な場合は境界頂点に触れる統合を拒否するため、受け入れられる内部エッジの統合では境界面積ペナルティがゼロになります。元の境界の二次形式も固定された頂点に残ります。この状態で **Area Weight** や **Boundary Weight** を上げても、身体に密着する服の内部領域は保護できません。

**Max Surface Deviation** は、変更しない元のメッシュを基準に、軽量化後のサンプル点から元の表面への距離を制限します。統合先の頂点、残る三角形の各エッジ中点、三角形の重心を検査します。最適位置が条件を満たさなければ、エッジの両端と中点も試し、有効な候補を実際のコストでキューに登録します。元の表面の検索用データは軽量化ごとに一度構築します。累積する表面のずれを抑える機能ですが、別のメッシュとの隙間、サンプル点の間の連続した表面、あらゆるアニメーション中の形状を保証するものではありません。近くにある元の別の層が距離判定を満たす場合もあります。保存済みの設定も含めて初期値はオフです。有効にする際は、品質と達成可能な三角形数のバランスを確認してください。

重ね着では、重なる両方の層を確認してください。外側の服の軽量化に問題がなくても、内側の層が外側へ動くと貫通する場合があります。必要な表面保護を両方に設定し、Play Mode で表示切り替えやアニメーションも含めて元の衣装と比較してください。アバター全体の目標を確認するときは、実際にビルドされた三角形数を使用してください。保護によって各メッシュが目標より多い状態で止まる場合があり、後続のビルド処理でも数が変わります。

NDMF プレビューは、ビルドと同じ身体メッシュ選択処理で Legacy、Auto、On、Off の関節保護設定を解決します。Auto のプレビューはリグ、候補レンダラー、メッシュ、軽量化設定の変更に応じて再評価されます。ただし、プレビューが扱うのは参加しているプレビューパスだけです。完全なビルドとは入力形状や最終的な数が異なる場合があるため、最終確認には **Analyze NDMF Build** または実際のビルド結果を使用してください。

幾何学的な評価項目と許容距離は、元のメッシュのバウンディングボックスの対角線長で正規化した座標を使います。各評価項目のスケール依存性は異なるため、これらの重みが論文の非正規化の重みと等価とは限りません。対角線長の `1e-8` より短いエッジは、シーム保護が無効でも統合を拒否します。同じ位置にある分離頂点はシーム保護の対象として固定し、自動溶接や仮想エッジは導入しません。

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

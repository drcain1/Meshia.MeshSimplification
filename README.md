# Meshia — drcain fork

[English](#english) · [日本語](#日本語) · [VPM repository](https://drcain1.github.io/Meshia.MeshSimplification/vpm/) · [Releases](https://github.com/drcain1/Meshia.MeshSimplification/releases) · [API documentation](https://drcain1.github.io/Meshia.MeshSimplification/)

## English

[Install](#installation) · [Get started](#how-to-use) · [Tune the result](#keeping-texture-details-and-shape) · [Troubleshoot](#troubleshooting)

Reduce a VRChat avatar's triangle count in Unity, preview the result, and adjust individual meshes where detail matters. This is an independent fork of Meshia by Ram.Type-0, maintained by drcain1.

### What this fork adds

- **FA-QEM simplification with texture-mapping preservation** to help clothing patterns and shading details survive reduction.
- **A shared avatar triangle budget**, with automatic allocation based on actual NDMF build measurements and manual control over each mesh.
- **Per-mesh shape and deformation protection**, including borders, seams, bone weights, and joint support for knees, elbows, and fingers.
- **Support for AAO and Modular Avatar cuts**, plus faster repeated slider edits through reusable FA-QEM previews.

Meshia keeps the existing material assignments and texture images. It changes geometry and can adjust UV coordinates; it does not bake, resize, or compress textures. Skin weights and blendshapes are carried through simplification, but their appearance can change, so check expressions and movement as well as the resting pose.

### Installation

Use **Unity 2022.3 or later**. For VRChat projects, use the editor version supported by the VRChat SDK.

1. Add the [fork's VPM repository](https://drcain1.github.io/Meshia.MeshSimplification/vpm/) to VCC or ALCOM.
2. Make these dependency repositories available:
   - [anatawa12](https://vpm.anatawa12.com/vpm.json) for **Custom Localization for Editor Extension** 1.x, version 1.2.1 or later.
   - [nadena](https://vpm.nadena.dev/vpm.json) for **NDMF** and **Modular Avatar**, used by the avatar workflow below.
3. Install **Meshia — drcain fork**.

This package replaces upstream Meshia. Keep only one variant installed, and back up an existing project before switching. See the [migration guide](docs/FORK_DISTRIBUTION.md#switching-an-existing-project) for details. AAO is optional.

For Git/UPM installs, install the VPM dependencies separately: Unity Package Manager does not resolve `vpmDependencies`.

### How to use

1. Create a child GameObject directly under the avatar root and add **Meshia Cascading Avatar Mesh Simplifier**.
2. Review the mesh list. Uncheck anything you want to leave untouched, especially a face with important expressions or face tracking. A mesh named `Body` (case-insensitive) starts excluded; other face meshes need to be excluded manually.
3. Set **Requested Triangle Budget** and choose a **Preset**. Start with **Conservative**, then try **Aggressive** or **Extreme** if you need more reduction.
4. Leave **Auto Adjust** on to distribute the budget across eligible meshes. With it off, **Adjust** distributes the budget when clicked.
5. Click **Analyze Build**. With Auto Adjust on, Meshia measures the complete NDMF result and tries to fit the budget. With it off, analysis only measures the result.
6. Keep **Enable NDMF Preview** on while tuning the mesh sliders. Check expressions, bent joints, and outfit toggles, then run **Analyze Build** again before uploading.

![Cascading inspector with the measured avatar total and per-mesh output sliders](https://raw.githubusercontent.com/drcain1/Meshia.MeshSimplification/main/.github/images/cascading-avatar-inspector.png)

*Inspector example. “Protection preset” in this screenshot is now called “Preset”.*

For a single mesh, add **Meshia Mesh Simplifier** (`MeshiaMeshSimplifier`) to its object instead. New components use FA-QEM; existing components keep their saved algorithm and settings.

### Reading the counts and adjusting meshes

| Display | Meaning |
| --- | --- |
| **Measured output** at the top | The avatar's total from a successful NDMF analysis of the current settings. |
| **Estimated output** | An estimate after edits. Run Analyze Build to verify it after all build tools have run. |
| **Output / Original** on a mesh row | Meshia's measured output before later tools, followed by its input count after supported cuts. Changing supported AAO Remove by BlendShape selections refreshes Original and the slider range, including Undo/Redo. |

A slider requests an output count. If protection prevents reaching it, the field shows the achieved count and an amber **!** explains the difference. **—** means unmeasured, a gray **?** means out of date, and a blue **…** means an edit is pending. The spinner beside the budget stays visible while calculations or preview updates are running.

Use each row's controls to tune the result:

- **Checkbox:** include or exclude that mesh from simplification.
- **Lock:** keep its allocation fixed during redistribution.
- **Cogwheel:** change its algorithm, texture preservation, and protection settings. **Preview UVs** compares the original and simplified UV layouts.
- **Marker or `/`:** open fine-tuning. Use **Reduce output by**, measure the reduction, then **Apply reduction** to keep the saving instead of giving it to other meshes.

With **Auto Adjust** on, lowering a slider returns allocation to other unlocked meshes; raising it takes allocation from them. Undo restores the edit and redistribution together. FA-QEM reuses calculations for repeated slider edits, though the first edit or a change to cuts or protections may take longer.

**The budget is a goal, not a guaranteed result.** Analyze Build may run several verification builds. It respects exclusions, locks, and protections; if automatic fitting cannot reach the target range, it restores the starting allocations. Use **Find ways to reduce** to review the largest contributors. Turning a protection off is always a separate choice.

### Presets and protection

Presets set how much change simplification can make. They leave algorithms, triangle allocations, locks, and exclusions in place, but replace the mesh protection options. Apply a preset before making individual adjustments.

| Preset | Behavior |
| --- | --- |
| **Conservative** | Tight shape limits, automatic bone-weight protection for deforming meshes, and joint-vertex protection. Retains more geometry. |
| **Aggressive** | Looser shape limits. Keeps deformation and joint protection on detected hand/finger and hair meshes, and meshes named `Body` or `Face`; disables it on other meshes. |
| **Extreme** | The same selection as Aggressive, with more surface movement allowed on the other meshes for further reduction. |

Detection uses bone weights, humanoid mappings, and common names. Check the selected meshes, especially on custom rigs. Applying a preset does not enable an excluded face.

The protection icon cycles **green → yellow → gray**:

- **Green:** geometry and deformation protection enabled with the mesh's settings.
- **Yellow:** geometry protection only, or a partial custom selection.
- **Gray — No protection:** bypasses shape, texture, and deformation guards. This can create holes or damage the mesh. Returning to green restores the saved geometry settings and enables deformation protection.

Green does not mean every setting is equally strict. A loose surface limit still allows more shape change. New entries start with geometry protection on and deformation protection off; choose a preset or enable it per mesh. Package updates keep existing saved settings.

### Keeping texture details and shape

#### Painted protection masks (experimental beta)

Use a mask when one mesh contains both an area you want to reduce and an area you want Meshia to leave unchanged, such as a face with tracking blendshapes.

1. Use **FA-QEM** and open the mesh's **cogwheel → Protected areas (experimental)**. Add an entry and enable **Protect painted area**.
2. Choose the **Material slot** and assign a mask texture. **Create** and **Edit** use the optional [MaskTextureEditor](https://github.com/nekobako/MaskTextureEditor); imported masks also work without the painter installed.
3. Paint **white to keep geometry unchanged** and **black to allow reduction**, then **Save** in the painter. Unsaved brush strokes do not affect Meshia's preview or build.
4. Use **Show area** to inspect the protected triangles. Adjust the triangle slider, check expressions and movement, and run **Analyze Build** before uploading.

Masks use **UV0** and apply per material slot. Add entries for additional slots. Meshia keeps the selected vertices and their blendshape data; triangles touching white paint and coincident seam vertices can expand the protected boundary. Overlapping UV islands share the painted selection. Gray paint is a threshold, not a gradual protection strength.

**Black does not force removal:** border, shape, texture-mapping, and deformation protections can still retain geometry. Painted protection remains active even with Extreme or the gray protection icon; disable the mask entry to release it. A heavily protected mesh may not reach its target.

This beta supports masks with FA-QEM. A nonempty mask on another algorithm keeps the mesh unchanged. Missing mask data or an incompatible upstream UV/vertex/material layout also keeps the mesh unchanged with a Console warning. Masks do not prevent another tool from cutting geometry or modifying the result later in the build.

#### Texture mapping

Under **cogwheel → FA-QEM Options**, leave **Preserve texture mapping** on and start at **Medium (5000)**. This is the default for new settings.

| Strength | Value | Use |
| --- | --- | --- |
| Low | 1000 | Give texture mapping less influence. |
| Medium | 5000 | Starting point for balancing texture detail and shape. |
| High | 10000 | Try when patterns still distort at Medium, then recheck shape and shading. |

Choose a strength or edit the number beside it. Higher is not always better, and texture strength does not repair holes. Texture-mapping preservation remains experimental.

#### Shape and movement

- **Preserve Border Edges** helps keep open edges such as hems, necklines, and hair-card outlines intact. **Lock Coincident Split Vertices** helps prevent cracks at split seams. Both start on.
- **Maximum Surface Deviation** limits movement away from the original surface. Start at **0.0005** if a mesh develops holes or dents. Smaller positive values protect more; larger values allow more change. **0 disables this limit.** Values are relative to each mesh's size, so one value will not suit every mesh.
- **Protect joint deformation** limits changes to skin weights. With FA-QEM, **Automatically protect deforming meshes** also detects connected humanoid knee and elbow transitions. **Preserve Vertices Near Joints** and **Joint Protection Bones** let you retain additional joint support, including fingers.

Tighter protection can leave more triangles than requested. Give a sensitive mesh more of the budget and reduce another one instead. Check moving poses: an acceptable resting shape does not prove that a knee, elbow, or expression will deform well.

#### AAO and Modular Avatar cuts

FA-QEM prepares supported **AAO Remove Mesh By BlendShape** cuts before simplification. Keep **Preserve Border Edges** on to protect the new openings. This requires AAO **1.9 or later**, a non-inverted selection, and the default clamped interpolation. Permanent MA cuts already present are treated as open borders; toggleable MA cuts receive visibility-transition protection. Use the normal AAO and MA releases—modified packages are not needed.

With border protection on, Meshia also protects nearby clothing openings and bending regions around supported AAO cuts. This overlap protection is experimental and can retain extra triangles. It does not cover every pair of close layers, such as a tie against a shirt. Check both meshes while bending joints and switching outfits.

### Troubleshooting

| Problem | What to try |
| --- | --- |
| **Still over budget** | Open **Find ways to reduce**. Review large meshes, fixed allocations, and geometry that is always hidden. Try a stronger preset or adjust individual meshes, then analyze again. Hair with many borders and seams may need a larger allocation. |
| **Holes, dents, or cracks** | Check that protection is not gray. Lower **Maximum Surface Deviation** to a smaller positive value and keep border/seam protection on. Compare with the original; these settings do not repair existing holes. |
| **Looks fine until it bends** | Enable deformation and joint-vertex protection on the affected mesh. Give it more triangles if needed. For clothing clipping, inspect both the body and clothing in the same pose. |
| **Texture detail looks worse only after building** | Check later texture-processing tools as well as Meshia. Meshia does not resize or compress textures; compare the built result with those tools disabled or configured to preserve the affected texture. |

For permanently hidden body geometry, [AAO Remove Mesh By BlendShape](https://vpm.anatawa12.com/avatar-optimizer/en/docs/reference/remove-mesh-by-blendshape/) or [MA Shape Changer in Delete mode](https://modular-avatar.nadena.dev/docs/reference/reaction/shape-changer) can help. Shrinking a body part or hiding it with a shader does not remove its triangles. Keep any regions needed by outfit toggles; animated MA deletion can hide geometry without lowering the reported count.

### Other algorithms and C# use

Choose an algorithm per mesh under its cogwheel, or use **Algorithm for All Meshes** to apply one across the list.

| Algorithm | Use |
| --- | --- |
| **FA-QEM** | Default, with texture-mapping and shape-preservation controls. |
| **Blender Decimate** | Alternative collapse algorithm derived from Blender. |
| **Meshia** | The original Meshia algorithm and its preservation/interpolation options. |
| **UV Loop Dissolve** | Removes suitable edge loops; falls back to Blender Decimate when needed to continue reduction. |

Meshia also works as a Unity library using Burst and the Job System, with synchronous, asynchronous, and batch APIs. See the [C# example](#c-api) below and [API documentation](https://drcain1.github.io/Meshia.MeshSimplification/). Implementation settings are defined in [MeshSimplifierOptions](Runtime/MeshSimplifierOptions.cs) and [FaQemOptions](Runtime/FaQemOptions.cs).

---

## 日本語

[インストール](#インストール) · [使い方](#使い方) · [見た目の調整](#テクスチャの細部と形状を保つ) · [困ったときは](#困ったときは)

Unity で VRChat アバターの三角形数を減らし、プレビューを見ながらメッシュごとに調整できるツールです。Ram.Type-0 氏の Meshia を元に、drcain1 が開発している独立したフォークです。

### このフォークの追加機能

- **テクスチャの歪みを抑える FA-QEM**：軽量化した際の服の模様や陰影の崩れを抑えます。
- **アバター全体の三角形数を配分**：NDMF ビルドの実測値を使って自動調整し、メッシュごとの手動調整もできます。
- **メッシュごとの形状・変形保護**：境界、シーム、ボーンウェイトに加え、膝・肘・指などの関節付近を保護できます。
- **AAO・Modular Avatar のカットに対応**：FA-QEM のプレビューでは計算結果を再利用し、繰り返しのスライダー操作を高速化しています。

元のマテリアルの割り当てとテクスチャ画像を使います。形状や UV 座標は変わることがありますが、テクスチャのベイク・縮小・圧縮は行いません。ボーンウェイトやブレンドシェイプも引き継ぎますが、見た目が変わる場合があるため、静止状態だけでなく表情や動きも確認してください。

### インストール

**Unity 2022.3 以降**が必要です。VRChat プロジェクトでは、VRChat SDK が対応するエディターのバージョンを使用してください。

1. [このフォークの VPM リポジトリ](https://drcain1.github.io/Meshia.MeshSimplification/vpm/)を VCC または ALCOM に追加します。
2. 依存パッケージ用のリポジトリを追加します。
   - [anatawa12](https://vpm.anatawa12.com/vpm.json)：**Custom Localization for Editor Extension** 1.x（1.2.1 以上）。
   - [nadena](https://vpm.nadena.dev/vpm.json)：以下のアバター向け手順で使う **NDMF** と **Modular Avatar**。
3. **Meshia — drcain fork** をインストールします。

このパッケージは元の Meshia を置き換えます。両方を同時に導入せず、既存のプロジェクトでは切り替える前にバックアップしてください。詳しくは[移行ガイド（英語）](docs/FORK_DISTRIBUTION.md#switching-an-existing-project)を参照してください。AAO は任意です。

Git / UPM から導入する場合、VPM の依存パッケージは別途インストールしてください。Unity Package Manager は `vpmDependencies` を自動解決しません。

### 使い方

1. アバターのルート直下に子 GameObject を作成し、**Meshia Cascading Avatar Mesh Simplifier** を追加します。
2. メッシュ一覧を確認し、変更したくないもののチェックを外します。特に、表情やフェイストラッキングを使う顔を確認してください。名前が `Body` のメッシュは大文字・小文字を区別せず初期状態で対象外になりますが、別名の顔メッシュは手動で除外する必要があります。
3. **目標三角形数** と **プリセット** を選びます。まずは **弱** から始め、さらに減らしたい場合は **中**、**強** を試します。
4. **自動調整** をオンにすると、対象メッシュに予算を配分します。オフの場合は **調整** を押したときに配分します。
5. **ビルド解析** を押します。自動調整がオンなら、NDMF ビルドの結果を実測し、予算に収まるよう調整します。オフなら計測のみ行います。
6. **NDMFプレビューを有効化** をオンにして、メッシュごとのスライダーを調整します。表情、曲げた関節、衣装の切り替えを確認し、アップロード前にもう一度 **ビルド解析** を実行してください。

![アバター全体の実測値とメッシュごとの出力スライダーを表示したインスペクター](https://raw.githubusercontent.com/drcain1/Meshia.MeshSimplification/main/.github/images/cascading-avatar-inspector-ja.png)

*画像は旧表記です。現在は「プリセット（弱・中・強）」「目標三角形数」「三角形数予算」と表示されます。*

単体のメッシュには、そのオブジェクトに **Meshia Mesh Simplifier**（`MeshiaMeshSimplifier`）を追加します。新規コンポーネントは FA-QEM を使用し、既存のコンポーネントは保存済みのアルゴリズムと設定を維持します。

### 数値の見方とメッシュごとの調整

| 表示 | 意味 |
| --- | --- |
| 上部の **実測出力数** | 現在の設定で正常に完了した NDMF 解析による、アバター全体の三角形数です。 |
| **推定出力数** | 編集後の推定値です。後続ツールの処理を含む最終結果は、ビルド解析で確認します。 |
| 各行の **出力 / 元の三角形数** | 後続ツールで処理する前の Meshia の実測出力と、対応するカットを反映した軽量化前の入力数です。対応する AAO Remove by BlendShape の選択を変更すると、元の数とスライダーの上限も更新されます。元に戻す・やり直しにも対応します。 |

スライダーでは希望する出力数を指定します。保護によって届かない場合は、実際に達成できた数を表示し、黄橙色の **!** で違いを説明します。**—** は未計測、灰色の **?** は古い計測値、青の **…** は編集の反映待ちです。計算やプレビューの更新中は、三角形数予算の横にスピナーが表示されます。

各行の操作は次のとおりです。

- **チェックボックス**：軽量化するメッシュを選びます。
- **鍵**：再配分の際に、そのメッシュの配分を固定します。
- **歯車**：アルゴリズム、テクスチャ保護、形状・変形保護を設定します。**UVをプレビュー** で元の UV と軽量化後の UV を比較できます。
- **マークまたは `/`**：微調整を開きます。**減らす三角形数** を指定して計測し、**削減を適用** すると、削減分を他のメッシュへ再配分せずに残せます。

**自動調整** がオンの場合、スライダーを下げると他の固定されていないメッシュへ配分を戻し、上げると他のメッシュから配分を移します。「元に戻す」で編集と再配分をまとめて取り消せます。FA-QEM は繰り返しのスライダー操作で計算を再利用しますが、初回やカット・保護設定を変えた後は時間がかかる場合があります。

**目標数まで必ず減らせるわけではありません。** ビルド解析では複数回の検証ビルドを行う場合があります。対象外・固定・保護の設定は維持し、自動調整で目標範囲に届かない場合は開始時の配分に戻します。**削減方法を確認** で三角形数の多いメッシュを確認できます。保護を自動で解除することはありません。

### プリセットと保護

プリセットは、軽量化でどこまで形状や変形の変化を許容するかを設定します。アルゴリズム、三角形数の配分、固定、対象外の設定は維持しますが、各メッシュの保護設定は置き換えます。個別の調整をする前に選んでください。

| プリセット | 動作 |
| --- | --- |
| **弱（Conservative）** | 形状の変化を厳しく制限し、変形するメッシュのボーンウェイトと関節付近の頂点を保護します。残る三角形数は多めになります。 |
| **中（Aggressive）** | 形状の制限を緩めます。検出された手・指・髪のメッシュと、`Body`・`Face` という名前のメッシュでは変形と関節の保護を維持し、それ以外ではオフにします。 |
| **強（Extreme）** | 「中」と同じ対象を保護し、それ以外のメッシュでは表面の移動をさらに許容して削減しやすくします。 |

検出にはボーンウェイト、ヒューマノイドの対応情報、一般的な名前を使います。独自のリグなどでは、選ばれたメッシュを確認してください。プリセットを適用しても、対象外にした顔が有効になることはありません。

保護アイコンはクリックするたびに **緑 → 黄 → 灰** と切り替わります。

- **緑**：そのメッシュの設定で、形状と変形の保護が有効です。
- **黄**：形状のみの保護、または一部だけ有効にした設定です。
- **灰・保護なし**：形状、テクスチャ、変形の保護を無効にします。穴や形状の崩れが生じる場合があります。緑に戻すと保存済みの形状設定を復元し、変形の保護を有効にします。

緑でも、すべての設定が厳しいとは限りません。表面のずれの上限が大きければ、その分だけ形状の変化を許容します。新規項目は形状保護がオン、変形保護がオフで始まります。プリセットを選ぶか、メッシュごとに有効にしてください。パッケージ更新では保存済みの設定を維持します。

### テクスチャの細部と形状を保つ

#### マスクで領域を保護（実験的ベータ）

フェイストラッキング用の顔など、同じメッシュの中で軽量化する部分と変更したくない部分を分けたい場合に使います。

1. **FA-QEM** を選び、メッシュの **歯車 → 保護する領域（実験的）** に項目を追加して、**塗った領域を保護** をオンにします。
2. **マテリアルスロット** とマスク画像を指定します。作成・編集には任意の [MaskTextureEditor](https://github.com/nekobako/MaskTextureEditor) を使います。導入していなくても、用意したマスク画像を指定できます。
3. **白は変更せず保持、黒は軽量化を許可**する領域です。塗り終えたらペイント画面で **Save** を押してください。未保存の描画は Meshia のプレビューやビルドに反映されません。
4. 領域表示で保護される三角形を確認し、スライダーで調整します。表情や動きを確認して、アップロード前に **ビルド解析** を実行してください。

マスクは **UV0** を使い、マテリアルスロットごとに適用されます。複数のスロットを保護する場合は項目を追加してください。選択された頂点とブレンドシェイプのデータを保持します。白に触れる三角形全体や同じ位置のシーム頂点も保護するため、塗った範囲より境界が広がる場合があります。UV が重なる部分は同時に選択されます。灰色はしきい値で判定し、保護の強さを連続的に変えるものではありません。

**黒に塗っても必ず削減されるわけではありません。** 境界・形状・テクスチャ・変形の保護は引き続き適用されます。プリセット「強」や灰色の保護アイコンでも、マスクの保護は維持します。解除する場合はマスク項目をオフにしてください。保護する範囲が広いと目標数まで減らせない場合があります。

このベータのマスクは FA-QEM 向けです。他のアルゴリズムで保護対象がある場合は、そのメッシュを変更しません。画像などの情報が不足している場合や、先行する処理で UV・頂点・マテリアル構成が対応しなくなった場合も、メッシュを保持して Console に警告を表示します。マスクは、他のツールによるカットや後続の変更を防ぐものではありません。

#### テクスチャ

**歯車 → FA-QEM設定 → テクスチャの歪みを抑える** をオンにして、まずは **中（5000）** から試してください。新しい設定ではこの状態が初期値です。

| 強さ | 数値 | 使い方 |
| --- | --- | --- |
| 弱 | 1000 | テクスチャの保持を重視する度合いを下げます。 |
| 中 | 5000 | 模様の保持と形状のバランスを確認するための開始値です。 |
| 強 | 10000 | 「中」でも模様が歪む場合に試し、形状や陰影も再確認します。 |

強さを選ぶか、隣の数値欄を編集できます。強くすれば常に良くなるわけではなく、穴を修復する設定でもありません。テクスチャ保護は引き続き実験的な機能です。

#### 形状と動き

- **境界エッジを保持** は、裾・襟ぐり・板状の髪などの開いた境界を保ちます。**同じ位置にある分離頂点の固定** は、分離したシームに亀裂ができるのを抑えます。どちらも初期値はオンです。
- **元の表面からのずれの上限（Maximum Surface Deviation）** は、元の表面からの移動を制限します。穴やへこみができる場合は **0.0005** を目安にしてください。小さい正の値ほど厳しく、大きい値ほど変化を許容します。**0 は制限なし**です。メッシュの大きさに対する比率なので、同じ値がすべてのメッシュに適するわけではありません。
- **関節を動かしたときの形状を保護** は、ボーンウェイトの変化を制限します。FA-QEM の **変形するメッシュを自動保護** は、接続されたヒューマノイドの膝・肘の境界も検出します。**関節付近の頂点を保持** と **関節を保護するボーン** で、指などの保護を追加できます。

保護を強めると、指定した数より多くの三角形が残る場合があります。崩れやすいメッシュには多めに配分し、別のメッシュで削減量を確保してください。静止状態で問題なくても、膝・肘・表情を動かした状態で確認が必要です。

#### AAO・Modular Avatar のカット

FA-QEM は、対応する **AAO Remove Mesh By BlendShape** のカットを軽量化前に反映します。**境界エッジを保持** をオンにして、新しくできる切り口を保護してください。AAO **1.9 以降**、反転していない選択、既定のクランプ補間が必要です。MA が先に行った恒久的なカットは開いた境界として扱い、表示を切り替える MA のカットでは表示・非表示の境界を保護します。通常配布の AAO・MA に対応しており、改造版は不要です。

境界保護がオンの場合、対応する AAO の切り口に近い衣装の開口部と、関連する変形範囲も保護します。この重なりの保護は実験的で、三角形数が増える場合があります。ネクタイとシャツのような近接したメッシュすべてを対象にするものではありません。関節を曲げたり衣装を切り替えたりして、両方のメッシュを確認してください。

### 困ったときは

| 症状 | 確認すること |
| --- | --- |
| **予算を超えている** | **削減方法を確認** を開き、大きいメッシュ、固定配分、常に隠れている形状を見直します。強めのプリセットや個別調整を試し、再解析してください。境界やシームの多い髪は、多めの配分が必要な場合があります。 |
| **穴・へこみ・亀裂ができる** | 保護が灰色でないことを確認します。**元の表面からのずれの上限** をより小さい正の値にし、境界とシームの保護をオンにしてください。元のメッシュとも比較します。これらの設定は元からある穴を修復するものではありません。 |
| **曲げたときだけ崩れる** | 対象メッシュの変形保護と関節付近の頂点保持を有効にし、必要なら配分を増やします。服が貫通する場合は、同じポーズで素体と服の両方を確認します。 |
| **ビルド後だけテクスチャの細部が崩れる** | 後続のテクスチャ処理も確認します。Meshia は画像を縮小・圧縮しません。その処理を無効にするか、該当テクスチャを保持する設定で比較してください。 |

常に服で隠れている素体には、[AAO Remove Mesh By BlendShape](https://vpm.anatawa12.com/avatar-optimizer/ja/docs/reference/remove-mesh-by-blendshape/) や [MA Shape Changer の Delete モード](https://modular-avatar.nadena.dev/ja/docs/reference/reaction/shape-changer)が使えます。縮小やシェーダーで隠すだけでは三角形は削除されません。衣装切り替えで必要な部分は残してください。MA の削除をアニメーションで切り替える場合、非表示になっても報告される数は減らないことがあります。

### その他のアルゴリズムと C# からの利用

歯車でメッシュごとのアルゴリズムを選ぶか、**全メッシュのアルゴリズム** で一覧全体に適用できます。

| アルゴリズム | 用途 |
| --- | --- |
| **FA-QEM** | 初期設定。テクスチャと形状の保護を調整できます。 |
| **Blender デシメート** | Blender 由来の辺の統合による軽量化です。 |
| **Meshia** | 元の Meshia のアルゴリズムと、保持・補間の設定を使います。 |
| **UV Loop Dissolve** | 適したエッジループを削除し、さらに削減が必要な場合は Blender デシメートを使います。 |

Burst と Job System を使った Unity ライブラリとしても利用でき、同期・非同期・一括処理に対応しています。以下の [C# の例](#c-api)と [API ドキュメント](https://drcain1.github.io/Meshia.MeshSimplification/)を参照してください。実装上の設定は [MeshSimplifierOptions](Runtime/MeshSimplifierOptions.cs) と [FaQemOptions](Runtime/FaQemOptions.cs) に定義されています。

---

## C# API

Start from `MeshSimplifierOptions.Default` and provide a separate destination mesh. The report gives the achieved count and explains whether the target was reached or constraints stopped reduction.

`MeshSimplifierOptions.Default` から設定を作り、出力先には別のメッシュを用意します。レポートで実際の出力数と、目標に到達したか、制約により削減が止まったかを確認できます。

```csharp
using Meshia.MeshSimplification;
using UnityEngine;

var options = MeshSimplifierOptions.Default;
var target = new MeshSimplificationTarget
{
    Kind = MeshSimplificationTargetKind.FaQemTriangleCount,
    Value = 5000,
};

var simplifiedMesh = new Mesh();
var report = MeshSimplifier.SimplifyWithReport(
    originalMesh, target, options, simplifiedMesh);

Debug.Log($"Triangles: {report.InputTriangleCount} → {report.OutputTriangleCount}; " +
          $"result: {report.FaQemTermination}");
```

The same target works with `Simplify`, `SimplifyAsync`, and `SimplifyBatch`. Release generated meshes when no longer needed. Avatar-specific automatic protection is resolved by NDMF; direct API callers must configure skinning protection themselves.

同じターゲットを `Simplify`、`SimplifyAsync`、`SimplifyBatch` に渡せます。生成したメッシュが不要になったら解放してください。アバター向けの自動保護は NDMF が判定するため、API から直接使う場合はスキニング保護を設定する必要があります。

## Credits and license / クレジット・ライセンス

Based on Meshia by **Ram.Type-0**, with UV quadric work adapted from [touma-tw's fork](https://github.com/touma-tw/Meshia.MeshSimplification) and Blender-derived decimation code. This fork is distributed under **GPL-2.0-or-later**. See [LICENSE.md](LICENSE.md), the [Blender attribution](BLENDER_PORT_NOTICE.md), and the retained [upstream MIT notice](Licenses/UPSTREAM-MIT.txt).

**Ram.Type-0** 氏の Meshia を元に、[touma-tw 氏のフォーク](https://github.com/touma-tw/Meshia.MeshSimplification)の UV 評価処理と Blender 由来の軽量化コードを取り入れています。このフォークの配布ライセンスは **GPL-2.0-or-later** です。詳細は上記のライセンス・帰属表示を参照してください。

---
_layout: landing
---

# Meshia — drcain fork

- [English](#english)
- [日本語](#日本語)

## English
Mesh simplification tool/library for Unity, VRChat.

Based on Unity Job System, and Burst. 
Provides fast, asynchronous mesh simplification.

Can be executed at runtime or in the editor.

### Installation

### VPM

This independent fork is maintained by drcain1. Add its VPM repository to VCC or ALCOM and enable prerelease packages for the beta. The [fork repository](vpm/index.html) will distribute `io.github.drcain1.meshia.mesh-simplification`. Install only one Meshia variant per project. See the [migration and distribution guide](https://github.com/drcain1/Meshia.MeshSimplification/blob/main/docs/FORK_DISTRIBUTION.md) before switching.


### How to use

#### NDMF integration

Attach `MeshiaMeshSimplifier` to your models.

You can preview the result in EditMode.


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

Unity、VRChat向けのメッシュ軽量化ツールです。
Unity Job Systemで動作するため、Burstと合わせて高速、かつ非同期で処理ができるのが特徴です。
ランタイム、エディターの双方で動作します。

### インストール

### VPM

[このフォークの VPM リポジトリ](https://drcain1.github.io/Meshia.MeshSimplification/vpm/index.json) を VCC または ALCOM に追加し、プレリリースを有効にしてください。上流版と同時にインストールしないでください。[移行手順と公開状況](https://github.com/drcain1/Meshia.MeshSimplification/blob/main/docs/FORK_DISTRIBUTION.md) を参照してください。

### 使い方

#### NDMF統合

NDMFがプロジェクトにインポートされている場合、`MeshiaMeshSimplifier`が使えます。
エディターで軽量化結果をプレビューしながらパラメーターの調整ができます。

#### C#から呼び出す

```csharp

using Meshia.MeshSimplification;

Mesh simplifiedMesh = new();

// 非同期API

await MeshSimplifier.SimplifyAsync(originalMesh, target, options, simplifiedMesh);

// 同期API

MeshSimplifier.Simplify(originalMesh, target, options, simplifiedMesh);

```



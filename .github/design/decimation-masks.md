# Decimation masks: exploratory design

Status: source/API investigation, 2026-10-12. No mask feature is implemented yet.
Branch: `experiment/decimation-masks`, based on main `ed0a2f94728eb4e13d7984151b574a650edf7b32`.

## User problem

A renderer can contain both a face with tracking blendshapes and a body that can be reduced. Excluding the entire renderer wastes the opportunity to simplify the body. The user needs to paint an area that Meshia leaves unchanged.

## Recommendation

Add **Protected areas** under each mesh's cogwheel, initially for FA-QEM. Reuse MaskTextureEditor's public painting API as an optional editor integration. Implement selection and preservation in Meshia; do not add or repurpose a Modular Avatar Mesh Cutter, which removes or hides geometry.

Proposed first UI:

- Material slot, mask texture, and Create / Edit buttons.
- **White: keep unchanged. Black: allow reduction.** Black does not disable the mesh's other protections.
- Show the actual protected triangles as an overlay, including any extra boundary protection.
- Support multiple material slots by adding mask entries; combine selections by union.
- Use UV0 first. Offer imported masks even when the painter is absent. Do not silently paint a different UV channel.
- An explicit mask is separate from presets and the green/yellow/gray protection cycle. Clearing or disabling the mask is the only way to release its protection.
- Explain an unreachable target as a limit imposed by protected geometry. Never weaken a mask to reach the requested triangle count.

Start without strength sliders, feathering, or automatic face detection. These obscure the promise that the selected area will not be decimated.

## What can be reused

[MA's Vertex Filter By Mask documentation](https://modular-avatar.nadena.dev/docs/reference/reaction/mesh-cutter/by-mask) establishes the renderer/material-slot painting workflow. Its cutter selects geometry for deletion or hiding, not preservation.

[MaskTextureEditor](https://github.com/nekobako/MaskTextureEditor) is MIT licensed and provides a separate public editor API. Inspected version: **1.3.0**, commit `785bdb1ac5a153366c9712936b945c57e7e0bd0c`.

- `Utility.CreateTexture(Vector2Int, Color)` creates a saved PNG. It also enables Read/Write on the newly created texture.
- `Window.TryOpen(Texture2D, Renderer, int?, string)` opens the painter for a texture and material slot. Use a Meshia-specific token.
- `Window.IsOpenFor(...)` and `TryClose()` support an edit button.
- With NDMF support, `Window.ObserveTextureFor(ComputeContext, Texture2D, Renderer, int?, string)` exposes the in-progress texture, including unsaved paint edits.
- Assembly: `net.nekobako.MaskTextureEditor.Editor`. Keep the dependency in a separate optional editor assembly with a package version define, following the existing optional AAO integration pattern.

Installed MA 1.18.7 uses these APIs in `MaskTextureEditorOpener.cs`. Its internal `VertexFilterByMask` and `VertexFilterRegistry` are not a suitable public dependency. Use public painter APIs without copying MA's filter internals. No modified MA or AAO package is required.

## Preservation contract

For the exact mesh input received by Meshia, preserve selected triangles and their vertices, including positions, normals, tangents, UVs, colors, bone indices/weights, and every blendshape frame's position/normal/tangent deltas. Preserve blendshape names and frame weights. Mesh output may compact/reindex vertices; original numeric vertex indices are not part of the contract.

This is a Meshia guarantee, not a guarantee that another build plugin cannot subsequently modify or delete geometry. A mask cannot restore geometry removed before Meshia runs.

Preserving only positions or increasing a quadric penalty is insufficient for face tracking. Reject every collapse touching a protected vertex. Protect all corners of a selected face and preserve compatible seam duplicates around the selection boundary. Any boundary expansion must be bounded and visible, not an accidental flood across the entire connected mesh.

An initial one-ring buffer is worth evaluating, but is not a substitute for testing. Do not join coincident vertices with different skinning or blendshape behavior just because their positions match. Conservative additional locking is acceptable; welding them is not.

## Integration points and traps found in this checkout

| Location | Required work |
| --- | --- |
| `Ndmf/Runtime/MeshiaCascadingAvatarMeshSimplifier.cs` renderer entry | Store mask asset, slot, enabled state separately from preset options. Address standalone mesh components as well. |
| `Runtime/Jobs/SimplifyJob.FaQem.cs`, `IsFaQemProtected` | Add an unconditional explicit-mask exclusion before `AllowUnsafeGeometry`. Enforce it both when queuing candidates and when accepting them. |
| Same file, `RemoveFaQemDegenerateFaces` | Respect selected source faces during cleanup, including rest-pose faces that open under blendshapes. Invalid input needs a diagnostic, not silent deletion inside a protected region. |
| Same file, `ReplayFaQem` branch | Replay currently applies recorded merges without the ordinary candidate checks. Mask changes must invalidate replay, and replay must verify mask compatibility before applying any merges. |
| `Runtime/Jobs/SimplifyJob.FaQemUvResearch.cs`, tangent rebuild | Rebuild currently visits all surviving vertices and blendshape frames. Skip writes to protected base tangents and protected blendshape tangent deltas. Locking collapses alone does not preserve them. |
| `Runtime/Jobs/WriteToMeshDataJob.cs` | Output compaction copies surviving attributes and all blendshape frame deltas through a source-index mapping. Verify preservation through this mapping rather than comparing vertex indices. |
| `Runtime/SourceVertexRanges.cs` | This fixed-capacity range container is not suitable for arbitrary masks. Fragmented selections can exhaust it. Use an owned dynamic bitset or byte array. |
| `Runtime/MeshSimplifier.cs` and preview preparation | Carry immutable protection data through sync, async, batch, count-only, history and replay APIs with explicit native allocation ownership. |
| `Ndmf/Editor/Preview/MeshiaMeshSimplifierPreviewBase.cs` | Include the prepared mask signature in settings, replay and count cache identity. |
| `Ndmf/Editor/CutMeshPreparation.cs`, `Aao/AaoCutPreparation.cs` | Apply protection to the actual prepared mesh. The inspected AAO cut preparation changes triangles while retaining source vertex indices; do not assume all other build operations preserve indices, UVs or material slots. |

## Texture selection

Use a binary interpretation first. Proposed sampling rule: treat mask red-channel values at least 0.5 as protected, ignore alpha, and use raw mask values without color-space-dependent threshold changes. New masks should be uncompressed grayscale data textures without mipmaps. Imported texture readback must not change the user's importer settings; support unreadable textures using a temporary readback and reliable cleanup.

Do not just test the three UV vertices: a white patch can lie wholly inside a large triangle. Conservatively intersect triangle UV coverage with protected texels and lock every vertex of each selected triangle. Define edge-touching coverage consistently and handle zero-area UV triangles. This needs a bounded raster/coverage implementation with acceleration for empty/full mask tiles, not an unbounded pixel scan on every slider change.

Respect texture wrapping, including triangles crossing tile boundaries. Mirrored or overlapping UV regions select every overlapping surface within the chosen slot; a 2D mask cannot distinguish them. Explain that limitation next to the overlay rather than presenting it as a bug.

Material slots and UVs must match the geometry the painter shows. Resolve masks before Meshia's own UV optimization. If an earlier plugin changes UVs, merges slots, or replaces the mesh, either carry a verified source mapping to the prepared input or stop simplifying that affected renderer with an actionable message. Silently applying the mask to another slot is unsafe. Establish this correspondence in the first integration prototype before promising general AAO/MA compatibility.

## Preview, builds and performance

Resolve mask pixels and protected topology once per mesh/configuration revision. Cache by source geometry/UV/topology identity, selected slot, texture content and dimensions, wrap mode, threshold, and any boundary expansion rules. Slider changes should reuse the resolved selection and a compatible simplification history.

Observe painter edits with the public NDMF API, coalesce updates, and show the overlay promptly. Never reuse an old history after a mask edit, Undo, asset reimport, slot change or source-mesh replacement. Preserve the existing calculating indicator while reduction catches up.

Unsaved paint is a special state: preview may show the live painter texture while builds use the saved asset. Label that state and require Save or Discard before Analyze Build or upload can proceed with an enabled mask. Do not auto-save unrelated edits or claim that an unsaved preview has been measured. Confirm the available painter save-state API during implementation; `IsOpen` alone does not mean there are unsaved changes.

The number of selected triangles is a minimum retained amount, not necessarily the final attainable count: transition geometry and other protections can impose a higher limit. Show actual measured output as today, with a concise explanation when protected areas prevent further reduction.

## Implementation order

1. **Core proof:** synthetic skinned mesh with face/body regions, multiple blendshape frames and explicit protected vertex/face sets. Reduce the body while preserving the selected region, including tangent rebuilding, degenerate cleanup and replay. No painting UI yet.
2. **Mask resolver:** slot-aware UV selection, triangle-interior coverage, seams, wrapping and immutable signatures. Prove that changing masks invalidates cached reductions.
3. **Inspector integration:** per-mesh texture entries and overlay, then optional Create/Edit integration. Imported masks must work without MaskTextureEditor installed.
4. **NDMF parity:** preview, Analyze Build, manual bake and upload-path processing on temporary copies. Cover AAO removal/merge and MA cut operations without modifying those packages.
5. **Avatar validation:** reduce a body sharing a renderer with a protected face and compare facial tracking poses and combined blendshapes. Do not change the live Luchika setup during the core proof.

Initially support FA-QEM explicitly. If a user selects another algorithm with an enabled mask, keep that renderer unsimplified and explain the incompatibility; never ignore the mask. Extend algorithms only after equivalent tests pass.

## Acceptance tests

- No mask matches current behavior; black allows existing simplification; white preserves the entire selected slot.
- Face region retains all supported attribute values and every blendshape delta after compaction while the unpainted body loses triangles. Include multiple frames, simultaneous expressions and skinned poses.
- Texture-mapping preservation enabled, Extreme selected, and gray protection state cannot bypass explicit masking.
- White patch inside a triangle with black corners is protected. Include UV seams, overlapping islands, tiled UVs, degenerate UVs and shared vertices across material slots.
- Highly fragmented/checkerboard mask has no capacity truncation; missing UV/texture/slot produces a visible safe fallback.
- Sync, async, count, replay and full build agree. Mask edit/Undo/reimport during a pending job cannot display stale output as current.
- AAO/MA prepared geometry receives the intended selection, or the affected renderer remains unsimplified with a clear diagnostic.
- Package compiles with and without the optional painter installed; public API compatibility and cancellation/disposal paths are covered.
- Measure mask preparation separately from repeated slider latency. Slider drags do not repeatedly read textures or rerasterize UVs.

## Investigation result

The public painter API and existing FA-QEM hard exclusions make this feasible. The work is larger than adding a texture field: tangent rebuilding, cleanup, replay, cache invalidation and prepared-mesh correspondence must obey the same preservation contract.

Validation so far is source/API inspection only. No Unity runtime tests, live project installation, or avatar modifications were performed for this exploration.

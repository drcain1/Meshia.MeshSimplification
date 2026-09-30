# Blender Decimator Port Notice

The implementation selected by `MeshSimplificationTargetKind.BlenderDecimateRatio`
is derived from Blender 5.2's `BM_mesh_decimate_collapse` implementation, principally
`source/blender/bmesh/tools/bmesh_decimate_collapse.cc`.

Blender is licensed under GPL-2.0-or-later. The Blender-derived policy code in
`Runtime/Jobs/SimplifyJob.cs` and `Runtime/ErrorQuadric.cs` carry those terms and
must not be represented as MIT-licensed. The combined fork source is offered under
GPL-2.0-or-later; upstream Meshia's original MIT notice is retained separately.
Full license texts are provided in `Licenses/`.

## Source attribution and changes

The source used during development is the Blender checkout at commit
`fbe6228777e7d9afefcd61a413844e790ae75db7`, including:

- `source/blender/bmesh/tools/bmesh_decimate_collapse.cc`, copyright 2023 Blender Authors.
- Blender's quadric and geometry helpers used by the collapse policy.

The source is available at https://projects.blender.org/blender/blender at that
commit. This identifies the local reference checkout, not an assertion that every
upstream routine was copied or that Blender itself is included in this package.

The fork adapts the collapse policy into C# and Unity Job System/Burst code, with
Meshia topology and attribute handling, skinning protection, and other fork changes.
The adaptations are maintained by drcain1. See Git history and release source
archives for the implementation and modifications associated with each version.

## Distribution

Release ZIPs contain C# source and notices. Each release also supplies a separate
source ZIP containing package source, build/release scripts, tests, and documentation,
with provenance identifying the reference commit and whether local changes existed.
Both are built together from the same checkout. Do not replace published ZIPs.

No linking exception is asserted for the Blender-derived code. Unity, VRChat, and
third-party packages retain their own licenses. External dependency compatibility
and any compiled distribution require separate assessment before publication;
the GPL notice does not settle that assessment. See `docs/FORK_DISTRIBUTION.md`.

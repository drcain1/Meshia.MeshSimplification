# Blender Decimator Port Notice

The implementation selected by `MeshSimplificationTargetKind.BlenderDecimateRatio`
is derived from Blender 5.2's `BM_mesh_decimate_collapse` implementation, principally
`source/blender/bmesh/tools/bmesh_decimate_collapse.cc`.

Blender is licensed under GPL-2.0-or-later. The Blender-derived policy code in
`Runtime/Jobs/SimplifyJob.cs` and `Runtime/MergeFactory.cs` carries those terms and
must not be represented as MIT-licensed. This repository is currently intended for
private use. Review the complete licensing and source-distribution obligations before
sharing binaries, packages, or source containing this port.

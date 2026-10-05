# Fork distribution license

This fork's combined source distribution is offered under the GNU General Public
License, version 2 or (at your option) any later version (GPL-2.0-or-later).
See [the full GPL version 2 text](Licenses/GPL-2.0.txt).

Original Meshia contributions by Ram.Type-0 retain their MIT license and copyright
notice, reproduced unchanged in [UPSTREAM-MIT.txt](Licenses/UPSTREAM-MIT.txt).
Those original contributions remain available under MIT independently; this fork's
combined distribution must not be represented as MIT-only.

The UV attribute quadric in `Runtime/FaQem/FaQemUvQuadric.cs` is adapted from
[touma-tw/Meshia.MeshSimplification](https://github.com/touma-tw/Meshia.MeshSimplification)
at commit `ed01f0b9b9f0f049d3e899c11c977bc70d0ebbc5`, distributed under the same
MIT notice retained above. Adaptations include double precision, normalized
coordinates, a combined geometry/UV solve, and FA-QEM integration.

The Blender-derived code and adaptations carry GPL-2.0-or-later terms. See
[BLENDER_PORT_NOTICE.md](BLENDER_PORT_NOTICE.md) for source attribution and changes.
Fork contributions by drcain1 are distributed under GPL-2.0-or-later.

This license notice does not relicense Unity, VRChat, or any other external dependency,
and does not grant a linking exception on behalf of Blender's copyright holders.
Review external dependency compatibility before distributing compiled combinations.
The prepared release contains source rather than compiled assemblies.

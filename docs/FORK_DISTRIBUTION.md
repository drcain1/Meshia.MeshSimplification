# Independent Meshia distribution

This is an independent fork maintained by drcain1, based on Meshia by Ram.Type-0.
It is an alternative continuation, not an official upstream release. Upstream's
future maintenance status is unknown; a gap between releases is not a statement
that the project has been abandoned.

## Identity and compatibility

| Item | Fork |
| --- | --- |
| Display name | Meshia — drcain fork |
| Package ID | `io.github.drcain1.meshia.mesh-simplification` |
| Listing ID | `io.github.drcain1.meshia.repository` |
| First stable version | `1.0.0` |
| Release tags | `fork-v<package version>` |
| Feed | `https://drcain1.github.io/Meshia.MeshSimplification/vpm/index.json` |

Versions belong to this fork and do not compete with upstream's `3.x` sequence.
The feed only accepts ZIPs bearing the fork package ID, with matching fork tags
and manifest versions. Normally, published versions remain available. The initial beta was withdrawn because it packaged the superseded baking implementation and private benchmark assumptions; use beta.2 or later.

C# namespaces, assembly names, component types, and existing Unity asset GUIDs
are intentionally retained to support existing references during replacement.
**The two variants cannot be installed together.** This is a replacement package,
not a side-by-side implementation. `legacyPackages` declares upstream's package
for removal when installing the fork through a supporting VPM client. This does
not redirect packages which explicitly depend on upstream's ID, prevent a later
reinstallation of upstream, or repair manually duplicated files under Assets.
Review the client's proposed changes before applying them. Do not add upstream
as a dependency or publish fork versions under upstream's package ID.

## Switching an existing project

1. Back up the complete Unity project, including scenes, prefabs, and Packages.
   Close Unity before changing packages.
2. Add the fork feed and install the stable release. Prerelease packages do not
   need to be enabled for `1.0.0`; that setting is only needed for beta versions.
3. Add `https://vpm.anatawa12.com/vpm.json` for the required localization package.
   Avatar integration also needs NDMF; cascading avatar features need Modular
   Avatar. Both are available through `https://vpm.nadena.dev/vpm.json`.
   These integrations remain optional for the core mesh library. LAC and Avatar
   Optimizer remain optional integrations, with no added package dependencies.
4. Install the fork and confirm that upstream Meshia is removed. If the old copy
   was installed through a Git URL or a local path, remove that entry through the
   appropriate package manager while Unity is closed. Check both
   `Packages/manifest.json` and `Packages/vpm-manifest.json`. Do not leave both IDs,
   embedded package folders, or an older Assets copy in the same project.
5. Reopen Unity and verify compilation, component references on scenes/prefabs,
   inspector styling, and a representative NDMF avatar build. Preserve the backup
   until those checks pass. Retained GUIDs reduce migration risk but are not proof
   that every historical component or serialized setting is compatible.

To return to upstream, close Unity, remove the fork, and install upstream explicitly.
Fork-only components or settings may not be understood by upstream. Restore the
backup when needed; do not promise lossless downgrade of fork-created content.
Packages that depend on upstream's ID must be assessed separately before switching.

## Publication prerequisites

Version `1.0.0` is the first stable source-only release. Check the GitHub Releases
page and feed for availability. Client installation checks remain distinct from
Unity local-package validation. ALCOM and VCC GUI installation have both been
manually validated; VCC replacement and dependency-resolution checks were confirmed
by the maintainer before promoting the tested beta.8 code to stable.

The combined fork source is now offered under GPL-2.0-or-later. `LICENSE.md`
describes that scope; `Licenses/GPL-2.0.txt` supplies the full text and
`Licenses/UPSTREAM-MIT.txt` retains the original upstream notice unchanged.
`BLENDER_PORT_NOTICE.md` identifies the reference checkout, adaptations, and
Blender attribution. No exception for linking proprietary dependencies is asserted.

Source-only release scope:

- Distribute Meshia's own C# source, assets, and notices; do not bundle Unity,
  VRChat SDK, NDMF, Modular Avatar, or other dependency packages.
- Include the matching source ZIP with the source/build information and provenance
  for the actual package. Package/source content and checksums have been verified.
- Let Unity resolve dependencies and compile the package on the user's computer.
  Both release archives are checked to contain no compiled libraries or executables.
- `distributionReady` is now `true` for this source-only release scope.
  This does not authorize bundling dependencies or shipping compiled binaries.

Stable-release installation checks cover the published feed in VCC and ALCOM.
The maintainer confirmed the final VCC installation/replacement and dependency
checks worked as intended. ALCOM installation, representative NDMF avatar builds,
and a tuned avatar tested in a live VRChat session were also manually validated.
These checks do not imply compatibility with every historical project or avatar.

`release-check` blocks the GitHub release workflow while `distributionReady` is
false. This is a release safeguard, not a legal determination. The packager separately
rejects compiled artifacts. License/attribution requirements still apply to our source.

## Geometry-only release validation

Version `1.1.0-beta.4` adds a per-mesh protection cycle: green for geometry
and deformation protection, yellow for geometry-only or partial protection, and
gray for No protection. Gray explicitly bypasses shape, boundary, seam, FA-QEM
material-boundary, selected-bone, skinning, surface-deviation and face-flip guards.
Basic connectivity and finite-number checks remain. UV Loop Dissolve uses its
Blender fallback directly while unprotected. Saved geometry settings remain intact;
returning to green restores them and explicitly enables deformation protection.
Existing entries retain their protection settings, with former deformation-off
icons now yellow when geometry safeguards remain. Auto Adjust never disables
protection; applying a protection preset clears the bypass. Undo restores changes.

The control works for skinned and static meshes, with English/Japanese tooltips
and README guidance. FA-QEM is now named consistently in both languages, and the
Japanese Blender Decimate labels and help use Blender デシメート. Changing protection
requires a new Analyze Build before editing measured output. No protection may
create holes or damage appearance and deformation; arbitrary exact counts are not
guaranteed even in this mode.

All 293 Meshia EditMode tests passed in Unity 2022.3.22f1 with no failures or skips,
including all four algorithms, unprotected count-profile parity, matching NDMF
preview/build geometry, and the protection cycle with Undo. Live Unity compilation
has no errors and the maintainer confirmed the inspector behavior. All 12
distribution tests and release prerequisites passed. This prerelease does not
claim a fresh VRChat or package-client installation test. Stable `1.0.1` remains
available; disabling protection is an explicit per-mesh choice.

Version `1.1.0-beta.3` fixes language changes incorrectly marking the current
analysis out of date. Nested property drawers emit change notifications while
translating labels; the cascading inspector now checks whether serialized settings
actually changed before invalidating the result. Real protection and allocation
edits still invalidate measurements. Embedded mesh settings share the inspector's
language selector; a standalone options drawer retains its own selector.

All 61 relevant inspector/localization EditMode tests passed in Unity 2022.3.22f1,
with no failures or skips. Regression coverage confirms that switching English and
Japanese preserves measured results and cached inputs, while real protection edits
invalidate them. Standalone and embedded language controls are covered. Live Unity
compilation has no errors, and the maintainer confirmed the updated behavior.
All 12 distribution tests and release prerequisite checks passed. This patch
changes inspector behavior only; geometry algorithms, defaults, dependencies, and
saved avatar settings are unchanged. Stable `1.0.1` remains available.

Version `1.1.0-beta.2` speeds up repeated FA-QEM output edits by reusing triangle
counts recorded along the normal collapse sequence. Requests outside the cached
range trigger one asynchronous count-only measurement; changes to captured inputs
or protections invalidate the cache. Other algorithms retain per-request measurement.
Analyze Build can prepare allocations from valid cached measurements before its
first verification build, and use an isolated mesh change between verified builds
to estimate downstream effects. Complete NDMF builds still verify the final count;
failed fitting restores the starting allocations and never marks an estimate verified.

A compact spinner and "Calculating..." beside Triangle budget show queued and
running background calculations, including with the section collapsed. The label
and usage guidance are available in English and Japanese. Geometry protections,
package dependencies, and saved avatar settings are not changed by this update.

All 286 Meshia EditMode tests passed locally in Unity 2022.3.22f1 with no failures
or skips, including count-profile parity, fitting/rollback, and inspector tests.
Representative edited-avatar fitting completed in two full builds; cached count
lookups reduce repeated mesh work but do not make the full build instantaneous.
All 12 distribution tests and release prerequisite checks passed. Live editor
compilation and the indicator were checked. This prerelease does not claim a new
VRChat or package-client installation test. Stable `1.0.1` remains available.

Version `1.1.0-beta.1` introduces measured output controls for cascading avatar
budgets. After analysis, mesh rows show output triangles alongside the original
count. Editing an output count searches for a matching simplification request;
pending, stale, and unattainable results are distinguished without adding another
line to each mesh. Internal requests remain available in calculation details.
Per-mesh reduction tools measure possible savings before applying them, while
ordinary edits continue to redistribute the remaining budget with Auto Adjust.

Automatic fitting uses measured responses, bounds each run's reductions, and
restores the starting allocations when it cannot fit within those limits. It
does not relax mesh protections. FA-QEM now consistently defaults Maximum Surface
Deviation to `0.0005`; explicitly saved values, including zero, are retained.
The deviation control supports finer adjustments, and English/Japanese text and
README guidance describe the updated behavior.

All 269 EditMode tests passed locally in Unity 2022.3.22f1, with no failures or
skips. A representative full NDMF analysis completed below its requested triangle
limit without changing saved settings; the live inspector showed the measured
per-mesh output. All 12 distribution tests and release prerequisite checks passed.
This is a prerelease for further avatar testing, not a new VRChat or package-client
installation certification. Hosted Unity test execution is separate from these
local results. Stable `1.0.1` remains available.

Version `1.0.1` fixes Analyze Build immediately reporting "Out of date" after
build plugins record delayed Undo changes on temporary materials or animation
assets. Analysis completion flushes these records while build invalidation is
still suppressed; subsequent user edits continue to invalidate the result.
Mesh simplification algorithms, protection defaults, and saved avatar settings
are unchanged. All 28 inspector EditMode tests passed locally in Unity 2022.3.22f1,
including delayed build Undo and genuine settings-edit invalidation. The fix was
also confirmed in a live project that reproduced the issue. All 12 distribution
tests and release prerequisite checks passed.

Version `1.0.0` promotes the tested `1.0.0-beta.8` implementation unchanged. Only
the package version and release/installation documentation change. The 27 inspector
EditMode tests passed locally in Unity 2022.3.22f1 for that implementation; the live
editor compiled without errors. All 12 distribution tests and release prerequisite
checks passed again for stable packaging. Hosted Unity tests remain separate and
skip when license credentials are not configured.

Both client installation checks and the prior live VRChat validation are complete.
Triangle budgets remain targets; geometry protection can prevent further reduction,
and individual avatars still need animated visual checks and appropriate settings.


Version `1.0.0-beta.2` uses the current geometry-only implementation. New cascading
entries default to FA-QEM; existing algorithm selections are retained. Texture
baking, atlas generation, and persistent baking are not included. The other mesh
algorithms remain manually selectable, and FA-QEM does not automatically bypass
its guards by falling back when a target cannot be reached.

Local Unity 2022.3.22f1 validation passed 117/117 EditMode tests for this cleaned
source, with no failures or skips. Twelve distribution checks also passed. The
removed tests covered only the retired private benchmark utilities; algorithm
regression coverage is retained.

Version `1.0.0-beta.3` adds Japanese translations for the inspectors, FA-QEM and
skinning controls, algorithm selection, build analysis, and UV preview. Both
inspectors expose a language selector, and open controls update without changing
mesh settings. Five targeted Unity EditMode inspector tests passed, including
language switching, panel reattachment, algorithm selection, Undo, and numeric
limits. English and Japanese catalogs each contain 196 matching translation keys.
The geometry algorithms and default protection settings are unchanged from beta.2.

Version `1.0.0-beta.4` fixes a missing job dependency when Smart Link is disabled
and preserves source zero-area faces on meshes with deformation data. Such faces
can open during skinning or blend-shape animation, so their vertices are locked
even when optional border/seam protection is disabled. Static zero-area faces
can still be removed. English and Japanese documentation describes this behavior.
Local Unity 2022.3.22f1 validation passed 126/126 EditMode tests, with no failures
or skips. Hosted Unity tests explicitly skip when license credentials are absent;
that skip is not a passing Unity test result. Package CI remains independent.

Version `1.0.0-beta.5` adds optional joint-transition protection to FA-QEM, including
an adjacent ring of vertices around changes in dominant bone influence. New avatar
configurations use conservative defaults: per-mesh automatic bone-weight protection
for deforming meshes, joint protection, border/seam preservation, and a small surface
deviation limit. Existing saved settings and the core library default are retained.
The cascading inspector offers an undoable action to apply conservative defaults
to existing entries without changing their algorithms, targets, or enabled/fixed
states. Preview and build policy resolution agree, and English/Japanese controls
and documentation explain protection and target shortfalls.

Protection takes priority over the requested triangle target. These defaults do
not guarantee 70,000 triangles or eliminate every possible clothing intersection;
inspect animated poses and tune mesh budgets before uploading an avatar. A fresh
default configuration and an existing tuned configuration passed full local NDMF
builds, with the tuned configuration's serialized settings unchanged. Local Unity
2022.3.22f1 validation passed 94 distinct targeted EditMode tests across the final
regression runs. Twelve distribution tests also passed. This beta is the candidate
for final validation before a stable release; it remains a prerelease.

Version `1.0.0-beta.6` improves budget editing and makes deformation protection
an explicit choice for new avatar entries. New entries start with deformation
protection off; existing saved settings are preserved. Conservative and Aggressive
preset buttons apply broad protection or focus it on detected hands, hair, and
named face meshes. The aggressive selection is approximate and applies to whole
meshes. Neither preset relaxes border, seam, or surface-deviation guards. New
entries named `Body` (case-insensitive) start excluded; this is a naming convention,
not a guarantee of face detection.

Lowering a mesh slider or number field now keeps the savings instead of raising
other allocations. Auto Adjust only reduces other unlocked targets when needed;
the explicit Adjust button can redistribute spare budget. Locks, exclusions,
inspector refresh, and Undo are covered by regression tests. A deformation shortcut
beside each row's lock/cog shows configured options consistently. Compact budget
guidance replaces repeated preview warnings, with collapsible details and advice
for removing geometry hidden beneath clothing. English and Japanese UI and README
content cover the changes.

Local Unity 2022.3.22f1 validation passed 67 targeted EditMode tests, with no failures.
Twelve distribution tests passed. Triangle targets remain allocation goals: retained
geometry can exceed them even with deformation protection off, because other guards
remain active. This beta does not claim a guaranteed 70,000-triangle result or a new
live VRChat validation of the preset defaults.

Version `1.0.0-beta.7` restores two-way Auto Adjust against the requested
triangle budget: lowering one mesh raises other unlocked targets, and raising it
lowers them. The edited mesh and locked allocations are retained. Allocations
reduced to zero can recover when budget becomes available again. AAO and calibrated
estimates no longer change the allocation budget, including on Reset; excluded
meshes reserve their source triangle counts. Actual output can still exceed an
allocation when geometry guards prevent further reduction.

The compact Triangle budget panel shows allocations and one main result. A current
successful build takes priority, outdated results are subdued, and unverified
preview estimates are labeled. Missing preview counts are not replaced by targets.
Calculation details starts collapsed and retains intermediate counts and diagnostics.
Current overruns use amber; failed analyses use red. English/Japanese controls,
tooltips, documentation, and live count updates are included.

Local Unity 2022.3.22f1 validation passed 77 distinct targeted EditMode tests across
the final regression runs. Twelve distribution tests passed. The live editor
compiled without errors and the new summary was checked without changing saved
avatar settings. Hosted Unity test jobs remain separate and can skip when no
license is configured. This is a prerelease, not a new live VRChat visual validation.

Version `1.0.0-beta.8` hides inspector settings that the selected algorithm does
not use, in both cascading mesh entries and standalone components. Blender
Decimate retains bone-weight protection. UV Loop Dissolve exposes that protection
for its Blender fallback, with a localized explanation; loop removal uses its own
built-in guards. Original Meshia hides the unused deformation controls and shortcut.
Switching algorithms retains saved options, including through Undo and language changes.

Vertex-protection labels are now **Preserve Vertices Near Joints** and
**Lock Coincident Split Vertices**, with clearer tooltips. English and Japanese
inspector labels and README terminology are aligned, including FA-QEM parameters.
This release changes inspector presentation and documentation, not the decimation
algorithms or saved avatar allocations.

Local Unity 2022.3.22f1 validation passed all 27 inspector EditMode tests, including
algorithm switching, English/Japanese localization, preserved settings, and Undo.
The live editor compiled without errors. Twelve distribution tests passed.
This remains a prerelease; no new VRChat visual validation is claimed.

The public repository contains generic synthetic mesh, skinning, inspector,
serialization, and NDMF regression tests. Avatar-specific manual investigations,
external animation paths, private reports, and local dependency-patch claims are
not part of this release. Local Unity test results and package CI are separate
checks; VCC/ALCOM installation through the published feed remains a distinct
validation step.

If an older experimental version saved a persistent texture bake into your project,
restore the original assets with that version before updating. Removing the baker
does not reconstruct the originals.

## Build and hosting

Maintainers: follow the [release procedure](https://github.com/drcain1/Meshia.MeshSimplification/blob/main/RELEASE_PROCEDURE.md) for the complete publication and verification workflow.

Run `python .github/scripts/vpm.py pack` to prepare a local review ZIP in `dist/`.
Only package source, paired metadata, README, this migration guide, and license/port notices are included;
test harnesses, benchmarks, workflow files, repository instructions, and API doc
build outputs are excluded. No `.unitypackage` is published by this workflow.
The same command also builds a separate `-source.zip` containing the current source,
tests, build/release scripts, and documentation. Its `SOURCE_PROVENANCE.json` records
the reference commit, local-change status, package ZIP checksum, and source file
checksums. Local review archives include uncommitted work; published builds use
the release workflow's checked-out commit. Both ZIPs are attached to the release.

The Build Release workflow creates a draft under `fork-v<version>` after its release
gate passes. Inspect its package before publishing. Publishing triggers Build Docs,
which builds API docs and a `/vpm/` listing/installation page together. Enable Pages
with GitHub Actions as its source before the first deployment. This uses the current
repository's single Pages site, avoiding competing deployments.

The listing builder reads published releases only, skips inherited upstream assets
and drafts, validates package identity/tag/version/prerelease flags, and computes
`zipSHA256` from the actual downloaded package ZIP. It keeps all matching published
versions, including betas. A failed download or invalid fork release fails deployment
instead of publishing an incomplete listing. Do not edit or replace release ZIPs
after publication; ship a new version.

For offline review, run `python .github/scripts/vpm.py listing --empty --out dist/vpm`.
This creates an empty feed and page without claiming that any package is available.

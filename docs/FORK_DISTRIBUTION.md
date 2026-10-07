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

### Original investigation: texture mapping near UV seams

Investigate whether simplification can better preserve texture mapping and shading
in triangles next to UV seams, especially for fine normal-map details. Locking
coincident split vertices protects the seam vertices, but does not guarantee that
mapping across neighbouring triangles remains unchanged as the mesh is reduced.

Original report: an investigation idea, not a confirmed Meshia defect. In that case,
freezing the normal map in LAC and using a 1x divisor resolved the major shading
distortion. A minor discontinuity remained near a suspected UV seam; its cause
has not been isolated from texture processing or the original material/mesh.
The separately reproduced blend-shape shading-offset bug is already fixed in
`1.1.3-beta.1` and should not be treated as proof of this issue's cause.

For further seam investigations, compare Meshia on/off with texture processing, pose,
material, and camera held fixed, and inspect the source/output UVs and textures.
The UV-aware scoring and joint shape/UV optimization shipped in `1.1.3-beta.2`
address texture-mapping preservation, but do not establish the cause of every
seam discontinuity. Isolate any remaining issues before adding further guards.
Use synthetic seam fixtures with fine texture details to check mapping continuity,
normal/tangent behaviour, and the triangle-count and performance tradeoffs.
Any additional protection should be an explicit per-mesh choice with clear
budget implications; exact appearance preservation is not guaranteed.

### Release history

Version `1.2.0-beta.1` also extends FA-QEM's per-mesh Auto policy to resolve humanoid knee/elbow pairs,
including separate clothing rigs through stock MA Merge Armature mappings. The
reduction kernel preserves only connected dominant-weight transitions for those
pairs and one source support ring. Mapping dependencies invalidate preview caches;
slider replay reuses the prepared result. Manual policies, legacy Auto, and No
protection do not gain this automatic guard. Unmapped custom rigs retain the
existing weight checks. English/Japanese cogwheel explanations and documentation
describe the independent automatic and manual protection. Saved avatar settings
are not rewritten. Validation passed 388 Unity EditMode tests, followed by 79
focused integration/inspector tests after the final mapping and tooltip changes,
and 12 distribution tests. Live editor inspection detected the knee support bands
on both sampled tights meshes. A new in-game bending check remains pending.

Version `1.2.0-beta.1` starts the 1.2.0 release series, incorporating the published
`1.1.3-beta.1` through `1.1.3-beta.7` changes and the interactive preview work
previously prepared as an unpublished `1.1.3-beta.8` draft. Since stable `1.1.2`,
the main additions are experimental per-mesh texture-mapping preservation with
Low/Medium/High strength controls (new/reset settings default to On/Medium 5000),
supported AAO cuts and MA visibility-boundary protection, experimental clothing
contact/deformation protection around supported cuts, and the Extreme protection
preset with a revised reduction range. Existing saved settings are retained.
The earlier beta entries below describe each feature and its limitations.

This beta improves interactive FA-QEM previews. Slider-only edits
reuse exact reduction sequences and unchanged mesh outputs. Initial preparation
avoids unused legacy work, repeated surface queries, and unchanged edge candidates.
Full-detail meshes defer their reduction sequence until an edit needs it.

With NDMF Preview enabled, FA-QEM sliders remain editable after protection changes.
The latest requested output waits for fresh preview measurements instead of using
the previous protection settings. Analyze Build still verifies the complete final
avatar. Undo and Redo restore measured slider values and cancel obsolete edits.
The calculating indicator remains active until the preview is displayed. Inspector
refreshes share settings snapshots and skip hidden calculation details.

Before the automatic joint support addition, Unity 2022.3.22f1 passed **385 EditMode tests**, including replay/fresh
output parity, protection cycling, rapid pending edits, Undo/Redo, deferred count
preparation, concurrent measurements, and disposal during measurement. Local
comparison runs matched all 19 sampled output mesh fingerprints before and after
the final deferred-preparation change. The user confirmed interactive behavior.
These timings and checks do not establish performance on every avatar or constitute
a new in-game geometry validation. No protection preset defaults or private avatar
settings are changed by this release. English and Japanese guidance is updated.

Version `1.1.3-beta.7` extends the experimental overlap protection around supported
cuts into the covering clothing's bending region. It identifies meaningful bone
influence pairs at contact, follows the corresponding blended region, and retains
the associated joint transitions along with the user's existing joint selection.
Meshes with more than four bone influences per vertex are supported. Detection
uses geometry and weights rather than avatar-specific mesh or bone names.

The resolved protection is shared by synchronous builds, asynchronous previews,
and measured-output trials. It follows Preserve Border Edges and No protection;
serialized user options are not rewritten by the automatic protection. English
and Japanese documentation describes the behavior and remaining limits. This
release does not include private avatar tuning or change protection preset defaults.

Validation: all 363 Meshia EditMode tests passed in Unity 2022.3.22f1 with stock
AAO 1.9.20 and MA 1.18.7, including four new tests for contact influence selection,
joint-selection merging, variable bone-influence counts, and matching synchronous
and asynchronous simplification. All 12 distribution tests and release prerequisites
passed. Representative complete NDMF builds and static, 90-degree, and 120-degree
bent-knee diagnostics were checked locally. These checks do not establish freedom
from clipping in every pose or outfit. Close clothing layers without a supported
cut remain outside the detection, and stronger protection can prevent fitting a
requested budget. No fresh VCC/ALCOM installation or complete in-game clearance
of this release is claimed. Hosted Unity license-dependent jobs are separate evidence.

Version `1.1.3-beta.6` prepares supported AAO Remove Mesh By BlendShape cuts
before FA-QEM simplification so Preserve Border Edges can retain the new cut
boundaries. This requires AAO 1.9+, non-inverted selection, and clamped
interpolation. MA visibility boundaries are protected independently of the
deformation preset. Stock AAO and MA packages are supported without modification.

Experimental overlap protection limits local shape changes on both meshes near
close-fitting clothing openings over supported cuts. Preview, build output, and
measurement trials share the resolved protection. This is reference-pose surface
protection, not collision prevention: clipping during joint bends or between
nearby clothing layers remains possible. The separate knee-deformation and tie
diagnostic experiments are not included in this release.

Aggressive now uses the previous Extreme baseline; Extreme allows additional
surface deviation on meshes outside the deformation-protected selection.
Existing settings change only when a preset is applied. Budget adjustment
accounts for the changed cut order and handles protection plateaus more robustly;
new protections can retain extra triangles, so fitting the budget is not guaranteed.
Empty cut meshes and implicit single-weight visibility data are handled safely.
English and Japanese documentation explains the supported cases and limits.

Validation: 359 Meshia EditMode tests passed in Unity 2022.3.22f1 with stock
AAO 1.9.20 and MA 1.18.7. Without AAO, 352 passed and seven AAO tests were skipped.
All 12 distribution tests and release prerequisites passed. Representative NDMF
analysis preserved all 797 measured body boundary positions and left original
component settings unchanged; its final measured count remained 338 above a
70,000 target. Static and bent-pose diagnostics identified the remaining clipping
limitations. No complete in-game clearance or fresh VCC/ALCOM installation of
this beta is claimed. Hosted Unity license-dependent jobs are separate evidence.

Version `1.1.3-beta.5` refines Extreme: it retains the default limb and hand
joint-vertex selection, including elbows and knees, and allows a surface deviation
of 0.001 away from protected vertices. Its lighter bone-weight limits remain
unchanged. Apply Extreme again to use the revised preset on existing entries;
saved settings are not migrated automatically. English and Japanese descriptions
explain the revised behavior.

When Prefabulous Generate Twist Bones opts into the Optimizing phase, build order
is explicitly Meshia, then twist generation, then Avatar Optimizer. Prefabulous
remains optional and its earlier phases are unaffected. This ordering does not
fix rig-specific twist deformation or guarantee compatibility with existing
twist-bone weights.

Validation: all 341 Meshia EditMode tests passed locally in Unity 2022.3.22f1,
with zero failures or skips, including Extreme joint preservation and optional
plugin ordering. All 12 distribution tests and release prerequisites passed.
Representative local NDMF builds completed successfully. No fresh in-game
confirmation of the packaged beta or guaranteed triangle budget is claimed.
Hosted Unity jobs may skip without license credentials; local tests are separate.

Version `1.1.3-beta.4` adds an explicit Extreme protection preset for further
reduction. It retains conservative surface, border, seam, face-flip and texture
protection, with lighter bone-weight limits and joint-vertex protection focused
on hands and fingers. Existing settings change only when the preset is applied;
algorithms, targets, exclusions and fixed allocations are retained, with Undo.
Automatic deformation protection now hides the inactive manual override so it
does not appear to report that protection is off. The Original column uses source
mesh counts rather than counts from a different preview processing stage.
English and Japanese UI text and documentation cover the changes.

Validation: all 338 Meshia EditMode tests passed in Unity 2022.3.22f1, with zero
failures or skips. All 12 distribution tests and release prerequisites passed.
The live editor compiled without errors and all six avatar configurations were
unchanged after refresh. The simplification algorithm is unchanged; the new
preset changes explicitly selected settings. No new in-game visual validation
is claimed for Extreme. Texture preservation remains experimental.

Version `1.1.3-beta.3` simplifies experimental texture preservation inside each
mesh's cogwheel: one toggle, Low/Medium/High strength presets, and an editable
number on the same row. Custom values remain supported; the method selector and
Advanced foldout are removed. New and reset options default to enabled with
Medium strength (5000). Existing initialized settings retain their strength and
legacy method until explicitly re-enabled or reset. Narrow inspector layouts
keep the controls visible, and preset/numeric changes stay synchronized.

Validation: 62 inspector EditMode tests passed in Unity 2022.3.22f1, including
300/360/500-pixel layouts in English and Japanese, custom values, Undo, reset,
and serialized-settings preservation. Both live validation editors compiled
without errors. All 12 distribution tests and the release prerequisite check
passed. Geometry implementation is unchanged from beta.2; the new Medium default
changes the strength for new/reset options, with no automatic migration of saved
meshes. Texture preservation remains experimental. At publication, no new in-game
validation was claimed for this UI release or for the Medium default.

Version `1.1.3-beta.2` adds experimental per-mesh texture-mapping preservation
for FA-QEM, including a joint vertex-position/UV solve and tangent rebuilding.
New settings and Reset Options enable both controls at strength 1000. Existing
saved choices retain their values; users can disable either control per mesh.
The gray No protection state bypasses UV preservation. Texture images are not
modified. Existing shape, boundary and deformation protections continue to apply;
UV preservation does not guarantee perfect mapping or prevent all gaps.

The UV attribute quadric is adapted from touma-tw's MIT-licensed Meshia fork;
source attribution and the license notice are retained. Private comparison tools,
research notes, avatar fixtures and rendered comparisons are excluded from this
release and its public commit history. The earlier seam investigation above
remains relevant for incomplete mapping coverage and broader compatibility.

Local Unity 2022.3.22f1 validation passed 331 EditMode tests, including solve
fallbacks, mirrored UVs, tangent/deformation data, asynchronous/batch equivalence,
count profiles, settings identity, saved opt-outs, localization and Undo/Reset.
The user reported a successful in-game visual check on a test avatar at strength
1000. This is not a claim of exhaustive pose/material coverage or a new VCC/ALCOM
installation test. Hosted test availability is reported separately from local tests.


Version `1.1.3-beta.1` fixes blend-shape shading offsets being normalized during
vertex merging. Normal and tangent deltas now retain their magnitude under both
edge and barycentric interpolation. The defect was reproduced with FA-QEM and
Blender Decimate: small shading adjustments could become unit-length offsets,
causing abrupt shading changes when the affected blend shape is active. This
changes shading data, not normal-map textures or saved protection settings.

All 304 local Meshia EditMode tests passed in Unity 2022.3.22f1, with no failures
or skips. Nine new regression cases cover interpolation modes, synchronous and
asynchronous simplification, multiple frames, and zero shading offsets. Six cases
failed before the fix. A live synthetic-mesh check confirmed that normal/tangent
offsets of 0.02/0.03 remain unchanged rather than becoming 1.0. The running editor
reported no compilation errors or warnings. The reported in-game artifact has
not yet been confirmed on the affected avatar; this beta is available for that
comparison. No new VRChat or package-client installation validation is claimed.
Stable `1.1.2` remains available.

Version `1.1.2` removes two editor compiler warnings. Cascading previews use
explicit render-group equality on NDMF 1.13 and newer, preserving the component
and entry-index identity used previously. A version guard retains the legacy API
for older NDMF releases. The localization getter now falls back to English if
the library has no current locale. No simplification algorithms, protection
defaults, avatar settings, or package dependencies change.

All 75 affected preview and inspector EditMode tests passed in Unity 2022.3.22f1
with NDMF 1.14.8, without failures or skips. Regression coverage checks preview
group identity; a live missing-locale check confirmed the English fallback.
The running editor reported no compilation errors or warnings. Older NDMF
compatibility was checked against its API source, not a separate execution of
the test suite. All 12 distribution tests and release prerequisites passed.
No new VRChat or package-client installation validation is claimed for this patch.

Version `1.1.1` fixes mesh object fields displaying None after switching
inspector languages. References remained intact; the localization visitor was
replaying the placeholder text captured before each row received its renderer.
Object fields now translate only their authored label and tooltip, leaving
Unity responsible for displaying the current object. Mesh settings, geometry,
protection defaults, and package dependencies are unchanged.

Both regression checks reproduced the issue before the fix. All 59 inspector
EditMode tests passed in Unity 2022.3.22f1 after the fix, with no failures or skips,
including late assignment, reused fields, panel reattachment, reference/display
consistency, and analysis freshness. Live English/Japanese switching retained
all 34 inspected object-field names and left avatar settings unchanged; Unity
compilation had no errors. All 12 distribution tests and release prerequisites
passed. This UI-only patch does not claim a new VRChat or package-client test.

Version `1.1.0` promotes the `1.1.0-beta.4` implementation unchanged to stable.
It includes measured per-mesh output controls, asynchronous FA-QEM count reuse,
bounded automatic budget fitting with rollback, background calculation status,
the green/yellow/gray protection cycle, and English/Japanese localization fixes.
The README now gives practical English/Japanese steps for holes and distorted
shapes, including surface-deviation tuning, bone-weight protection, and the
triangle-budget tradeoff. It also corrects the older description of guards
bypassed by No protection. No avatar settings or algorithm defaults change
between beta.4 and this stable release.

The unchanged implementation passed all 293 local Meshia EditMode tests in
Unity 2022.3.22f1 with no failures or skips for beta.4. All 12 distribution tests
and release prerequisites were rerun for 1.1.0. Release archives are checked
against the committed source and published feed checksums. Hosted Unity test
execution remains separate and can be skipped without license credentials.
The maintainer approved stable publication after live inspector use and a
successful tail-settings correction. No fresh 1.1.0 VCC/ALCOM upgrade or VRChat
validation is claimed. Existing stable and beta releases remain available.

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

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
| First beta version | `1.0.0-beta.1` |
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
2. Add the fork feed after its first public release. Beta versions require enabling
   prerelease packages in the client.
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

The first release is a source-only beta. Check the GitHub Releases page and feed
for availability. Client installation checks remain distinct from Unity local-package
validation; this document does not claim that VCC/ALCOM GUI installation has passed.

The combined fork source is now offered under GPL-2.0-or-later. `LICENSE.md`
describes that scope; `Licenses/GPL-2.0.txt` supplies the full text and
`Licenses/UPSTREAM-MIT.txt` retains the original upstream notice unchanged.
`BLENDER_PORT_NOTICE.md` identifies the reference checkout, adaptations, and
Blender attribution. No exception for linking proprietary dependencies is asserted.

Source beta release scope:

- Distribute Meshia's own C# source, assets, and notices; do not bundle Unity,
  VRChat SDK, NDMF, Modular Avatar, or other dependency packages.
- Include the matching source ZIP with the source/build information and provenance
  for the actual package. Package/source content and checksums have been verified.
- Let Unity resolve dependencies and compile the package on the user's computer.
  Both release archives are checked to contain no compiled libraries or executables.
- `distributionReady` is now `true` for this source-only beta release scope.
  This does not authorize bundling dependencies or shipping compiled binaries.

Before calling the first release stable, validate installation through the published
feed in both VCC and ALCOM, including each client's upstream replacement behavior,
dependency resolution, and a representative avatar build. The current validation
covers local-package migration and Unity compilation, not a deployed feed.

`release-check` blocks the GitHub release workflow while `distributionReady` is
false. This is a release safeguard, not a legal determination. The packager separately
rejects compiled artifacts. License/attribution requirements still apply to our source.

## Geometry-only release validation

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

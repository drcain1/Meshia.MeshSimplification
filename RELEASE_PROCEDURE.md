# Release procedure

Maintainer runbook for `drcain1/Meshia.MeshSimplification`. Use alongside
`AGENTS.md` and [distribution guidance](docs/FORK_DISTRIBUTION.md).
Last verified against **1.0.1 on 2026-10-02**. Rediscover versions, run IDs,
Unity instances, local paths, and test counts for each release.

## 1. Confirm authorization, checkout, and identity

A request to publish a release authorizes its normal commit, push, draft,
verification, and publication steps. Preparing a release or writing documentation
alone does not authorize publication. Do not add redundant approval gates when
publishing is already authorized. Honor an explicit request to wait for a manual
test until the user reports its outcome.

Run examples from the **package repository root**, not a parent folder or Unity
project. Examples use PowerShell, Git, GitHub CLI (`gh`), and Python 3. Check native
command exit codes and stop on failure: PowerShell does not automatically stop
when Git, Python, or gh returns a nonzero `$LASTEXITCODE`.

```powershell
git rev-parse --show-toplevel
git status --short --branch
git remote -v
git fetch origin main
git log -6 --oneline --decorate
git diff --stat
git config user.name
git config user.email
gh auth status
$ReleaseConfig = Get-Content .github/vpm.json -Raw | ConvertFrom-Json
$Manifest = Get-Content package.json -Raw | ConvertFrom-Json
$Repo = $ReleaseConfig.repository
$PackageId = $Manifest.name
$FeedUrl = $ReleaseConfig.url
gh release list --repo $Repo --limit 8
```

- Preserve unrelated/uncommitted changes and other agents' work. Do not reset,
  clean, stash, rebase, or force-push merely to simplify a release.
- Main is the normal release branch. Update a suitable clean checkout with
  `git pull --ff-only origin main`; investigate divergence instead of rewriting it.
- Verify the configured author uses the maintainer's approved GitHub no-reply
  identity. Keep personal emails, machine usernames, and local paths out of public
  notes. Do not print credentials while checking authentication.
- **Always specify `--repo` for gh commands.** The fork/upstream relationship can
  otherwise send commands to the wrong repository.

| Item | Expected value |
| --- | --- |
| Repository | `drcain1/Meshia.MeshSimplification` |
| Package ID | `io.github.drcain1.meshia.mesh-simplification` |
| Display name | `Meshia — drcain fork` |
| Tag | `fork-v<package.json version>` |
| VPM page | https://drcain1.github.io/Meshia.MeshSimplification/vpm/ |
| Feed | https://drcain1.github.io/Meshia.MeshSimplification/vpm/index.json |
| Upstream replacement ID | `com.ramtype0.meshia.mesh-simplification` |

Preserve namespaces, assembly identities, component types, and Unity GUIDs unless
intentionally migrating them. Only one Meshia variant belongs in a project. Never
publish under upstream's package ID or add it as a dependency of this fork.

## 2. Validate the change and the correct Unity project

**Use Unity MCP when available to discover, select, inspect, and control running
Unity Editors.** Never substitute OS process inspection or call its HTTP bridge
directly. Discover the current instances, select the user-requested project, and
include its port in subsequent calls. Do not assume a previously used project is
still running.

Through MCP, inspect `UnityEditor.PackageManager.PackageInfo.FindForAssembly(...)`
using a Meshia editor assembly. Check `resolvedPath`, `source`, and `version`:

- A local package can reference this Git checkout; source changes apply after an
  asset refresh.
- An embedded/VPM installation is a separate copy. Pushing Git or publishing a
  release does not update that copy. Use the appropriate update workflow when the
  user also asks to update it, preserving any local patches.
- Refresh through MCP, allow importing/compiling to finish, and check compilation
  errors. Do not save scenes, overwrite avatar settings, toggle Play Mode, or
  replace dependencies merely to refresh the package.

Choose validation proportional to the change:

| Change | Evidence |
| --- | --- |
| Inspector/localization | Relevant inspector EditMode tests; live behavior; English/Japanese switching; settings preservation and Undo where affected; UI evidence where practical. |
| Geometry/deformation | Relevant geometry/skinning tests, representative NDMF builds, and animated visual checks. |
| Analysis/budgeting | Complete NDMF analysis; correct counts/freshness; preservation of user allocations. |
| Packaging/dependencies/migration | Install/upgrade through the published feed, dependency resolution, and upstream replacement in affected clients. |
| Documentation only | Links, factual instructions, and archive/distribution validation. |

Prefer a separate validation project for automated tests when it avoids disturbing
an open user project. Read the Unity testing skill/tool help before using the CLI.
The validation project must reference the intended package checkout and declare
it in `testables`; include dependencies required by the tested assemblies. Inspector
tests require NDMF and Modular Avatar. Zero executed tests is not a passing result.

Example for a separate, closed project (replace placeholder paths and choose the
appropriate filter; do not always restrict geometry changes to inspector tests):

```powershell
unity test '<validation-project-path>' --mode EditMode --filter InspectorLocalizationTests --output '<private-results-path>/inspector.xml' --timeout 180 --format json
```

Read the XML results and record Unity version, scope, passed/failed/skipped counts.
Keep raw reports and avatar fixtures outside tracked release content. Historical
reference: 1.0.1 passed 28 inspector tests; that is not the full suite size or proof
that a future revision passed.

**Hosted Unity CI can be green with tests skipped.** `run-tests.yml` checks license
credentials and skips actual test jobs when absent. Inspect job results before
claiming Unity CI passed. Local tests and package CI are separate evidence. Keep
CI; do not remove it or obtain paid licensing simply to change a badge.

Manual VRChat, VCC, and ALCOM checks apply only to the version/configuration tested.
ALCOM success does not prove a VCC GUI test. Static renders or Play Mode do not
prove in-game motion quality. Retest changed geometry settings in VRChat; UI-only
patches may retain prior geometry validation without claiming a fresh VRChat run.
Triangle count alone does not establish the overall VRChat performance rating.

## 3. Prepare version, notes, and release gates

1. Choose the requested semantic version and confirm it has not shipped. Stable
   versions look like `1.0.1`; prereleases like `1.1.0-beta.1`.
2. Update `package.json`. Preserve identity, license, dependencies, and
   `legacyPackages`. `zipSHA256` belongs in the feed, not this manifest.
3. Add a factual change/validation entry in `docs/FORK_DISTRIBUTION.md`. Clearly
   retain older entries as history. Update English/Japanese UI and README text
   when behavior or terminology changes.
4. Write focused **English and Japanese release notes** to a UTF-8 file outside
   tracked content. Lead with the user-visible change, then limits and verification.
   The workflow's initial notes are the entire distribution guide; replace those
   before publishing. Use `--notes-file`, not shell-built multiline text.
5. Prefer separate commits for the fix and version/release documentation. Stage
   explicit files and review the diff rather than sweeping in unrelated work.

Privacy and package scope:

- Never include private avatar/model assets, scenes, prefabs, animations, personal
  investigation reports, credentials, or retired texture-baking code.
- The packager rejects forbidden types/paths, but its checks do not replace a
  content review. Permission to publish rendered comparison images does not
  authorize distributing their source avatars.
- Retain license/attribution notices. Distribution is source-only GPL-2.0-or-later
  with upstream and Blender notices. Do not bundle Unity, SDKs, dependencies,
  compiled libraries, or executables.
- `docs/` currently permits only `FORK_DISTRIBUTION.md` and its `.meta` in archives.
  Root-level maintainer files such as this runbook enter the source archive. Do not
  relax the privacy allowlist to include a local investigation report.
- Pair Unity assets with `.meta` files; preserve existing GUIDs.

```powershell
python .github/scripts/vpm.py release-check
python -m unittest discover -s .github/scripts -p 'test_*.py'
git diff --check
```

Stop on failure. Do not flip `distributionReady` to bypass a failed prerequisite.
There were 12 distribution tests in 1.0.1; record the actual future count.
For optional local review, `python .github/scripts/vpm.py pack` writes ignored
`dist/` archives. Local packaging includes eligible uncommitted/untracked files;
public archives must be built from the clean, committed release revision.

After committing the intended changes, push and capture the release identity:

```powershell
$Manifest = Get-Content package.json -Raw | ConvertFrom-Json
$Version = $Manifest.version
$Tag = "fork-v$Version"
$ReleaseCommit = git rev-parse HEAD
git push origin main
git status --short --branch
```

Confirm the tree is clean and remote main matches `$ReleaseCommit`. If another
agent advances main, reconcile the intended commit before releasing; do not
silently publish an unreviewed head.

## 4. Build the draft through GitHub Actions

```powershell
gh workflow run release.yml --repo $Repo --ref main
gh run list --repo $Repo --workflow release.yml --limit 5 --json databaseId,headSha,status,url
```

Select the newly dispatched run with `headSha == $ReleaseCommit`. Replace the run
ID placeholder below with that actual ID. Interactive agents should wait in bounded
intervals and keep the user informed, rather than blocking silently.

```powershell
$ReleaseRunId = '<release-run-id>'
gh run watch $ReleaseRunId --repo $Repo --exit-status --interval 10
gh release view $Tag --repo $Repo --json isDraft,isPrerelease,targetCommitish,assets,url
```

The workflow validates prerequisites and creates a **draft** with three assets:

- `$PackageId-$Version.zip`: installable Unity/VPM source package.
- `$PackageId-$Version-source.zip`: matching source, tests, scripts, documentation,
  and `SOURCE_PROVENANCE.json`.
- `package.json`: the matching manifest.

Hyphenated prerelease versions get the prerelease flag. Draft URLs may temporarily
contain `untagged-...`; verify tag/target fields instead of using that as the final
release URL. If a draft/tag exists, inspect it before retrying. Never delete or
overwrite a published release to rerun this workflow.

## 5. Verify the actual draft archives

Download into a fresh directory outside the tracked repository:

```powershell
$VerificationDirectory = '<absolute-private-verification-directory>'
gh release download $Tag --repo $Repo --dir $VerificationDirectory
```

Verify the actual ZIP contents, without extracting over a Unity project:

- Tag, ZIP names, both archived manifests, and standalone manifest match the
  intended package ID/version.
- The package contains the fix and excludes tests/private files. The source ZIP
  contains matching implementation and regression tests.
- `referenceCommit` is `$ReleaseCommit`; `workingTreeModified` is `false`.
- `packageZipSHA256` matches the downloaded installable archive; every
  `sourceSHA256` entry matches the corresponding source ZIP entry.
- Licenses, notices, and required `.meta` files are present.

For reproducible verification, save the following as `verify-release.py` outside
tracked content, then run the command beneath it:

```python
import hashlib, json, sys, zipfile
from pathlib import Path
folder, package_id, version, commit = sys.argv[1:]
folder = Path(folder)
package = folder / f'{package_id}-{version}.zip'
source = folder / f'{package_id}-{version}-source.zip'
with zipfile.ZipFile(package) as p, zipfile.ZipFile(source) as s:
    manifest = json.loads(p.read('package.json'))
    assert manifest['name'] == package_id and manifest['version'] == version
    assert json.loads(s.read('package.json')) == manifest
    assert json.loads((folder / 'package.json').read_text(encoding='utf-8')) == manifest
    provenance = json.loads(s.read('SOURCE_PROVENANCE.json'))
    assert provenance['referenceCommit'] == commit
    assert provenance['workingTreeModified'] is False
    assert provenance['packageZipSHA256'] == hashlib.sha256(package.read_bytes()).hexdigest()
    for path, digest in provenance['sourceSHA256'].items():
        assert hashlib.sha256(s.read(path)).hexdigest() == digest, path
print('Manifests, commit provenance, and checksums verified.')
```

```powershell
python '<private-path>/verify-release.py' $VerificationDirectory $PackageId $Version $ReleaseCommit
```

Also inspect the release commit's package CI result. For packaging/dependency
changes, smoke-test the downloaded package in a suitable test project, not only
the local Git checkout. Do not claim artifact installation if only contents and
hashes were checked.

## 6. Publish and verify the public VPM feed

Once the authorized release is verified, replace draft notes and publish:

```powershell
$NotesFile = '<absolute-path-to-release-notes.md>'
$ReleaseTitle = "Meshia drcain fork $Version — <brief user-visible change>"
# Stable release:
gh release edit $Tag --repo $Repo --title $ReleaseTitle --notes-file $NotesFile --draft=false --prerelease=false --latest
# For a prerelease, use --prerelease=true --latest=false instead; do not run both.
gh release view $Tag --repo $Repo --json isDraft,isPrerelease,publishedAt,url,assets
```

The version suffix and GitHub prerelease flag must agree or VPM generation fails.
Stable releases need no prerelease client setting. Publication triggers **Build
Docs** (`docs.yml`), which builds DocFX and the VPM feed and deploys both to the
repository's single GitHub Pages site.

```powershell
gh run list --repo $Repo --workflow docs.yml --limit 5 --json databaseId,headSha,status,event
$DocsRunId = '<release-triggered-docs-run-id>'
gh run watch $DocsRunId --repo $Repo --exit-status --interval 10
```

Allow time for the release event to appear. If no deployment starts, or a failed
run needs retrying after its cause is resolved, dispatch manually:

```powershell
gh workflow run docs.yml --repo $Repo --ref main
```

Documentation-only pushes do not automatically deploy Pages. Do not duplicate a
healthy deployment while it runs. Listing generation reads published releases,
skips drafts/upstream releases, downloads the package archives, and fails on an
invalid release rather than silently omitting its version.

Verify the **public feed**, not only the workflow status:

```powershell
$Feed = Invoke-RestMethod -Uri $FeedUrl -Headers @{ 'Cache-Control' = 'no-cache' }
$Published = $Feed.packages.$PackageId.versions.$Version
if ($null -eq $Published) { throw "Version $Version is missing from the feed" }
$ExpectedUrl = "https://github.com/$Repo/releases/download/$Tag/$PackageId-$Version.zip"
if ($Published.url -ne $ExpectedUrl) { throw 'Unexpected download URL' }
$ExpectedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $VerificationDirectory "$PackageId-$Version.zip")).Hash.ToLowerInvariant()
if ($Published.zipSHA256 -ne $ExpectedHash) { throw 'Feed checksum mismatch' }
```

Check that the VPM landing page also loads with the expected release status.
Allow a short cache delay after successful deployment; retry with cache bypass
before declaring a feed failure.

## 7. Finish and report

- Confirm main is pushed; distinguish any unrelated local changes.
- Give the GitHub release and VPM links, version, stable/prerelease status, actual
  test results, archive verification, and deployment outcome.
- Distinguish local compilation, automated tests, manual installations, avatar
  validation, and hosted CI skips.
- If requested, update the user's Unity project and confirm package version and
  compilation through MCP. Local checkouts may already be updated; VPM installs
  do not automatically update because a release was published.
- If publication or deployment is incomplete, state exactly what succeeded and
  what remains. Do not present a queued/failed deployment as complete.

## Recovery

- Before publication: fix source, rerun affected checks, commit, and rebuild only
  after confirming the draft is unpublished. Reverify any replacement archives.
- After publication: keep tags/assets immutable and ship fixes as a new patch.
  Clients cache versioned packages/checksums.
- Broken feed: inspect Build Docs logs and release metadata; fix the actual cause.
  Never upload an empty feed to hide an error. `listing --empty` is for offline
  page review only.
- Withdrawing releases, deleting public tags, rewriting history, or changing
  license scope needs its own explicit authorization and recovery plan. Routine
  patch publication does not require a force push.

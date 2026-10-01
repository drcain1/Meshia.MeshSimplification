"""Build a fork-only package and VPM listing using the Python standard library."""
import argparse
import hashlib
import html
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.parse
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = "com.ramtype0.meshia.mesh-simplification"
SEMVER = re.compile(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\Z")


def config():
    return json.loads((ROOT / ".github/vpm.json").read_text(encoding="utf-8"))


def validate(manifest, settings):
    if manifest["name"] != settings["packageId"] or manifest["name"] == UPSTREAM:
        raise ValueError("Package identity must belong to this fork")
    match = SEMVER.fullmatch(manifest["version"])
    if not match:
        raise ValueError("Invalid semantic version")
    for identifier in (match.group(4) or "").split("."):
        if identifier.isdigit() and len(identifier) > 1 and identifier.startswith("0"):
            raise ValueError("Prerelease numeric identifiers cannot have leading zeroes")
    if UPSTREAM not in manifest.get("legacyPackages", []):
        raise ValueError("Upstream replacement declaration is required")
    if UPSTREAM in manifest.get("vpmDependencies", {}):
        raise ValueError("The fork cannot depend on upstream Meshia")
    if "zipSHA256" in manifest:
        raise ValueError("ZIP checksum belongs in the listing, not package.json")
    if manifest.get("license") != "GPL-2.0-or-later":
        raise ValueError("Fork distribution must declare its selected GPL license")


def require_distribution_ready(settings):
    if settings.get("distributionReady") is not True:
        raise ValueError("Public distribution is disabled: complete docs/FORK_DISTRIBUTION.md release prerequisites first")


def package_paths():
    # Include uncommitted package changes for local review, but never ignored fixtures.
    output = subprocess.check_output(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=ROOT
    ).decode("utf-8")
    names = set(output.split("\0")) - {""}
    roots = {"Runtime", "Editor", "Ndmf"}
    files = {"package.json", "README.md", "LICENSE.md", "BLENDER_PORT_NOTICE.md",
             "docs/FORK_DISTRIBUTION.md"}
    for name in names:
        path = Path(name)
        if path.parts[0] in roots and "Tests" not in path.parts and path.name != "Tests.meta":
            files.add(name)
        if path.parts[0] == "Licenses":
            files.add(name)
    for name in list(files) + list(roots) + ["docs", "Licenses"]:
        if name + ".meta" in names:
            files.add(name + ".meta")
    return sorted(name for name in files if (ROOT / name).is_file())


def source_paths():
    names = subprocess.check_output(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=ROOT
    ).decode("utf-8").split("\0")
    roots = {"Runtime", "Editor", "Ndmf", "docs", "Licenses", ".github", ".docfx"}
    return sorted({name for name in names if name and (ROOT / name).is_file()
                   and (Path(name).parts[0] in roots or "/" not in name)
                   and not any(part in {"_site", "__pycache__"} for part in Path(name).parts)
                   and Path(name).name not in {"AGENTS.md", "AGENTS.md.meta"}})


def pack(out):
    settings = config()
    manifest = json.loads((ROOT / "package.json").read_text(encoding="utf-8"))
    validate(manifest, settings)
    out.mkdir(parents=True, exist_ok=True)
    target = out / f'{manifest["name"]}-{manifest["version"]}.zip'
    paths = package_paths()
    sources = source_paths()
    validate_source_paths(paths + sources)
    compiled_suffixes = {".dll", ".exe", ".so", ".dylib", ".a", ".lib"}
    if any(Path(name).suffix.lower() in compiled_suffixes for name in paths + sources):
        raise ValueError("This release workflow supports source-only distribution; compiled artifacts are forbidden")
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
        for name in paths:
            archive.write(ROOT / name, name)
    print(target)
    source_target = out / f'{manifest["name"]}-{manifest["version"]}-source.zip'
    paths = sources
    provenance = {
        "package": manifest["name"], "version": manifest["version"],
        "referenceCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT).decode().strip(),
        "workingTreeModified": bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT)),
        "packageZipSHA256": hashlib.sha256(target.read_bytes()).hexdigest(),
        "sourceSHA256": {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest() for name in paths}
    }
    with zipfile.ZipFile(source_target, "w", zipfile.ZIP_DEFLATED) as archive:
        for name in paths:
            archive.write(ROOT / name, name)
        archive.writestr("SOURCE_PROVENANCE.json", json.dumps(provenance, indent=2) + "\n")
    print(source_target)


def validate_source_paths(paths):
    """Keep private fixtures and retired baking features out of either archive."""
    forbidden_suffixes = {".fbx", ".blend", ".unity", ".prefab", ".anim", ".pem", ".key"}
    for name in paths:
        path = Path(name)
        asset_name = name.removesuffix(".meta")
        if Path(asset_name).suffix.lower() in forbidden_suffixes or path.name.startswith(".env"):
            raise ValueError(f"Private fixture or credential file is not release content: {name}")
        if "FaQemBaking" in path.parts or path.name.startswith(("FaQemAtlas", "FaQemPersistentBake", "MeshiaPersistentBake")):
            raise ValueError(f"Retired texture-baking implementation is not release content: {name}")
        if name.startswith("docs/") and name not in {"docs/FORK_DISTRIBUTION.md", "docs/FORK_DISTRIBUTION.md.meta"}:
            raise ValueError(f"Private investigation reports are not release content: {name}")


def download(url, token=None):
    headers = {"User-Agent": "meshia-fork-vpm-builder"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()


def releases(settings):
    page = 1
    while True:
        url = f'https://api.github.com/repos/{settings["repository"]}/releases?per_page=100&page={page}'
        batch = json.loads(download(url, os.environ.get("GH_TOKEN")))
        yield from batch
        if len(batch) < 100:
            break
        page += 1


def add_release(listing, release, settings, fetch=download):
    if release["draft"]:
        return
    prefix = settings["packageId"] + "-"
    candidates = [asset for asset in release["assets"]
                  if asset["name"].startswith(prefix) and asset["name"].endswith(".zip")
                  and not asset["name"].endswith("-source.zip")]
    if not candidates:
        return  # Inherited upstream tags/releases must never enter this feed.
    if len(candidates) != 1:
        raise ValueError("Release must have exactly one fork package ZIP")
    require_distribution_ready(settings)
    asset = candidates[0]
    url = asset["browser_download_url"]
    allowed = f'https://github.com/{settings["repository"]}/releases/download/'
    if not url.startswith(allowed):
        raise ValueError("Package must be an asset in the fork repository")
    data = fetch(url)
    import io
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        manifest = json.loads(archive.read("package.json"))
    validate(manifest, settings)
    version = manifest["version"]
    source_name = prefix + version + "-source.zip"
    source_assets = [item for item in release["assets"] if item["name"] == source_name]
    if len(source_assets) != 1 or not source_assets[0].get("browser_download_url", "").startswith(allowed):
        raise ValueError("Release must provide the matching fork source archive")
    if release["tag_name"] != "fork-v" + version or asset["name"] != prefix + version + ".zip":
        raise ValueError("Release tag, ZIP name, and package version must match")
    if bool(release["prerelease"]) != bool(SEMVER.fullmatch(version).group(4)):
        raise ValueError("Release prerelease flag must match package version")
    versions = listing["packages"].setdefault(manifest["name"], {"versions": {}})["versions"]
    if version in versions:
        raise ValueError("Duplicate package version")
    versions[version] = dict(manifest, url=url, zipSHA256=hashlib.sha256(data).hexdigest())


def build_listing(out, empty=False):
    settings = config()
    listing = {key: settings[key] for key in ("name", "id", "author", "url")}
    listing["packages"] = {}
    if not empty:
        for release in releases(settings):
            add_release(listing, release, settings)
    out.mkdir(parents=True, exist_ok=True)
    (out / "index.json").write_text(json.dumps(listing, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    link = "vcc://vpm/addRepo?url=" + urllib.parse.quote(settings["url"], safe="")
    count = sum(len(package["versions"]) for package in listing["packages"].values())
    status = f"{count} package version(s) available." if count else "No installable releases are available yet."
    page = f'''<!doctype html>
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>{html.escape(settings["name"])}</title>
<style>body{{font:18px system-ui;max-width:760px;margin:60px auto;padding:24px;line-height:1.6}}a{{color:#3156be}}code{{overflow-wrap:anywhere}}</style>
<h1>{html.escape(settings["name"])}</h1>
<p>An independent fork of Meshia by Ram.Type-0, maintained by drcain1.</p>
<p>The main additions in this fork are <strong>FA-QEM</strong> (the default),
<strong>Blender Decimate</strong>, and <strong>UV Loop Dissolve</strong>:
three geometry-reduction modes for lowering avatar polygon counts while keeping existing textures and materials.</p>
<p lang="ja">このフォークの主な追加機能は、<strong>FA-QEM</strong>（標準）、
<strong>Blender Decimate</strong>、<strong>UV Loop Dissolve</strong> の3つの軽量化モードです。
既存のテクスチャとマテリアルを維持しながら、アバターのポリゴン数を削減できます。</p>
<p>{status}</p>
<p><a href="{html.escape(link)}">Add repository to VCC or ALCOM</a></p>
<p>You can also paste this URL into either application's Add Repository dialog:<br>
<code>{html.escape(settings["url"])}</code></p>
<p>This package replaces upstream Meshia. Back up your project and close Unity before switching.
Install only one Meshia variant per project. Enable prerelease packages to see beta versions.</p>
<p>Add <a href="https://vpm.anatawa12.com/vpm.json">anatawa12's repository</a> for the localization dependency.
For avatar integration, install NDMF and Modular Avatar from <a href="https://vpm.nadena.dev/vpm.json">nadena's repository</a>.</p>
<p><a href="../comparisons/70k/">View the 70k avatar comparison / 7万ポリゴンの比較を見る</a><br>
Compare original and simplified renders, with the measured build count and visual tradeoffs.</p>
<p><a href="https://github.com/{html.escape(settings["repository"])}/blob/main/docs/FORK_DISTRIBUTION.md">Migration and release status</a>
 · <a href="https://github.com/{html.escape(settings["repository"])}">Source and attribution</a></p>
</html>'''
    (out / "index.html").write_text(page, encoding="utf-8")
    print(f"Listing contains {count} versions")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("pack", "listing", "validate", "release-check"))
    parser.add_argument("--out", type=Path, default=ROOT / "dist")
    parser.add_argument("--empty", action="store_true", help="Build an offline empty listing for review")
    args = parser.parse_args()
    if args.command == "pack":
        pack(args.out)
    elif args.command == "listing":
        build_listing(args.out, args.empty)
    else:
        settings = config()
        validate(json.loads((ROOT / "package.json").read_text(encoding="utf-8")), settings)
        if args.command == "release-check":
            require_distribution_ready(settings)
        print("Validation passed")

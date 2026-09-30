import copy
import hashlib
import io
import json
from pathlib import Path
import unittest
import zipfile

import vpm


class DistributionTests(unittest.TestCase):
    def setUp(self):
        self.settings = dict(vpm.config(), distributionReady=True)
        self.manifest = json.loads((vpm.ROOT / "package.json").read_text(encoding="utf-8"))
        self.listing = {"packages": {}}
        self.name = f'{self.manifest["name"]}-{self.manifest["version"]}.zip'
        self.release = {
            "draft": False, "prerelease": bool(vpm.SEMVER.fullmatch(self.manifest["version"]).group(4)),
            "tag_name": "fork-v" + self.manifest["version"],
            "assets": [{"name": self.name, "browser_download_url":
                        f'https://github.com/{self.settings["repository"]}/releases/download/fork-v{self.manifest["version"]}/{self.name}'}]
        }
        self.release["assets"].append({
            "name": self.name[:-4] + "-source.zip",
            "browser_download_url": self.release["assets"][0]["browser_download_url"][:-4] + "-source.zip"
        })

    def archive(self, manifest=None):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            archive.writestr("package.json", json.dumps(manifest or self.manifest))
        return stream.getvalue()

    def test_published_fork_records_actual_hash(self):
        data = self.archive()
        vpm.add_release(self.listing, self.release, self.settings, lambda _: data)
        manifest = self.listing["packages"][self.manifest["name"]]["versions"][self.manifest["version"]]
        self.assertEqual(manifest["zipSHA256"], hashlib.sha256(data).hexdigest())
        self.assertIn(vpm.UPSTREAM, manifest["legacyPackages"])

    def test_drafts_and_inherited_releases_never_download(self):
        def fail(_):
            self.fail("Excluded release was downloaded")
        vpm.add_release(self.listing, dict(self.release, draft=True), self.settings, fail)
        inherited = dict(self.release, assets=[{"name": vpm.UPSTREAM + "-3.2.0.zip"}])
        vpm.add_release(self.listing, inherited, self.settings, fail)
        self.assertEqual(self.listing, {"packages": {}})

    def test_release_gate_blocks_fork_publication(self):
        with self.assertRaisesRegex(ValueError, "Public distribution is disabled"):
            vpm.add_release(self.listing, self.release, dict(self.settings, distributionReady=False))

    def test_fork_named_zip_cannot_contain_upstream_manifest(self):
        manifest = dict(self.manifest, name=vpm.UPSTREAM)
        with self.assertRaisesRegex(ValueError, "identity"):
            vpm.add_release(self.listing, self.release, self.settings, lambda _: self.archive(manifest))

    def test_wrong_tag_or_prerelease_flag_fails(self):
        for release in (dict(self.release, tag_name="3.2.0"), dict(self.release, prerelease=not self.release["prerelease"])):
            with self.subTest(release=release), self.assertRaises(ValueError):
                vpm.add_release(self.listing, release, self.settings, lambda _: self.archive())

    def test_asset_outside_fork_is_rejected(self):
        release = copy.deepcopy(self.release)
        release["assets"][0]["browser_download_url"] = "https://github.com/RamType0/Meshia.MeshSimplification/releases/download/3.2.0/package.zip"
        with self.assertRaisesRegex(ValueError, "fork repository"):
            vpm.add_release(self.listing, release, self.settings)

    def test_duplicate_versions_fail(self):
        vpm.add_release(self.listing, self.release, self.settings, lambda _: self.archive())
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            vpm.add_release(self.listing, self.release, self.settings, lambda _: self.archive())

    def test_missing_source_archive_blocks_listing(self):
        release = dict(self.release, assets=self.release["assets"][:1])
        with self.assertRaisesRegex(ValueError, "source archive"):
            vpm.add_release(self.listing, release, self.settings, lambda _: self.archive())

    def test_distribution_contains_notices_and_no_harnesses(self):
        paths = vpm.package_paths()
        for name in ("package.json", "README.md", "LICENSE.md", "BLENDER_PORT_NOTICE.md",
                     "Licenses/GPL-2.0.txt", "Licenses/UPSTREAM-MIT.txt"):
            self.assertIn(name, paths)
        self.assertTrue(any(name.endswith(".cs") for name in paths))
        for name in paths:
            self.assertNotIn("Tests", Path(name).parts)
            self.assertNotEqual(Path(name).name, "Tests.meta")
            self.assertFalse(name.startswith((".github/", ".docfx/")))
            if name.startswith("docs/"):
                self.assertIn(name, ("docs/FORK_DISTRIBUTION.md", "docs/FORK_DISTRIBUTION.md.meta"))
            if name.endswith(".meta"):
                asset = name[:-5]
                self.assertTrue(asset in paths or any(path.startswith(asset + "/") for path in paths), name)

    def test_numeric_prerelease_identifiers_follow_semver(self):
        with self.assertRaises(ValueError):
            vpm.validate(dict(self.manifest, version="1.0.0-beta.01"), self.settings)

    def test_both_archives_have_no_compiled_dependency_payloads(self):
        for name in vpm.package_paths() + vpm.source_paths():
            self.assertNotIn(Path(name).suffix.lower(), {".dll", ".exe", ".so", ".dylib", ".a", ".lib"}, name)
            self.assertFalse(name.startswith(("Packages/", "Library/")), name)

    def test_private_fixtures_and_retired_features_cannot_reenter_archives(self):
        for name in ("Fixtures/avatar.fbx", "Fixtures/avatar.prefab.meta", ".env.local",
                     "key.pem", "Ndmf/Editor/FaQemBaking/Baker.cs",
                     "Runtime/FaQemAtlasCompression.cs", "docs/private-investigation.md"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                vpm.validate_source_paths([name])
        vpm.validate_source_paths(vpm.package_paths() + vpm.source_paths())


if __name__ == "__main__":
    unittest.main()

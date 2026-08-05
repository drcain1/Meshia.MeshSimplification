# Repository Guidelines

## Project Structure & Module Organization

This repository is a Unity package for Burst-accelerated mesh simplification. `Runtime/` contains the public API, Job System implementation, supporting collections, and NUnit tests in `Runtime/Tests/`. `Editor/` contains inspectors, UI Toolkit drawers, and localization used only in the Unity editor. `Ndmf/` provides optional NDMF runtime components, editor integration, and previews. Documentation sources and DocFX configuration live in `.docfx/`; CI, testing, documentation, and release automation live in `.github/workflows/`.

Keep Unity `.meta` files paired with their assets. Do not commit generated `Library/`, `Temp/`, project files, builds, or IDE settings.

## Build, Test, and Development Commands

- Open a Unity test project using Unity 2022.3 or a supported Unity 6 release, then add this repository as a local package.
- Run EditMode and PlayMode suites from **Window > General > Test Runner**. CI also exercises standalone tests across its Unity version matrix.
- `docfx .docfx/docfx.json` builds API documentation into `.docfx/_site/` (DocFX requires .NET 8 in CI).
- `git lfs pull` downloads binary fixtures. Run it only when tests or assets require those files.

## Coding Style & Naming Conventions

Use four-space indentation and Allman braces. Follow existing C# naming: PascalCase for types, methods, properties, and existing fields; camelCase for parameters and locals. Preserve `#nullable` annotations and add XML documentation to public APIs. Code used by Burst jobs must remain Burst-compatible; avoid managed allocations in job paths, propagate `JobHandle` dependencies, and dispose native allocations with `using` or explicit cleanup.

## Testing Guidelines

Add NUnit tests under `Runtime/Tests/` and keep their assembly definition references minimal. Name tests after observable behavior, typically `Should...`, and use `[TestCase]` for repeated inputs. Cover synchronous and asynchronous APIs when behavior affects both. Destroy temporary Unity objects and dispose native containers even when assertions fail.

## Commit & Pull Request Guidelines

Write concise, imperative commit subjects. Existing history permits plain subjects and scoped prefixes such as `fix:`, `feat:`, and `ci:`. Pull requests should explain behavioral impact, link relevant issues, and report Unity versions and test modes exercised. Include screenshots or recordings for inspector, preview, or other editor UI changes. Keep version bumps and release-only changes separate from functional work when practical.

# RoT Elephant Thread Fix GitHub Build Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Build a GitHub-ready repository that compiles RoTElephantThreadFix v1.0.1 and emits an install-ready module ZIP.

**Architecture:** Keep the existing Harmony source unchanged, compile it with public NuGet reference assemblies, and use a small tested Python packager to construct the Bannerlord module archive. GitHub Actions owns restore/build/package/artifact upload.

**Tech Stack:** C# / .NET Framework 4.7.2, Harmony 2.4.2, Bannerlord.ReferenceAssemblies 1.4.8.119303, Python 3, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-08-24-rot-elephant-github-build-design.md`

## Global Constraints
- Preserve RoTElephantThreadFix v1.0.1 Harmony behavior.
- Target `net472`.
- Use `Bannerlord.ReferenceAssemblies` version `1.4.8.119303`.
- Use `Lib.Harmony` version `2.4.2`.
- Do not commit or package TaleWorlds DLLs, `ROT.dll`, or `0Harmony.dll`.
- Emit `RoTElephantThreadFix.dll` and an install-ready `RoTElephantThreadFix_v1.0.1.zip`.

---

### Task 1: Tested module packager

**Files:**
- Create: `tests/test_package_module.py`
- Create: `scripts/package_module.py`

**Interfaces:**
- Consumes: compiled DLL path and `module/SubModule.xml`.
- Produces: `build_package(dll_path, submodule_path, output_zip)` and the install-ready ZIP.

- [x] Step 1: Write tests that require the exact module archive layout and reject a missing DLL.
- [x] Step 2: Run tests and verify they fail because `scripts.package_module` does not exist.
- [x] Step 3: Implement the smallest packager that satisfies those tests.
- [x] Step 4: Run tests and verify they pass.
- [x] Step 5: Commit the packager and tests.

### Task 2: Reproducible C# project and repository validation

**Files:**
- Create: `src/RoTElephantThreadFix/RoTElephantThreadFix.csproj`
- Create: `tests/test_repo_contract.py`
- Create: `README.md`

**Interfaces:**
- Consumes: existing v1.0.1 source and module XML.
- Produces: an SDK-style `net472` build with exact dependency versions and a documented user workflow.

- [x] Step 1: Write repository-contract tests for target framework, package versions, module id/version, and source reflection invariants.
- [x] Step 2: Run the tests and verify they fail because the project/README are not complete.
- [x] Step 3: Add the project and documentation with the exact dependency/version constraints.
- [x] Step 4: Run the repository tests and verify they pass.
- [x] Step 5: Commit project metadata and documentation.

### Task 3: GitHub Actions CI and artifact contract

**Files:**
- Create: `.github/workflows/build.yml`
- Modify: `tests/test_repo_contract.py`

**Interfaces:**
- Consumes: C# project, Python tests, packager.
- Produces: CI that restores, tests, builds Release, packages, and uploads raw DLL + module ZIP.

- [x] Step 1: Extend repository-contract tests to require the workflow build/package/artifact steps.
- [x] Step 2: Run tests and verify they fail because the workflow is missing.
- [x] Step 3: Add the GitHub Actions workflow.
- [x] Step 4: Run all Python tests and static repository checks.
- [x] Step 5: Commit CI configuration.

### Task 4: Release bundle and final verification

**Files:**
- Generate: `RoTElephantThreadFix-github-repo.zip`
- Generate: `RoTElephantThreadFix.git.bundle`

**Interfaces:**
- Consumes: completed Git repository.
- Produces: portable repo ZIP and full Git bundle for the user to push to GitHub.

- [x] Step 1: Verify git status is clean and inspect commit history.
- [x] Step 2: Run the complete local Python test suite.
- [x] Step 3: Verify no proprietary game/RoT DLLs are present.
- [x] Step 4: Export repository ZIP and Git bundle.
- [x] Step 5: Verify exported artifacts can be listed/read successfully.

# RoT Elephant Thread Fix GitHub Build Design

## Goal
Package the existing RoTElephantThreadFix v1.0.1 source as a clean Git repository that GitHub Actions can compile into a real Bannerlord module DLL and install-ready ZIP without committing TaleWorlds or Realm of Thrones binaries.

## Compatibility
- Mount & Blade II: Bannerlord: v1.4.8.119303
- Realm of Thrones: source behavior preserved from RoTElephantThreadFix v1.0.1
- Target framework: .NET Framework 4.7.2 (`net472`)
- Harmony compile dependency: `Lib.Harmony` 2.4.2
- Bannerlord compile references: `Bannerlord.ReferenceAssemblies` 1.4.8.119303
- No `ROT.dll` compile dependency; RoT elephant types continue to be resolved dynamically by reflection at runtime.

## Repository Layout
- `src/RoTElephantThreadFix/`: C# source and SDK-style project.
- `module/SubModule.xml`: Bannerlord module descriptor.
- `scripts/package_module.py`: deterministic install-ready ZIP packager.
- `tests/`: packager and repository-structure tests.
- `.github/workflows/build.yml`: restore, validate, build, package, and upload artifacts.

## Build
GitHub Actions installs a current .NET SDK, restores NuGet dependencies, builds the `net472` project in Release mode, runs validation/tests, and packages only the mod DLL plus module metadata. Bannerlord reference assemblies and Harmony are compile-time inputs only and are not redistributed in the module ZIP.

## Packaging
The output ZIP must contain:
- `RoTElephantThreadFix/SubModule.xml`
- `RoTElephantThreadFix/bin/Win64_Shipping_Client/RoTElephantThreadFix.dll`

The workflow also uploads the raw DLL separately for debugging and testing.

## Safety/Failure Behavior
The existing Harmony patch behavior remains unchanged. It still refuses to patch unless it finds exactly one compatible `Agent.RegisterBlow` call inside `RoTElephantAgentComponent.OnTickParallel`, and it flushes deferred blows from RoT mission logic on the main mission tick.

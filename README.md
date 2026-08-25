# RoT Elephant Thread Fix

A small Harmony patch for the Realm of Thrones elephant crash seen on **Bannerlord v1.4.8.119303**.

The v1.0.1 patch preserves the behavior developed from the crash dump: it replaces the elephant `Agent.RegisterBlow` call made from RoT's parallel tick with a deferred queue, then flushes those blows from RoT mission logic on the main mission tick. The patch resolves RoT elephant types by reflection, so the project does not compile against or redistribute `ROT.dll`.

## Compatibility

- Mount & Blade II: Bannerlord: `v1.4.8.119303`
- RoT Elephant Thread Fix: `v1.0.1`
- .NET Framework: `4.7.2`
- Harmony: `2.4.2`

This is deliberately version-locked at the compile-reference level. A future Bannerlord or RoT update can change the underlying method layout, and the patch is intentionally strict rather than silently patching the wrong call.

## Install

Download the GitHub Actions artifact named `RoTElephantThreadFix_v1.0.1.zip` and extract it so the game has:

```text
Mount & Blade II Bannerlord\Modules\RoTElephantThreadFix\SubModule.xml
Mount & Blade II Bannerlord\Modules\RoTElephantThreadFix\bin\Win64_Shipping_Client\RoTElephantThreadFix.dll
```

Then enable **RoT Elephant Thread Fix** in the Bannerlord launcher and place it after `ROT-Core`.

## Build locally

With a current .NET SDK installed:

```bash
dotnet restore src/RoTElephantThreadFix/RoTElephantThreadFix.csproj
dotnet build src/RoTElephantThreadFix/RoTElephantThreadFix.csproj --configuration Release --no-restore
```

The project uses public NuGet reference assemblies rather than proprietary game files:

- `Bannerlord.ReferenceAssemblies` `1.4.8.119303`
- `Lib.Harmony` `2.4.2`
- `Microsoft.NETFramework.ReferenceAssemblies` `1.0.3`

## GitHub Actions

Every push and pull request runs tests, restores dependencies, builds `RoTElephantThreadFix.dll`, packages the Bannerlord module, and uploads both:

- `RoTElephantThreadFix.dll`
- `RoTElephantThreadFix_v1.0.1.zip`

No TaleWorlds DLLs, Realm of Thrones DLLs, or `0Harmony.dll` are included in the repository or release package.

## Why this exists

The crash logs showed RoT requesting/rendering the elephant asset immediately before the failure path, while the dump investigation pointed to elephant attack processing calling `RegisterBlow` from parallel agent ticking. This patch moves that blow registration to the main mission tick without changing RoT's assembly on disk.

# RoT Elephant Thread Fix

A small Harmony patch for the Realm of Thrones elephant crash seen on **Bannerlord v1.4.8.119303**.

The v1.0.3 patch replaces the elephant `Agent.RegisterBlow` call made from RoT's parallel tick with a deferred queue. It drains that queue from `AfterAsyncTickTick` in the same `TickAgentsAndTeamsImp` cycle, after agent ticking has completed. Before replaying each blow, it verifies both the managed mission lifetime and the victim agent's current mission/activity; valid blows call `RegisterBlow` directly.

To prevent this mod from triggering Bannerlord's folded or sideways character-preview initialization bug, v1.0.3 does not install its mission Harmony patch during `OnSubModuleLoad`. It waits for `OnGameInitializationFinished`, avoiding the earlier startup window in which action and animation definitions are still loading. `OnGameEnd` removes only this mod's Harmony patches, disables and clears the queue, and allows a clean install for the next game session.

The Harmony transpiler deliberately remains strict: it targets RoT's reflected elephant component and accepts exactly one compatible `Agent.RegisterBlow` call. The project uses only public reference packages, so it neither compiles against nor redistributes `ROT.dll` or TaleWorlds game DLLs.

## Compatibility

- Mount & Blade II: Bannerlord: `v1.4.8.119303`
- RoT Elephant Thread Fix: `v1.0.3`
- .NET Framework: `4.7.2`
- Harmony: `2.4.2`

This is deliberately version-locked at the compile-reference level. A future Bannerlord or RoT update can change the underlying method layout, and the patch is intentionally strict rather than silently patching the wrong call.

## Install

Download the install artifact named `RoTElephantThreadFix-v1.0.3`. It contains `RoTElephantThreadFix_v1.0.3.zip`; extract that ZIP so the game has:

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

Every push and pull request runs repository tests, restores dependencies, builds `RoTElephantThreadFix.dll`, runs the same-tick behavior harness, packages the Bannerlord module, and uploads both:

- `RoTElephantThreadFix.dll`
- `RoTElephantThreadFix-v1.0.3`, containing `RoTElephantThreadFix_v1.0.3.zip`

No TaleWorlds DLLs, Realm of Thrones DLLs, or `0Harmony.dll` are included in the repository or release package.

## In-game release check

Before publishing a build, launch Bannerlord with the normal RoT load order and verify the inventory or character screen shows an upright model. Then load an elephant battle, confirm elephant attacks no longer produce the original thread crash, return to the menu, and start or load a second game in the same process. Automated tests cover the lifecycle and concurrency rules, but cannot exercise Bannerlord's native renderer.

## Why this exists

The crash logs showed RoT requesting/rendering the elephant asset immediately before the failure path, while the dump investigation pointed to elephant attack processing calling `RegisterBlow` from parallel agent ticking. This patch keeps the registration on the same tick but at the post-agent boundary, without changing RoT's assembly on disk.

Automated tests verify the delayed Harmony lifecycle, game-end cleanup, queue, same-tick dispatch boundary, liveness defenses, package layout, and release contract. They do not reproduce the TaleWorlds native crash or renderer; an in-game reproduction remains the final runtime validation.

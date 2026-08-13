# Tools

Development utilities for keeping this mod compatible with Slay the Spire 2 updates.
Both are .NET 9 console apps (no restore-time game dependency; the game's DLLs are
loaded with `MetadataLoadContext`, so no game process is required).

## Prerequisites

- .NET SDK 9.0+
- A Slay the Spire 2 install (for the DLLs to verify against)

Default game data directory (override with the `STS2_GAME_DIR` env var):

```
D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64
```

## verify-api

Static API-drift + patch-signature verifier.

- Resolves every `[HarmonyPatch]` target in the mod DLL against the game's
  `sts2.dll` and checks that method/field names, types and generic arity still exist.
- Checks that every `Prefix`/`Postfix`/`Finalizer` parameter name and type matches
  the patched target (Harmony binds parameters by name — a mismatch crashes
  `PatchAll` at load time).
- Checks reflection (`AccessTools`/field) targets.

```powershell
dotnet run --project tools/verify-api
```

Override paths with `STS2_GAME_DIR` and `STS2_MOD_DLL`
(default: `..\bin\Release\net9.0\ForkedRoad.dll`).

Exit code 0 = all checks passed; the run also prints `OK=<n> FAIL=<n>`.

## dump-patch

Dumps the `Prefix`/`Postfix`/`Finalizer` parameter lists of every patched type in a
mod DLL, so patched signatures can be inspected after building:

```powershell
dotnet run --project tools/dump-patch -- path\to\ForkedRoad.dll
```

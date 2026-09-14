# Repository overview

This is a monorepo of programmable block scripts for Space Engineers.

The projects use [Malware's Development Kit 2](https://github.com/malforge/mdk2). Reusable code belongs in `Mixins` and should be imported by scripts rather than copied into individual script directories.

## Project conventions

- Keep each script under `Scripts/<ScriptName>/<ScriptName>` with its solution in `Scripts/<ScriptName>`.
- Maintain the `Instructions.readme` file for every script whenever its behaviour, commands, configuration, or usage changes.
- Treat each script's `Instructions.readme` as part of the packaged code. Keep it concise and practical, with only the information needed to configure and use the script.
- Keep programmable block code in the `IngameScript` namespace. The MDK2 packager removes namespaces from the deployed script.
- Keep `Program` as a public partial class deriving from `MyGridProgram`.
- Target .NET Framework 4.8 and C# 6, matching the MDK2 programmable block template and the game's scripting restrictions.
- Reference the current stable `Mal.Mdk2.PbAnalyzers`, `Mal.Mdk2.PbPackager`, and `Mal.Mdk2.References` packages.
- Put shared helpers in MDK2 mixin `.shproj` and `.projitems` projects under `Mixins`.
- Commit `<ProjectName>.mdk.ini`. Do not commit `<ProjectName>.mdk.local.ini`, which is for machine-specific paths and behaviour.
- Build Release when verifying scripts because MDK2 packs and deploys Release builds by default.
- Do not add migration paths, legacy fallbacks, backwards-compatibility shims, or cleanup for superseded behaviour unless explicitly requested. Remove obsolete behaviour outright.

## Verification

Build each script project directly:

```powershell
dotnet build Scripts/GridRenamer/GridRenamer/GridRenamer.csproj -c Release
dotnet build Scripts/Tagger/Tagger/Tagger.csproj -c Release
dotnet build Scripts/LaunchControl/LaunchControl/LaunchControl.csproj -c Release
```

MDK2 requires the .NET 9 SDK and a local Space Engineers installation so it can resolve the game assemblies.

## External documentation

- [Space Engineers programmable block API](https://malforge.github.io/spaceengineers/pbapi/)
- [MDK2 documentation](https://malforge.github.io/spaceengineers/mdk2/)
- [MDK2 source](https://github.com/malforge/mdk2)

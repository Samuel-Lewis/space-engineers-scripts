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

## Script family conventions

Tagger, LaunchControl and FleetTelemetry are one family and must feel the same. `SystemStager` is dead code; ignore it. Apply these rules to every script, and update all three when a rule changes.

### Custom Data

- One lowercase section per script, named after the script: `[tagger]`, `[launchcontrol]`, `[fleet]`. The same word in square brackets is the script's name tag.
- The PB's own section is created and kept complete by `IniDocument`: missing keys are added with defaults, invalid values fall back with a warning, malformed INI is left untouched while defaults apply, unknown keys warn. No comments are written.
- Per-block sections are opt-in. A block opts in by carrying the name tag or by already having the section. Once opted in, its section is completed the same way as the PB's. Blocks that have not opted in are never written to.
- Keys are `snake_case`. Quantities carry their unit as a suffix: `_seconds`, `_minutes`, `_percent`. Booleans are `true`/`false`. Names of other blocks or grids are matched exactly, case-insensitively, against the current name.
- Dock port rule: a grid with one connector uses it; with several, the dock port is marked with `dock_port=true` in that connector's section, and nothing is assumed until one is marked.
- Blocks are found by type, never by name, except for the name tag and for names the user gives in config.

### Displays

- A block shows a script's screens when its name contains the script's tag. Its section then gets one `display_<n>=` key per surface (`display_0` for an LCD; one per screen for a cockpit). The value is the screen role, optionally followed by that role's option (`map heading`, `track Miner 1`); blank leaves the surface alone. A one-screen script uses the role `status`.
- The PB's own surfaces always have `display_<n>` keys in the PB section, blank by default, with no tag needed.
- All drawing goes through `SurfaceDashboard` in `Mixins/Display`. Title is the script's name as written (`LaunchControl`, `FleetTelemetry`). Same palette, same header, same gauge rows; status screens use `DrawStatus`, tables `DrawTable`, plots `DrawRadar`, empty states `DrawMessage`.

### Terminal output and commands

- Commands go through the `CLI` mixin. Every script has `help` and `config` (print the active PB section); scripts with state also have `status` and make it or the main action the default command. Switches use `--name`, such as `--force`.
- Echo format: first line `<ScriptName> | <STATE or summary>`, then one fact per line, then warnings and errors prefixed `! `. No version numbers anywhere; the MDK build-date macro in `Program.cs` is the only version marker and is never echoed.
- Rescan blocks every 10 seconds or immediately after a command or a Custom Data change.

## Verification

Build each script project directly:

```powershell
dotnet build Scripts/Tagger/Tagger/Tagger.csproj -c Release
dotnet build Scripts/LaunchControl/LaunchControl/LaunchControl.csproj -c Release
dotnet build Scripts/FleetTelemetry/FleetTelemetry/FleetTelemetry.csproj -c Release
```

MDK2 requires the .NET 9 SDK and a local Space Engineers installation so it can resolve the game assemblies.

## External documentation

- [Space Engineers programmable block API](https://malforge.github.io/spaceengineers/pbapi/)
- [MDK2 documentation](https://malforge.github.io/spaceengineers/mdk2/)
- [MDK2 source](https://github.com/malforge/mdk2)

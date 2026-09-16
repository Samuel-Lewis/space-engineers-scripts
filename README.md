# Space Engineers Scripts

A collection of programmable block scripts for [Space Engineers](https://www.spaceengineersgame.com/), built with [Malware's Development Kit 2](https://github.com/malforge/mdk2).

## Projects

- [Tagger](Scripts/Tagger) names blocks after their grid and assigns reusable INI tags by block type.
- [LaunchControl](Scripts/LaunchControl) switches ship systems between docked and flight, with launch checks.
- [FleetTelemetry](Scripts/FleetTelemetry) broadcasts each grid's state over antenna and shows the fleet on LCDs: status, table, map and per-ship tracking.
- [Mixins](Mixins) contains shared command-line, display, event, and INI helpers used by the scripts.

The three scripts share one configuration style: a `[scriptname]` Custom Data section on the PB, the same word in square brackets as a name tag for blocks that take part, `display_<n>=` keys to pick screen roles, and the same terminal output and command conventions. See `CLAUDE.md` for the rules.

## Building

Install the .NET 9 SDK and Space Engineers, then build a script in Release mode so MDK2 packs and deploys it:

```powershell
dotnet build Scripts/FleetTelemetry/FleetTelemetry/FleetTelemetry.csproj -c Release
```

Machine-specific MDK2 settings belong in `<ProjectName>.mdk.local.ini`; these files are ignored by Git.

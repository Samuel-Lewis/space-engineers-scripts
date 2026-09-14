# Space Engineers Scripts

A collection of programmable block scripts for [Space Engineers](https://www.spaceengineersgame.com/), built with [Malware's Development Kit 2](https://github.com/malforge/mdk2).

## Projects

- [Grid Renamer](Scripts/GridRenamer) prefixes block names with their grid name and standardises common names. [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3272722169)
- [Tagger](Scripts/Tagger) assigns reusable INI tags to blocks according to block type.
- [LaunchControl](Scripts/LaunchControl) switches ship systems between docked and flight, with launch checks.
- [Mixins](Mixins) contains shared command-line, display, event, and INI helpers used by the scripts.

## Building

Install the .NET 9 SDK and Space Engineers, then build a script in Release mode so MDK2 packs and deploys it:

```powershell
dotnet build Scripts/GridRenamer/GridRenamer/GridRenamer.csproj -c Release
```

Machine-specific MDK2 settings belong in `<ProjectName>.mdk.local.ini`; these files are ignored by Git.

# FleetTelemetry — Requirements & Design Notes

Status: draft, for handoff to an implementation pass. Written from a brainstorming session; several items below are still open and marked as such.

## Summary

A programmable-block script, installed on every ship and base that wants visibility into the fleet. Every install computes its own telemetry independently and broadcasts it over antenna (IGC); any install can also display incoming fleet data on configured LCDs/cockpits. There is no separate "sender" build and "dashboard" build — every copy does both, and which screens a given surface shows is just configuration.

## Confirmed decisions

- **Transport: antenna broadcast (IGC) only.** No dock-sync fallback. Fleet visibility is limited by antenna range and relay chains — a ship out of range of the viewer simply doesn't appear. This is an accepted constraint, not a bug to work around in software; base placement/relay antennas are the player's tool for extending coverage.
- **State source: standalone.** The script computes power %, hydrogen %, cargo %, thruster/gyro operational status, and docked/flight state directly from blocks on its own grid. It does **not** read LaunchControl's or SystemStager's Custom Data. This duplicates some detection logic those scripts already have, traded for zero coupling — FleetTelemetry works on any ship regardless of what else is installed.
- **Do not reuse Tagger's `facing_`/`position_` orientation math as a reference.** Samuel doesn't trust it. Any orientation/bearing math FleetTelemetry needs (reference-block forward vector, bearing-to-contact calculations) must be derived independently from the PB API, not copied from Tagger.
- **North reference:** Space Engineers has no native "north" outside of planet gravity/rotation. Default to a fixed world axis (e.g. world −Z) so "north-up" is at least consistent grid-to-grid and in deep space. Open to being tied to a specific planet's orientation instead if that's preferred.
- **Fleet Map rotating mode uses heading, not velocity.** The "travel direction" toggle rotates the map to the viewing grid's nose orientation (from its reference cockpit/remote control), not its actual velocity vector. This is stable at any speed, including zero, so no low-speed fallback logic is needed.
- **No "docked here" feature.** The earlier idea of highlighting/filtering ships docked at this grid is dropped — the Track screen already covers per-grid detail on demand, so a separate cross-grid "docked here" view is redundant.
- **Factionless for now.** IGC broadcasts are not faction-scoped in this build. Acceptable for the current (solo) context; revisit if this ever runs on a shared server, since anyone with an antenna on the shared tag could otherwise listen in on fleet position/cargo/power data.
- **Grid identity: the grid's name, as set in the Info tab (`CubeGrid.CustomName`).** Same identity source Tagger already uses as its root grid ID, so this stays consistent with the rest of the repo. Trade-off accepted: renaming a grid mid-playthrough will orphan any `track_target` pointed at the old name, same as it would with Tagger's `grid_id`.
- **Broadcast cadence: `Update100`.** The script runs on Space Engineers' `UpdateFrequency.Update100` (every 100 ticks, ~1.67s at 60 UPS), and broadcasts once per tick of its own loop — no separate throttling counter on top of that.
- **Staleness threshold: assumed default, not yet confirmed.** Proposed: a contact is "stale" (visually flagged, not removed) after ~5 missed cycles (~8s of silence), and dropped from the roster entirely after ~60s. Flag if these numbers are wrong.
- **Fleet Map zoom: auto-fit.** No fixed radius or configurable `map_range` — the scale expands each frame to be just wide enough to fit every currently-known contact, so nothing is ever clipped at the edge. Worth flagging for the build: with one very distant contact (e.g. a mining ship tens of km out) and several close ones, auto-fit will zoom out far enough that the close contacts bunch up near the center and become hard to tell apart. Not blocking, but a real UX trade-off of "always fit everyone" worth being aware of — a log-scaled or floor-limited distance mapping is one way to soften it if it becomes annoying in practice.

## Screens

Screen role is selected per LCD/cockpit via that surface's Custom Data, following the same per-block override convention SystemStager already uses for `display_surface`.

### Onboard
This grid's own telemetry only. Full detail: power, hydrogen, cargo, thruster/gyro status, docked/flight state. Always populated — no antenna or broadcast required to render this screen, since it's local data.

### Fleet
Summary table of every ship heard over IGC. One row per contact: name, state, power/hydrogen/cargo %, last-seen age. Stale contacts (no update for some configurable period) should be visually distinguished, not silently dropped from the table immediately.

### Fleet Map
Radar-style spatial plot, self-centered on the viewing grid.
- Each contact is a dot with a short always-visible label (callsign/marker) — SE `IMyTextSurface` sprites have **no hover/click/tooltip capability**, so a label can't appear on demand. Keep labels short to avoid clutter at scale; full names/detail live on the Fleet screen instead.
- **Orientation toggle, per screen:** north-up vs heading-up (rotates with the viewing grid's nose orientation).
- Needs a reference block (cockpit/remote control) on the viewing grid to establish forward orientation, independently derived per the note above.

### Track
Detail view of one specific other grid, same layout style as Onboard but sourced from its last broadcast: last-reported stats, docked/flight state, last known GPS position, last-seen time. Which grid to show is picked via a Custom Data key on that LCD, e.g. `track_target=<grid name>` — exact-name match, consistent with `docking_connector` in SystemStager. Should render a clear "no data" / "never heard from" state if the target hasn't been seen.

## Data model / message schema (draft — needs finalizing during build)

Each broadcast should be one compact string (not a structured object) to keep IGC traffic cheap. Candidate fields:
- Grid identity — see open question below (name vs. a stable ID)
- State: docked / flight (derived from connector status, not from LaunchControl/SystemStager)
- Power %, hydrogen %, cargo %
- GPS position
- Docked-target grid identity, if docked (informational only — shown on that grid's own Onboard/Track entry, not used for any cross-grid "docked here" feature)
- Timestamp (for last-seen / staleness calculation on the receiving end)

IGC tag: one shared broadcast tag (e.g. `"FleetTelemetry"`) so every install both sends and listens on it.

## Repo conventions to follow

- MDK2 project layout: `Scripts/FleetTelemetry/FleetTelemetry`, `Program` partial class in `IngameScript` namespace, C# 6 / .NET Framework 4.8.
- Reuse `Mixins`: `IniHandler` for Custom Data config (auto-populate missing keys, preserve malformed INI while defaulting, one key=value per line), `EventListener`/`Connector.cs` for dock/undock transition detection, `CLI` for command parsing, `Display`/`SurfaceDashboard` for rendering — Onboard and Track can likely extend the existing gauge/status layout; Fleet (table) and Fleet Map (radar) need new draw methods added to `SurfaceDashboard`, reusing its existing sprite primitives (`Circle`, `SquareSimple`, text).
- Maintain `Instructions.readme` for the script per repo convention.
- Verify with `dotnet build Scripts/FleetTelemetry/FleetTelemetry/FleetTelemetry.csproj -c Release` once scaffolded.

- **Telemetry data does not persist restarts; config does.** Screen role, orientation mode, thresholds, `track_target`, etc. all live in Custom Data / LCD naming, per the existing repo convention, and survive recompiles and reloads normally. The fleet roster itself (received telemetry, positions, last-seen times) is runtime-only, held in the script's memory, and is wiped on recompile or world reload — every screen goes blank until fresh broadcasts repopulate it, which at `Update100` cadence should take a couple of seconds once other ships are back in range and ticking. Not persisted to Custom Data; not worth the complexity for data that's this disposable.

## Open questions (need a decision before/during build)

1. **Staleness/drop thresholds.** Cadence is confirmed (`Update100`), but the ~8s stale / ~60s drop numbers above are a proposed default, not yet confirmed — sanity check them.

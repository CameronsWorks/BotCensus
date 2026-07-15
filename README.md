# Bot Census

An in-raid overlay that tallies the AI you're sharing the map with, custom factions included. RUAF,
UNTAR, Black Division and friends register above the vanilla bot range, where the stock *Detailed Bot
Counter* can't see them. Bot Census counts them, and cleans up a pile of vanilla miscounts on the way.

## What it counts

Everything the stock counter does, plus the parts it drops:

- **Custom factions** (via MoreBotsAPI): RUAF, Remnant, UNTAR, Black Division, each on its own line.
  They spawn as `WildSpawnType` values above the vanilla ceiling of 67, so a counter that only knows
  the built-in branches reads them as nothing. Bot Census pulls names from MoreBotsAPI's live registry
  by reflection, which means factions added after this was written show up too; with MoreBotsAPI absent
  it falls back to the known ID ranges.
- **The Goons.** Knight, Big Pipe and Birdeye get their own line, so you know the trio is on the map.
- **The BTR gunner**, on its own line.
- **AI PMCs**, separated from the scav side by faction (USEC / BEAR).
- **Raiders and Rogues** on separate lines by default. Fold them into one row from F12 if you'd rather
  keep the panel short.
- **Bosses and their guards** on separate lines too, so you can tell whether the boss himself is still up
  or only his guards are left. Merge them from F12 as well.
- **Scavs, bosses, cultists and the infected event**, with the vanilla oddities the stock counter
  mis-sorts put right: `crazyAssaultEvent` reads as a Scav instead of a boss, and `cursedAssault`,
  `assaultGroup` and the misspelled `sectactPriestEvent` all land where they should. Anything genuinely
  unrecognised still lands under **Other** rather than disappearing.

The panel re-scans every few seconds, so patrols that spawn well into a raid (a RUAF push, a Black
Division hunt) turn up on the next tick. It eases in as the raid loads in rather than snapping onto the
screen, so it settles alongside the rest of the HUD instead of popping over it.

## Install

Put the `BotCensus` folder in `BepInEx/plugins` and launch. Nothing to configure to get going.
**MoreBotsAPI** (custom-faction names) and **Fika** (co-op) are optional; each is used automatically
when it's installed and ignored when it isn't.

## Settings

In-game **F12**, or `BepInEx/config/com.sipto.botcensus.cfg`:

| Setting | Default | Notes |
|---|---|---|
| Enable | on | Master toggle |
| Only In Raid | on | Keeps it off the menu and hideout screens |
| Font Size | 16 | 10–30 |
| Offset Right / Top | 20 / 40 | Where the panel sits |
| Use Tarkov Font | on | The game's Bender face, with fallbacks |
| Background Opacity | 0.72 | Panel backing, 0 (clear) to 1 (solid) |
| Split Rogue And Raider | on | Off merges them into a single row |
| Split Boss And Guard | on | Off merges them into a single row |
| Update Interval | 5s | 5s / 10s / 15s / 30s / 1min |

Every bot type also has its own visibility mode: **Always** keeps the row up even at zero,
**WhenPresent** only shows it while some are alive (nice for bosses and the Goons), **Hidden** takes it
off entirely. PMC and Scav start on Always, the rest on WhenPresent.

## Co-op (Fika)

On a Fika raid it counts off the shared player list, so your numbers match the host's. It only reads
state and draws to your own screen; nothing networked, no patches.

## Running with the stock Bot Counter

Both draw in the top-right, so pick one. Either turn the stock counter off in its config, or bump
**Offset Top** here so the two don't overlap.

## Build

`dotnet build -c Release`, with the .NET SDK 8+ installed. Path resolution keys off `SptRoot` in the
csproj; by default it walks three folders up to the SPT install, so override that if your checkout lives
elsewhere. The build drops `BotCensus.dll` into `BepInEx/plugins/BotCensus`.

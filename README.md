# Olla - Irrigation Pottery for Vintage Story

A Vintage Story mod that adds traditional olla pottery for subsurface irrigation of farmland.

## Overview

This mod introduces **ollas** - ancient ceramic vessels used for efficient, subsurface irrigation. Fill them with water, bury them near your crops, and they'll slowly water the surrounding farmland over time, simulating traditional sustainable farming techniques.

## For Players

### What is an Olla?

An olla is an unglazed clay pot used in traditional agriculture. When buried in soil and filled with water, it slowly seeps moisture into the surrounding earth, providing consistent irrigation directly to plant roots with minimal water loss.

### Features

- **Craft traditional irrigation pottery** using clay forming
- **9 colours** - blue, fire and red clay from a pit kiln, and the same tans,
  oranges, browns, creams, grays and blacks a beehive kiln gives vanilla pottery
- **Subsurface irrigation** - waters a 5x5 area around the buried olla
- **Water capacity** - holds 60 liters of water
- **Efficient watering** - closer blocks receive more water, just like real subsurface irrigation

### How to Use

1. **Craft**: Use clay at a clay forming station to shape a raw olla
2. **Fire**: Place the raw olla in a kiln to create a fired olla. A pit kiln gives
   the plain clay colour; a beehive kiln fires by atmosphere, so how many of its
   back doors stand open picks the colour, exactly as for vanilla pots
3. **Fill**: Right-click a fired olla with a water container (bucket, watering can, etc.)
4. **Bury**: Right-click the filled olla with a soil block to bury it in the ground
5. **Irrigate**: Once buried, the olla will automatically water nearby farmland

### Irrigation Details

- **Range**: 5x5 blocks (2 blocks in each direction from the olla)
- **Only works when buried**: You must bury the olla for it to irrigate
- **Distance matters**: Blocks closer to the olla receive water faster
- **Water consumption**: Approximately 1.25 liters per unit of moisture intensity
- **Full saturation**: Watering all 24 surrounding blocks requires ~30 liters

### Overlapping Ollas

Farmland keeps a *moisture floor* set by how close its nearest water is - the game's own
rule is `1 - distance / 4`, so a block two away from water never drops below 50%.

Ollas within range of the same block now **blend** rather than competing, and natural water
joins the blend as just another source. Each source wets whatever share the others left dry:

| sources on one block | moisture floor |
|---|---|
| one olla 2 away | 50% |
| **two ollas 2 away** | **75%** |
| three ollas 2 away | 87.5% |
| two ollas 1 away | 93.75% |
| pond 3 away + olla 2 away | 62.5% |

This is deliberately asymptotic - overlap always pays, but reaching a true 100% floor still
needs a source right alongside the block. Laying out a grid so radii overlap is now a real
planning decision rather than wasted pottery.

The floor is also what keeps soil wet while a chunk is unloaded, and unlike active
irrigation it costs no water and never decays. That is the right trade on vanilla and the
wrong one under a farming overhaul where crops need to dry out - see
[Configuration](#configuration) if you run one.

### Water Types

An olla takes clean, fresh water only. Vanilla `waterportion` works, as do
[Hydrate or Diedrate](https://mods.vintagestory.at/hydrateordiedrate)'s rain, distilled,
well and boiled water. Salt water is refused because it would poison the soil, and
muddy/tainted/poisoned water is refused because it is meant to be purified first. Trying
to pour one of those in tells you so rather than silently doing nothing.

### Configuration

Optional. On first launch the mod writes `ModConfig/olla.json` in your Vintage Story
data directory with one setting:

```json
{
  "IrrigationTarget": 1.0
}
```

**`IrrigationTarget`** (0 to 1, default `1.0`) is the moisture level an olla works
towards, and it caps *both* of the mod's mechanisms:

- active irrigation stops adding water once farmland reaches it
- the blended moisture floor cannot be pushed past it, however many ollas overlap
  the same block

The default is the behaviour olla has always had — water to saturation, and let a
grid of overlapping ollas blend towards a 100% floor. On vanilla that is what you
want, and there is no reason to change it.

Lower it for farming overhauls that give crops a moisture **band** rather than
"wetter is better". [Farming Revamped](https://mods.vintagestory.at/show/mod/52211)
is the case this was added for: it damages crops held too wet, and caps its own
watering can at 0.75 for exactly that reason. An uncapped olla pins its whole 5x5
above that permanently — and because the moisture floor never decays, the soil can
never dry out again, which kills the crop rather than feeding it. Around `0.6`
suits it.

Two things worth knowing:

- It is **server-side**. On a multiplayer server the host's setting is the one in
  effect; editing it on a client does nothing.
- An olla is never a downgrade. A capped blend that comes out drier than the water
  already beside the block is discarded, so burying pottery next to a pond cannot
  make the soil worse than the pond alone.
- `0` turns the mod's watering off entirely. There is no small non-zero value that
  does something subtler: vanilla's own growth curve stops distinguishing moisture
  below 10%, so anything under that is a disabled mod that looks enabled. The log
  says so on startup, along with the target actually in effect.

### Installation

1. Download the latest release from the [Releases](./Releases/) folder or mod portal
2. Place the `.zip` file in your Vintage Story `Mods` folder
3. Launch Vintage Story

## For Developers

### Prerequisites

- .NET 10.0 SDK
- Vintage Story 1.22 installed
- Set the `VINTAGE_STORY` environment variable to your Vintage Story installation directory

```bash
export VINTAGE_STORY="/path/to/VintageStory"
```

### Project Structure

```
olla/
├── assets/olla/          # Game assets
│   ├── blocktypes/       # Block definitions (JSON)
│   ├── shapes/block/     # 3D models (JSON)
│   ├── recipes/          # Crafting recipes
│   └── lang/             # Localization files
├── Block/                # Block behavior classes
├── BlockEntity/          # Block entity classes (game logic)
├── OllaModSystem.cs      # Mod entry point
├── OllaConfig.cs         # ModConfig/olla.json
├── olla.csproj           # Project file
└── modinfo.json          # Mod metadata

tests/                    # In-game test suite (see Testing)
```

### Building

The project uses Cake Frosting for build automation.

```bash
# Build the mod
./build.sh

# Clean build artifacts
./clean.sh
```

The build process:
1. Validates all JSON asset files
2. Compiles the C# project
3. Packages the mod into a `.zip` file in the `Releases/` folder

### Testing

`tests/` holds an in-game test suite that runs against a real, running Vintage
Story world — placing blocks, advancing the calendar and asserting on live block
entity state. It needs [vstestkit](../vstestkit):

```bash
cd ../vstestkit
bash scripts/run.sh ../olla/tests --mod ../olla/olla
```

That builds the mod, boots a headless server with olla loaded, runs the suite and
exits non-zero on failure — about 7 seconds.

Some tests need a real client, and the Hydrate or Diedrate compatibility tests
need that mod loaded. Both come from one longer invocation:

```bash
cairn-cli sync ollahod                       # H&D -> ~/.cairn/packs/ollahod/Mods
bash scripts/run.sh ../olla/tests --mod ../olla/olla \
     --mods ~/.cairn/packs/ollahod/Mods --client
```

**Run that form before releasing.** Two bugs here were invisible headless and
appear only in singleplayer, where the mod loads on both sides:

- `ModSystem.Start()` runs once per side against the same assembly, so a
  `PatchAll()` there registers the Harmony postfix **twice**. Harmless while the
  patch only did `Math.Min` — applying it twice changes nothing — and silently
  doubled every olla's contribution once blending arrived. The patch is now
  server-side only, and `ThePatchIsRegisteredExactlyOnce` guards it.
- The client keeps its own copy of the inventory. A test that fills a bucket
  server-side without `MarkDirty` leaves the client holding an empty one, which
  makes fill tests fail and *refusal* tests pass for the wrong reason.

Without the H&D pack those tests log "this test proved nothing" and pass, so read
the log line rather than the green tick.

The tests are plain `.cs` files, compiled inside the game by the Roslyn it already
ships — there is nothing to build in `tests/`. `tests/olla.tests.csproj` exists
only so an editor can type-check them; building it is optional and needs
`VSTESTKIT` pointing at the vstestkit checkout.

**Why bother, when the mod compiles.** Most of what breaks olla is invisible to
the compiler:

- `HarmonyPatches` targets `BlockEntitySoilNutrition.GetNearbyWaterDistance`
  **by name**. In 1.22 that method moved from `BlockEntityFarmland` to this new
  base class. A patch aimed at a method that is not there attaches to nothing,
  compiles perfectly, and silently does nothing — ollas would simply stop
  contributing moisture, with no error anywhere.
  `OllaShortensTheWaterDistanceVanillaReports` calls the patched method directly
  and fails loudly if the patch is not in effect.
- `UpdateSoilMoisture` reaches farmland through `dynamic` inside a `try/catch`
  that swallows everything. Any change to `MoistureLevel` or `WaterFarmland`
  turns irrigation into a silent no-op.

What is covered:

| suite | asserts |
|---|---|
| `OllaIrrigation` | the block entity — burial and empty gates, the range limit, that watering costs the litres it delivers, and that the patch is attached at all |
| `OllaBlending` | the moisture floor — blend arithmetic against vanilla's curve, the olla cache, and that the patch is registered exactly once |
| `OllaIrrigationTarget` | the `IrrigationTarget` cap — that the default is a no-op, that overlapping ollas cannot climb past it, that a capped olla never dries out natural water, and that farmland settles at it |
| `OllaWaterTypes` | which liquids an olla accepts, checked against the live item registry |
| `OllaFilling` | a real player filling one from a bucket, vanilla and Hydrate or Diedrate; needs `--client` |

Two things worth knowing if you add tests:

- The block entity ticks on a **5-second real-time listener** and works from
  `Calendar.TotalHours` deltas, discarding the first firing to take a baseline.
  So the calendar has to advance *between* two firings — `World.TickNow(pos)`
  fires them on demand rather than waiting the interval out.
- The harness pins **precipitation to 0**. Sky-exposed farmland absorbs every hour
  of rain since its last update, so without that, advancing the calendar wets soil
  regardless of what the olla did — including farmland deliberately placed out of
  range.

### Key Components

#### OllaModSystem.cs
Entry point that registers custom block and block entity classes with the game.

#### OllaConfig.cs
Reads, sanitises and writes back `ModConfig/olla.json`. Loaded server-side only,
because both consumers of it are server-side; see [Configuration](#configuration).

#### BlockOllaFired.cs
Handles player interactions with placed olla blocks:
- Filling from water containers
- Burying with soil blocks
- Displaying help text and information

#### BlockEntityOllaFired.cs
Core irrigation logic:
- Stores water (0-60 liters)
- Updates irrigation every 5 seconds (game time)
- Calculates moisture distribution based on distance
- Persists water levels and state

### Technical Details

#### Water Mechanics

The irrigation system uses distance-based water distribution:

```
Base rate: 0.48 moisture intensity per game hour
Distance scaling: rate / max(distance, 1)
Water consumption: 1.25 liters per moisture unit
```

Separately from that active watering, a Harmony patch on `BlockEntitySoilNutrition.
GetNearbyWaterDistance` lets farmland see buried ollas as water sources, which is what
sets the moisture floor and what survives the chunk being unloaded. Sources are combined
as a probabilistic union and the result is handed back as a *fractional* water distance,
so the game's own `1 - distance / 4` does the rest untouched.

#### State Management

Ollas have two states:
- **Normal**: Can be filled with water, shows current water level
- **Buried**: Actively irrigating, only neck visible above ground

#### Burial Mechanic

Burial is a one-way operation that:
1. Consumes a soil block from the player's inventory
2. Switches the block state to "buried"
3. Activates irrigation behavior

### Development Notes

- **Server-side processing**: Irrigation updates only run on the server
- **Dynamic typing**: Uses reflection to interact with farmland blocks for cross-mod compatibility
- **Manhattan distance**: Prioritizes closer blocks for efficient water distribution
- **Game hour-based timing**: Ensures consistent behavior across different game speeds

### API Dependencies

- VintagestoryAPI
- VSSurvivalMod
- VSEssentials
- VSCreativeMod
- 0Harmony
- Newtonsoft.Json

### Contributing

Contributions are welcome! Please ensure:
1. JSON files are valid (validated during build)
2. Code follows existing patterns and conventions
3. Changes are tested in-game — run the suite (see [Testing](#testing)), and add
   a test when you change irrigation behaviour or touch the Harmony patch
4. Commit messages are descriptive

### Version History

- **1.3.1**: No gameplay change. Releases are now built on GitHub Actions and
  carry Sigstore-signed build provenance, so the zip on ModDB can be checked
  back to the commit it was built from with
  `gh attestation verify olla_1.3.1.zip --repo dizzyd/olla`.
- **1.3.0**: Added `IrrigationTarget` in `ModConfig/olla.json` — the moisture
  level an olla works towards, capping both active irrigation and the blended
  moisture floor. The default of `1.0` is the behaviour olla has always had;
  lower it for farming overhauls like Farming Revamped, where crops have a
  moisture band and soil that can never dry out kills them.
- **1.2.1**: The clay forming pattern now matches the pot it produces. It shaped
  a squat flared bowl, 8 voxels tall and 15 wide at the rim, where the model is a
  tall narrow olla with a neck; every layer now follows the model. Clay cost is
  unchanged at 16.
- **1.2.0**: Overlapping water sources blend into a single moisture floor instead
  of only the closest one counting, and natural water joins the blend. Hydrate or
  Diedrate's clean fresh water types are accepted; salt and polluted water are
  refused with a reason. Irrigation no longer bills for twice the moisture it
  delivers, so a tank lasts as long as the tooltip always claimed. The farmland
  patch is applied server-side only, and an in-game test suite was added.
- **1.1.0**: Vintage Story 1.22 compatibility — retargeted to `net10.0`, the
  farmland patch moved to `BlockEntitySoilNutrition` (where 1.22 relocated
  `GetNearbyWaterDistance`), and the `game` dependency raised to 1.22.0.
- **1.0.0**: Reworked the irrigation maths to follow farmland's own catch-up
  model, and added the Harmony hook that lets farmland see a buried olla as a
  water source — which is what keeps soil from drying out while a chunk is
  unloaded.
- **0.9.1**: Burial mechanic, a round of balancing, and a fix for water held in
  stacks of containers.
- **0.1.0**: Initial implementation — blocks, irrigation and models.

## License

[Add your license information here]

## Credits

**Author**: DizzyD

## Support

For bug reports or feature requests, please open an issue on the project repository.

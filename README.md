# Olla - Irrigation Pottery for Vintage Story

A Vintage Story mod that adds traditional olla pottery for subsurface irrigation of farmland.

## Overview

This mod introduces **ollas** - ancient ceramic vessels used for efficient, subsurface irrigation. Fill them with water, bury them near your crops, and they'll slowly water the surrounding farmland over time, simulating traditional sustainable farming techniques.

## For Players

### What is an Olla?

An olla is an unglazed clay pot used in traditional agriculture. When buried in soil and filled with water, it slowly seeps moisture into the surrounding earth, providing consistent irrigation directly to plant roots with minimal water loss.

### Features

- **Craft traditional irrigation pottery** using clay forming
- **3 color variants** - blue clay, fire clay, and red clay
- **Subsurface irrigation** - waters a 5x5 area around the buried olla
- **Water capacity** - holds 60 liters of water
- **Efficient watering** - closer blocks receive more water, just like real subsurface irrigation

### How to Use

1. **Craft**: Use clay at a clay forming station to shape a raw olla
2. **Fire**: Place the raw olla in a kiln to create a fired olla
3. **Fill**: Right-click a fired olla with a water container (bucket, watering can, etc.)
4. **Bury**: Right-click the filled olla with a soil block to bury it in the ground
5. **Irrigate**: Once buried, the olla will automatically water nearby farmland

### Irrigation Details

- **Range**: 5x5 blocks (2 blocks in each direction from the olla)
- **Only works when buried**: You must bury the olla for it to irrigate
- **Distance matters**: Blocks closer to the olla receive water faster
- **Water consumption**: Approximately 1.25 liters per unit of moisture intensity
- **Full saturation**: Watering all 24 surrounding blocks requires ~30 liters

### Installation

1. Download the latest release from the [Releases](./Releases/) folder or mod portal
2. Place the `.zip` file in your Vintage Story `Mods` folder
3. Launch Vintage Story

## For Developers

### Prerequisites

- .NET 8.0 SDK
- Vintage Story installed
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
├── olla.csproj           # Project file
└── modinfo.json          # Mod metadata
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

### Key Components

#### OllaModSystem.cs
Entry point that registers custom block and block entity classes with the game.

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
3. Changes are tested in-game
4. Commit messages are descriptive

### Version History

- **0.9.0**: Added burial mechanic, balancing improvements
- Earlier versions: Initial implementation of blocks, irrigation mechanics, and visual models

## License

[Add your license information here]

## Credits

**Author**: DizzyD

## Support

For bug reports or feature requests, please open an issue on the project repository.

# MCToCMZWorldConverter

![MCToCMZWorldConverter Preview](MCToCMZWorldConverter/_Images/Preview.png)

> A C# console tool for converting rendered Minecraft Java worlds directly into playable CastleMiner Z world folders using native CMZ chunk files and a customizable JSON block map.

This tool converts Minecraft Anvil `.mca` region chunks into CastleMiner Z native world chunks, such as:

```text
X0Y-64Z0.dat
X16Y-64Z0.dat
X-16Y-64Z32.dat
```

It can also build a full CastleMiner Z world folder with:

```text
world.info
mc-to-cmz-world.info-check.txt
mc-to-cmz-world.unmapped.txt
X#Y-64Z#.dat
```

The generated files are written using CastleMiner Z's `RTSD` SaveDevice wrapper so the game can load them normally from its saved-world list.

---

## Features

- Converts rendered Minecraft Java Edition worlds into CastleMiner Z native chunk `.dat` files.
- Reads Minecraft Anvil region files from `.mca` files.
- Supports legacy and modern Minecraft world layouts:
  - `<world>/region`
  - `<world>/dimensions/minecraft/overworld/region`
- Supports configurable Minecraft dimensions, including overworld, Nether, End, and custom datapack dimensions.
- Converts Minecraft block names and block states through a customizable JSON block map.
- Supports modern Minecraft block-state palette packing.
- Supports gzip, zlib, raw-deflate fallback, and uncompressed region chunk payloads.
- Can skip unreadable Minecraft chunks instead of stopping the entire conversion.
- Generates a complete CastleMiner Z world folder with a random UUID folder name.
- Writes CMZ `world.info` using the CastleMiner Z SaveDevice `RTSD` wrapper.
- Writes CMZ chunk `.dat` files using the CastleMiner Z SaveDevice `RTSD` wrapper.
- Infers the Steam save key from the CastleMiner Z SteamID save path.
- Supports streaming chunk writes for large worlds without holding the full conversion in RAM.
- Supports explicit vertical Y mapping, such as `Minecraft Y 60 -> CMZ Y -64`.
- Generates an unmapped block report for missing Minecraft block mappings.

---

## Requirements

- Windows
- .NET Framework 4.8.1
- CastleMiner Z Steam save folder
- A rendered Minecraft Java Edition world
- `fNbt.dll`
- `System.Text.Json.dll` and its required dependency DLLs

The converter is currently built as a C# console application.

---

## Project Structure

```text
MCToCMZWorldConverter/
│
├── README.md
├── LICENSE
├── BuildRelease.bat
├── MCToCMZWorldConverter.sln
│
└── MCToCMZWorldConverter/
    ├── block-map.json
    ├── config.json
    ├── ConvertWorldToCMZ.bat
    ├── MCToCMZWorldConverter.csproj
    ├── Program.cs
    ├── Config.cs
    │
    ├── CastleMinerZ/
    │   ├── CmzBlockType.cs
    │   ├── CmzChunkWriter.cs
    │   └── CmzWorldInfoWriter.cs
    │
    ├── Mapping/
    │   └── BlockMap.cs
    │
    ├── Minecraft/
    │   ├── AnvilWorldReader.cs
    │   ├── BigEndianBinaryReader.cs
    │   ├── MinecraftChunk.cs
    │   ├── MinecraftSection.cs
    │   └── RegionFileReader.cs
    │
    ├── Properties/
    │   └── AssemblyInfo.cs
    │
    └── ReferenceAssemblies/
        ├── fNbt.dll
        ├── Microsoft.Bcl.AsyncInterfaces.dll
        ├── System.Buffers.dll
        ├── System.IO.Pipelines.dll
        ├── System.Memory.dll
        ├── System.Numerics.Vectors.dll
        ├── System.Runtime.CompilerServices.Unsafe.dll
        ├── System.Text.Encodings.Web.dll
        ├── System.Text.Json.dll
        └── System.Threading.Tasks.Extensions.dll
```

---

## Usage

The converter is config-based:

```bat
MCToCMZWorldConverter.exe config.json
```

A batch file is included:

```bat
ConvertWorldToCMZ.bat
```

Typical workflow:

1. Open Minecraft and render/explore the world area you want to convert.
2. Exit Minecraft.
3. Create or locate your CastleMiner Z Steam save folder.
4. Edit `config.json`.
5. Run `ConvertWorldToCMZ.bat` or run the `.exe` with `config.json`.
6. Open CastleMiner Z and check the Creative/Infinite Resources world list.

---

## Recommended Config

```jsonc
{
  "InputMinecraftWorld": "C:/Users/John/AppData/Roaming/.minecraft/saves/CMZ-WORLD-TEST",

  // This should point to CMZ's actual Worlds folder.
  // Example Steam save path:
  // C:/Users/John/AppData/Local/CastleMinerZ/<SteamID>/Worlds
  "OutputCmzWorldFolder": "C:/Users/John/AppData/Local/CastleMinerZ/76561198296842857/Worlds",

  "BlockMap": "block-map.json",

  "Minecraft": {
    "ConvertAllRenderedChunks": true,

    // These bounds are only used when ConvertAllRenderedChunks=false.
    "MinChunkX": -1,
    "MaxChunkX": 1,
    "MinChunkZ": -1,
    "MaxChunkZ": 1,

    // Clear vertical mapping.
    // This says: Minecraft Y 60 becomes CMZ Y -64.
    "VerticalMapping": {
      "MinecraftY": 60,
      "CmzY": -64
    },

    "Dimension": "minecraft:overworld",
    "PreferModernDimensionPath": true,
    "SkipUnreadableChunks": true,
    "RegionFolder": ""
  },

  "Placement": {
    "OffsetX": 0,
    "OffsetZ": 0
  },

  "AirHandling": {
    "WriteAir": true
  },

  "Cmz": {
    "CreateWorldFolderWithRandomUuid": true,
    "OutputPathIsCastleMinerZSaveRoot": false,
    "RequireOutputFolderNamedWorlds": true,
    "WriteWorldInfoDebugReport": true,

    "InferSaveDeviceSteamIdFromOutputPath": true,
    "SaveDeviceSteamId": "",
    "UseCommonSaveDeviceKey": false,

    "WorldName": "Converted Minecraft World",
    "OwnerGamerTag": "",
    "CreatorGamerTag": "",
    "ServerMessage": "Converted Minecraft World",
    "ServerPassword": "",
    "InfiniteResourceMode": true,

    "RandomSeed": true,
    "Seed": 0,

    "LastPositionX": 8.0,
    "LastPositionY": 128.0,
    "LastPositionZ": -8.0,

    "ClearExistingChunkFiles": true,
    "KeepFloorBedrock": true,
    "StreamingChunkWrites": true,
    "WrapChunkFilesWithSaveDevice": true,

    "OverrideUnmappedBlocks": false,
    "DefaultUnmappedBlock": "Empty"
  }
}
```

---

## Finding the CastleMiner Z Worlds Folder

For the Steam version, CastleMiner Z normally stores saves under:

```text
%LOCALAPPDATA%\CastleMinerZ\<SteamID>\Worlds
```

Example:

```text
C:\Users\John\AppData\Local\CastleMinerZ\76561198296842857\Worlds
```

`OutputCmzWorldFolder` should usually point directly to that `Worlds` folder.

The converter will create a random UUID folder inside it:

```text
Worlds/
  b0d30a87-0872-44c5-b77f-8a21459380a7/
    world.info
    mc-to-cmz-world.info-check.txt
    X0Y-64Z0.dat
    X16Y-64Z0.dat
    ...
```

If your config points one folder above `Worlds`, set:

```json
"OutputPathIsCastleMinerZSaveRoot": true
```

Example:

```jsonc
"OutputCmzWorldFolder": "C:/Users/John/AppData/Local/CastleMinerZ/76561198296842857",

"Cmz": {
  "OutputPathIsCastleMinerZSaveRoot": true
}
```

The converter will append `/Worlds` automatically.

---

## Full CMZ World Folder Generation

By default, the converter creates a new CastleMiner Z world folder:

```json
"CreateWorldFolderWithRandomUuid": true
```

This creates:

```text
<Worlds>/<random-guid>/world.info
<Worlds>/<random-guid>/X0Y-64Z0.dat
<Worlds>/<random-guid>/X16Y-64Z0.dat
```

The generated `world.info` includes:

```text
WorldInfo version 5
Terrain version CastleMinerZ
Random folder UUID
Random internal WorldID GUID
Random seed unless RandomSeed=false
World name from config
Owner/creator gamertag from config
Empty crates/doors/spawners
Default last position from config
```

The folder UUID and internal `WorldID` are intentionally different, matching how CastleMiner Z creates worlds.

### Important SaveDevice Notes

CastleMiner Z does not load raw `world.info` or raw chunk `.dat` files directly.

Both must be written through the CMZ SaveDevice `RTSD` wrapper:

```text
world.info      -> RTSD wrapped
X0Y-64Z0.dat    -> RTSD wrapped
X16Y-64Z0.dat   -> RTSD wrapped
```

For Steam saves, the SaveDevice key is based on your SteamID folder:

```text
MD5(SteamUserID + "CMZ778")
```

For a path like:

```text
C:\Users\John\AppData\Local\CastleMinerZ\76561198296842857\Worlds
```

leave this enabled:

```json
"InferSaveDeviceSteamIdFromOutputPath": true
```

The converter will infer:

```text
SteamID = 76561198296842857
SaveDevice key = MD5("76561198296842857CMZ778")
```

---

## Existing World Mode

To write into an already-created blank CastleMiner Z world folder, set:

```json
"CreateWorldFolderWithRandomUuid": false
```

Then `OutputCmzWorldFolder` is treated as the exact CMZ world folder, not the parent `Worlds` folder.

Example:

```jsonc
"OutputCmzWorldFolder": "C:/Users/John/AppData/Local/CastleMinerZ/76561198296842857/Worlds/b0d30a87-0872-44c5-b77f-8a21459380a7",

"Cmz": {
  "CreateWorldFolderWithRandomUuid": false
}
```

Use this mode if you want CastleMiner Z itself to create `world.info`, then let the converter replace/add the chunk `.dat` files.

---

## Minecraft Region Folder Support

Older Minecraft Java worlds usually store overworld region files here:

```text
<world>/region
```

Modern/custom-dimension/datapack worlds may store the overworld here:

```text
<world>/dimensions/minecraft/overworld/region
```

Usually leave `RegionFolder` blank:

```json
"RegionFolder": ""
```

The converter will auto-detect the best folder based on `Dimension` and `PreferModernDimensionPath`.

### Dimension Examples

Overworld:

```json
"Dimension": "minecraft:overworld"
```

Nether:

```json
"Dimension": "minecraft:the_nether"
```

End:

```json
"Dimension": "minecraft:the_end"
```

Custom datapack/modded dimension:

```json
"Dimension": "terralith:some_dimension"
```

This maps to:

```text
<world>/dimensions/terralith/some_dimension/region
```

### Explicit Region Folder

You can override auto-detection with a relative path:

```json
"RegionFolder": "dimensions/minecraft/overworld/region"
```

or an absolute path:

```json
"RegionFolder": "C:/Users/John/AppData/Roaming/.minecraft/saves/MyWorld/dimensions/minecraft/overworld/region"
```

The converter reads only the `region` folder. It does not read Minecraft `entities` or `poi` folders.

---

## Vertical Y Mapping

CastleMiner Z chunks only store 128 vertical block positions:

```text
CMZ Y -64 through CMZ Y 63
```

Use `VerticalMapping` to say exactly which Minecraft height should become which CMZ height.

Default:

```json
"VerticalMapping": {
  "MinecraftY": 60,
  "CmzY": -64
}
```

This means:

```text
Minecraft Y 60  -> CMZ Y -64
Minecraft Y 124 -> CMZ Y 0
Minecraft Y 187 -> CMZ Y 63
```

So the imported slice is:

```text
Minecraft Y 60..187 -> CMZ Y -64..63
```

Formula:

```text
CMZ_Y = Minecraft_Y + (CmzY - MinecraftY)
```

For the default config:

```text
CMZ_Y = Minecraft_Y - 124
```

### Legacy CenterY

Older configs may use:

```json
"CenterY": 124
```

That is equivalent to:

```json
"VerticalMapping": {
  "MinecraftY": 60,
  "CmzY": -64
}
```

`VerticalMapping` is recommended because it directly says what height maps to what height.

---

## Convert All Rendered Chunks

By default:

```json
"ConvertAllRenderedChunks": true
```

This converts every chunk already present in the Minecraft region files.

For a small test conversion, set it to `false`:

```jsonc
"ConvertAllRenderedChunks": false,
"MinChunkX": -5,
"MaxChunkX": 5,
"MinChunkZ": -5,
"MaxChunkZ": 5
```

The `MinChunkX`, `MaxChunkX`, `MinChunkZ`, and `MaxChunkZ` values are only used when `ConvertAllRenderedChunks=false`.

---

## Placement Offsets

Horizontal offsets can move the converted world inside CastleMiner Z:

```json
"Placement": {
  "OffsetX": 0,
  "OffsetZ": 0
}
```

When streaming chunk writes are enabled, offsets should be multiples of 16:

```text
Valid:   0, 16, -16, 32, -32
Invalid: 1, 8, 15, 20
```

This prevents partially-overwritten CMZ chunks.

---

## Air Handling

Default:

```json
"WriteAir": true
```

This writes `Empty` blocks into CMZ so the converted Minecraft terrain replaces the generated CMZ terrain.

If set to `false`, air is skipped:

```json
"WriteAir": false
```

Skipping air can be useful for overlay-style conversions, but it will leave existing CMZ terrain in empty spaces.

---

## Tools

The `Tools/` folder contains helper scripts for maintaining and updating `block-map.json`.

These tools are not required for normal schematic conversion, but they make it easier to generate Minecraft block id lists and quickly fill large block maps with reasonable CastleMiner Z defaults.

```text
Tools/
├── AutoFillBlockMap.ps1
└── DumpMinecraftBlockIds.bat
````

---

### DumpMinecraftBlockIds.bat

`DumpMinecraftBlockIds.bat` generates a plain text list of Minecraft block ids from Minecraft's extracted `blockstates` folder.

Minecraft blockstates are stored inside the Minecraft Java Edition client jar at:

```text
assets/minecraft/blockstates
```

Each `.json` file in that folder represents one Minecraft block id.

Example:

```text
acacia_log.json      -> minecraft:acacia_log
twisting_vines.json  -> minecraft:twisting_vines
stone.json           -> minecraft:stone
oak_stairs.json      -> minecraft:oak_stairs
```

#### Usage

1. Open or extract the Minecraft Java Edition jar you want to support.

Typical jar location:

```text
%APPDATA%\.minecraft\versions\<version>\<version>.jar
```

2. Extract this folder from the jar:

```text
assets\minecraft\blockstates
```

3. Copy `DumpMinecraftBlockIds.bat` into the extracted `blockstates` folder.

4. Run the batch file.

The script will create:

```text
minecraft-block-ids.txt
```

containing entries like:

```text
minecraft:acacia_log
minecraft:twisting_vines
minecraft:stone
minecraft:oak_stairs
```

#### Notes

Use the newest Minecraft jar when updating the main `block-map.json`.

Use an older jar only if you specifically want to support or compare against an older Minecraft version.

This tool only dumps base block ids. It does not generate every possible block state combination, such as:

```text
minecraft:oak_stairs[facing=north,half=bottom,shape=straight,waterlogged=false]
```

---

### AutoFillBlockMap.ps1

`AutoFillBlockMap.ps1` automatically fills blank or existing Minecraft mapping entries with guessed CastleMiner Z block types.

It is useful after generating or expanding `block-map.json` with a large Minecraft block list.

Example input:

```json
"minecraft:oak_log": "",
"minecraft:oak_leaves": "",
"minecraft:stone": "",
"minecraft:glass": "",
"minecraft:water": ""
```

Example output:

```json
"minecraft:oak_log": "Log",
"minecraft:oak_leaves": "Leaves",
"minecraft:stone": "Rock",
"minecraft:glass": "GlassMystery",
"minecraft:water": "Empty"
```

#### Usage

From the folder containing `block-map.json`, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\AutoFillBlockMap.ps1
```

By default, this reads:

```text
block-map.json
```

and writes:

```text
block-map.autofilled.json
```

You can also pass custom paths:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\AutoFillBlockMap.ps1 -InputPath ".\block-map.json" -OutputPath ".\block-map.autofilled.json"
```

#### Important

Review the generated file before replacing your main `block-map.json`.

The auto-fill script uses broad name-matching rules. It is designed to create a fast first pass, not a perfect hand-authored map.

For example:

```text
minecraft:oak_stairs       -> Wood
minecraft:deepslate        -> Rock
minecraft:diamond_ore      -> DiamondOre
minecraft:redstone_wire    -> Empty
minecraft:white_bed        -> Empty
```

Some Minecraft blocks do not have a clean CastleMiner Z equivalent, so they may need manual adjustment.

Recommended workflow:

1. Back up `block-map.json`.
2. Run `AutoFillBlockMap.ps1`.
3. Open `block-map.autofilled.json`.
4. Review important mappings.
5. Rename it to `block-map.json` when satisfied.

---

## Block Map

The `block-map.json` file controls how Minecraft blocks are converted into CastleMiner Z blocks.

Example:

```json
{
  // Default CMZ block used when a Minecraft block is not listed below.
  // "Empty" means unmapped blocks become air.
  "DefaultBlock": "Empty",

  // Minecraft block -> CastleMinerZ block mappings.
  "Mappings": {
    "minecraft:air": "Empty",
    "minecraft:cave_air": "Empty",
    "minecraft:void_air": "Empty",

    "minecraft:acacia_log": "Log",
    "Acacia Log": "Log",

    "minecraft:oak_log": "Log",
    "minecraft:spruce_log": "Log",
    "minecraft:birch_log": "Log",
    "minecraft:jungle_log": "Log",
    "minecraft:dark_oak_log": "Log",
    "minecraft:mangrove_log": "Log",
    "minecraft:cherry_log": "Log",

    "minecraft:oak_leaves": "Leaves",
    "minecraft:spruce_leaves": "Leaves",
    "minecraft:birch_leaves": "Leaves",
    "minecraft:jungle_leaves": "Leaves",
    "minecraft:acacia_leaves": "Leaves",
    "minecraft:dark_oak_leaves": "Leaves",
    "minecraft:mangrove_leaves": "Leaves",
    "minecraft:cherry_leaves": "Leaves",

    "minecraft:twisting_vines": "Leaves",
    "Twisting Vines": "Leaves",

    "minecraft:stone": "Rock",
    "minecraft:cobblestone": "Rock",
    "minecraft:dirt": "Dirt",
    "minecraft:grass_block": "Grass",
    "minecraft:sand": "Sand",
    "minecraft:snow_block": "Snow",
    "minecraft:ice": "Ice",

    "minecraft:glass": "GlassBasic",
    "minecraft:tnt": "TNT",
    "minecraft:torch": "Torch",
    "minecraft:chest": "Crate"
  }
}
```

---

## Block Map Notes

The converter normalizes Minecraft block names before lookup.

These can all resolve to the same mapping:

```text
minecraft:acacia_log
acacia_log
Acacia Log
```

Minecraft block states can also be mapped directly:

```json
{
  "Mappings": {
    "minecraft:torch[facing=east]": "TorchPOSX",
    "minecraft:torch[facing=west]": "TorchNEGX",
    "minecraft:torch[facing=north]": "TorchNEGZ",
    "minecraft:torch[facing=south]": "TorchPOSZ",
    "minecraft:torch": "Torch"
  }
}
```

This allows broad mappings and more specific state-based mappings.

---

## Unmapped Block Report

When the converter finds Minecraft blocks that are not listed in `block-map.json`, it writes a report inside the generated CMZ world folder:

```text
mc-to-cmz-world.unmapped.txt
```

Example contents:

```text
minecraft:polished_andesite
minecraft:oak_stairs[facing=north,half=bottom,shape=straight,waterlogged=false]
minecraft:lantern[hanging=false,waterlogged=false]
```

Add missing blocks to `block-map.json`, then rerun the converter.

---

## Debug Reports

When enabled:

```json
"WriteWorldInfoDebugReport": true
```

The converter writes:

```text
mc-to-cmz-world.info-check.txt
```

This report shows:

```text
Generated world folder
WorldInfo version
Terrain version
WorldName
OwnerGamerTag
CreatorGamerTag
CreatedDate
LastPlayedDate
Seed
WorldID
LastPosition
InfiniteResourceMode
SaveDevice key source
RTSD wrapping status
```

If the world does not appear in CastleMiner Z, check this file first.

---

## Current Limitations

This converter handles terrain/block conversion only.

The following Minecraft data is not converted:

- Entities
- Mobs
- Players
- Villagers
- Chests/barrel inventory contents
- Sign text
- Command block data
- Item frames
- Paintings
- Banner patterns
- Redstone behavior
- Waterlogging behavior
- Stairs/fence/wall connection shapes
- Biomes
- Lighting data
- Minecraft height outside the selected 128-block CMZ vertical slice

Some Minecraft blocks do not have a clean CastleMiner Z equivalent and may need manual block-map tuning.

---

## Recommended Workflow

1. Back up your CastleMiner Z saves.
2. Open Minecraft and render the area you want converted.
3. Exit Minecraft.
4. Edit `config.json`.
5. Point `InputMinecraftWorld` to your Minecraft save folder.
6. Point `OutputCmzWorldFolder` to your actual CastleMiner Z `Worlds` folder.
7. Keep `ConvertAllRenderedChunks=true` for a full conversion, or set it to `false` for a small test.
8. Adjust `VerticalMapping` if needed.
9. Run the converter.
10. Open CastleMiner Z and load the generated world.
11. Review `mc-to-cmz-world.unmapped.txt` and update `block-map.json`.
12. Rerun the converter when needed.

---

## Example Console Output

```text
MC to CMZ world conversion
Input MC world:           C:\Users\John\AppData\Roaming\.minecraft\saves\CMZ-WORLD-TEST
Output CMZ Worlds folder: C:\Users\John\AppData\Local\CastleMinerZ\76561198296842857\Worlds
Output CMZ world:         C:\Users\John\AppData\Local\CastleMinerZ\76561198296842857\Worlds\b0d30a87-0872-44c5-b77f-8a21459380a7
World folder UUID:        b0d30a87-0872-44c5-b77f-8a21459380a7
World ID:                 30227c27-896c-495b-8cd9-c0aa084220b8
SaveDevice key:           inferred SteamID 76561198296842857 from Worlds folder parent
MC region folder:         C:\Users\John\AppData\Roaming\.minecraft\saves\CMZ-WORLD-TEST\dimensions\minecraft\overworld\region
Region files:             55
Chunks queued:            37321
Y slice:                  MC 60..187 -> CMZ -64..63
Y mapping:                MC Y 60 -> CMZ Y -64
Write air:                True
Keep bedrock:             True
Streaming writes:         True
Wrap chunks RTSD:         True
```

---

## Build Notes

The project targets:

```text
.NET Framework 4.8.1
```

Recommended build settings:

```text
Platform: x86
Configuration: Debug or Release
```

Build with:

```bat
BuildRelease.bat
```

The following files should be copied next to the final `.exe`:

```text
config.json
block-map.json
fNbt.dll
System.Text.Json.dll
System.Memory.dll
System.Buffers.dll
System.Runtime.CompilerServices.Unsafe.dll
System.Text.Encodings.Web.dll
System.Threading.Tasks.Extensions.dll
Microsoft.Bcl.AsyncInterfaces.dll
System.IO.Pipelines.dll
```

---

## License

This project is licensed under the GPL-3.0-or-later license.

See `LICENSE` for details.

---

## Credits

Created by RussDev7 for CastleMiner Z / CastleForge tooling.

This project uses `fNbt` for reading Minecraft NBT/Anvil data.
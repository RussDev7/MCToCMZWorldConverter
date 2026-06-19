/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2026 RussDev7
This file is part of https://github.com/RussDev7/MCToCMZWorldConverter - see LICENSE for details.
*/

using MCToCMZWorldConverter.CastleMinerZ;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using System;

namespace MCToCMZWorldConverter
{
    #region Root Converter Config

    /// <summary>
    /// Root JSON configuration model for MCToCMZWorldConverter.
    /// </summary>
    /// <remarks>
    /// This class is responsible for loading config.json, normalizing relative paths,
    /// resolving Minecraft region folders, validating conversion settings, and preparing
    /// parsed values used later by the converter.
    /// </remarks>
    public sealed class ConverterConfig
    {
        #region Top-Level Paths

        /// <summary>
        /// Minecraft Java world folder to read from.
        /// </summary>
        public string InputMinecraftWorld { get; set; }

        /// <summary>
        /// CastleMiner Z Worlds folder, save-root folder, or explicit output folder depending on Cmz settings.
        /// </summary>
        public string OutputCmzWorldFolder { get; set; }

        /// <summary>
        /// JSON block mapping file used to convert Minecraft block states into CMZ block enum names.
        /// </summary>
        public string BlockMap { get; set; } = "block-map.json";

        #endregion

        #region Nested Config Sections

        public MinecraftConfig Minecraft { get; set; } = new MinecraftConfig();
        public PlacementConfig Placement { get; set; } = new PlacementConfig();
        public AirHandlingConfig AirHandling { get; set; } = new AirHandlingConfig();
        public CmzConfig Cmz { get; set; } = new CmzConfig();

        #endregion

        #region Runtime-Only Normalized Values

        /// <summary>
        /// Directory containing the loaded config file. Relative config paths resolve from here.
        /// </summary>
        public string ConfigDirectory { get; private set; }

        #endregion

        #region Load And Validation

        /// <summary>
        /// Loads config.json, allows comments/trailing commas, then validates and normalizes paths/settings.
        /// </summary>
        public static ConverterConfig Load(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string json = File.ReadAllText(fullPath);

            var config = JsonSerializer.Deserialize<ConverterConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (config == null)
                throw new InvalidOperationException("Could not read config file.");

            config.ConfigDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
            config.ValidateAndNormalize();
            return config;
        }

        /// <summary>
        /// Validates required settings, expands relative paths, resolves derived settings,
        /// and applies safe defaults for optional metadata.
        /// </summary>
        private void ValidateAndNormalize()
        {
            if (string.IsNullOrWhiteSpace(InputMinecraftWorld))
                throw new InvalidOperationException("Config value InputMinecraftWorld is required.");

            if (string.IsNullOrWhiteSpace(OutputCmzWorldFolder))
                throw new InvalidOperationException("Config value OutputCmzWorldFolder is required.");

            InputMinecraftWorld = MakeFullPath(InputMinecraftWorld);
            OutputCmzWorldFolder = MakeFullPath(OutputCmzWorldFolder);
            BlockMap = MakeFullPath(BlockMap);

            if (!Directory.Exists(InputMinecraftWorld))
                throw new DirectoryNotFoundException("Minecraft world folder was not found: " + InputMinecraftWorld);

            if (Minecraft == null)
                Minecraft = new MinecraftConfig();
            if (Placement == null)
                Placement = new PlacementConfig();
            if (AirHandling == null)
                AirHandling = new AirHandlingConfig();
            if (Cmz == null)
                Cmz = new CmzConfig();

            Minecraft.ResolvedRegionFolder = ResolveMinecraftRegionFolder(InputMinecraftWorld, Minecraft);

            if (!File.Exists(BlockMap))
                throw new FileNotFoundException("Block map file was not found.", BlockMap);

            if (Minecraft.MinChunkX > Minecraft.MaxChunkX)
                throw new InvalidOperationException("Minecraft.MinChunkX cannot be greater than Minecraft.MaxChunkX.");
            if (Minecraft.MinChunkZ > Minecraft.MaxChunkZ)
                throw new InvalidOperationException("Minecraft.MinChunkZ cannot be greater than Minecraft.MaxChunkZ.");

            Minecraft.NormalizeVerticalMapping();

            Cmz.ParsedDefaultUnmappedBlock = ParseCmzBlock(Cmz.DefaultUnmappedBlock);

            if (string.IsNullOrWhiteSpace(Cmz.WorldName))
                Cmz.WorldName = "Converted Minecraft World";
            if (string.IsNullOrWhiteSpace(Cmz.OwnerGamerTag))
                Cmz.OwnerGamerTag = Environment.UserName ?? "CMZConverter";
            if (string.IsNullOrWhiteSpace(Cmz.CreatorGamerTag))
                Cmz.CreatorGamerTag = Cmz.OwnerGamerTag;
            if (Cmz.ServerMessage == null)
                Cmz.ServerMessage = Cmz.OwnerGamerTag + "'s Server";
            if (Cmz.ServerPassword == null)
                Cmz.ServerPassword = string.Empty;

            if (Cmz.StreamingChunkWrites && ((Placement.OffsetX % 16) != 0 || (Placement.OffsetZ % 16) != 0))
            {
                throw new InvalidOperationException("Cmz.StreamingChunkWrites requires Placement.OffsetX and Placement.OffsetZ to be multiples of 16. This prevents partially-overwritten CMZ chunks. Set offsets to 0, 16, -16, etc., or disable streaming for very small tests only.");
            }
        }
        #endregion

        #region Path Helpers

        /// <summary>
        /// Resolves an absolute path. Relative paths are resolved from the config file directory.
        /// </summary>
        private string MakeFullPath(string path)
        {
            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);

            return Path.GetFullPath(Path.Combine(ConfigDirectory ?? Directory.GetCurrentDirectory(), path));
        }

        /// <summary>
        /// Resolves the Minecraft region folder, supporting both legacy and modern dimension layouts.
        /// </summary>
        private static string ResolveMinecraftRegionFolder(string worldFolder, MinecraftConfig minecraft)
        {
            if (!string.IsNullOrWhiteSpace(minecraft.RegionFolder))
            {
                string custom = minecraft.RegionFolder;
                if (!Path.IsPathRooted(custom))
                    custom = Path.Combine(worldFolder, custom);

                custom = Path.GetFullPath(custom);
                if (!Directory.Exists(custom))
                    throw new DirectoryNotFoundException("Configured Minecraft region folder was not found: " + custom);

                return custom;
            }

            var candidates = BuildRegionFolderCandidates(worldFolder, minecraft);
            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate))
                    return candidate;
            }

            throw new DirectoryNotFoundException(
                "Minecraft region folder was not found. Tried:" + Environment.NewLine +
                "  " + string.Join(Environment.NewLine + "  ", candidates) + Environment.NewLine +
                "For modern worlds, set Minecraft.RegionFolder to something like: dimensions/minecraft/overworld/region");
        }

        /// <summary>
        /// Builds possible region-folder locations in preferred search order.
        /// </summary>
        private static List<string> BuildRegionFolderCandidates(string worldFolder, MinecraftConfig minecraft)
        {
            var candidates = new List<string>();
            string dimension = string.IsNullOrWhiteSpace(minecraft.Dimension) ? "minecraft:overworld" : minecraft.Dimension.Trim();

            string modernDimensionPath = GetModernDimensionRegionPath(worldFolder, dimension);
            string legacyDimensionPath = GetLegacyDimensionRegionPath(worldFolder, dimension);
            string legacyOverworldPath = Path.Combine(worldFolder, "region");

            if (minecraft.PreferModernDimensionPath)
            {
                AddIfNotNullOrDuplicate(candidates, modernDimensionPath);
                AddIfNotNullOrDuplicate(candidates, legacyDimensionPath);
                AddIfNotNullOrDuplicate(candidates, legacyOverworldPath);
            }
            else
            {
                AddIfNotNullOrDuplicate(candidates, legacyOverworldPath);
                AddIfNotNullOrDuplicate(candidates, legacyDimensionPath);
                AddIfNotNullOrDuplicate(candidates, modernDimensionPath);
            }

            return candidates;
        }

        /// <summary>
        /// Builds a modern dimension region path like dimensions/minecraft/overworld/region.
        /// </summary>
        private static string GetModernDimensionRegionPath(string worldFolder, string dimension)
        {
            string namespaceName;
            string dimensionName;

            int colon = dimension.IndexOf(':');
            if (colon >= 0)
            {
                namespaceName = dimension.Substring(0, colon);
                dimensionName = dimension.Substring(colon + 1);
            }
            else
            {
                namespaceName = "minecraft";
                dimensionName = dimension;
            }

            if (string.IsNullOrWhiteSpace(namespaceName) || string.IsNullOrWhiteSpace(dimensionName))
                return null;

            dimensionName = dimensionName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(worldFolder, "dimensions", namespaceName, dimensionName, "region");
        }

        /// <summary>
        /// Builds legacy vanilla dimension region paths: region, DIM-1/region, or DIM1/region.
        /// </summary>
        private static string GetLegacyDimensionRegionPath(string worldFolder, string dimension)
        {
            switch ((dimension ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "minecraft:overworld":
                case "overworld":
                    return Path.Combine(worldFolder, "region");
                case "minecraft:the_nether":
                case "the_nether":
                case "nether":
                    return Path.Combine(worldFolder, "DIM-1", "region");
                case "minecraft:the_end":
                case "the_end":
                case "end":
                    return Path.Combine(worldFolder, "DIM1", "region");
                default:
                    return null;
            }
        }

        /// <summary>
        /// Adds a path candidate only when it is present and not already listed.
        /// </summary>
        private static void AddIfNotNullOrDuplicate(List<string> list, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            path = Path.GetFullPath(path);
            foreach (string existing in list)
            {
                if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            list.Add(path);
        }
        #endregion

        #region CMZ Block Parsing

        /// <summary>
        /// Converts a config string into a CMZ block enum, defaulting blank values to Empty.
        /// </summary>
        private static CmzBlockType ParseCmzBlock(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return CmzBlockType.Empty;

            if (!Enum.TryParse(name, true, out CmzBlockType block))
                throw new InvalidOperationException("Unknown CMZ block type in config: " + name);

            return block;
        }
        #endregion
    }
    #endregion

    #region Minecraft Config

    /// <summary>
    /// Minecraft-side conversion settings: chunk selection, vertical mapping, region discovery, and read-error behavior.
    /// </summary>
    public sealed class MinecraftConfig
    {
        #region Chunk Selection

        /// <summary>
        /// When true, converts every rendered chunk found in the selected region folder.
        /// When false, uses Min/Max chunk bounds below.
        /// </summary>
        public bool ConvertAllRenderedChunks { get; set; } = true;

        /// <summary>
        /// Minimum Minecraft chunk X used only when ConvertAllRenderedChunks is false.
        /// </summary>
        public int MinChunkX { get; set; } = -10;

        /// <summary>
        /// Maximum Minecraft chunk X used only when ConvertAllRenderedChunks is false.
        /// </summary>
        public int MaxChunkX { get; set; } = 10;

        /// <summary>
        /// Minimum Minecraft chunk Z used only when ConvertAllRenderedChunks is false.
        /// </summary>
        public int MinChunkZ { get; set; } = -10;

        /// <summary>
        /// Maximum Minecraft chunk Z used only when ConvertAllRenderedChunks is false.
        /// </summary>
        public int MaxChunkZ { get; set; } = 10;

        #endregion

        #region Vertical Mapping

        // Legacy/backward-compatible vertical setting.
        // Old formula: CMZ_Y = Minecraft_Y - CenterY.
        // Example: CenterY=64 imports Minecraft Y 0..127 into CMZ Y -64..63.
        // Prefer VerticalMapping below for new configs because it is clearer.
        public int CenterY { get; set; } = 64;

        // Preferred vertical mapping. This directly says which Minecraft Y should become which CMZ Y.
        // Example: { "MinecraftY": 60, "CmzY": -64 } imports Minecraft Y 60..187 into CMZ Y -64..63.
        // If null/omitted, the legacy CenterY setting is used.
        public VerticalMappingConfig VerticalMapping { get; set; }

        #region Derived Vertical Mapping Values

        /// <summary>
        /// First Minecraft Y included in the 128-block CMZ vertical slice.
        /// </summary>
        internal int MinecraftMinY { get; private set; }

        /// <summary>
        /// Last Minecraft Y included in the 128-block CMZ vertical slice.
        /// </summary>
        internal int MinecraftMaxY { get; private set; }

        /// <summary>
        /// Offset applied with CMZ_Y = Minecraft_Y + YOffsetToCmz.
        /// </summary>
        internal int YOffsetToCmz { get; private set; }

        /// <summary>
        /// Minecraft Y coordinate used as the source anchor for the mapping.
        /// </summary>
        internal int MappingSourceMinecraftY { get; private set; }

        /// <summary>
        /// CMZ Y coordinate used as the target anchor for the mapping.
        /// </summary>
        internal int MappingTargetCmzY { get; private set; }

        /// <summary>
        /// True when the old CenterY setting was used instead of VerticalMapping.
        /// </summary>
        internal bool UsedLegacyCenterY { get; private set; }

        #endregion

        /// <summary>
        /// Normalizes either the preferred VerticalMapping or legacy CenterY into final Y slice values.
        /// </summary>
        internal void NormalizeVerticalMapping()
        {
            int sourceMinecraftY;
            int targetCmzY;

            if (VerticalMapping != null)
            {
                sourceMinecraftY = VerticalMapping.MinecraftY;
                targetCmzY = VerticalMapping.CmzY;
                UsedLegacyCenterY = false;
            }
            else
            {
                sourceMinecraftY = CenterY;
                targetCmzY = 0;
                UsedLegacyCenterY = true;
            }

            if (targetCmzY < -64 || targetCmzY > 63)
                throw new InvalidOperationException("Minecraft.VerticalMapping.CmzY must be between -64 and 63 because CMZ chunks only store Y=-64..63.");

            MappingSourceMinecraftY = sourceMinecraftY;
            MappingTargetCmzY = targetCmzY;
            YOffsetToCmz = targetCmzY - sourceMinecraftY;
            MinecraftMinY = -64 - YOffsetToCmz;
            MinecraftMaxY = 63 - YOffsetToCmz;
        }
        #endregion

        #region Region Discovery

        // Leave blank for auto-detect. For your modern save layout this can be:
        // dimensions/minecraft/overworld/region
        public string RegionFolder { get; set; } = "";

        // Examples: minecraft:overworld, minecraft:the_nether, minecraft:the_end,
        // or custom namespace:path for modern dimension folders.
        public string Dimension { get; set; } = "minecraft:overworld";

        // Modern datapack/custom-dimension worlds may put overworld region files under
        // dimensions/minecraft/overworld/region instead of the legacy root region folder.
        public bool PreferModernDimensionPath { get; set; } = true;

        // If true, a single unreadable/corrupt/unsupported chunk is logged and skipped instead of stopping the whole conversion.
        public bool SkipUnreadableChunks { get; set; } = true;

        /// <summary>
        /// Final resolved Minecraft region folder selected during config validation.
        /// </summary>
        internal string ResolvedRegionFolder { get; set; }

        #endregion
    }
    #endregion

    #region Vertical Mapping Config

    /// <summary>
    /// Preferred explicit Y mapping: maps one Minecraft Y coordinate to one CMZ Y coordinate.
    /// </summary>
    public sealed class VerticalMappingConfig
    {
        // The Minecraft world Y coordinate you want to line up.
        public int MinecraftY { get; set; } = 0;

        // The CMZ world Y coordinate that MinecraftY should become.
        // Valid CMZ range is -64 through 63.
        public int CmzY { get; set; } = -64;
    }
    #endregion

    #region Placement Config

    /// <summary>
    /// Horizontal placement offset applied to converted Minecraft world X/Z coordinates.
    /// </summary>
    public sealed class PlacementConfig
    {
        /// <summary>
        /// World-space X offset added to every converted block. Streaming writes require multiples of 16.
        /// </summary>
        public int OffsetX { get; set; }

        /// <summary>
        /// World-space Z offset added to every converted block. Streaming writes require multiples of 16.
        /// </summary>
        public int OffsetZ { get; set; }
    }
    #endregion

    #region Air Handling Config

    /// <summary>
    /// Controls whether mapped Empty blocks are written or skipped.
    /// </summary>
    public sealed class AirHandlingConfig
    {
        /// <summary>
        /// When true, writes Empty blocks so Minecraft air clears/generated CMZ terrain.
        /// </summary>
        public bool WriteAir { get; set; } = true;
    }
    #endregion

    #region CMZ Output Config

    /// <summary>
    /// CastleMiner Z output settings, including world-folder creation, SaveDevice wrapping, and CMZ metadata.
    /// </summary>
    public sealed class CmzConfig
    {
        #region Chunk Output Behavior

        /// <summary>
        /// When true, removes existing CMZ chunk .dat files from the output folder before conversion.
        /// </summary>
        public bool ClearExistingChunkFiles { get; set; } = true;

        /// <summary>
        /// Forces CMZ Y=-64 to Bedrock while writing the converted world.
        /// </summary>
        public bool KeepFloorBedrock { get; set; } = true;

        /// <summary>
        /// When true, uses DefaultUnmappedBlock for unmapped Minecraft blocks instead of the block map default behavior.
        /// </summary>
        public bool OverrideUnmappedBlocks { get; set; }

        #endregion

        #region World Folder Creation

        // When true, OutputCmzWorldFolder is treated as a parent folder and the converter
        // creates a complete CMZ world folder under it using a random UUID folder name.
        // Example: OutputCmzWorldFolder\3f5e9a29-...\world.info
        public bool CreateWorldFolderWithRandomUuid { get; set; } = true;

        // If true, OutputCmzWorldFolder points to the CastleMiner Z save root and the converter
        // will create/use a Worlds subfolder under it. If false, OutputCmzWorldFolder should
        // already be CMZ's actual Worlds folder.
        public bool OutputPathIsCastleMinerZSaveRoot { get; set; } = false;

        // Helpful safety check: CMZ only enumerates saved worlds from its actual Worlds folder.
        // Keep true unless intentionally exporting to a staging folder before manually copying.
        public bool RequireOutputFolderNamedWorlds { get; set; } = true;

        // Writes mc-to-cmz-world.info-check.txt after creating world.info.
        public bool WriteWorldInfoDebugReport { get; set; } = true;

        #endregion

        #region SaveDevice Key Settings

        // Steam CMZ saves use MD5(SteamUserID + "CMZ778") as the SaveDevice key.
        // The converter normally infers SteamUserID from the save root folder name:
        // C:/Users/.../AppData/Local/CastleMinerZ/<SteamUserID>/Worlds
        public bool InferSaveDeviceSteamIdFromOutputPath { get; set; } = true;
        public string SaveDeviceSteamId { get; set; } = "";

        // Last-resort fallback for old/non-Steam saves only. Steam saves should leave this false.
        public bool UseCommonSaveDeviceKey { get; set; } = false;

        #endregion

        #region WorldInfo Metadata

        // Metadata written to world.info when CreateWorldFolderWithRandomUuid is enabled.
        public string WorldName { get; set; } = "Converted Minecraft World";
        // Best result: set OwnerGamerTag to your exact CMZ/Steam display name.
        // Leaving it blank uses your Windows username so the field is never null.
        public string OwnerGamerTag { get; set; } = "";
        public string CreatorGamerTag { get; set; } = "";
        public string ServerMessage { get; set; } = "";
        public string ServerPassword { get; set; } = "";
        public bool InfiniteResourceMode { get; set; } = false;

        // If RandomSeed is true, Seed is ignored and a random int is written to world.info.
        public bool RandomSeed { get; set; } = true;
        public int Seed { get; set; } = 0;

        // These are written for WorldInfo version 5. Stock worlds usually start at 0/0.
        public int HellBossesSpawned { get; set; } = 0;
        public int MaxHellBossSpawns { get; set; } = 0;

        // Default CMZ spawn/last-position used by the stock game for new worlds.
        public float LastPositionX { get; set; } = 8f;
        public float LastPositionY { get; set; } = 128f;
        public float LastPositionZ { get; set; } = -8f;

        #endregion

        #region Streaming And SaveDevice Wrapping

        // Strongly recommended for whole-world conversion, especially with WriteAir=true.
        // Saves and clears each finished CMZ chunk instead of keeping the entire world in RAM.
        public bool StreamingChunkWrites { get; set; } = true;

        // Stock CMZ loads every chunk .dat through SaveDevice.Load(...), so generated chunks
        // must be RTSD-wrapped/encrypted/compressed just like files saved by the game.
        public bool WrapChunkFilesWithSaveDevice { get; set; } = true;

        #endregion

        #region Unmapped Block Handling

        /// <summary>
        /// CMZ block type used when OverrideUnmappedBlocks is true.
        /// </summary>
        public string DefaultUnmappedBlock { get; set; } = "Empty";

        /// <summary>
        /// Parsed form of DefaultUnmappedBlock, populated during config validation.
        /// </summary>
        internal CmzBlockType ParsedDefaultUnmappedBlock { get; set; } = CmzBlockType.Empty;

        #endregion
    }
    #endregion
}
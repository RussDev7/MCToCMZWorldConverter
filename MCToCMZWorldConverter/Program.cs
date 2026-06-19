/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2026 RussDev7
This file is part of https://github.com/RussDev7/MCToCMZWorldConverter - see LICENSE for details.
*/

using MCToCMZWorldConverter.CastleMinerZ;
using MCToCMZWorldConverter.Minecraft;
using MCToCMZWorldConverter.Mapping;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System;

namespace MCToCMZWorldConverter
{
    /// <summary>
    /// Console entry point for converting rendered Minecraft Java Anvil chunks into
    /// CastleMiner Z native world chunk files.
    /// </summary>
    /// <remarks>
    /// This class coordinates configuration loading, Minecraft region enumeration,
    /// CMZ world-folder preparation, chunk conversion, progress output, and final
    /// conversion reporting. The actual Minecraft parsing, block mapping, CMZ chunk
    /// writing, and CMZ save-device wrapping are handled by helper classes.
    /// </remarks>
    internal static class Program
    {
        #region Entry Point

        /// <summary>
        /// Loads the converter configuration, prepares the output CMZ world folder,
        /// converts the selected Minecraft chunks, and writes conversion reports.
        /// </summary>
        /// <param name="args">
        /// Command-line arguments. The first argument must be the path to config.json.
        /// </param>
        /// <returns>
        /// 0 when conversion succeeds, 1 when usage is invalid, or 2 when an exception occurs.
        /// </returns>
        private static int Main(string[] args)
        {
            try
            {
                #region Argument and Configuration Loading

                // A config file is required because conversion has multiple path, mapping,
                // vertical-slice, save-device, and output-folder settings.
                if (args.Length < 1)
                {
                    PrintUsage();
                    return 1;
                }

                string configPath            = args[0];
                ConverterConfig config       = ConverterConfig.Load(configPath);
                BlockMap blockMap            = BlockMap.Load(config.BlockMap);
                var minecraftWorld           = new AnvilWorldReader(config.InputMinecraftWorld, config.Minecraft.ResolvedRegionFolder);
                CmzCreatedWorld createdWorld = CmzWorldFolderBuilder.PrepareOutputWorldFolder(config.OutputCmzWorldFolder, config.Cmz);
                var cmzWriter                = new CmzWorldWriter(createdWorld.FolderPath, createdWorld.SaveDeviceKey, config.Cmz.WrapChunkFilesWithSaveDevice);

                #endregion

                #region Output Folder Preparation

                // Clear only CMZ chunk delta files. World metadata files such as world.info
                // are managed by CmzWorldFolderBuilder and should not be removed here.
                if (config.Cmz.ClearExistingChunkFiles)
                {
                    Console.WriteLine("Clearing existing CMZ chunk .dat files from output folder...");
                    CmzWorldWriter.ClearExistingChunkFiles(createdWorld.FolderPath);
                }
                #endregion

                #region Chunk Selection and Vertical Slice

                // Convert either every rendered chunk discovered in the selected Minecraft
                // region folder or a bounded chunk rectangle for smaller test conversions.
                List<ChunkLocation> chunks = config.Minecraft.ConvertAllRenderedChunks
                    ? minecraftWorld.GetRenderedChunks().OrderBy(c => c.ChunkZ).ThenBy(c => c.ChunkX).ToList()
                    : minecraftWorld.GetChunksInBounds(
                        config.Minecraft.MinChunkX,
                        config.Minecraft.MaxChunkX,
                        config.Minecraft.MinChunkZ,
                        config.Minecraft.MaxChunkZ).ToList();

                // CMZ chunks use a fixed local Y range of -64..63. The Minecraft slice is
                // resolved from the configured vertical mapping before conversion begins.
                int mcMinY = config.Minecraft.MinecraftMinY;
                int mcMaxY = config.Minecraft.MinecraftMaxY;

                #endregion

                #region Startup Summary

                Console.WriteLine("MC to CMZ world conversion");
                Console.WriteLine("Input MC world:           " + config.InputMinecraftWorld);
                Console.WriteLine("Output CMZ config path:   " + config.OutputCmzWorldFolder);
                Console.WriteLine("Output CMZ Worlds folder: " + createdWorld.WorldsFolderPath);
                Console.WriteLine("Output CMZ world:         " + createdWorld.FolderPath);
                if (config.Cmz.CreateWorldFolderWithRandomUuid)
                {
                    Console.WriteLine("World folder UUID:        " + createdWorld.FolderId);
                    Console.WriteLine("World ID:                 " + createdWorld.WorldId);
                    Console.WriteLine("World info:               " + createdWorld.WorldInfoPath);
                    Console.WriteLine("SaveDevice key:           " + createdWorld.SaveDeviceKeyDescription);
                    Console.WriteLine("Info check report:        " + Path.Combine(createdWorld.FolderPath, "mc-to-cmz-world.info-check.txt"));
                }
                Console.WriteLine("MC region folder:         " + minecraftWorld.RegionFolder);
                Console.WriteLine("Region files:             " + minecraftWorld.RegionFileCount);
                Console.WriteLine("Block map:                " + config.BlockMap);
                Console.WriteLine("Chunks queued:            " + chunks.Count);
                Console.WriteLine($"Y slice:                 MC {mcMinY}..{mcMaxY} -> CMZ -64..63");
                Console.WriteLine($"Y mapping:               MC Y {config.Minecraft.MappingSourceMinecraftY} -> CMZ Y {config.Minecraft.MappingTargetCmzY}" + (config.Minecraft.UsedLegacyCenterY ? " (legacy CenterY)" : ""));
                Console.WriteLine("Write air:                " + config.AirHandling.WriteAir);
                Console.WriteLine("Keep bedrock:             " + config.Cmz.KeepFloorBedrock);
                Console.WriteLine("Skip read errors:         " + config.Minecraft.SkipUnreadableChunks);
                Console.WriteLine("Streaming writes:         " + config.Cmz.StreamingChunkWrites);
                Console.WriteLine("Wrap chunks RTSD:         " + config.Cmz.WrapChunkFilesWithSaveDevice);
                Console.WriteLine();

                #endregion

                #region Conversion Loop

                var stats = new ConversionStats();
                Stopwatch sw = Stopwatch.StartNew();

                for (int i = 0; i < chunks.Count; i++)
                {
                    ChunkLocation location = chunks[i];
                    MinecraftChunk chunk;
                    try
                    {
                        chunk = minecraftWorld.ReadChunk(location);
                    }
                    catch (Exception ex) when (config.Minecraft.SkipUnreadableChunks)
                    {
                        stats.ReadErrors++;
                        Console.WriteLine($"WARNING: skipped unreadable MC chunk {location}: {ex.Message}");
                        continue;
                    }

                    if (chunk == null)
                    {
                        stats.MissingChunks++;
                        continue;
                    }

                    ConvertChunk(config, blockMap, chunk, cmzWriter, stats, mcMinY, mcMaxY);
                    stats.ChunksConverted++;

                    // Streaming writes keep memory usage low for full-world conversions by
                    // saving completed CMZ chunks to disk between Minecraft chunk reads.
                    if (config.Cmz.StreamingChunkWrites)
                    {
                        cmzWriter.SaveAllAndClear();
                    }

                    if ((i + 1) % 25 == 0 || i + 1 == chunks.Count)
                    {
                        Console.WriteLine($"Progress: {i + 1}/{chunks.Count} MC chunks, active CMZ chunks {cmzWriter.ChunkCount}, CMZ chunks written {cmzWriter.ChunksWritten}, blocks queued {cmzWriter.BlocksQueued:n0}");
                    }
                }

                cmzWriter.SaveAll();
                sw.Stop();

                #endregion

                #region Final Summary

                Console.WriteLine();
                Console.WriteLine("Conversion complete.");
                Console.WriteLine("Elapsed:             " + sw.Elapsed);
                Console.WriteLine("MC chunks converted: " + stats.ChunksConverted);
                Console.WriteLine("Missing chunks:      " + stats.MissingChunks);
                Console.WriteLine("Read-error chunks:   " + stats.ReadErrors);
                Console.WriteLine("CMZ chunks written:  " + cmzWriter.ChunksWritten);
                Console.WriteLine("Blocks written:      " + cmzWriter.BlocksQueued.ToString("n0"));
                Console.WriteLine("Air blocks written:  " + stats.AirBlocksWritten.ToString("n0"));
                Console.WriteLine("Bedrock forced:      " + stats.BedrockForced.ToString("n0"));
                Console.WriteLine("Blocks skipped air:  " + stats.AirBlocksSkipped.ToString("n0"));

                #endregion

                #region Unmapped Block Report

                // Any Minecraft block state not found in block-map.json is collected during
                // conversion and written next to the generated CMZ world for easy review.
                if (blockMap.UnmappedBlocks.Count > 0)
                {
                    string unmappedPath = Path.Combine(createdWorld.FolderPath, "mc-to-cmz-world.unmapped.txt");
                    Directory.CreateDirectory(createdWorld.FolderPath);
                    File.WriteAllLines(unmappedPath, blockMap.UnmappedBlocks.OrderBy(x => x));
                    Console.WriteLine();
                    Console.WriteLine($"WARNING: {blockMap.UnmappedBlocks.Count} Minecraft block types were unmapped.");
                    Console.WriteLine("Unmapped report: " + unmappedPath);
                }
                #endregion

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR:");
                Console.Error.WriteLine(ex.ToString());
                return 2;
            }
        }
        #endregion

        #region Chunk Conversion

        /// <summary>
        /// Converts one Minecraft chunk into CMZ world-space block writes.
        /// </summary>
        /// <remarks>
        /// Minecraft chunk-local X/Z coordinates are converted to Minecraft world
        /// coordinates, placement offsets are applied, and the configured Y mapping is
        /// used to place the selected Minecraft vertical slice into CMZ's -64..63 range.
        /// </remarks>
        private static void ConvertChunk(
            ConverterConfig config,
            BlockMap blockMap,
            MinecraftChunk chunk,
            CmzWorldWriter cmzWriter,
            ConversionStats stats,
            int mcMinY,
            int mcMaxY)
        {
            int baseWorldX = chunk.ChunkX * 16;
            int baseWorldZ = chunk.ChunkZ * 16;

            for (int localZ = 0; localZ < 16; localZ++)
            {
                for (int localX = 0; localX < 16; localX++)
                {
                    int mcWorldX = baseWorldX + localX;
                    int mcWorldZ = baseWorldZ + localZ;

                    int cmzWorldX = mcWorldX + config.Placement.OffsetX;
                    int cmzWorldZ = mcWorldZ + config.Placement.OffsetZ;

                    for (int mcY = mcMinY; mcY <= mcMaxY; mcY++)
                    {
                        int cmzY = mcY + config.Minecraft.YOffsetToCmz;
                        string blockState = chunk.GetBlockState(mcY, localX, localZ);
                        CmzBlockType cmzBlock = blockMap.Resolve(blockState);

                        if (config.Cmz.OverrideUnmappedBlocks && blockMap.UnmappedBlocks.Contains(blockState))
                            cmzBlock = config.Cmz.ParsedDefaultUnmappedBlock;

                        if (config.Cmz.KeepFloorBedrock && cmzY == -64)
                        {
                            cmzBlock = CmzBlockType.Bedrock;
                            stats.BedrockForced++;
                        }

                        if (!config.AirHandling.WriteAir && cmzBlock == CmzBlockType.Empty)
                        {
                            stats.AirBlocksSkipped++;
                            continue;
                        }

                        if (cmzBlock == CmzBlockType.Empty)
                            stats.AirBlocksWritten++;

                        cmzWriter.SetBlock(cmzWorldX, cmzY, cmzWorldZ, cmzBlock);
                    }
                }
            }
        }
        #endregion

        #region Console Help

        /// <summary>
        /// Prints the minimal command-line usage and high-level output behavior.
        /// </summary>
        private static void PrintUsage()
        {
            Console.WriteLine("MCToCMZWorldConverter config.json");
            Console.WriteLine();
            Console.WriteLine("This converts rendered Minecraft Java Anvil chunks into CMZ native chunk .dat files.");
            Console.WriteLine("By default this creates a complete CMZ world folder with a random UUID under OutputCmzWorldFolder.");
            Console.WriteLine("Set Cmz.CreateWorldFolderWithRandomUuid=false to write into an existing CMZ world folder.");
        }
        #endregion
    }

    #region Conversion Statistics

    /// <summary>
    /// Tracks conversion counters used for progress and final reporting.
    /// </summary>
    internal sealed class ConversionStats
    {
        public int  ChunksConverted;
        public int  MissingChunks;
        public int  ReadErrors;
        public long AirBlocksWritten;
        public long AirBlocksSkipped;
        public long BedrockForced;
    }
    #endregion
}
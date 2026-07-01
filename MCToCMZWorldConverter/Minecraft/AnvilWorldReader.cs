/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2026 RussDev7
This file is part of https://github.com/RussDev7/MCToCMZWorldConverter - see LICENSE for details.
*/

using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using System;
using fNbt;

namespace MCToCMZWorldConverter.Minecraft
{
    #region Anvil World Reader

    /// <summary>
    /// Indexes Minecraft Anvil <c>.mca</c> region files and reads chunk NBT from them.
    /// </summary>
    /// <remarks>
    /// This class does not decide which dimension path to use. The config layer resolves
    /// the final region folder first, then passes that folder into this reader.
    /// </remarks>
    public sealed class AnvilWorldReader
    {
        #region Constants / Static Fields

        // Matches Minecraft Anvil region filenames such as r.0.0.mca or r.-1.2.mca.
        private static readonly Regex RegionNameRegex = new Regex(@"^r\.(-?\d+)\.(-?\d+)\.mca$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        #endregion

        #region Fields

        private readonly string _worldFolder;
        private readonly string _regionFolder;
        private readonly Dictionary<RegionKey, RegionFileInfo> _regions = new Dictionary<RegionKey, RegionFileInfo>();
        private readonly List<string> _skippedRegionFiles = new List<string>();

        #endregion

        #region Properties

        /// <summary>
        /// Full path to the Minecraft world root folder.
        /// </summary>
        public string WorldFolder => _worldFolder;

        /// <summary>
        /// Full path to the Minecraft region folder currently being converted.
        /// </summary>
        public string RegionFolder => _regionFolder;

        /// <summary>
        /// Number of discovered <c>.mca</c> region files in <see cref="RegionFolder" />.
        /// </summary>
        public int RegionFileCount => _regions.Count;

        /// <summary>
        /// Number of discovered region files that were ignored because they cannot contain
        /// a valid Anvil header.
        /// </summary>
        public int SkippedRegionFileCount => _skippedRegionFiles.Count;

        /// <summary>
        /// Human-readable warnings for ignored region files.
        /// </summary>
        public IEnumerable<string> SkippedRegionFiles => _skippedRegionFiles;

        #endregion

        #region Construction

        /// <summary>
        /// Creates a reader for a Minecraft world and a specific region folder.
        /// </summary>
        /// <param name="worldFolder">Minecraft Java world root folder.</param>
        /// <param name="regionFolder">Resolved region folder containing <c>r.*.*.mca</c> files.</param>
        public AnvilWorldReader(string worldFolder, string regionFolder)
        {
            if (string.IsNullOrWhiteSpace(worldFolder))
                throw new ArgumentException("World folder is required.", nameof(worldFolder));
            if (string.IsNullOrWhiteSpace(regionFolder))
                throw new ArgumentException("Region folder is required.", nameof(regionFolder));

            _worldFolder = Path.GetFullPath(worldFolder);
            _regionFolder = Path.GetFullPath(regionFolder);

            if (!Directory.Exists(_regionFolder))
                throw new DirectoryNotFoundException("Minecraft region folder was not found: " + _regionFolder);

            IndexRegions();
        }
        #endregion

        #region Chunk Enumeration

        /// <summary>
        /// Returns every chunk that exists in every indexed region file.
        /// </summary>
        /// <remarks>
        /// This is used by <c>ConvertAllRenderedChunks=true</c>. It queries each region file's
        /// location table so only actually-rendered chunks are converted.
        /// </remarks>
        public IEnumerable<ChunkLocation> GetRenderedChunks()
        {
            var result = new HashSet<ChunkLocation>();

            foreach (RegionFileInfo info in _regions.Values)
            {
                using (var region = new RegionFileReader(info.Path, info.RegionX, info.RegionZ))
                {
                    foreach (ChunkLocation chunk in region.ExistingChunks())
                        result.Add(chunk);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns chunk coordinates inside a manual chunk-range selection.
        /// </summary>
        /// <remarks>
        /// A coordinate is returned only when its containing region file exists. The chunk may
        /// still be missing inside that region file; <see cref="ReadChunk" /> handles that later.
        /// </remarks>
        public IEnumerable<ChunkLocation> GetChunksInBounds(int minChunkX, int maxChunkX, int minChunkZ, int maxChunkZ)
        {
            for (int chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
            {
                for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
                {
                    RegionKey key = new RegionKey(FloorDiv(chunkX, 32), FloorDiv(chunkZ, 32));
                    if (_regions.ContainsKey(key))
                        yield return new ChunkLocation(chunkX, chunkZ);
                }
            }
        }
        #endregion

        #region Chunk Loading

        /// <summary>
        /// Reads and decodes a single Minecraft chunk into the converter's chunk model.
        /// </summary>
        /// <returns>
        /// A <see cref="MinecraftChunk" /> when the chunk exists; otherwise <c>null</c>.
        /// </returns>
        public MinecraftChunk ReadChunk(ChunkLocation location)
        {
            RegionKey key = new RegionKey(FloorDiv(location.ChunkX, 32), FloorDiv(location.ChunkZ, 32));
            if (!_regions.TryGetValue(key, out RegionFileInfo info))
                return null;

            using (var region = new RegionFileReader(info.Path, info.RegionX, info.RegionZ))
            {
                NbtCompound root = region.ReadChunkRoot(location.ChunkX, location.ChunkZ);
                if (root == null)
                    return null;

                return MinecraftChunk.FromNbt(location.ChunkX, location.ChunkZ, root);
            }
        }
        #endregion

        #region Region Indexing

        /// <summary>
        /// Scans the selected region folder and records available Anvil region files.
        /// </summary>
        private void IndexRegions()
        {
            foreach (string file in Directory.GetFiles(_regionFolder, "r.*.*.mca", SearchOption.TopDirectoryOnly))
            {
                Match match = RegionNameRegex.Match(Path.GetFileName(file));
                if (!match.Success)
                    continue;

                int rx = int.Parse(match.Groups[1].Value);
                int rz = int.Parse(match.Groups[2].Value);

                long length = new FileInfo(file).Length;
                if (length < RegionFileReader.HeaderBytes)
                {
                    _skippedRegionFiles.Add($"Skipped Minecraft region file with invalid Anvil header ({length} bytes): {file}");
                    continue;
                }

                _regions[new RegionKey(rx, rz)] = new RegionFileInfo(file, rx, rz);
            }
        }
        #endregion

        #region Math Helpers

        /// <summary>
        /// Performs mathematical floor division for both positive and negative coordinates.
        /// </summary>
        /// <remarks>
        /// Minecraft region coordinates are chunk coordinates divided by 32 and floored.
        /// C# integer division truncates toward zero, so negative coordinates need this helper.
        /// </remarks>
        private static int FloorDiv(int value, int divisor)
        {
            int result = value / divisor;
            int remainder = value % divisor;
            if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
                result--;
            return result;
        }
        #endregion
    }
    #endregion

    #region Region Lookup Types

    /// <summary>
    /// Dictionary key for a Minecraft Anvil region coordinate pair.
    /// </summary>
    internal readonly struct RegionKey : IEquatable<RegionKey>
    {
        #region Fields

        private readonly int _x;
        private readonly int _z;

        #endregion

        #region Construction

        public RegionKey(int x, int z)
        {
            _x = x;
            _z = z;
        }
        #endregion

        #region Equality

        public bool Equals(RegionKey other) => _x == other._x && _z == other._z;
        public override bool Equals(object obj) => obj is RegionKey other && Equals(other);
        public override int GetHashCode() => (_x * 397) ^ _z;

        #endregion
    }

    /// <summary>
    /// File-system metadata for an indexed Minecraft Anvil region file.
    /// </summary>
    internal sealed class RegionFileInfo
    {
        #region Fields

        public readonly string Path;
        public readonly int RegionX;
        public readonly int RegionZ;

        #endregion

        #region Construction

        public RegionFileInfo(string path, int regionX, int regionZ)
        {
            Path = path;
            RegionX = regionX;
            RegionZ = regionZ;
        }
        #endregion
    }
    #endregion
}

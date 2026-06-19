/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2026 RussDev7
This file is part of https://github.com/RussDev7/MCToCMZWorldConverter - see LICENSE for details.
*/

using System.Collections.Generic;
using System.IO;
using System;

namespace MCToCMZWorldConverter.CastleMinerZ
{
    /// <summary>
    /// Collects converted CMZ block deltas by chunk and writes them into CastleMiner Z chunk files.
    /// </summary>
    /// <remarks>
    /// CMZ chunk files are named from their world-space chunk corner, for example:
    /// X0Y-64Z0.dat, X16Y-64Z0.dat, X-16Y-64Z32.dat.
    ///
    /// Stock CastleMiner Z loads chunk .dat files through SaveDevice.Load(...), so generated chunk
    /// files usually need to be RTSD-wrapped with the same save-device key used for world.info.
    /// </remarks>
    public sealed class CmzWorldWriter
    {
        #region Fields

        private readonly string _worldFolder;
        private readonly byte[] _saveDeviceKey;
        private readonly bool _wrapChunkFiles;
        private readonly Dictionary<ChunkKey, CmzChunkDelta> _chunks = new Dictionary<ChunkKey, CmzChunkDelta>();
        private readonly HashSet<ChunkKey> _writtenChunks = new HashSet<ChunkKey>();

        #endregion

        #region Construction

        public CmzWorldWriter(string worldFolder, byte[] saveDeviceKey, bool wrapChunkFiles)
        {
            _worldFolder = worldFolder;
            _saveDeviceKey = saveDeviceKey;
            _wrapChunkFiles = wrapChunkFiles;
        }
        #endregion

        #region Properties

        public int ChunkCount => _chunks.Count;
        public int ChunksWritten => _writtenChunks.Count;
        public long BlocksQueued { get; private set; }
        public long BlocksSaved { get; private set; }

        #endregion

        #region Block Queuing

        /// <summary>
        /// Queues a block write into the correct CMZ chunk delta.
        /// </summary>
        /// <remarks>
        /// CMZ converted world chunks currently store the vertical range Y=-64 through Y=63.
        /// Blocks outside that range are ignored here before chunk routing.
        /// </remarks>
        public void SetBlock(int worldX, int worldY, int worldZ, CmzBlockType blockType)
        {
            if (worldY < -64 || worldY > 63)
                return;

            int chunkX = FloorToChunkCorner(worldX);
            int chunkZ = FloorToChunkCorner(worldZ);
            var key = new ChunkKey(chunkX, chunkZ);

            if (!_chunks.TryGetValue(key, out CmzChunkDelta chunk))
            {
                chunk = new CmzChunkDelta(chunkX, chunkZ);
                _chunks.Add(key, chunk);
            }

            if (chunk.SetBlock(worldX, worldY, worldZ, blockType))
                BlocksQueued++;
        }
        #endregion

        #region Saving

        /// <summary>
        /// Saves every currently queued CMZ chunk delta to disk.
        /// </summary>
        public void SaveAll()
        {
            Directory.CreateDirectory(_worldFolder);

            foreach (CmzChunkDelta chunk in _chunks.Values)
            {
                chunk.Save(_worldFolder, _saveDeviceKey, _wrapChunkFiles);
                _writtenChunks.Add(chunk.Key);
                BlocksSaved += chunk.EntryCount;
            }
        }

        /// <summary>
        /// Saves every queued CMZ chunk delta and clears the in-memory queue.
        /// </summary>
        /// <remarks>
        /// This is used by streaming conversion to avoid holding an entire Minecraft world in RAM.
        /// </remarks>
        public void SaveAllAndClear()
        {
            SaveAll();
            _chunks.Clear();
        }

        /// <summary>
        /// Removes generated CMZ chunk files from a world folder without touching world.info or other save files.
        /// </summary>
        public static void ClearExistingChunkFiles(string worldFolder)
        {
            if (!Directory.Exists(worldFolder))
                return;

            foreach (string file in Directory.GetFiles(worldFolder, "X*Y-64Z*.dat", SearchOption.TopDirectoryOnly))
            {
                File.Delete(file);
            }
        }
        #endregion

        #region Chunk Math

        /// <summary>
        /// Floors any world coordinate to the matching 16x16 CMZ chunk corner.
        /// </summary>
        /// <remarks>
        /// This intentionally uses Math.Floor so negative coordinates route to the correct negative chunk.
        /// Example: -1 floors to -16, not 0.
        /// </remarks>
        public static int FloorToChunkCorner(int value)
        {
            return (int)Math.Floor(value / 16.0) * 16;
        }
        #endregion
    }

    /// <summary>
    /// Represents the modified block entries for one CMZ chunk file.
    /// </summary>
    /// <remarks>
    /// The converter writes modern CMZ delta chunks using:
    ///
    /// uint magic = 3203334144
    /// int entryCount
    /// int deltaEntry[entryCount]
    ///
    /// Each delta entry packs block type and local x/y/z into one 32-bit value.
    /// </remarks>
    internal sealed class CmzChunkDelta
    {
        #region Constants

        private const uint ModernChunkMagic = 3203334144U;

        #endregion

        #region Fields

        private readonly int _chunkX;
        private readonly int _chunkZ;
        private readonly Dictionary<int, int> _entriesByLocation = new Dictionary<int, int>();

        #endregion

        #region Construction

        public CmzChunkDelta(int chunkX, int chunkZ)
        {
            _chunkX = chunkX;
            _chunkZ = chunkZ;
            Key = new ChunkKey(chunkX, chunkZ);
        }

        #endregion

        #region Properties

        public ChunkKey Key { get; }
        public int EntryCount => _entriesByLocation.Count;

        #endregion

        #region Block Entries

        /// <summary>
        /// Adds or replaces a single block delta inside this CMZ chunk.
        /// </summary>
        /// <remarks>
        /// Location bits mirror CMZ's DeltaEntry layout:
        /// local X uses bits 16..19, local Y uses bits 8..14, and local Z uses bits 0..3.
        /// Block type is stored in bits 24..31.
        /// </remarks>
        public bool SetBlock(int worldX, int worldY, int worldZ, CmzBlockType blockType)
        {
            int localX = worldX - _chunkX;
            int localY = worldY + 64;
            int localZ = worldZ - _chunkZ;

            if (localX < 0 || localX > 15)
                throw new ArgumentOutOfRangeException(nameof(worldX));
            if (localY < 0 || localY > 127)
                throw new ArgumentOutOfRangeException(nameof(worldY));
            if (localZ < 0 || localZ > 15)
                throw new ArgumentOutOfRangeException(nameof(worldZ));

            int location =
                ((localX & 15) << 16) |
                ((localY & 127) << 8) |
                (localZ & 15);

            int entry = ((int)blockType << 24) | location;
            bool isNew = !_entriesByLocation.ContainsKey(location);
            _entriesByLocation[location] = entry;
            return isNew;
        }
        #endregion

        #region Saving

        /// <summary>
        /// Writes this CMZ chunk delta to disk, optionally wrapped through the CMZ SaveDevice RTSD format.
        /// </summary>
        public void Save(string worldFolder, byte[] saveDeviceKey, bool wrapChunkFiles)
        {
            if (_entriesByLocation.Count == 0)
                return;

            string path = Path.Combine(worldFolder, $"X{_chunkX}Y-64Z{_chunkZ}.dat");

            byte[] rawPayload;
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                writer.Write(ModernChunkMagic);
                writer.Write(_entriesByLocation.Count);

                foreach (int entry in _entriesByLocation.Values)
                    writer.Write(entry);

                writer.Flush();
                rawPayload = ms.ToArray();
            }

            byte[] fileBytes = rawPayload;
            if (wrapChunkFiles)
            {
                if (saveDeviceKey == null || saveDeviceKey.Length == 0)
                    throw new InvalidOperationException("Cannot SaveDevice-wrap CMZ chunk files because the SaveDevice key was not resolved.");

                // CachedChunk.Save() writes chunks through CastleMinerZGame.Instance.SaveDevice.Save(fname, true, true, ...).
                // That means every X..Y..Z..dat file must be RTSD-wrapped, compressed, encrypted,
                // and MD5-checked exactly like world.info. Plain chunk payloads trigger
                // DNA.IO.Storage.SaveDevice.LoadData exceptions when CMZ streams chunks in.
                fileBytes = CmzWorldFolderBuilder.BuildSaveDeviceFile(rawPayload, true, true, saveDeviceKey);
            }

            File.WriteAllBytes(path, fileBytes);
        }
        #endregion
    }

    /// <summary>
    /// Dictionary key for identifying a CMZ chunk by its X/Z world-space chunk corner.
    /// </summary>
    internal readonly struct ChunkKey : IEquatable<ChunkKey>
    {
        #region Fields

        private readonly int _x;
        private readonly int _z;

        #endregion

        #region Construction

        public ChunkKey(int x, int z)
        {
            _x = x;
            _z = z;
        }
        #endregion

        #region Equality

        public bool Equals(ChunkKey other) => _x == other._x && _z == other._z;
        public override bool Equals(object obj) => obj is ChunkKey other && Equals(other);
        public override int GetHashCode() => (_x * 397) ^ _z;

        #endregion
    }
}
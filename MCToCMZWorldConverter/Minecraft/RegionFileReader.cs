/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2026 RussDev7
This file is part of https://github.com/RussDev7/MCToCMZWorldConverter - see LICENSE for details.
*/

using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System;
using fNbt;

namespace MCToCMZWorldConverter.Minecraft
{
    /// <summary>
    /// Reads Minecraft Java Anvil region files and returns decompressed chunk NBT roots.
    /// </summary>
    /// <remarks>
    /// Region files are named like <c>r.x.z.mca</c>. Each file contains a 32x32 grid
    /// of chunks and begins with an 8 KiB header. The first 4 KiB stores chunk sector
    /// offsets/counts, and the second 4 KiB stores timestamps that this converter does
    /// not currently need.
    ///
    /// Notes:
    /// - Chunk NBT is returned after gzip/zlib/uncompressed payload decoding.
    /// - LZ4-compressed chunks are detected but intentionally rejected until an LZ4 dependency is added.
    /// - External <c>.mcc</c> payloads are supported when the external-stream flag is present.
    /// </remarks>
    public sealed class RegionFileReader : IDisposable
    {
        #region Constants

        /// <summary>
        /// Minecraft Anvil region sectors are 4096 bytes each.
        /// </summary>
        private const int SectorBytes = 4096;

        /// <summary>
        /// High bit on the compression byte indicating the chunk payload is stored externally.
        /// </summary>
        private const byte ExternalStreamFlag = 0x80;

        #endregion

        #region Fields

        private readonly FileStream _stream;
        private readonly int[] _sectorOffsets = new int[1024];
        private readonly int[] _sectorCounts = new int[1024];

        #endregion

        #region Properties

        /// <summary>
        /// Gets the full path to the opened region file.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the region X coordinate from the <c>r.x.z.mca</c> filename.
        /// </summary>
        public int RegionX { get; }

        /// <summary>
        /// Gets the region Z coordinate from the <c>r.x.z.mca</c> filename.
        /// </summary>
        public int RegionZ { get; }

        #endregion

        #region Construction

        /// <summary>
        /// Opens a Minecraft Anvil region file and reads its chunk location table.
        /// </summary>
        public RegionFileReader(string path, int regionX, int regionZ)
        {
            Path = path;
            RegionX = regionX;
            RegionZ = regionZ;
            _stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            if (_stream.Length < 8192)
                throw new InvalidDataException("Region file is too small: " + path);

            ReadHeader();
        }
        #endregion

        #region Chunk Enumeration

        /// <summary>
        /// Enumerates chunk coordinates that have a valid sector offset/count in this region file.
        /// </summary>
        public IEnumerable<ChunkLocation> ExistingChunks()
        {
            for (int localZ = 0; localZ < 32; localZ++)
            {
                for (int localX = 0; localX < 32; localX++)
                {
                    int index = GetIndex(localX, localZ);
                    if (_sectorOffsets[index] <= 0 || _sectorCounts[index] <= 0)
                        continue;

                    yield return new ChunkLocation((RegionX * 32) + localX, (RegionZ * 32) + localZ);
                }
            }
        }
        #endregion

        #region Chunk Loading

        /// <summary>
        /// Reads and decompresses a chunk's root NBT compound.
        /// </summary>
        /// <remarks>
        /// Returns <c>null</c> when the chunk has no sector entry or contains an empty payload.
        /// Read failures are wrapped with region/chunk diagnostics so the caller can either
        /// stop conversion or skip the bad chunk based on config.
        /// </remarks>
        public NbtCompound ReadChunkRoot(int chunkX, int chunkZ)
        {
            int localX = PositiveModulo(chunkX, 32);
            int localZ = PositiveModulo(chunkZ, 32);
            int index = GetIndex(localX, localZ);

            int sectorOffset = _sectorOffsets[index];
            int sectorCount = _sectorCounts[index];
            if (sectorOffset <= 0 || sectorCount <= 0)
                return null;

            int length = 0;
            byte compressionType = 0;
            bool externalStream = false;

            try
            {
                _stream.Position = sectorOffset * (long)SectorBytes;

                byte[] payload;

                using (var reader = new BigEndianBinaryReader(_stream))
                {
                    length = reader.ReadInt32();
                    compressionType = reader.ReadByte();

                    if (length <= 1)
                        return null;

                    int maxLength = sectorCount * SectorBytes;
                    if (length > maxLength)
                        throw new InvalidDataException($"Chunk length {length} exceeds allocated sectors ({maxLength} bytes) in {Path}.");

                    externalStream = (compressionType & ExternalStreamFlag) != 0;
                    byte actualCompressionType = (byte)(compressionType & ~ExternalStreamFlag);

                    if (externalStream)
                    {
                        compressionType = actualCompressionType;
                        payload = ReadExternalChunkPayload(chunkX, chunkZ);
                    }
                    else
                    {
                        compressionType = actualCompressionType;
                        payload = reader.ReadBytes(length - 1);
                    }
                }

                byte[] decompressed = DecodeChunkPayload(payload, compressionType);

                using (var nbtStream = new MemoryStream(decompressed, writable: false))
                {
                    var nbt = new NbtFile();
                    nbt.LoadFromStream(nbtStream, NbtCompression.None, null);
                    return nbt.RootTag;
                }
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException) && !(ex is StackOverflowException))
            {
                string source = externalStream ? "external .mcc stream" : "region stream";
                throw new InvalidDataException(
                    $"Could not read Minecraft chunk {chunkX},{chunkZ} from {Path}. " +
                    $"Local {localX},{localZ}, sectorOffset={sectorOffset}, sectorCount={sectorCount}, " +
                    $"length={length}, compressionType={compressionType}, source={source}.", ex);
            }
        }

        /// <summary>
        /// Reads an external Minecraft chunk payload from a sibling <c>c.x.z.mcc</c> file.
        /// </summary>
        /// <remarks>
        /// The region file still supplies the compression type; this method only loads the raw
        /// external payload bytes.
        /// </remarks>
        private byte[] ReadExternalChunkPayload(int chunkX, int chunkZ)
        {
            string regionFolder = System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
            string externalPath = System.IO.Path.Combine(regionFolder, $"c.{chunkX}.{chunkZ}.mcc");

            if (!File.Exists(externalPath))
                throw new FileNotFoundException("Minecraft region chunk points to an external .mcc file, but the file was not found.", externalPath);

            return File.ReadAllBytes(externalPath);
        }
        #endregion

        #region Region Header

        /// <summary>
        /// Reads the Anvil region location table from the first 4096 bytes of the file.
        /// </summary>
        /// <remarks>
        /// Each 4-byte entry stores:
        /// - 3 bytes: sector offset.
        /// - 1 byte: sector count.
        ///
        /// The timestamp table immediately after the location table is intentionally ignored.
        /// </remarks>
        private void ReadHeader()
        {
            _stream.Position = 0;

            using (var reader = new BigEndianBinaryReader(_stream))
            {
                for (int i = 0; i < 1024; i++)
                {
                    byte b0 = reader.ReadByte();
                    byte b1 = reader.ReadByte();
                    byte b2 = reader.ReadByte();
                    byte b3 = reader.ReadByte();

                    _sectorOffsets[i] = (b0 << 16) | (b1 << 8) | b2;
                    _sectorCounts[i] = b3;
                }
            }
        }
        #endregion

        #region Compression

        /// <summary>
        /// Decodes a Minecraft region chunk payload based on its compression type byte.
        /// </summary>
        private static byte[] DecodeChunkPayload(byte[] payload, byte compressionType)
        {
            switch (compressionType)
            {
                case 1: // gzip
                    using (var input = new MemoryStream(payload, writable: false))
                    using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                        return ReadAllBytes(gzip);

                case 2: // zlib in Minecraft's region format
                    return DecodeZlibPayload(payload);

                case 3: // uncompressed NBT
                    return payload;

                case 4:
                    throw new NotSupportedException(
                        "This Minecraft region chunk uses LZ4 compression. " +
                        "This converter build supports gzip, zlib, and uncompressed chunks. " +
                        "Open and re-save/export the world with standard zlib region compression, or add an LZ4 decoder dependency.");

                default:
                    throw new NotSupportedException("Unsupported Minecraft region chunk compression type: " + compressionType);
            }
        }

        /// <summary>
        /// Decodes a zlib/deflate region payload with compatibility fallback for .NET Framework behavior.
        /// </summary>
        /// <remarks>
        /// Some .NET Framework builds expect a zlib-wrapped stream here, while others expect
        /// raw deflate. This tries the normal path first, then falls back to skipping the
        /// 2-byte zlib header and 4-byte Adler32 trailer.
        /// </remarks>
        private static byte[] DecodeZlibPayload(byte[] payload)
        {
            Exception firstFailure;

            // Some .NET Framework builds expect a zlib-wrapped stream here, while others expect raw deflate.
            // Try the normal path first, then fall back to skipping the 2-byte zlib header and 4-byte Adler32 trailer.
            try
            {
                return InflateDeflateStream(payload, 0, payload.Length);
            }
            catch (Exception ex) when (IsCompressionFailure(ex))
            {
                firstFailure = ex;
            }

            if (payload.Length > 6)
            {
                try
                {
                    return InflateDeflateStream(payload, 2, payload.Length - 6);
                }
                catch (Exception ex) when (IsCompressionFailure(ex))
                {
                    throw new InvalidDataException(
                        "Zlib chunk payload could not be decompressed as either wrapped zlib or raw deflate.", ex);
                }
            }

            throw new InvalidDataException("Zlib chunk payload was too short to decompress.", firstFailure);
        }

        /// <summary>
        /// Inflates a payload slice using <see cref="DeflateStream"/>.
        /// </summary>
        private static byte[] InflateDeflateStream(byte[] payload, int offset, int count)
        {
            using (var input = new MemoryStream(payload, offset, count, writable: false))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                return ReadAllBytes(deflate);
        }

        /// <summary>
        /// Returns whether an exception should be treated as a compression/decompression failure.
        /// </summary>
        private static bool IsCompressionFailure(Exception ex)
        {
            return ex is InvalidDataException || ex is IOException;
        }
        #endregion

        #region Utility Helpers

        /// <summary>
        /// Copies all bytes from a stream into a byte array.
        /// </summary>
        private static byte[] ReadAllBytes(Stream stream)
        {
            using (var output = new MemoryStream())
            {
                stream.CopyTo(output);
                return output.ToArray();
            }
        }

        /// <summary>
        /// Converts a local chunk coordinate pair into the 0..1023 Anvil header index.
        /// </summary>
        private static int GetIndex(int localX, int localZ) => localX + (localZ * 32);

        /// <summary>
        /// Computes a positive modulo result for negative chunk coordinates.
        /// </summary>
        private static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
        #endregion

        #region IDisposable

        /// <summary>
        /// Closes the underlying region file stream.
        /// </summary>
        public void Dispose()
        {
            _stream.Dispose();
        }
        #endregion
    }

    /// <summary>
    /// Identifies a Minecraft chunk by world chunk X/Z coordinates.
    /// </summary>
    /// <remarks>
    /// This is used for chunk enumeration, bounded conversion, and diagnostic output.
    /// </remarks>
    public readonly struct ChunkLocation : IEquatable<ChunkLocation>
    {
        #region Fields

        public readonly int ChunkX;
        public readonly int ChunkZ;

        #endregion

        #region Construction

        public ChunkLocation(int chunkX, int chunkZ)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
        }
        #endregion

        #region Equality

        public bool Equals(ChunkLocation other) => ChunkX == other.ChunkX && ChunkZ == other.ChunkZ;
        public override bool Equals(object obj) => obj is ChunkLocation other && Equals(other);
        public override int GetHashCode() => (ChunkX * 397) ^ ChunkZ;
        public override string ToString() => ChunkX + "," + ChunkZ;

        #endregion
    }
}

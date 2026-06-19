/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2026 RussDev7
This file is part of https://github.com/RussDev7/MCToCMZWorldConverter - see LICENSE for details.
*/

using System.Collections.Generic;
using System;
using fNbt;

namespace MCToCMZWorldConverter.Minecraft
{
    /// <summary>
    /// Represents one 16-block-tall Minecraft Anvil chunk section.
    /// </summary>
    /// <remarks>
    /// This class decodes modern and legacy palette-based section data into normalized
    /// Minecraft block-state strings that the shared block map can resolve into CMZ blocks.
    ///
    /// Important:
    /// - This class does not convert blocks directly.
    /// - It only resolves Minecraft section-local coordinates to Minecraft block-state names.
    /// - Pre-flattening chunk formats are intentionally treated as air instead of failing conversion.
    /// </remarks>
    public sealed class MinecraftSection
    {
        #region Fields

        // Palette entries are normalized names such as:
        // minecraft:stone
        // minecraft:oak_log[axis=y]
        private readonly string[] _palette;

        // Raw packed palette indices from the Anvil section.
        private readonly long[] _data;

        // Number of bits needed to store a palette index. Minecraft uses at least 4 bits.
        private readonly int _bitsPerBlock;

        // Modern Java and some older Anvil versions pack block-state indices differently.
        private readonly PackedBlockStateFormat _packedFormat;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the section Y coordinate, where each section represents 16 Minecraft Y levels.
        /// </summary>
        /// <remarks>
        /// Example:
        /// SectionY 0 covers Minecraft Y 0..15.
        /// SectionY 4 covers Minecraft Y 64..79.
        /// Negative section values are possible in modern Minecraft worlds.
        /// </remarks>
        public int SectionY { get; }

        #endregion

        #region Construction

        /// <summary>
        /// Creates a decoded section wrapper from palette and block-state data.
        /// </summary>
        private MinecraftSection(int sectionY, string[] palette, long[] data)
        {
            SectionY = sectionY;
            _palette = palette ?? new[] { "minecraft:air" };
            _data = data ?? Array.Empty<long>();
            _bitsPerBlock = Math.Max(4, RequiredBits(_palette.Length - 1));
            _packedFormat = DetectPackedFormat(_data.Length, _bitsPerBlock);
        }
        #endregion

        #region NBT Loading

        /// <summary>
        /// Builds a <see cref="MinecraftSection"/> from a Minecraft Anvil section NBT compound.
        /// </summary>
        /// <remarks>
        /// Supported layouts:
        /// - Modern layout: section/block_states/palette + section/block_states/data.
        /// - Older palette layout: section/Palette + section/BlockStates.
        ///
        /// Unsupported pre-flattening layouts are returned as air-only sections so one old or
        /// unknown section does not stop an entire world conversion.
        /// </remarks>
        public static MinecraftSection FromNbt(NbtCompound section)
        {
            int sectionY = ReadSectionY(section);

            #region Modern 1.18+ block_states layout

            NbtCompound blockStates = section.Get<NbtCompound>("block_states");
            if (blockStates != null)
            {
                string[] palette = ReadModernPalette(blockStates.Get<NbtList>("palette"));
                long[] data = blockStates.Get<NbtLongArray>("data")?.Value;
                return new MinecraftSection(sectionY, palette, data);
            }
            #endregion

            #region Legacy 1.13-1.17 palette layout

            // 1.13 through 1.17-ish layout.
            NbtList oldPaletteTag = section.Get<NbtList>("Palette");
            if (oldPaletteTag != null)
            {
                string[] palette = ReadModernPalette(oldPaletteTag);
                long[] data = section.Get<NbtLongArray>("BlockStates")?.Value;
                return new MinecraftSection(sectionY, palette, data);
            }
            #endregion

            #region Unsupported section layouts

            // Older pre-flattening chunks are intentionally not decoded here.
            // Treat unknown section layouts as air instead of failing the entire conversion.
            return new MinecraftSection(sectionY, new[] { "minecraft:air" }, null);

            #endregion
        }
        #endregion

        #region Block Lookup

        /// <summary>
        /// Resolves a local section block coordinate into a Minecraft block-state string.
        /// </summary>
        /// <remarks>
        /// The caller passes section-local coordinates:
        /// - localX: 0..15
        /// - localY: 0..15
        /// - localZ: 0..15
        ///
        /// If the palette is missing, empty, or out of range, this safely returns minecraft:air.
        /// </remarks>
        public string GetBlockState(int localX, int localY, int localZ)
        {
            if (_palette.Length == 0)
                return "minecraft:air";

            if (_palette.Length == 1 || _data.Length == 0)
                return _palette[0];

            int index = (localY * 256) + (localZ * 16) + localX;
            int paletteIndex = ReadPackedValue(_data, index, _bitsPerBlock);

            if (paletteIndex < 0 || paletteIndex >= _palette.Length)
                return "minecraft:air";

            return _palette[paletteIndex];
        }
        #endregion

        #region Palette Decoding

        /// <summary>
        /// Reads a Minecraft palette list into normalized block-state strings.
        /// </summary>
        /// <remarks>
        /// Vanilla palette entries are stored as a block Name plus optional Properties compound.
        /// Properties are sorted before string formatting so block-map lookups are deterministic.
        /// </remarks>
        private static string[] ReadModernPalette(NbtList paletteTag)
        {
            if (paletteTag == null || paletteTag.Count == 0)
                return new[] { "minecraft:air" };

            var result = new List<string>();

            foreach (NbtTag tag in paletteTag)
            {
                if (!(tag is NbtCompound compound))
                    continue;

                string name = compound.Get<NbtString>("Name")?.Value ?? "minecraft:air";
                NbtCompound properties = compound.Get<NbtCompound>("Properties");

                if (properties == null || properties.Count == 0)
                {
                    result.Add(name);
                    continue;
                }

                var parts = new List<string>();
                foreach (NbtTag property in properties)
                {
                    if (property is NbtString stringProperty)
                        parts.Add(property.Name + "=" + stringProperty.Value);
                }

                parts.Sort(StringComparer.Ordinal);
                result.Add(name + "[" + string.Join(",", parts) + "]");
            }

            return result.Count == 0 ? new[] { "minecraft:air" } : result.ToArray();
        }
        #endregion

        #region Section Metadata

        /// <summary>
        /// Reads the section Y value from NBT.
        /// </summary>
        /// <remarks>
        /// Different Minecraft versions and tooling may store the Y tag as byte, short, or int.
        /// </remarks>
        private static int ReadSectionY(NbtCompound section)
        {
            NbtTag y = section.Get("Y");
            if (y is NbtByte yByte)
                return yByte.Value;
            if (y is NbtShort yShort)
                return yShort.Value;
            if (y is NbtInt yInt)
                return yInt.Value;
            return 0;
        }
        #endregion

        #region Bit Math

        /// <summary>
        /// Returns the number of bits needed to store the supplied maximum palette index.
        /// </summary>
        private static int RequiredBits(int maxValue)
        {
            int bits = 0;
            do
            {
                bits++;
                maxValue >>= 1;
            }
            while (maxValue > 0);
            return bits;
        }

        /// <summary>
        /// Reads a palette index from packed block-state data using the detected packing format.
        /// </summary>
        private int ReadPackedValue(long[] values, int index, int bitsPerValue)
        {
            if (_packedFormat == PackedBlockStateFormat.PaddedLongs)
                return ReadPaddedLongPackedValue(values, index, bitsPerValue);

            return ReadCompactPackedValue(values, index, bitsPerValue);
        }
        #endregion

        #region Modern Padded-Long Packing

        // Modern Java Anvil paletted containers pack values into each 64-bit long without
        // allowing an individual palette index to cross a long boundary. For example,
        // 5-bit entries store 12 values per long and leave 4 unused bits at the top.
        // Reading those files as one continuous bitstream creates the repeated vertical
        // sheets/cubes seen in CMZ because every row becomes misaligned after the unused bits.
        private static int ReadPaddedLongPackedValue(long[] values, int index, int bitsPerValue)
        {
            int valuesPerLong = 64 / bitsPerValue;
            if (valuesPerLong <= 0)
                return 0;

            int longIndex = index / valuesPerLong;
            int valueIndex = index % valuesPerLong;
            int startBit = valueIndex * bitsPerValue;

            if (longIndex < 0 || longIndex >= values.Length)
                return 0;

            ulong mask = bitsPerValue == 64 ? ulong.MaxValue : ((1UL << bitsPerValue) - 1UL);
            return (int)((((ulong)values[longIndex]) >> startBit) & mask);
        }
        #endregion

        #region Legacy Compact-Bitstream Packing

        // Some older Anvil variants use a continuous bitstream where palette indices may
        // straddle a 64-bit long boundary. Keep this fallback so old worlds still decode.
        private static int ReadCompactPackedValue(long[] values, int index, int bitsPerValue)
        {
            int bitIndex = index * bitsPerValue;
            int longIndex = bitIndex >> 6;
            int startBit = bitIndex & 63;

            if (longIndex >= values.Length)
                return 0;

            ulong value = ((ulong)values[longIndex]) >> startBit;
            int bitsRead = 64 - startBit;

            if (bitsRead < bitsPerValue && longIndex + 1 < values.Length)
                value |= ((ulong)values[longIndex + 1]) << bitsRead;

            ulong mask = bitsPerValue == 64 ? ulong.MaxValue : ((1UL << bitsPerValue) - 1UL);
            return (int)(value & mask);
        }
        #endregion

        #region Packing-Format Detection

        /// <summary>
        /// Detects whether a section uses modern padded-long packing or older compact bitstream packing.
        /// </summary>
        /// <remarks>
        /// Modern padded-long packing leaves unused bits at the end of each 64-bit value.
        /// Older compact packing treats the entire long array like one continuous bitstream.
        /// </remarks>
        private static PackedBlockStateFormat DetectPackedFormat(int longCount, int bitsPerValue)
        {
            if (longCount <= 0)
                return PackedBlockStateFormat.PaddedLongs;

            int valuesPerLong = 64 / bitsPerValue;
            if (valuesPerLong <= 0)
                return PackedBlockStateFormat.CompactBitstream;

            int paddedLength = (4096 + valuesPerLong - 1) / valuesPerLong;
            int compactLength = ((4096 * bitsPerValue) + 63) / 64;

            if (longCount == paddedLength)
                return PackedBlockStateFormat.PaddedLongs;

            if (longCount == compactLength)
                return PackedBlockStateFormat.CompactBitstream;

            // Prefer the modern padded-long interpretation when unsure. It is the format
            // used by current Java worlds and avoids visible repeating bit-alignment artifacts.
            if (longCount >= paddedLength)
                return PackedBlockStateFormat.PaddedLongs;

            return PackedBlockStateFormat.CompactBitstream;
        }

        /// <summary>
        /// Identifies how palette indices are packed inside the section block-state long array.
        /// </summary>
        private enum PackedBlockStateFormat
        {
            PaddedLongs,
            CompactBitstream
        }
        #endregion
    }
}

/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2026 RussDev7
This file is part of https://github.com/RussDev7/MCToCMZWorldConverter - see LICENSE for details.
*/

using System.Collections.Generic;
using fNbt;

namespace MCToCMZWorldConverter.Minecraft
{
    #region Minecraft chunk model

    /// <summary>
    /// Represents one Minecraft Java chunk after its NBT data has been decoded into vertical sections.
    /// </summary>
    /// <remarks>
    /// Minecraft stores block data in 16-block-tall sections. This class keeps those sections keyed by
    /// their section Y coordinate and exposes block lookup by absolute Minecraft world Y plus local X/Z.
    /// Missing sections are treated as air so sparse or partially-generated chunks can still convert safely.
    /// </remarks>
    public sealed class MinecraftChunk
    {
        #region Fields

        /// <summary>
        /// Section lookup keyed by Minecraft section Y. Each section covers 16 vertical blocks.
        /// </summary>
        private readonly Dictionary<int, MinecraftSection> _sections = new Dictionary<int, MinecraftSection>();

        #endregion

        #region Properties

        /// <summary>
        /// Minecraft chunk X coordinate.
        /// </summary>
        public int ChunkX { get; }

        /// <summary>
        /// Minecraft chunk Z coordinate.
        /// </summary>
        public int ChunkZ { get; }

        #endregion

        #region Construction

        /// <summary>
        /// Creates a chunk wrapper for the supplied Minecraft chunk coordinate.
        /// </summary>
        private MinecraftChunk(int chunkX, int chunkZ)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
        }
        #endregion

        #region NBT loading

        /// <summary>
        /// Creates a <see cref="MinecraftChunk"/> from a decoded Anvil chunk NBT root.
        /// </summary>
        /// <remarks>
        /// Supports both older chunks where data is nested under a <c>Level</c> compound and newer chunks
        /// where the section list is stored directly on the root payload. Section keys are accepted as both
        /// lowercase <c>sections</c> and legacy uppercase <c>Sections</c>.
        /// </remarks>
        public static MinecraftChunk FromNbt(int chunkX, int chunkZ, NbtCompound root)
        {
            var chunk = new MinecraftChunk(chunkX, chunkZ);
            if (root == null)
                return chunk;

            NbtCompound data = root.Get<NbtCompound>("Level") ?? root;
            NbtList sections = data.Get<NbtList>("sections") ?? data.Get<NbtList>("Sections");

            if (sections != null)
            {
                foreach (NbtTag tag in sections)
                {
                    if (!(tag is NbtCompound sectionTag))
                        continue;

                    MinecraftSection section = MinecraftSection.FromNbt(sectionTag);
                    chunk._sections[section.SectionY] = section;
                }
            }

            return chunk;
        }
        #endregion

        #region Block lookup

        /// <summary>
        /// Gets the Minecraft block state at the supplied world Y and local chunk X/Z coordinate.
        /// </summary>
        /// <remarks>
        /// The caller supplies absolute Minecraft Y, but X/Z are local to the 16x16 chunk. Negative Y values
        /// are handled with floor division and positive modulo so modern below-zero Minecraft sections map
        /// to the correct section/local-Y pair.
        /// </remarks>
        public string GetBlockState(int worldY, int localX, int localZ)
        {
            int sectionY = FloorDiv(worldY, 16);
            int localY = PositiveModulo(worldY, 16);

            if (!_sections.TryGetValue(sectionY, out MinecraftSection section))
                return "minecraft:air";

            return section.GetBlockState(localX, localY, localZ);
        }
        #endregion

        #region Coordinate helpers

        /// <summary>
        /// Integer floor division helper used for converting world Y into Minecraft section Y.
        /// </summary>
        /// <remarks>
        /// C# integer division truncates toward zero, which is wrong for negative Minecraft coordinates.
        /// This helper floors instead, so Y=-1 divided by 16 becomes section -1 instead of 0.
        /// </remarks>
        private static int FloorDiv(int value, int divisor)
        {
            int result = value / divisor;
            int remainder = value % divisor;
            if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
                result--;
            return result;
        }

        /// <summary>
        /// Returns a modulo result in the positive range 0..divisor-1.
        /// </summary>
        /// <remarks>
        /// Used to convert absolute Minecraft Y into the local Y coordinate inside a 16-block section.
        /// </remarks>
        private static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
        #endregion
    }
    #endregion
}
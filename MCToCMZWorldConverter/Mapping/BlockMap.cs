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

namespace MCToCMZWorldConverter.Mapping
{
    #region Block Map

    /// <summary>
    /// Loads and resolves Minecraft block names/states into CastleMinerZ block types.
    /// </summary>
    /// <remarks>
    /// The block map allows Minecraft blocks to be converted into CMZ blocks through JSON.
    ///
    /// Example mappings:
    /// <code>
    /// minecraft:acacia_log -> Log
    /// minecraft:twisting_vines -> Leaves
    /// minecraft:air -> Empty
    /// </code>
    ///
    /// This class also normalizes lookup keys so mappings can be written in a more user-friendly way.
    /// For example:
    /// - "Acacia Log"
    /// - "acacia_log"
    /// - "minecraft:acacia_log"
    ///
    /// can all resolve to the same normalized key.
    /// </remarks>
    public sealed class BlockMap
    {
        #region Properties

        /// <summary>
        /// Fallback CastleMinerZ block type used when a Minecraft block has no explicit mapping.
        /// </summary>
        /// <remarks>
        /// Defaults to <see cref="CmzBlockType.Empty"/>.
        /// This means unmapped Minecraft blocks become air unless the JSON changes the default.
        /// </remarks>
        public CmzBlockType DefaultBlock { get; private set; } = CmzBlockType.Empty;

        #endregion

        #region Fields

        /// <summary>
        /// Normalized Minecraft block key to CastleMinerZ block type lookup table.
        /// </summary>
        /// <remarks>
        /// Uses case-insensitive keys so the JSON block map is easier to edit by hand.
        /// </remarks>
        private readonly Dictionary<string, CmzBlockType> _map =
            new Dictionary<string, CmzBlockType>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks Minecraft block names/states that were encountered but not found in the block map.
        /// </summary>
        /// <remarks>
        /// This is useful for generating an unmapped report after conversion.
        /// The converter can write this list to a file so missing mappings are easy to fill in later.
        /// </remarks>
        public readonly HashSet<string> UnmappedBlocks =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Load

        /// <summary>
        /// Loads a block map JSON file from disk.
        /// </summary>
        /// <param name="path">Path to the block map JSON file.</param>
        /// <returns>A loaded <see cref="BlockMap"/> instance.</returns>
        /// <remarks>
        /// Expected JSON shape:
        /// <code>
        /// {
        ///   "DefaultBlock": "Empty",
        ///   "Mappings": {
        ///     "minecraft:acacia_log": "Log",
        ///     "minecraft:twisting_vines": "Leaves"
        ///   }
        /// }
        /// </code>
        ///
        /// Notes:
        /// - Property names are case-insensitive.
        /// - Comments are allowed.
        /// - Trailing commas are allowed.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the JSON cannot be read into a valid block map or references an unknown CMZ block type.
        /// </exception>
        public static BlockMap Load(string path)
        {
            string json = File.ReadAllText(path);

            var dto = JsonSerializer.Deserialize<BlockMapDto>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                }) ?? throw new InvalidOperationException("Failed to read block map.");
            var result = new BlockMap();

            if (!string.IsNullOrWhiteSpace(dto.DefaultBlock))
            {
                result.DefaultBlock = ParseCmzBlock(dto.DefaultBlock);
            }

            if (dto.Mappings != null)
            {
                foreach (var pair in dto.Mappings)
                {
                    string key = NormalizeKey(pair.Key);
                    CmzBlockType value = ParseCmzBlock(pair.Value);

                    result._map[key] = value;
                }
            }

            return result;
        }
        #endregion

        #region Resolve

        /// <summary>
        /// Resolves a Minecraft block name or block state into a CastleMinerZ block type.
        /// </summary>
        /// <param name="minecraftBlockState">
        /// Minecraft block id or block state, such as <c>minecraft:oak_log[axis=y]</c>.
        /// </param>
        /// <returns>
        /// The mapped CastleMinerZ block type, or <see cref="DefaultBlock"/> if no mapping exists.
        /// </returns>
        /// <remarks>
        /// Resolution attempts multiple normalized lookup forms.
        ///
        /// Example input:
        /// <code>
        /// minecraft:oak_log[axis=y]
        /// </code>
        ///
        /// Lookup attempts:
        /// - oak_log[axis=y]
        /// - oak_log
        /// - oak_log
        ///
        /// If no mapping is found, the original Minecraft block state is added to
        /// <see cref="UnmappedBlocks"/> for reporting.
        /// </remarks>
        public CmzBlockType Resolve(string minecraftBlockState)
        {
            foreach (string key in BuildLookupKeys(minecraftBlockState))
            {
                if (_map.TryGetValue(key, out CmzBlockType mapped))
                    return mapped;
            }

            UnmappedBlocks.Add(minecraftBlockState);
            return DefaultBlock;
        }
        #endregion

        #region Lookup Key Helpers

        /// <summary>
        /// Builds the possible normalized lookup keys for a Minecraft block id or block state.
        /// </summary>
        /// <param name="raw">
        /// Raw Minecraft block id or block state from the schematic palette.
        /// </param>
        /// <returns>A sequence of normalized lookup keys to try against the block map.</returns>
        /// <remarks>
        /// This allows the block map to support both exact block-state mappings and broad base-block mappings.
        ///
        /// Example:
        /// <code>
        /// minecraft:torch[facing=east]
        /// </code>
        ///
        /// can resolve through:
        /// - exact state mapping
        /// - base block mapping
        /// - namespace-stripped mapping
        /// </remarks>
        private static IEnumerable<string> BuildLookupKeys(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            // Exact-ish normalized block state:
            // minecraft:oak_log[axis=y] -> oak_log[axis=y]
            yield return NormalizeKey(raw);

            // Base block without state:
            // minecraft:oak_log[axis=y] -> minecraft:oak_log
            int bracket = raw.IndexOf('[');
            string baseId = bracket >= 0 ? raw.Substring(0, bracket) : raw;

            yield return NormalizeKey(baseId);

            // No namespace:
            // minecraft:oak_log -> oak_log
            int colon = baseId.IndexOf(':');
            if (colon >= 0 && colon + 1 < baseId.Length)
                yield return NormalizeKey(baseId.Substring(colon + 1));
        }

        /// <summary>
        /// Normalizes a Minecraft block key for case-insensitive block map lookup.
        /// </summary>
        /// <param name="value">Raw block map key or Minecraft block id.</param>
        /// <returns>A normalized lookup key.</returns>
        /// <remarks>
        /// Normalization:
        /// - Trims whitespace.
        /// - Converts to lowercase.
        /// - Removes the <c>minecraft:</c> namespace.
        /// - Converts spaces to underscores.
        /// - Converts hyphens to underscores.
        ///
        /// Example:
        /// <code>
        /// Acacia Log -> acacia_log
        /// minecraft:acacia_log -> acacia_log
        /// </code>
        /// </remarks>
        private static string NormalizeKey(string value)
        {
            value = value.Trim().ToLowerInvariant();

            if (value.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("minecraft:".Length);

            value = value.Replace(' ', '_');
            value = value.Replace('-', '_');

            return value;
        }
        #endregion

        #region CMZ Block Parsing

        /// <summary>
        /// Parses a CastleMinerZ block type name from the block map JSON.
        /// </summary>
        /// <param name="value">The CMZ block type name from JSON.</param>
        /// <returns>The matching <see cref="CmzBlockType"/> value.</returns>
        /// <remarks>
        /// This is case-insensitive, so JSON values such as <c>Log</c>, <c>log</c>,
        /// and <c>LOG</c> can all resolve to <see cref="CmzBlockType.Log"/>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the supplied name does not exist in <see cref="CmzBlockType"/>.
        /// </exception>
        private static CmzBlockType ParseCmzBlock(string value)
        {
            if (Enum.TryParse(value, ignoreCase: true, out CmzBlockType block))
                return block;

            throw new InvalidOperationException($"Unknown CMZ block type in block map: '{value}'");
        }
        #endregion

        #region DTO

        /// <summary>
        /// JSON data transfer object used when reading the block map file.
        /// </summary>
        /// <remarks>
        /// This type mirrors the external JSON shape.
        /// It is kept private so the public API stays focused on <see cref="BlockMap"/>.
        /// </remarks>
        private sealed class BlockMapDto
        {
            #region Properties

            /// <summary>
            /// Optional fallback CMZ block type used when no mapping exists.
            /// </summary>
            public string DefaultBlock { get; set; }

            /// <summary>
            /// Minecraft block id/name/state to CMZ block type name mappings.
            /// </summary>
            public Dictionary<string, string> Mappings { get; set; }

            #endregion
        }
        #endregion
    }
    #endregion
}
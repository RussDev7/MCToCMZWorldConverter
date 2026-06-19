/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2026 RussDev7
This file is part of https://github.com/RussDev7/MCToCMZWorldConverter - see LICENSE for details.
*/

using System.Security.Cryptography;
using System.Collections.Generic;
using System.IO.Compression;
using System.Text;
using System.IO;
using System;

namespace MCToCMZWorldConverter.CastleMinerZ
{
    #region Created World Result

    /// <summary>
    /// Result object returned after the converter resolves/creates the output CMZ world folder.
    /// It carries the folder paths, generated IDs, seed, and SaveDevice key needed by chunk writing.
    /// </summary>
    public sealed class CmzCreatedWorld
    {
        public string FolderPath { get; set; }
        public Guid FolderId { get; set; }
        public Guid WorldId { get; set; }
        public int Seed { get; set; }
        public string WorldInfoPath { get { return Path.Combine(FolderPath, "world.info"); } }
        public string WorldsFolderPath { get; set; }
        public string SaveDeviceKeyDescription { get; set; }
        internal byte[] SaveDeviceKey { get; set; }
    }
    #endregion

    #region world.info Readback Summary

    /// <summary>
    /// Lightweight parsed view of a CMZ world.info file. Used for validation, duplicate WorldID
    /// detection, and debug reports after generating a converted world.
    /// </summary>
    public sealed class CmzWorldInfoSummary
    {
        public int Version;
        public int TerrainVersion;
        public string WorldName;
        public string OwnerGamerTag;
        public string CreatorGamerTag;
        public DateTime CreatedDate;
        public DateTime LastPlayedDate;
        public int Seed;
        public Guid WorldId;
        public float LastPositionX;
        public float LastPositionY;
        public float LastPositionZ;
        public int CrateCount;
        public int DoorCount;
        public int SpawnerCount;
        public bool InfiniteResourceMode;
        public string ServerMessage;
        public string ServerPassword;
        public int HellBossesSpawned;
        public int MaxHellBossSpawns;
    }
    #endregion

    #region CMZ World Folder + SaveDevice Builder

    /// <summary>
    /// Creates CMZ world folders, writes CMZ-compatible world.info files, and builds/reads
    /// DNA SaveDevice RTSD-wrapped payloads used by both world.info and chunk .dat files.
    /// </summary>
    public static class CmzWorldFolderBuilder
    {
        #region CMZ / DNA Format Constants

        // Matches DNA.CastleMinerZ.WorldInfo.Version / WorldInfoVersion.CurrentVersion.
        private const int WorldInfoVersion = 5;

        // Matches DNA.CastleMinerZ.WorldTypeIDs.CastleMinerZ.
        private const int TerrainVersionCastleMinerZ = 1;

        // Matches DNA.IO.Storage.SaveDevice file wrapper. Little-endian bytes are ASCII "RTSD".
        private const int SaveDeviceFileIdent = 1146311762;
        private const int SaveDeviceFileVersion = 5;
        private const uint SaveDeviceCompressed = 1U;
        private const uint SaveDeviceEncrypted = 2U;

        // DNA.IO.Storage.SaveDevice.CommonKey from CMZ/DNA.Common. Steam saves normally DO NOT use this;
        // Steam uses MD5(SteamUserID + "CMZ778") as FileSystemSaveDevice.LocalKey.
        private static readonly byte[] SaveDeviceCommonKey = new byte[]
        {
            236, 34, 252, 119, 2, 225, 246, 242, 214, 172,
            157, 191, 175, 246, 57, 246
        };
        #endregion

        #region Output Folder Preparation

        /// <summary>
        /// Resolves the target CMZ Worlds folder, resolves the correct SaveDevice key, creates a
        /// random UUID world folder when requested, writes world.info, and verifies it by reading it back.
        /// </summary>
        public static CmzCreatedWorld PrepareOutputWorldFolder(string configuredOutputPath, CmzConfig config)
        {
            string worldsFolder = ResolveWorldsFolder(configuredOutputPath, config);
            byte[] saveDeviceKey = ResolveSaveDeviceKey(worldsFolder, config, out string saveKeyDescription);

            if (!config.CreateWorldFolderWithRandomUuid)
            {
                Directory.CreateDirectory(configuredOutputPath);
                return new CmzCreatedWorld
                {
                    FolderPath = configuredOutputPath,
                    WorldsFolderPath = Path.GetDirectoryName(configuredOutputPath),
                    FolderId = Guid.Empty,
                    WorldId = Guid.Empty,
                    Seed = config.Seed,
                    SaveDeviceKeyDescription = saveKeyDescription,
                    SaveDeviceKey = saveDeviceKey
                };
            }

            Directory.CreateDirectory(worldsFolder);

            HashSet<Guid> existingWorldIds = ReadExistingWorldIds(worldsFolder, saveDeviceKey);

            Guid folderGuid;
            string worldFolder;
            do
            {
                folderGuid = Guid.NewGuid();
                worldFolder = Path.Combine(worldsFolder, folderGuid.ToString());
            }
            while (Directory.Exists(worldFolder));

            Directory.CreateDirectory(worldFolder);

            Guid worldId;
            do
            {
                worldId = Guid.NewGuid();
            }
            while (existingWorldIds.Contains(worldId));

            int seed = config.RandomSeed ? new Random().Next() : config.Seed;

            WriteWorldInfo(
                Path.Combine(worldFolder, "world.info"),
                saveDeviceKey,
                config.WorldName,
                config.OwnerGamerTag,
                config.CreatorGamerTag,
                seed,
                worldId,
                config.LastPositionX,
                config.LastPositionY,
                config.LastPositionZ,
                config.InfiniteResourceMode,
                config.ServerMessage,
                config.ServerPassword,
                config.HellBossesSpawned,
                config.MaxHellBossSpawns);

            CmzWorldInfoSummary summary = ReadWorldInfo(Path.Combine(worldFolder, "world.info"), saveDeviceKey);
            WriteDebugReport(worldFolder, worldsFolder, folderGuid, summary, config, saveKeyDescription);

            return new CmzCreatedWorld
            {
                FolderPath = worldFolder,
                WorldsFolderPath = worldsFolder,
                FolderId = folderGuid,
                WorldId = worldId,
                Seed = seed,
                SaveDeviceKeyDescription = saveKeyDescription,
                SaveDeviceKey = saveDeviceKey
            };
        }

        /// <summary>
        /// Resolves the configured output path into the actual CMZ Worlds folder that the game enumerates.
        /// </summary>
        private static string ResolveWorldsFolder(string configuredOutputPath, CmzConfig config)
        {
            string output = Path.GetFullPath(configuredOutputPath);

            if (config.OutputPathIsCastleMinerZSaveRoot)
            {
                output = Path.Combine(output, "Worlds");
            }

            if (config.RequireOutputFolderNamedWorlds)
            {
                string folderName = new DirectoryInfo(output).Name;
                if (!string.Equals(folderName, "Worlds", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "OutputCmzWorldFolder must point to CMZ's actual Worlds folder when Cmz.RequireOutputFolderNamedWorlds=true. " +
                        "Current value resolved to: " + output + Environment.NewLine +
                        "Expected the final folder name to be 'Worlds'. Example: C:/.../CastleMinerZ/Worlds");
                }
            }

            return output;
        }
        #endregion

        #region SaveDevice Key Resolution

        /// <summary>
        /// Resolves the encryption key used by DNA.IO.Storage.SaveDevice for this save root.
        /// Steam CMZ saves normally derive this from the numeric SteamID folder.
        /// </summary>
        private static byte[] ResolveSaveDeviceKey(string worldsFolder, CmzConfig config, out string description)
        {
            if (config.UseCommonSaveDeviceKey)
            {
                description = "CommonKey fallback (non-Steam/legacy only)";
                return SaveDeviceCommonKey;
            }

            string steamId;
            if (!string.IsNullOrWhiteSpace(config.SaveDeviceSteamId))
            {
                steamId = config.SaveDeviceSteamId.Trim();
                description = "explicit SteamID " + steamId + " from config";
                return MakeSteamSaveKey(steamId);
            }

            if (config.InferSaveDeviceSteamIdFromOutputPath)
            {
                steamId = InferSteamIdFromWorldsFolder(worldsFolder);
                if (!string.IsNullOrEmpty(steamId))
                {
                    description = "inferred SteamID " + steamId + " from Worlds folder parent";
                    return MakeSteamSaveKey(steamId);
                }
            }

            throw new InvalidOperationException(
                "Could not determine CMZ SaveDevice key. Steam CastleMiner Z saves use MD5(SteamID + \"CMZ778\"). " +
                "Your OutputCmzWorldFolder should usually look like: C:/Users/<you>/AppData/Local/CastleMinerZ/<SteamID>/Worlds. " +
                "Or set Cmz.SaveDeviceSteamId explicitly to the numeric SteamID folder name. " +
                "Only set Cmz.UseCommonSaveDeviceKey=true for old/non-Steam saves.");
        }

        /// <summary>
        /// Convenience fallback used by the public readback overload when only a world.info path is known.
        /// </summary>
        private static byte[] TryInferSaveDeviceKeyFromWorldInfoPath(string worldInfoPath)
        {
            try
            {
                string worldFolder = Path.GetDirectoryName(Path.GetFullPath(worldInfoPath));
                string worldsFolder = Path.GetDirectoryName(worldFolder);
                string steamId = InferSteamIdFromWorldsFolder(worldsFolder);
                if (!string.IsNullOrEmpty(steamId))
                    return MakeSteamSaveKey(steamId);
            }
            catch
            {
            }
            return SaveDeviceCommonKey;
        }

        /// <summary>
        /// Extracts the SteamID from the parent folder of CMZ's Worlds directory.
        /// Expected layout: CastleMinerZ/&lt;SteamID&gt;/Worlds.
        /// </summary>
        private static string InferSteamIdFromWorldsFolder(string worldsFolder)
        {
            if (string.IsNullOrWhiteSpace(worldsFolder))
                return null;

            DirectoryInfo worlds = new DirectoryInfo(Path.GetFullPath(worldsFolder));
            DirectoryInfo saveRoot = worlds.Parent;
            if (saveRoot == null)
                return null;

            string candidate = saveRoot.Name;
            if (ulong.TryParse(candidate, out _))
                return candidate;

            return null;
        }

        /// <summary>
        /// Creates the CMZ Steam SaveDevice key: MD5(SteamID + "CMZ778").
        /// </summary>
        private static byte[] MakeSteamSaveKey(string steamId)
        {
            using (MD5 md5 = MD5.Create())
                return md5.ComputeHash(Encoding.UTF8.GetBytes(steamId + "CMZ778"));
        }
        #endregion

        #region Existing World Detection

        /// <summary>
        /// Reads existing world.info files so generated worlds do not reuse a WorldID already present.
        /// Corrupt or non-CMZ folders are ignored, matching the game's forgiving world scan behavior.
        /// </summary>
        private static HashSet<Guid> ReadExistingWorldIds(string worldsFolder, byte[] saveDeviceKey)
        {
            HashSet<Guid> ids = new HashSet<Guid>();
            if (!Directory.Exists(worldsFolder))
                return ids;

            foreach (string dir in Directory.GetDirectories(worldsFolder))
            {
                string infoPath = Path.Combine(dir, "world.info");
                if (!File.Exists(infoPath))
                    continue;

                try
                {
                    ids.Add(ReadWorldInfo(infoPath, saveDeviceKey).WorldId);
                }
                catch
                {
                    // Ignore corrupt/non-CMZ folders here. The game will also ignore or list them as corrupt.
                }
            }

            return ids;
        }
        #endregion

        #region world.info Writing

        /// <summary>
        /// Builds raw WorldInfo bytes and writes them through the CMZ/DNA SaveDevice RTSD wrapper.
        /// </summary>
        private static void WriteWorldInfo(
            string path,
            byte[] saveDeviceKey,
            string worldName,
            string ownerGamerTag,
            string creatorGamerTag,
            int seed,
            Guid worldId,
            float lastPositionX,
            float lastPositionY,
            float lastPositionZ,
            bool infiniteResourceMode,
            string serverMessage,
            string serverPassword,
            int hellBossesSpawned,
            int maxHellBossSpawns)
        {
            byte[] rawWorldInfo = BuildRawWorldInfo(
                worldName,
                ownerGamerTag,
                creatorGamerTag,
                seed,
                worldId,
                lastPositionX,
                lastPositionY,
                lastPositionZ,
                infiniteResourceMode,
                serverMessage,
                serverPassword,
                hellBossesSpawned,
                maxHellBossSpawns);

            // Stock CMZ does not write world.info as the raw WorldInfo bytes. It writes it
            // through SaveDevice.Save(..., tamperProof: true, compressed: true), which creates
            // an RTSD wrapper, deflates the payload, encrypts it, and prepends an MD5 hash.
            // If world.info is left raw, CMZ marks the folder corrupt and never shows it.
            byte[] saveDeviceFile = BuildSaveDeviceFile(rawWorldInfo, true, true, saveDeviceKey);
            File.WriteAllBytes(path, saveDeviceFile);
        }

        /// <summary>
        /// Writes the raw WorldInfo version 5 payload before SaveDevice compression/encryption wrapping.
        /// Field order must match CMZ's WorldInfo serializer.
        /// </summary>
        private static byte[] BuildRawWorldInfo(
            string worldName,
            string ownerGamerTag,
            string creatorGamerTag,
            int seed,
            Guid worldId,
            float lastPositionX,
            float lastPositionY,
            float lastPositionZ,
            bool infiniteResourceMode,
            string serverMessage,
            string serverPassword,
            int hellBossesSpawned,
            int maxHellBossSpawns)
        {
            DateTime now = DateTime.Now;

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                writer.Write(WorldInfoVersion);
                writer.Write(TerrainVersionCastleMinerZ);
                writer.Write(worldName ?? "Converted Minecraft World");
                writer.Write(ownerGamerTag ?? "CMZConverter");
                writer.Write(creatorGamerTag ?? ownerGamerTag ?? "CMZConverter");
                writer.Write(now.Ticks);
                writer.Write(now.Ticks);
                writer.Write(seed);
                writer.Write(worldId.ToByteArray());

                // DNA.IO.BinaryWriterExtensions.Write(Vector3) writes X, Y, Z as Single values.
                writer.Write(lastPositionX);
                writer.Write(lastPositionY);
                writer.Write(lastPositionZ);

                // Empty metadata tables for crates, doors, and spawners.
                writer.Write(0); // Crates.Count
                writer.Write(0); // Doors.Count
                writer.Write(0); // Spawners.Count

                writer.Write(infiniteResourceMode);
                writer.Write(serverMessage ?? string.Empty);
                writer.Write(serverPassword ?? string.Empty);

                writer.Write(hellBossesSpawned);
                writer.Write(maxHellBossSpawns);
                writer.Flush();
                return ms.ToArray();
            }
        }
        #endregion

        #region SaveDevice RTSD File Building

        /// <summary>
        /// Builds a DNA SaveDevice file: RTSD header, version/options, optional deflate compression,
        /// optional AES encryption, and MD5 tamper-proof payload validation bytes.
        /// </summary>
        internal static byte[] BuildSaveDeviceFile(byte[] rawPayload, bool tamperProof, bool compressed, byte[] saveDeviceKey)
        {
            uint options = 0;
            byte[] data = rawPayload;

            if (compressed)
            {
                options |= SaveDeviceCompressed;
                data = CompressLikeSaveDevice(data);
            }

            if (tamperProof)
            {
                options |= SaveDeviceEncrypted;
                data = EncryptLikeSaveDevice(saveDeviceKey, data);

                byte[] hash;
                using (MD5 md5 = MD5.Create())
                    hash = md5.ComputeHash(data);

                using (MemoryStream hashedStream = new MemoryStream(data.Length + hash.Length + 8))
                using (BinaryWriter writer = new BinaryWriter(hashedStream))
                {
                    writer.Write(hash.Length);
                    writer.Write(hash);
                    writer.Write(data.Length);
                    writer.Write(data);
                    writer.Flush();
                    data = hashedStream.ToArray();
                }
            }

            using (MemoryStream outMs = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(outMs))
            {
                writer.Write(SaveDeviceFileIdent);
                writer.Write(SaveDeviceFileVersion);
                writer.Write(options);
                writer.Write(data.Length);
                writer.Write(data);
                writer.Flush();
                return outMs.ToArray();
            }
        }
        #endregion

        #region SaveDevice RTSD File Reading

        /// <summary>
        /// Reads either a raw WorldInfo payload or a SaveDevice RTSD-wrapped world.info file.
        /// Raw support is kept for diagnostics and older generated test files.
        /// </summary>
        private static byte[] LoadPossiblyWrappedWorldInfo(string path, byte[] saveDeviceKey)
        {
            byte[] file = File.ReadAllBytes(path);
            if (file.Length < 4)
                return file;

            using (MemoryStream ms = new MemoryStream(file))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                int ident = reader.ReadInt32();
                if (ident != SaveDeviceFileIdent)
                    return file;

                int version = reader.ReadInt32();
                if (version < 3 || version > SaveDeviceFileVersion)
                    throw new InvalidDataException("Unsupported SaveDevice file version: " + version);

                uint options = reader.ReadUInt32();
                int dataLength = reader.ReadInt32();
                byte[] data = reader.ReadBytes(dataLength);
                if (data.Length != dataLength)
                    throw new EndOfStreamException("Truncated SaveDevice file payload.");

                if ((options & SaveDeviceEncrypted) != 0)
                {
                    using (MemoryStream encMs = new MemoryStream(data))
                    using (BinaryReader encReader = new BinaryReader(encMs))
                    {
                        int hashLength = encReader.ReadInt32();
                        byte[] expectedHash = encReader.ReadBytes(hashLength);
                        int encryptedLength = encReader.ReadInt32();
                        byte[] encrypted = encReader.ReadBytes(encryptedLength);
                        if (encrypted.Length != encryptedLength)
                            throw new EndOfStreamException("Truncated encrypted SaveDevice payload.");

                        byte[] actualHash;
                        using (MD5 md5 = MD5.Create())
                            actualHash = md5.ComputeHash(encrypted);

                        if (!BytesEqual(expectedHash, actualHash))
                            throw new InvalidDataException("SaveDevice MD5 check failed.");

                        data = DecryptLikeSaveDevice(saveDeviceKey, encrypted);
                    }
                }

                if ((options & SaveDeviceCompressed) != 0)
                    data = DecompressLikeSaveDevice(data);

                return data;
            }
        }
        #endregion

        #region Compression Helpers

        /// <summary>
        /// Compresses payloads the same way SaveDevice does: deflate stream with the uncompressed length first.
        /// </summary>
        private static byte[] CompressLikeSaveDevice(byte[] data)
        {
            using (MemoryStream outMs = new MemoryStream())
            {
                using (DeflateStream ds = new DeflateStream(outMs, CompressionMode.Compress, true))
                using (BinaryWriter bw = new BinaryWriter(ds))
                {
                    bw.Write(data.Length);
                    bw.Write(data, 0, data.Length);
                    bw.Flush();
                }
                return outMs.ToArray();
            }
        }

        /// <summary>
        /// Reverses the SaveDevice deflate format and validates the expected uncompressed length.
        /// </summary>
        private static byte[] DecompressLikeSaveDevice(byte[] data)
        {
            using (MemoryStream inMs = new MemoryStream(data))
            using (DeflateStream ds = new DeflateStream(inMs, CompressionMode.Decompress, true))
            using (BinaryReader br = new BinaryReader(ds))
            {
                int len = br.ReadInt32();
                if (len < 0)
                    throw new InvalidDataException("Negative decompressed payload length.");
                byte[] payload = br.ReadBytes(len);
                if (payload.Length != len)
                    throw new EndOfStreamException("Truncated decompressed payload.");
                return payload;
            }
        }
        #endregion

        #region Encryption Helpers

        /// <summary>
        /// Prefixes the payload length, pads to an AES block boundary, then encrypts with AES-ECB/no-padding.
        /// </summary>
        private static byte[] EncryptLikeSaveDevice(byte[] key, byte[] data)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(data.Length);
                bw.Write(data);
                bw.Flush();
                return AesEcbNoPadding(key, PadToBlockMultiple(ms.ToArray()), true);
            }
        }

        /// <summary>
        /// Decrypts a SaveDevice encrypted payload and removes the length prefix added before encryption.
        /// </summary>
        private static byte[] DecryptLikeSaveDevice(byte[] key, byte[] cipher)
        {
            byte[] plain = AesEcbNoPadding(key, cipher, false);
            using (MemoryStream ms = new MemoryStream(plain))
            using (BinaryReader br = new BinaryReader(ms))
            {
                int len = br.ReadInt32();
                if (len < 0 || len > plain.Length - 4)
                    throw new InvalidDataException("Invalid encrypted payload length.");
                return br.ReadBytes(len);
            }
        }

        /// <summary>
        /// Shared AES primitive used to mirror the game's SaveDevice encryption mode.
        /// </summary>
        private static byte[] AesEcbNoPadding(byte[] key, byte[] input, bool encrypt)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                aes.IV = new byte[aes.BlockSize / 8];
                using (ICryptoTransform xform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor())
                    return xform.TransformFinalBlock(input, 0, input.Length);
            }
        }

        /// <summary>
        /// Pads data to a 16-byte AES block boundary. SaveDevice-style payloads always add at least one block.
        /// </summary>
        private static byte[] PadToBlockMultiple(byte[] data)
        {
            int rem = data.Length % 16;
            int pad = rem == 0 ? 16 : 16 - rem;
            byte[] output = new byte[data.Length + pad];
            Buffer.BlockCopy(data, 0, output, 0, data.Length);
            return output;
        }
        #endregion

        #region Validation Helpers

        /// <summary>
        /// Constant-time-ish byte comparison used for MD5 validation without early-returning on content differences.
        /// </summary>
        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
        #endregion

        #region world.info Readback

        /// <summary>
        /// Reads world.info by inferring the SaveDevice key from the file path when possible.
        /// </summary>
        public static CmzWorldInfoSummary ReadWorldInfo(string path)
        {
            byte[] key = TryInferSaveDeviceKeyFromWorldInfoPath(path);
            return ReadWorldInfo(path, key);
        }

        /// <summary>
        /// Reads and parses world.info with an explicit SaveDevice key.
        /// </summary>
        public static CmzWorldInfoSummary ReadWorldInfo(string path, byte[] saveDeviceKey)
        {
            byte[] payload = LoadPossiblyWrappedWorldInfo(path, saveDeviceKey);
            using (MemoryStream fs = new MemoryStream(payload))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                CmzWorldInfoSummary s = new CmzWorldInfoSummary
                {
                    Version = reader.ReadInt32()
                };
                if (s.Version < 1 || s.Version > WorldInfoVersion)
                    throw new InvalidDataException("Bad Info Version: " + s.Version);

                s.TerrainVersion = reader.ReadInt32();
                s.WorldName = reader.ReadString();
                s.OwnerGamerTag = reader.ReadString();
                s.CreatorGamerTag = reader.ReadString();
                s.CreatedDate = new DateTime(reader.ReadInt64());
                s.LastPlayedDate = new DateTime(reader.ReadInt64());
                s.Seed = reader.ReadInt32();
                s.WorldId = new Guid(reader.ReadBytes(16));
                s.LastPositionX = reader.ReadSingle();
                s.LastPositionY = reader.ReadSingle();
                s.LastPositionZ = reader.ReadSingle();

                s.CrateCount = reader.ReadInt32();
                if (s.CrateCount != 0)
                    throw new InvalidDataException("Generated converter world.info unexpectedly contains crates.");

                if (s.Version > 2)
                {
                    s.DoorCount = reader.ReadInt32();
                    if (s.DoorCount != 0)
                        throw new InvalidDataException("Generated converter world.info unexpectedly contains doors.");
                }

                if (s.Version > 3)
                {
                    s.SpawnerCount = reader.ReadInt32();
                    if (s.SpawnerCount != 0)
                        throw new InvalidDataException("Generated converter world.info unexpectedly contains spawners.");
                }

                s.InfiniteResourceMode = reader.ReadBoolean();
                s.ServerMessage = reader.ReadString();
                s.ServerPassword = reader.ReadString();

                if (s.Version > 4)
                {
                    s.HellBossesSpawned = reader.ReadInt32();
                    s.MaxHellBossSpawns = reader.ReadInt32();
                }

                return s;
            }
        }
        #endregion

        #region Debug Report

        /// <summary>
        /// Writes a human-readable validation report beside generated world.info to help diagnose visibility issues.
        /// </summary>
        private static void WriteDebugReport(string worldFolder, string worldsFolder, Guid folderGuid, CmzWorldInfoSummary s, CmzConfig config, string saveKeyDescription)
        {
            if (!config.WriteWorldInfoDebugReport)
                return;

            string folderName = new DirectoryInfo(worldsFolder).Name;
            string visibilityNote = string.Equals(folderName, "Worlds", StringComparison.OrdinalIgnoreCase)
                ? "Output parent folder name is Worlds. This is the folder CMZ normally enumerates inside its save device."
                : "WARNING: Output parent folder name is not Worlds. CMZ only enumerates folders under its actual save-device Worlds folder. Move/copy the generated UUID folder into CMZ's real Worlds folder if it does not appear.";

            string reportPath = Path.Combine(worldFolder, "mc-to-cmz-world.info-check.txt");
            File.WriteAllText(reportPath,
                "CMZ world.info readback check" + Environment.NewLine +
                "Can parse generated world.info: yes" + Environment.NewLine +
                "world.info SaveDevice RTSD wrapped: yes" + Environment.NewLine +
                "SaveDevice key: " + saveKeyDescription + Environment.NewLine +
                visibilityNote + Environment.NewLine + Environment.NewLine +
                "Worlds folder: " + worldsFolder + Environment.NewLine +
                "Generated folder UUID/name: " + folderGuid + Environment.NewLine +
                "Generated world folder: " + worldFolder + Environment.NewLine +
                "world.info path: " + Path.Combine(worldFolder, "world.info") + Environment.NewLine + Environment.NewLine +
                "Version: " + s.Version + Environment.NewLine +
                "TerrainVersion: " + s.TerrainVersion + Environment.NewLine +
                "WorldName: " + s.WorldName + Environment.NewLine +
                "OwnerGamerTag: " + s.OwnerGamerTag + Environment.NewLine +
                "CreatorGamerTag: " + s.CreatorGamerTag + Environment.NewLine +
                "CreatedDate: " + s.CreatedDate + Environment.NewLine +
                "LastPlayedDate: " + s.LastPlayedDate + Environment.NewLine +
                "Seed: " + s.Seed + Environment.NewLine +
                "WorldID: " + s.WorldId + Environment.NewLine +
                "LastPosition: " + s.LastPositionX + ", " + s.LastPositionY + ", " + s.LastPositionZ + Environment.NewLine +
                "Crates: " + s.CrateCount + Environment.NewLine +
                "Doors: " + s.DoorCount + Environment.NewLine +
                "Spawners: " + s.SpawnerCount + Environment.NewLine +
                "InfiniteResourceMode: " + s.InfiniteResourceMode + Environment.NewLine +
                "ServerMessage: " + s.ServerMessage + Environment.NewLine +
                "ServerPassword length: " + (s.ServerPassword == null ? 0 : s.ServerPassword.Length) + Environment.NewLine +
                "HellBossesSpawned: " + s.HellBossesSpawned + Environment.NewLine +
                "MaxHellBossSpawns: " + s.MaxHellBossSpawns + Environment.NewLine + Environment.NewLine +
                "Note: .inv files are player inventory saves named from the player's hash. They are created/loaded per player and are not required for the world to show in the saved-world list." + Environment.NewLine +
                "Note: Do not copy world.info from another world unless you also change the internal WorldID, because CMZ stores loaded worlds in a dictionary keyed by WorldID." + Environment.NewLine);
        }
        #endregion
    }
    #endregion
}
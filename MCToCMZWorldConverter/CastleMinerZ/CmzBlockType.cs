/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2026 RussDev7
This file is part of https://github.com/RussDev7/MCToCMZWorldConverter - see LICENSE for details.
*/

namespace MCToCMZWorldConverter.CastleMinerZ
{
    #region CMZ Block Types

    /// <summary>
    /// CastleMinerZ block type ids used by the CMZ WorldEdit schematic writer.
    /// </summary>
    /// <remarks>
    /// The numeric values are intentionally based on the enum order.
    /// Do not reorder these entries unless the matching CastleMinerZ block ids also change.
    ///
    /// The converter writes these values directly into the CMZ WorldEdit schematic file as integers.
    ///
    /// Example:
    /// <code>
    /// minecraft:acacia_log -> CmzBlockType.Log
    /// minecraft:twisting_vines -> CmzBlockType.Leaves
    /// minecraft:air -> CmzBlockType.Empty
    /// </code>
    /// </remarks>
    public enum CmzBlockType
    {
        #region Basic Terrain Blocks

        Empty,
        Dirt,
        Grass,
        Sand,
        Lantern,
        FixedLantern,
        Rock,
        GoldOre,
        IronOre,
        CopperOre,
        CoalOre,
        DiamondOre,
        SurfaceLava,
        DeepLava,
        Bedrock,
        Snow,
        Ice,
        Log,
        Leaves,
        Wood,
        BloodStone,
        SpaceRock,
        IronWall,
        CopperWall,
        GoldenWall,
        DiamondWall,
        Torch,
        TorchPOSX,
        TorchNEGZ,
        TorchNEGX,
        TorchPOSZ,
        TorchPOSY,
        TorchNEGY,
        Crate,
        NormalLowerDoorClosedZ,
        NormalLowerDoorClosedX,
        NormalLowerDoor,
        NormalUpperDoorClosed,
        NormalLowerDoorOpenZ,
        NormalLowerDoorOpenX,
        NormalUpperDoorOpen,
        TNT,
        C4,
        Slime,
        SpaceRockInventory,
        GlassBasic,
        GlassIron,
        GlassStrong,
        GlassMystery,
        CrateStone,
        CrateCopper,
        CrateIron,
        CrateGold,
        CrateDiamond,
        CrateBloodstone,
        CrateSafe,
        SpawnPointBasic,
        SpawnPointBuilder,
        SpawnPointCombat,
        SpawnPointExplorer,
        StrongLowerDoorClosedZ,
        StrongLowerDoorClosedX,
        StrongLowerDoor,
        StrongUpperDoorClosed,
        StrongLowerDoorOpenZ,
        StrongLowerDoorOpenX,
        StrongUpperDoorOpen,
        LanternFancy,
        TurretBlock,
        LootBlock,
        LuckyLootBlock,
        BombBlock,
        EnemySpawnOn,
        EnemySpawnOff,
        EnemySpawnRareOn,
        EnemySpawnRareOff,
        EnemySpawnAltar,
        TeleportStation,
        CraftingStation,
        HellForge,
        AlienSpawnOn,
        AlienSpawnOff,
        HellSpawnOn,
        HellSpawnOff,
        BossSpawnOn,
        BossSpawnOff,
        EnemySpawnDim,
        EnemySpawnRareDim,
        AlienSpawnDim,
        HellSpawnDim,
        BossSpawnDim,
        AlienHordeOn,
        AlienHordeOff,
        AlienHordeDim,
        NumberOfBlocks,

        // Matches your game's enum alias.
        Uninitialized = 94

        #endregion
    }
    #endregion
}
param(
    [string]$InputPath = "block-map.json",
    [string]$OutputPath = "block-map.autofilled.json"
)

function Get-CmzBlockGuess {
    param([string]$MinecraftId)

    $id = $MinecraftId.ToLowerInvariant()

    # ----------------------------
    # Air / invisible / unsupported
    # ----------------------------
    # IMPORTANT:
    # Do not use a loose match like "air|light" here.
    # "stairs" contains "air", and "light_blue_concrete" contains "light".
    # Use exact Minecraft ids so normal blocks are not accidentally mapped to Empty.
    if ($id -match "^minecraft:(air|cave_air|void_air|barrier|structure_void|light|end_portal|end_gateway|nether_portal)$") {
        return "Empty"
    }

    # ----------------------------
    # Fluids
    # ----------------------------
    if ($id -match "water|kelp|seagrass|bubble_column") {
        return "Empty"
    }

    if ($id -match "lava") {
        return "SurfaceLava"
    }

    # ----------------------------
    # Natural terrain
    # ----------------------------
    if ($id -match "grass_block|dirt_path|podzol|mycelium") {
        return "Grass"
    }

    if ($id -match "dirt|farmland|mud|clay|rooted_dirt|coarse_dirt") {
        return "Dirt"
    }

    if ($id -match "sand|sandstone|red_sand") {
        return "Sand"
    }

    if ($id -match "snow|powder_snow") {
        return "Snow"
    }

    if ($id -match "ice|packed_ice|blue_ice|frosted_ice") {
        return "Ice"
    }

    if ($id -match "bedrock") {
        return "Bedrock"
    }

    # ----------------------------
    # Ores / raw ore blocks
    # ----------------------------
    if ($id -match "diamond_ore|deepslate_diamond_ore|diamond_block") {
        return "DiamondOre"
    }

    if ($id -match "gold_ore|deepslate_gold_ore|gold_block|raw_gold_block") {
        return "GoldOre"
    }

    if ($id -match "iron_ore|deepslate_iron_ore|iron_block|raw_iron_block") {
        return "IronOre"
    }

    if ($id -match "copper_ore|deepslate_copper_ore|copper_block|raw_copper_block|cut_copper|chiseled_copper|copper_bars|copper_grate|copper_chain") {
        return "CopperOre"
    }

    if ($id -match "coal_ore|deepslate_coal_ore|coal_block") {
        return "CoalOre"
    }

    # ----------------------------
    # Trees / wood
    # ----------------------------
    if ($id -match "leaves|azalea_leaves|mangrove_roots|wart_block") {
        return "Leaves"
    }

    if ($id -match "_log|_wood|_stem|hyphae|bamboo_block|stripped_") {
        return "Log"
    }

    if ($id -match "oak_|spruce_|birch_|jungle_|acacia_|dark_oak_|mangrove_|cherry_|bamboo_|pale_oak_|crimson_|warped_") {
        if ($id -match "planks|slab|stairs|fence|fence_gate|button|pressure_plate|trapdoor|sign|hanging_sign|shelf|bookshelf|chiseled_bookshelf|crafting_table|lectern|loom|cartography_table|fletching_table|smithing_table|barrel|beehive|bee_nest") {
            return "Wood"
        }
    }

    # ----------------------------
    # Plants / flowers / small decoration
    # ----------------------------
    if ($id -match "sapling|flower|tulip|dandelion|poppy|allium|bluet|orchid|cornflower|daisy|rose|peony|lilac|fern|grass|bush|roots|vines|moss|lily|cactus|coral|mushroom|crop|wheat|carrots|potatoes|beetroots|melon_stem|pumpkin_stem|sugar_cane|pink_petals|wildflowers|leaf_litter|spore_blossom") {
        return "Leaves"
    }

    # ----------------------------
    # Stone-like blocks
    # ----------------------------
    if ($id -match "stone|cobblestone|andesite|diorite|granite|deepslate|tuff|calcite|dripstone|blackstone|basalt|obsidian|netherrack|nether_brick|bricks|brick|quartz|prismarine|purpur|end_stone|terracotta|concrete|concrete_powder|amethyst|sculk|resin|magma|soul_sand|soul_soil|gravel") {
        return "Rock"
    }

    # ----------------------------
    # Glass
    # ----------------------------
    if ($id -match "glass|glass_pane|tinted_glass|stained_glass") {
        return "GlassMystery"
    }

    # ----------------------------
    # Lights
    # ----------------------------
    if ($id -match "torch|wall_torch") {
        return "Torch"
    }

    if ($id -match "lantern|glowstone|sea_lantern|shroomlight|froglight|redstone_lamp|jack_o_lantern|campfire|candle|end_rod") {
        return "Lantern"
    }

    # ----------------------------
    # Containers
    # ----------------------------
    if ($id -match "chest|trapped_chest|shulker_box|hopper|dropper|dispenser|furnace|smoker|blast_furnace") {
        return "Crate"
    }

    # ----------------------------
    # Explosives / slime
    # ----------------------------
    if ($id -match "tnt") {
        return "TNT"
    }

    if ($id -match "slime_block") {
        return "Slime"
    }

    # ----------------------------
    # Doors
    # ----------------------------
    # Minecraft doors are multi-block and stateful.
    # Mapping them to Wood is safer than producing broken CMZ door halves.
    if ($id -match "door") {
        return "Wood"
    }

    # ----------------------------
    # Redstone / rails / unsupported tech
    # ----------------------------
    if ($id -match "rail|redstone|repeater|comparator|piston|observer|lever|tripwire|daylight_detector|command_block|jigsaw|structure_block|crafter|trial_spawner|vault|spawner") {
        return "Empty"
    }

    # ----------------------------
    # Beds / banners / heads / item-only visuals
    # ----------------------------
    if ($id -match "bed|banner|wall_banner|head|wall_head|skull|item_frame|painting|pot|decorated_pot|cake") {
        return "Empty"
    }

    # ----------------------------
    # Beacons / respawns / conduits
    # ----------------------------
    if ($id -match "beacon|respawn_anchor|conduit") {
        return "SpawnPointBasic"
    }

    # Fallback for anything unknown.
    return "Empty"
}

if (!(Test-Path $InputPath)) {
    Write-Host "ERROR: Input file not found: $InputPath"
    exit 1
}

$lines = Get-Content $InputPath
$output = New-Object System.Collections.Generic.List[string]

foreach ($line in $lines) {
    if ($line -match '^\s*"(?<id>minecraft:[^"]+)"\s*:\s*"(?<value>[^"]*)"(?<comma>,?)\s*$') {
        $id = $Matches["id"]
        $comma = $Matches["comma"]
        $guess = Get-CmzBlockGuess $id

        $indent = ($line -replace '^(\s*).*$', '$1')
        $output.Add("$indent`"$id`": `"$guess`"$comma")
    }
    else {
        $output.Add($line)
    }
}

$output | Set-Content $OutputPath -Encoding UTF8

Write-Host "Auto-filled block map written to:"
Write-Host $OutputPath
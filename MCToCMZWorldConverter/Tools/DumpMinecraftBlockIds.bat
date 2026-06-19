@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ================================================================
REM DumpMinecraftBlockIds.bat
REM
REM Reads all .json files in the same folder as this batch file and
REM converts their file names into namespaced Minecraft block ids.
REM
REM Example:
REM   acacia_log.json      -> minecraft:acacia_log
REM   twisting_vines.json  -> minecraft:twisting_vines
REM   stone.json           -> minecraft:stone
REM   oak_stairs.json      -> minecraft:oak_stairs
REM
REM ----------------------------------------------------------------
REM Minecraft Jar Notes
REM ----------------------------------------------------------------
REM To get the most up-to-date Minecraft block id list, extract the
REM "blockstates" folder from the latest Minecraft Java Edition jar
REM you want this converter to support.
REM
REM Typical client jar location:
REM   %APPDATA%\.minecraft\versions\<version>\<version>.jar
REM
REM Inside the jar, extract this folder:
REM   assets\minecraft\blockstates
REM
REM Each .json file in that folder represents one Minecraft block id.
REM For example:
REM   assets\minecraft\blockstates\acacia_log.json
REM becomes:
REM   minecraft:acacia_log
REM
REM Important:
REM - Use the newest jar when updating your block-map.json.
REM - Use an older jar only if you specifically want to target that
REM   older Minecraft version.
REM - This script only dumps base block ids from file names.
REM - It does not dump every possible block state combination, such as:
REM     minecraft:oak_stairs[facing=north,half=bottom,...]
REM ================================================================
REM
REM Usage:
REM   1. Open/extract the latest Minecraft Java Edition jar.
REM   2. Find this folder inside the jar:
REM        assets\minecraft\blockstates
REM   3. Copy this .bat into the extracted blockstates folder.
REM   4. Run this .bat.
REM
REM Output:
REM   minecraft-block-ids.txt
REM ================================================================

set "BLOCKSTATES_DIR=%~dp0"
set "OUTPUT=%~dp0minecraft-block-ids.txt"

echo Dumping Minecraft block ids...
echo Source:
echo "%BLOCKSTATES_DIR%"
echo.
echo Output:
echo "%OUTPUT%"
echo.

REM Clear old output file.
break > "%OUTPUT%"

REM Convert every .json file name in this folder into minecraft:<block_id>.
for %%F in ("%BLOCKSTATES_DIR%*.json") do (
    echo minecraft:%%~nF>> "%OUTPUT%"
)

echo Done.
echo.
echo Saved block ids to:
echo "%OUTPUT%"
echo.
pause
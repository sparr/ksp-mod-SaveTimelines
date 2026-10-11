#!/usr/bin/env bash
# Build testenv/KSP: a throwaway KSP install holding only what the integration
# test needs. Everything large is symlinked out of the real install, and nothing
# the test does can touch its saves or mods.
#
#   ./setup_testenv.sh            # from the install in SaveTimelines.props.user
#   KSP_ROOT=/path/to/KSP ./setup_testenv.sh
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
# KSP_ROOT, or else the KSPBT_GameRoot the build uses.
src="${KSP_ROOT:-$(sed -n 's:.*<KSPBT_GameRoot>\(.*\)</KSPBT_GameRoot>.*:\1:p' "$root/SaveTimelines.props.user" 2>/dev/null || true)}"
dst="$root/testenv/KSP"

[[ -f "$src/KSP.x86_64" ]] || { echo "no KSP install at '$src'; set KSP_ROOT or SaveTimelines.props.user" >&2; exit 2; }

echo "==> source install: $src"
rm -rf "$dst"
mkdir -p "$dst"/{GameData,saves,Logs,PluginData,thumbs,Screenshots,Ships/VAB,Ships/SPH}

# The executable and the native runtime must be real files: Unity looks for
# KSP_Data next to the binary it actually resolves.
cp "$src/KSP.x86_64" "$src/UnityPlayer.so" "$dst/"
cp "$src/Physics.cfg" "$dst/"
[[ -f "$src/buildID.txt" ]] && cp "$src/buildID.txt" "$dst/"

# A primed part database keeps startup short: without it KSP re-renders every
# part's drag cubes, which on a software GL stack takes many minutes. KSP
# consumes the file as it loads, so keep a seed copy to restore before each run.
if [[ -f "$src/PartDatabase.cfg" ]]; then
    cp "$src/PartDatabase.cfg" "$root/testenv/PartDatabase.seed.cfg"
    cp "$src/PartDatabase.cfg" "$dst/PartDatabase.cfg"
fi

# KSP_Data must be a real directory, not a symlink: KSP builds paths like
# "KSP_Data/../saves", and ".." resolves through a symlink to the target's
# parent, which would send saves into the real install. Mirror the tree
# instead: real directories, symlinked files.
mirror_tree() {
    local from="$1" to="$2"
    ( cd "$from" && find . -mindepth 1 -type d -printf '%P\0' ) | ( cd "$to" && xargs -0 --no-run-if-empty mkdir -p )
    ( cd "$from" && find . -mindepth 1 ! -type d -printf '%P\0' ) | while IFS= read -r -d '' f; do
        ln -sfn "$from/$f" "$to/$f"
    done
}
for d in KSP_Data KSP_x64_Data Parts Internals Resources sounds Plugins Missions; do
    [[ -e "$src/$d" ]] || continue
    mkdir -p "$dst/$d"
    mirror_tree "$src/$d" "$dst/$d"
done
# Unity rewrites boot.config on some launches; keep that off the real install.
[[ -e "$dst/KSP_Data/boot.config" ]] && cp --remove-destination "$src/KSP_Data/boot.config" "$dst/KSP_Data/boot.config"

# Squad, minus nearly all of its parts. The test never builds a vessel.
mkdir -p "$dst/GameData/Squad/Parts/Command"
for d in Agencies Contracts Controls Experience Flags FlagsAgency FlagsOrganization \
         FX Interiors Localization MenuProps PartList Plugins Props Resources Sounds Strategies; do
    [[ -e "$src/GameData/Squad/$d" ]] && ln -sfn "$src/GameData/Squad/$d" "$dst/GameData/Squad/$d"
done
for f in squadcore.ksp squadcorefx.ksp; do
    [[ -e "$src/GameData/Squad/$f" ]] && ln -sfn "$src/GameData/Squad/$f" "$dst/GameData/Squad/$f"
done
cp "$src/GameData/Squad/Parts/VariantThemes.cfg" "$dst/GameData/Squad/Parts/" 2>/dev/null || true
ln -sfn "$src/GameData/Squad/Parts/Resources" "$dst/GameData/Squad/Parts/Resources"
ln -sfn "$src/GameData/Squad/Parts/Command/probeCoreOcto_v2" "$dst/GameData/Squad/Parts/Command/probeCoreOcto_v2"

# Harmony is a hard dependency: without it KSP silently skips the plugin.
cp -r "$src/GameData/000_Harmony" "$dst/GameData/000_Harmony"

# Nothing else by default. ST_EXTRA_MODS adds mods for a compatibility run,
# for example ST_EXTRA_MODS="KSPCommunityFixes", which rewrites the same save
# path this mod hooks. ModuleManager comes along with any of them.
if [[ -n "${ST_EXTRA_MODS:-}" ]]; then
    for mm in "$src/GameData"/ModuleManager*.dll; do
        [[ -e "$mm" ]] && ln -sfn "$mm" "$dst/GameData/$(basename "$mm")"
    done
    for m in $ST_EXTRA_MODS; do
        [[ -d "$src/GameData/$m" ]] || { echo "no such mod: $m" >&2; exit 2; }
        cp -r "$src/GameData/$m" "$dst/GameData/$m"
    done
fi

# Windowed and small, with the first-run dialogs off and every graphics
# setting at its cheapest. The run uses software GL, where the 3D scenes behind
# the dialogs cost minutes per scene at the player's settings. The test driver
# also blanks the 3D cameras; this covers what is computed regardless.
if [[ -f "$src/settings.cfg" ]]; then
    sed -e 's/^SCREEN_RESOLUTION_WIDTH = .*/SCREEN_RESOLUTION_WIDTH = 1024/' \
        -e 's/^SCREEN_RESOLUTION_HEIGHT = .*/SCREEN_RESOLUTION_HEIGHT = 768/' \
        -e 's/^FULLSCREEN = .*/FULLSCREEN = False/' \
        -e 's/^TUTORIALS_EDITOR_ENABLE = .*/TUTORIALS_EDITOR_ENABLE = False/' \
        -e 's/^TUTORIALS_FLIGHT_ENABLE = .*/TUTORIALS_FLIGHT_ENABLE = False/' \
        -e 's/^SHOW_WHATSNEW_DIALOG = .*/SHOW_WHATSNEW_DIALOG = False/' \
        -e 's/^MISSION_SHOW_EXPANSION_INFO = .*/MISSION_SHOW_EXPANSION_INFO = False/' \
        -e 's/^SERENITY_SHOW_EXPANSION_INFO = .*/SERENITY_SHOW_EXPANSION_INFO = False/' \
        -e 's/^UI_SCALE = .*/UI_SCALE = 1/' \
        -e 's/^QUALITY_PRESET = .*/QUALITY_PRESET = 0/' \
        -e 's/^ANTI_ALIASING = .*/ANTI_ALIASING = 0/' \
        -e 's/^TEXTURE_QUALITY = .*/TEXTURE_QUALITY = 3/' \
        -e 's/^SYNC_VBL = .*/SYNC_VBL = 0/' \
        -e 's/^LIGHT_QUALITY = .*/LIGHT_QUALITY = 0/' \
        -e 's/^SHADOWS_QUALITY = .*/SHADOWS_QUALITY = 0/' \
        -e 's/^CELESTIAL_BODIES_CAST_SHADOWS = .*/CELESTIAL_BODIES_CAST_SHADOWS = False/' \
        -e 's/^PLANET_SCATTER = .*/PLANET_SCATTER = False/' \
        -e 's/^AERO_FX_QUALITY = .*/AERO_FX_QUALITY = 0/' \
        -e 's/^SURFACE_FX = .*/SURFACE_FX = False/' \
        -e 's/^HIGHLIGHT_FX = .*/HIGHLIGHT_FX = False/' \
        -e 's/^REFLECTION_PROBE_REFRESH_MODE = .*/REFLECTION_PROBE_REFRESH_MODE = 0/' \
        -e 's/^REFLECTION_PROBE_TEXTURE_RESOLUTION = .*/REFLECTION_PROBE_TEXTURE_RESOLUTION = 0/' \
        -e 's/^TERRAIN_SHADER_QUALITY = .*/TERRAIN_SHADER_QUALITY = 0/' \
        -e 's/^\(\s*\)preset = High$/\1preset = Low/' \
        "$src/settings.cfg" > "$dst/settings.cfg"
fi

echo "==> testenv ready at $dst"
du -sh "$dst" 2>/dev/null || true

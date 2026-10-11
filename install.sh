#!/usr/bin/env bash
# Copy the built mod into a KSP install.
#
#   ./install.sh                  # the install in SaveTimelines.props.user
#   KSP_ROOT=/path/to/KSP ./install.sh
#
# Each file is written under a temporary name and then renamed into place.
# A running game has the plugin DLL memory-mapped, and writing into that file
# changes the code under it, which shows up as exceptions in unrelated mods.
# A rename leaves the running game on the old file and takes effect at the
# next start.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# KSP_ROOT, or else the KSPBT_GameRoot the build uses.
ksp="${KSP_ROOT:-$(sed -n 's:.*<KSPBT_GameRoot>\(.*\)</KSPBT_GameRoot>.*:\1:p' "$root/SaveTimelines.props.user" 2>/dev/null || true)}"
src="$root/GameData/SaveTimelines"
dst="$ksp/GameData/SaveTimelines"

[[ -d "$ksp/GameData" ]] || { echo "no KSP install at '$ksp'; set KSP_ROOT or SaveTimelines.props.user" >&2; exit 2; }
[[ -f "$src/Plugins/SaveTimelines.dll" ]] || { echo "not built: run dotnet build" >&2; exit 2; }
if [[ -n "$(find "$root/src/SaveTimelines" -maxdepth 1 -name '*.cs' -newer "$src/Plugins/SaveTimelines.dll" -print -quit)" ]]; then
    echo "the built DLL is older than its sources; rebuild first" >&2
    exit 2
fi

# Everything in the mod folder except templates the release process reads.
( cd "$src" && find . -type f ! -name '*.versiontemplate' -print0 ) | while IFS= read -r -d '' f; do
    mkdir -p "$dst/$(dirname "$f")"
    cp "$src/$f" "$dst/$f.new"
    mv -f "$dst/$f.new" "$dst/$f"
done

# The mod was called SaveHierarchy before it was renamed. Both loaded at once
# would patch the game twice. Removing a file a running game has mapped is
# safe: the game keeps its copy until it exits.
old="$ksp/GameData/SaveHierarchy"
if [[ -d "$old" ]]; then
    rm -rf "$old"
    echo "removed the old $old"
fi

echo "installed to $dst"
if pgrep -f "^$ksp/KSP.x86_64" > /dev/null; then
    echo "KSP is running from this install; the new build loads at its next start"
fi

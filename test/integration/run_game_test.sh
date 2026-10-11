#!/usr/bin/env bash
# Build the mod and the in-game driver, run the scripted session headless, and
# check the save index and the load dialog's rows.
#
#   test/integration/run_game_test.sh
#   ST_REPLACE_LIST=1 test/integration/run_game_test.sh   # with another mod's
#       prefix replacing the dialog's list builder, as RP-1 does
#
# Runs on a private Xvfb display with software GL and no audio, at low
# priority and with a memory cap. Writes testenv/dialog-1.png and dialog-2.png.
set -euo pipefail

finish() {
    local rc=$?
    (( rc == 0 )) || echo "==> RUN DID NOT COMPLETE OR FAILED (exit $rc)." >&2
}
trap finish EXIT

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ksp="$root/testenv/KSP"
rows1="$ksp/sttest-rows-1.txt"
rows2="$ksp/sttest-rows-2.txt"
shot1="$root/testenv/dialog-1.png"
shot2="$root/testenv/dialog-2.png"
timeout_s="${ST_TIMEOUT:-300}"

[[ -x "$ksp/KSP.x86_64" ]] || { echo "no test install at $ksp - run test/integration/setup_testenv.sh first" >&2; exit 2; }
if pgrep -f '^\./KSP\.x86_64 .*-sttest' > /dev/null; then
    echo "a test KSP is already running" >&2
    exit 3
fi

echo "==> building"
dotnet build "$root/src/SaveTimelines/SaveTimelines.csproj" -v quiet --nologo
dotnet build "$root/test/SaveTimelines.GameTests/SaveTimelines.GameTests.csproj" -v quiet --nologo

echo "==> deploying"
# The whole mod folder, as a player would install it: the plugin, and the
# localization its button and labels read their text from.
KSP_ROOT="$ksp" "$root/install.sh" > /dev/null
for pair in "$root/src/SaveTimelines:$ksp/GameData/SaveTimelines/Plugins/SaveTimelines.dll" \
            "$root/test/SaveTimelines.GameTests:$ksp/GameData/SaveTimelinesGameTests/Plugins/SaveTimelines.GameTests.dll"; do
    src="${pair%%:*}"; dll="${pair##*:}"
    [[ -f "$dll" ]] || { echo "missing $dll" >&2; exit 4; }
    if [[ -n "$(find "$src" -maxdepth 1 -name '*.cs' -newer "$dll" -print -quit)" ]]; then
        echo "deployed $(basename "$dll") is older than its sources" >&2
        exit 4
    fi
done

rm -rf "$ksp/saves/sttest" "$rows1" "$rows2" "$shot1" "$shot2" "$root/testenv/dialog-collapsed.png" "$ksp"/sttest-*.txt "$ksp/sttest.craft"
sed -i -E 's/^(MASTER_VOLUME|SHIP_VOLUME|AMBIENCE_VOLUME|MUSIC_VOLUME|UI_VOLUME|VOICE_VOLUME) = .*/\1 = 0/' "$ksp/settings.cfg"
seed="$root/testenv/PartDatabase.seed.cfg"
[[ -f "$seed" ]] && cp "$seed" "$ksp/PartDatabase.cfg"

cd "$ksp"
[[ -f KSP.log ]] && mv -f KSP.log KSP.previous.log

# A display number nothing else is using.
display=""
for n in $(seq 90 120); do
    [[ -e "/tmp/.X11-unix/X$n" || -e "/tmp/.X$n-lock" ]] || { display=":$n"; break; }
done
[[ -n "$display" ]] || { echo "no free X display" >&2; exit 5; }

Xvfb "$display" -screen 0 1024x768x24 -nolisten tcp > "$root/testenv/xvfb.out" 2>&1 &
xvfb_pid=$!
ksp_pid=""
cleanup() {
    [[ -n "$ksp_pid" ]] && kill "$ksp_pid" 2>/dev/null || true
    kill "$xvfb_pid" 2>/dev/null || true
}
trap 'cleanup; finish' EXIT
sleep 1

extra=()
[[ -n "${ST_REPLACE_LIST:-}" ]] && extra+=(-sttest-replace-list)

echo "==> launching on $display"
DISPLAY="$display" LIBGL_ALWAYS_SOFTWARE=1 SDL_AUDIODRIVER=dummy \
    systemd-run --user --scope --quiet -p MemoryMax="${ST_MEMORY_MAX:-10G}" \
    nice -n 19 ./KSP.x86_64 -force-glcore -sttest "${extra[@]}" > run.out 2>&1 &
ksp_pid=$!

deadline=$(( SECONDS + timeout_s ))
while kill -0 "$ksp_pid" 2>/dev/null; do
    for pair in "$rows1:$shot1" "$rows2:$shot2" "$ksp/sttest-collapsed.txt:$root/testenv/dialog-collapsed.png"; do
        rows="${pair%%:*}"; shot="${pair##*:}"
        if [[ -f "$rows" && ! -f "$shot" ]]; then
            sleep 1
            import -display "$display" -window root "$shot" 2>/dev/null || echo "screenshot failed" >&2
        fi
    done
    if (( SECONDS > deadline )); then
        echo "timed out after ${timeout_s}s; last driver lines:" >&2
        grep -a "SaveTimelinesGameTests" KSP.log | tail -5 >&2 || true
        exit 6
    fi
    sleep 1
done
ksp_pid=""

if [[ -f "$ksp/sttest-failed.txt" ]]; then
    echo "the driver gave up: $(cat "$ksp/sttest-failed.txt")" >&2
    exit 7
fi
[[ -f "$ksp/sttest-done.txt" ]] || { echo "the game exited before the session finished; see $ksp/KSP.log" >&2; grep -a "SaveTimelinesGameTests\|\[EXC" "$ksp/KSP.log" | tail -8 >&2; exit 7; }

if [[ -n "${ST_REPLACE_LIST:-}" ]]; then
    n="$(grep -a -c "replacement list builder ran" "$ksp/KSP.log" || true)"
    (( n > 0 )) || { echo "the replacement list builder never ran" >&2; exit 8; }
    echo "==> replacement list builder ran $n times"
fi

"$root/test/integration/check_results.py" "$ksp"

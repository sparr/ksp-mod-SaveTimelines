#!/usr/bin/env python3
"""Check what a scripted session left behind. Usage: check_results.py KSP_DIR"""
import sys
from pathlib import Path

ksp = Path(sys.argv[1])
failures = []


def check(condition, message):
    if not condition:
        failures.append(message)


def rows(name):
    out = []
    for line in (ksp / name).read_text().splitlines():
        label, padding, columns, shown, arrow, count = line.split("\t")
        out.append((label, int(padding), int(columns), shown == "1", arrow == "1", count))
    return out


def sidecar():
    entries, current = {}, None
    for line in (ksp / "saves/sttest/SaveTimelines.cfg").read_text().splitlines():
        line = line.strip()
        if line == "{":
            current = {"ancestors": []}
        elif line == "}":
            entries[current["name"]] = current
        elif " = " in line and current is not None:
            key, value = line.split(" = ", 1)
            if key == "ancestor":
                current["ancestors"].append(value)
            else:
                current[key] = value
    return entries


# Part way through: A, B, C saved, B loaded, D and E saved, B loaded again,
# F saved. persistent is rewritten by each load, so it sits between B and F.
first_rows = rows("sttest-rows-1.txt")
first = [(label, padding) for label, padding, *_ in first_rows]
expected = [("A", 0), ("B", 0), ("D", 14), ("E", 14), ("C", 14), ("persistent", 0), ("F", 0)]
check(first == expected, f"rows before flight: expected {expected}, got {first}")
# One line column per nested level, none at the top level.
check([r[2] for r in first_rows] == [0, 0, 1, 1, 1, 0, 0], f"line columns: {[r[2] for r in first_rows]}")
check(all(r[3] and not r[4] for r in first_rows), "a row starts hidden or with an arrow")


def state(name):
    """Label (without any suffix) to (shown, arrow, full label, count)."""
    return {label.split("  ")[0]: (shown, arrow, label, count) for label, _, _, shown, arrow, count in rows(name)}


# Clicking the line of the group D, E collapses it to D.
for name, when in (("sttest-collapsed.txt", "after the click"), ("sttest-reopened.txt", "in a dialog opened afterwards")):
    st = state(name)
    check(st["E"][0] is False, f"E is still shown {when}")
    check(st["D"][0] and st["D"][1], f"D is not shown with an arrow {when}: {st['D']}")
    check(st["D"][3] == "+1", f"D does not say how many saves it hides {when}: {st['D'][3]!r}")
    check(st["D"][2] == "D", f"D's name label was changed {when}: {st['D'][2]!r}")
    check(all(v[0] and not v[1] for k, v in st.items() if k not in ("D", "E")), f"another row changed {when}")
st = state("sttest-expanded.txt")
check(all(v[0] and not v[1] for v in st.values()), f"not everything is shown again after expanding: {st}")
check(st["D"][3] == "", "D still shows a hidden count after expanding")

entries = sidecar()


def parent(name):
    """Nearest ancestor other than persistent, which every route rewrites."""
    for ancestor in entries[name]["ancestors"]:
        if ancestor != "persistent" and ancestor in entries:
            return ancestor
    return None


# Each line is one route into a save, and the parent it must record.
routes = [
    ("B", "A", "consecutive save"),
    ("C", "B", "consecutive save"),
    ("D", "B", "load through the Space Center pause menu"),
    ("E", "D", "consecutive save"),
    ("F", "B", "second load of the same save"),
    ("G", "F", "save in flight after launch"),
    ("H", "G", "consecutive save in flight"),
    ("I", "G", "flight quickload"),
    ("R", "G", "revert to launch, to the state the quickload restored"),
    ("T", "R", "save in flight after a second launch"),
    ("S", "R", "revert to the editor, to the state before the second launch"),
    ("M", "R", "resume from the main menu"),
]
for name, want, route in routes:
    if name not in entries:
        failures.append(f"{name} ({route}) is not in the index")
        continue
    got = parent(name)
    check(got == want, f"{name} ({route}): expected parent {want}, got {got}")
    check(entries[name].get("tracked") == "True", f"{name} is not marked tracked")
    check("inferred" not in entries[name], f"{name} is marked inferred")

# The scan: copies of A and H that the mod never saw written.
for name, want in (("legacyA", "A"), ("legacyH", "H")):
    if name not in entries:
        failures.append(f"{name} is not in the index")
        continue
    got = entries[name]["ancestors"][:1]
    check(got == [want], f"{name}: expected inferred parent {want}, got {got}")
    check(entries[name].get("inferred") == "True", f"{name} is not marked inferred")

second = rows("sttest-rows-2.txt")
labels = [label for label, *_ in second]
check(len(second) == len(entries), f"{len(second)} rows for {len(entries)} saves")
for name in ("legacyA", "legacyH"):
    check(any(l.startswith(name + " ") and "inferred" in l for l in labels), f"{name} row is not labelled inferred")
check(not any("inferred" in l for l in labels if not l.startswith("legacy")), "a recorded save is labelled inferred")
check((ksp / "saves/sttest/SaveTimelines.scan").exists(), "no scan cache was written")
# The newest file in the folder, and so its whole ancestry, is at the top
# level. A copy that is not the newest nests under its parent.
depth = {label.split("  ")[0]: padding for label, padding, *_ in second}
for name in ("legacyH", "H", "G", "F", "B", "A"):
    check(depth.get(name) == 0, f"{name} is not at the top level: {depth.get(name)}")
check(depth.get("legacyA", 0) > 0, "legacyA is not nested under A")
check(depth.get("M", 0) > 0, "M, on a branch played less recently than legacyH was written, is not nested")
check(all(r[3] for r in second), "a row is hidden after the scan")

print(f"{len(entries)} saves indexed, {len(second)} rows after the scan")
for label, padding, *_ in second:
    print(f"  {' ' * (padding // 7)}{label}")
if failures:
    print(f"==> FAIL ({len(failures)})")
    for f in failures:
        print("  " + f)
    sys.exit(1)
print(f"==> PASS ({len(routes)} routes, scan, dialog rows)")

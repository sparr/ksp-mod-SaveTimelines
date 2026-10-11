# Save Timelines

`README.md` covers what the mod does, the files it creates, and how to install
and build it. This file holds what a contributor needs beyond that.

The display name is "Save Timelines". Identifiers, paths, and the CKAN
identifier use `SaveTimelines`. Test flags, environment variables, and UI
object names use the prefix `ST`.

The plugin targets .NET Framework 4.8 and is built with the `KSPBuildTools`
NuGet package.

## Layout

- `src/SaveTimelines/` is the plugin source and its `SaveTimelines.csproj`.
- `GameData/SaveTimelines/` is the folder players install. Config, localization, and
  the `.version` file are tracked there. `Plugins/`, `LICENSE`, and `README.md`
  in it are build output and are ignored.
- `Directory.Build.props` holds everything shared by all projects: metadata,
  the mod folder location, and the import of `Version.props`.
- `test/SaveTimelines.Tests/` is the unit tests, `test/SaveTimelines.GameTests/`
  the in-game test driver, and `test/integration/` the scripts that run it.
  See Testing.
- `netkan/SaveTimelines.netkan` is the CKAN metadata to submit to `KSP-CKAN/NetKAN`.
  Once it is merged there, the copy upstream is the one that counts.
- `.github/workflows/` uses `KSPBuildTools`, pinned to the same version as the
  NuGet package in the csproj. Bump both together. `build.yml` and
  `validate.yml` call its reusable workflows. `create-release.yml` calls its
  actions directly, because the reusable release workflow names the zip after
  the repository and the zip is named after the mod.

## Building

The game install is only read for reference assemblies, and nothing is
deployed into it, provided `SaveTimelines.props.user` sets
`KSPBT_InstallCKANDependencies` to `false` as the example file does. Without
that, the Harmony `ModReference` makes `KSPBuildTools` run `ckan install`
against the install on every build and restore.

The Harmony `ModReference` sets `KSPAssemblyName` to `HarmonyKSP`, because
that is the name `0Harmony.dll` declares. The default, the file name, matches
nothing, and KSP then treats the dependency as missing.

## Installing into a game

`install.sh` writes each file under a temporary name and renames it into
place, so it is safe to run while the game is running. It also removes
`GameData/SaveHierarchy`, the mod's folder under its previous name.

## Version

The version is written in two tracked files, `Version.props` and
`GameData/SaveTimelines/SaveTimelines.version`. Both are generated from the
`.versiontemplate` file beside them. Do not edit the generated files. Edit the
templates when the content around the version needs to change, and regenerate.

The `create-release` workflow regenerates both. To do it locally, use
`update-version.sh` from the `KSPBuildTools` repository:

```sh
update-version.sh 1.2.3 .versiontemplate
```

## Changelog

`CHANGELOG.md` is in the format `yaclog` reads. Add entries as bullets under
`## Unreleased`. The release workflow renames that section to the version and
date, and uses `yaclog-ksp` to generate `GameData/SaveTimelines/changelog.cfg` for the
Kerbal Changelog mod. Do not edit `changelog.cfg` by hand. The table at the
top of `CHANGELOG.md` supplies that file's metadata.

## Releasing

Run the `create-release` workflow from the GitHub Actions tab with a version.
It updates the changelog and version files, commits, tags, pushes to the
branch it was run on, builds in Release, and creates a draft GitHub release
with the zip attached. Publishing the draft is a manual step.

## How it works

The source is split so that the half with no game types can be unit tested.

Game independent:

- `SaveIndex.cs` is the per-folder record: one entry per save with its
  ancestors by name, nearest first. Writing a name removes that name from every
  other entry, so a reused name is never mistaken for the save that had it
  before. `Reconcile` drops entries whose file is gone or no longer matches its
  recorded time and size.
- `SaveTree.cs` turns the index into display rows. At each fork the child whose
  descendants include the newest save continues the group, and the others
  nest.
- `Fingerprint.cs` reads the evidence a scan uses straight from a save file's
  bytes, without building a node tree, and `Inference.cs` picks a parent for
  each save that has none.

Game dependent:

- `Patches.cs` holds the Harmony patches. `Tracker.cs` follows the running game
  through saves, loads, and reverts and owns the files in the save folder.
- `DialogTree.cs` and `TreeView.cs` rearrange and draw the dialog's rows.
  `ScanButton.cs` and `Scan.cs` run a scan on worker threads.

Things that are easy to get wrong:

- The dialog patch is a postfix. RP-1 replaces `CreateLoadList` with a prefix
  that skips the original, and a postfix still runs.
- Scan workers must not call any Unity or game API, including `Debug.Log`.
  Errors are collected and logged from the main thread.
- The game's runtime reports file times in whole seconds.
- A log that is completely empty in a save is treated as unknown. Saves are
  sometimes written with a scenario's data missing.
- The files in a save folder were called `SaveHierarchy.cfg` and
  `SaveHierarchy.scan` before the rename. `Tracker` reads those when the
  current names are absent and removes them on the next write.

## Testing

`dotnet test test/SaveTimelines.Tests` runs the unit tests, with no game.

`test/integration/setup_testenv.sh` builds `testenv/KSP`, a throwaway install
with a trimmed Squad and Harmony and nothing else, symlinked from the real
install. `test/integration/run_game_test.sh` builds, installs into it, and runs
a scripted session on a private Xvfb display with software GL, no audio, low
priority, and a memory cap. The driver starts a sandbox game, makes saves, and
moves between them by every route (pause menu load, flight quickload, both
reverts, main menu resume), then clicks a group line and the scan button.
`check_results.py` checks the recorded parent for each route and the dialog's
rows. `ST_REPLACE_LIST=1` adds a prefix that replaces the list builder, as
RP-1 does. `ST_EXTRA_MODS` adds mods to the test install.

The driver blanks every 3D camera, and the test install's graphics settings
are at their minimum. Keep it that way: on software GL one frame of the Space
Center otherwise takes seconds.

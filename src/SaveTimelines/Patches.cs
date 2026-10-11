using System;
using HarmonyLib;

namespace SaveTimelines.Patches
{
    // Every stock save goes through one of these two overloads. The result is
    // the name actually written, which SaveMode.APPEND and file name
    // sanitizing can make differ from the name asked for, or empty when
    // nothing was written.

    [HarmonyPatch(typeof(GamePersistence), nameof(GamePersistence.SaveGame),
        new Type[] { typeof(Game), typeof(string), typeof(string), typeof(SaveMode) })]
    internal static class SaveGame_Game
    {
        private static void Postfix(string saveFolder, string __result)
        {
            Tracker.OnSaved(__result, saveFolder);
        }
    }

    [HarmonyPatch(typeof(GamePersistence), nameof(GamePersistence.SaveGame),
        new Type[] { typeof(GameBackup), typeof(string), typeof(string), typeof(SaveMode) })]
    internal static class SaveGame_Backup
    {
        private static void Postfix(string saveFolder, string __result)
        {
            Tracker.OnSaved(__result, saveFolder);
        }
    }

    // Resuming from the main menu. This method writes persistent.sfs before
    // it fires any event, so the lineage has to be in place on entry.
    [HarmonyPatch(typeof(MainMenu), "OnLoadDialogPipelineFinished")]
    internal static class MainMenu_Load
    {
        private static void Prefix(string save)
        {
            Tracker.OnLoaded("persistent", save);
        }
    }

    // The two in-game load paths. The file name is only known where the load
    // starts, and the load only becomes certain once the upgrade pipeline has
    // let it through, so the name is held between the two.

    [HarmonyPatch(typeof(KSCPauseMenu), "quickLoad")]
    internal static class KSCPauseMenu_QuickLoad
    {
        private static void Prefix(string filename, string folder)
        {
            Tracker.SetPendingLoad(filename, folder);
        }
    }

    [HarmonyPatch(typeof(KSCPauseMenu), "onPipelineFinished")]
    internal static class KSCPauseMenu_PipelineFinished
    {
        private static void Prefix()
        {
            Tracker.CommitPendingLoad();
        }
    }

    [HarmonyPatch(typeof(QuickSaveLoad), "quickLoad")]
    internal static class QuickSaveLoad_QuickLoad
    {
        private static void Prefix(string filename, string folder)
        {
            Tracker.SetPendingLoad(filename, folder);
        }
    }

    [HarmonyPatch(typeof(QuickSaveLoad), "onQuickloadPipelineFinished")]
    internal static class QuickSaveLoad_PipelineFinished
    {
        private static void Prefix()
        {
            Tracker.CommitPendingLoad();
        }
    }

    // Reverts restore an in-memory snapshot rather than a file.

    [HarmonyPatch(typeof(GameBackup), MethodType.Constructor, new Type[] { typeof(Game) })]
    internal static class GameBackup_Ctor
    {
        private static void Postfix(GameBackup __instance)
        {
            Tracker.OnSnapshot(__instance);
        }
    }

    [HarmonyPatch(typeof(FlightDriver), nameof(FlightDriver.RevertToLaunch))]
    internal static class FlightDriver_RevertToLaunch
    {
        private static void Prefix()
        {
            Tracker.OnRevert(FlightDriver.PostInitState);
        }
    }

    [HarmonyPatch(typeof(FlightDriver), nameof(FlightDriver.RevertToPrelaunch))]
    internal static class FlightDriver_RevertToPrelaunch
    {
        private static void Prefix()
        {
            Tracker.OnRevert(FlightDriver.PreLaunchState);
        }
    }

    // A postfix, not a prefix: RP-1 replaces this method with a prefix that
    // skips the original, and a postfix still runs after that.
    [HarmonyPatch(typeof(LoadGameDialog), "CreateLoadList")]
    internal static class LoadGameDialog_CreateLoadList
    {
        private static void Postfix(LoadGameDialog __instance)
        {
            DialogTree.Apply(__instance);
        }
    }
}

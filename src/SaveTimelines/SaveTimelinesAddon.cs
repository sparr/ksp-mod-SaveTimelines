using System;
using HarmonyLib;
using UnityEngine;

namespace SaveTimelines
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class SaveTimelinesAddon : MonoBehaviour
    {
        private static readonly Type[] patchClasses =
        {
            typeof(Patches.SaveGame_Game),
            typeof(Patches.SaveGame_Backup),
            typeof(Patches.MainMenu_Load),
            typeof(Patches.KSCPauseMenu_QuickLoad),
            typeof(Patches.KSCPauseMenu_PipelineFinished),
            typeof(Patches.QuickSaveLoad_QuickLoad),
            typeof(Patches.QuickSaveLoad_PipelineFinished),
            typeof(Patches.GameBackup_Ctor),
            typeof(Patches.FlightDriver_RevertToLaunch),
            typeof(Patches.FlightDriver_RevertToPrelaunch),
            typeof(Patches.LoadGameDialog_CreateLoadList),
        };

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            // Patched one class at a time so that one target going missing
            // costs that feature and not the rest.
            var harmony = new Harmony("SaveTimelines");
            int applied = 0;
            for (int i = 0; i < patchClasses.Length; i++)
            {
                try
                {
                    harmony.CreateClassProcessor(patchClasses[i]).Patch();
                    applied++;
                }
                catch (Exception e)
                {
                    Debug.LogError("[SaveTimelines] Patch " + patchClasses[i].Name + " failed: " + e);
                }
            }
            string version = typeof(SaveTimelinesAddon).Assembly.GetName().Version.ToString(3);
            Debug.Log("[SaveTimelines] Version " + version + ", applied " + applied + " of " + patchClasses.Length + " patches");

            GameEvents.onGameSceneLoadRequested.Add(OnSceneLoadRequested);
        }

        private void OnDestroy()
        {
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneLoadRequested);
        }

        private void OnSceneLoadRequested(GameScenes scene)
        {
            // Back at the main menu no game is running. Whatever is started
            // next sets its own lineage, or has none.
            if (scene == GameScenes.MAINMENU) Tracker.Forget();
        }
    }
}

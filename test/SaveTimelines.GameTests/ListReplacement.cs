using System;
using HarmonyLib;
using UnityEngine;

namespace SaveTimelines.GameTests
{
    /// <summary>
    /// With -sttest-replace-list, takes over LoadGameDialog.CreateLoadList
    /// the way RP-1 does: a prefix that builds the list itself and tells
    /// Harmony to skip the original. RP-1 cannot be loaded into the test
    /// install without most of Realism Overhaul, so this stands in for the
    /// one thing about it that matters here. The list is built by an
    /// unpatched copy of the stock method.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class ListReplacement : MonoBehaviour
    {
        internal static int calls;

        private void Awake()
        {
            if (!Driver.Enabled || Array.IndexOf(Environment.GetCommandLineArgs(), "-sttest-replace-list") < 0) return;
            var harmony = new Harmony("SaveTimelines.GameTests");
            harmony.CreateClassProcessor(typeof(StockCopy)).Patch();
            harmony.CreateClassProcessor(typeof(Replace)).Patch();
            Debug.Log("[SaveTimelinesGameTests] replacing CreateLoadList with a prefix");
        }

        [HarmonyPatch]
        private static class StockCopy
        {
            [HarmonyReversePatch]
            [HarmonyPatch(typeof(LoadGameDialog), "CreateLoadList")]
            internal static void CreateLoadList(LoadGameDialog instance)
            {
                throw new NotImplementedException("reverse patch stub");
            }
        }

        [HarmonyPatch(typeof(LoadGameDialog), "CreateLoadList")]
        private static class Replace
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(LoadGameDialog __instance)
            {
                calls++;
                Debug.Log("[SaveTimelinesGameTests] replacement list builder ran, call " + calls);
                StockCopy.CreateLoadList(__instance);
                return false;
            }
        }
    }
}

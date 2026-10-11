using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SaveTimelines
{
    /// <summary>
    /// Rearranges the rows the load dialog has just built into tree order and
    /// hands them to a <see cref="TreeView"/> to draw and manage.
    /// </summary>
    public static class DialogTree
    {
        private static readonly FieldInfo persistentField = AccessTools.Field(typeof(LoadGameDialog), "persistent");
        private static readonly FieldInfo directoryField = AccessTools.Field(typeof(LoadGameDialog), "directory");
        private static readonly FieldInfo itemsField = AccessTools.Field(typeof(LoadGameDialog), "items");
        private static readonly FieldInfo savesField = AccessTools.Field(typeof(LoadGameDialog), "saves");
        private static readonly FieldInfo contentField = AccessTools.Field(typeof(LoadGameDialog), "scrollListContent");
        private static readonly Type profileType = AccessTools.Inner(typeof(LoadGameDialog), "PlayerProfileInfo");
        private static readonly FieldInfo profileName = AccessTools.Field(profileType, "name");
        private static readonly FieldInfo profileGameNull = AccessTools.Field(profileType, "gameNull");

        public static void Apply(LoadGameDialog dialog)
        {
            try
            {
                // The main menu's list is one row per game, not per save.
                if ((bool)persistentField.GetValue(dialog)) return;
                string folder = (string)directoryField.GetValue(dialog);
                if (!Tracker.IsTracked(folder)) return;

                var items = (List<DialogGUIToggleButton>)itemsField.GetValue(dialog);
                var saves = (IList)savesField.GetValue(dialog);
                var content = (RectTransform)contentField.GetValue(dialog);

                // The dialog builds one row for each save it could read, in
                // the order of its sorted list, so the two can be paired up by
                // walking them together.
                var rowsByName = new Dictionary<string, DialogGUIToggleButton>(StringComparer.Ordinal);
                int next = 0;
                for (int i = 0; i < saves.Count; i++)
                {
                    object save = saves[i];
                    if (save == null || (bool)profileGameNull.GetValue(save)) continue;
                    if (next >= items.Count) return;
                    rowsByName[(string)profileName.GetValue(save)] = items[next++];
                }
                if (next != items.Count) return;

                ScanButton.Ensure(dialog, folder);

                // A scan that finished after its dialog was closed.
                if (Tracker.ActiveScan != null && Tracker.ActiveScan.Done && Tracker.ActiveScan.folder == folder)
                    Tracker.FinishScan();

                SaveIndex index = Tracker.GetIndex(folder);
                List<TreeRow> tree = SaveTree.Build(index);

                TreeView view = dialog.GetComponent<TreeView>();
                if (view == null) view = dialog.gameObject.AddComponent<TreeView>();
                view.Begin(dialog, folder, index);

                int sibling = 0;
                for (int i = 0; i < tree.Count; i++)
                {
                    DialogGUIToggleButton item;
                    if (!rowsByName.TryGetValue(tree[i].name, out item) || item.uiItem == null) continue;
                    item.uiItem.transform.SetSiblingIndex(sibling++);
                    view.Add(item.uiItem, tree[i]);
                }
                view.Refresh();
                LayoutRebuilder.MarkLayoutForRebuild(content);
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveTimelines] Failed to arrange the load dialog: " + e);
            }
        }
    }
}

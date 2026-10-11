using System.Collections;
using System.Reflection;
using HarmonyLib;
using KSP.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaveTimelines
{
    /// <summary>
    /// A button in the load dialog's footer that scans the folder's saves and
    /// guesses parents for those with none. Reading every save is too slow to
    /// do unasked, so nothing is scanned until this is pressed.
    /// </summary>
    public class ScanButton : MonoBehaviour
    {
        private const string ButtonName = "STScan";
        private static string IdleLabel
        {
            get { return Localizer.Format("#LOC_SaveTimelines_ScanSaves"); }
        }

        private static readonly FieldInfo cancelField = AccessTools.Field(typeof(LoadGameDialog), "btnCancel");
        private static readonly MethodInfo reloadMethod = AccessTools.Method(typeof(LoadGameDialog), "LoadGame");

        private LoadGameDialog dialog;
        private string folder;
        private Button button;
        private TMP_Text label;
        private bool polling;

        /// <summary>Add the button to a dialog that does not have it yet.</summary>
        public static void Ensure(LoadGameDialog dialog, string folder)
        {
            if (dialog.GetComponent<ScanButton>() != null) return;
            var cancel = (Button)cancelField.GetValue(dialog);
            if (cancel == null) return;

            // Cloned from the Cancel button so that it takes the dialog's
            // skin and sizing. Listeners added in code are not cloned.
            GameObject clone = Instantiate(cancel.gameObject, cancel.transform.parent);
            clone.name = ButtonName;
            clone.transform.SetSiblingIndex(cancel.transform.GetSiblingIndex());

            ScanButton self = dialog.gameObject.AddComponent<ScanButton>();
            self.dialog = dialog;
            self.folder = folder;
            self.button = clone.GetComponent<Button>();
            self.button.onClick = new Button.ButtonClickedEvent();
            self.button.onClick.AddListener(self.OnClick);
            self.button.interactable = true;
            self.label = clone.GetComponentInChildren<TMP_Text>(true);
            if (self.label != null)
            {
                self.label.text = IdleLabel;
                self.label.enableWordWrapping = false;
            }

            // The dialog may have been reopened while a scan was running.
            if (Tracker.ActiveScan != null && Tracker.ActiveScan.folder == folder)
                self.StartCoroutine(self.Poll());
        }

        private void OnClick()
        {
            if (polling) return;
            Tracker.StartScan(folder);
            StartCoroutine(Poll());
        }

        private IEnumerator Poll()
        {
            polling = true;
            button.interactable = false;
            ScanJob job = Tracker.ActiveScan;
            while (job != null && !job.Done)
            {
                if (label != null) label.text = job.Completed + " / " + job.total;
                yield return null;
            }
            if (job != null && Tracker.ActiveScan == job && job.folder == folder)
                Tracker.FinishScan();
            if (label != null) label.text = IdleLabel;
            button.interactable = true;
            polling = false;

            // Rebuild the list, which runs the tree arrangement again.
            reloadMethod.Invoke(dialog, null);
        }
    }
}

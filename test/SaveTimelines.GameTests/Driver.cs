using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaveTimelines.GameTests
{
    /// <summary>
    /// Plays a scripted session when KSP is started with -sttest. It makes
    /// named saves and moves between them by every route the game offers:
    /// the Space Center pause menu, the flight quickload, revert to launch,
    /// revert to the editor, and resuming from the main menu. Each route is
    /// entered through the stock method a player's click ends up in.
    ///
    /// The load dialog's rows are written to sttest-rows-1.txt part way
    /// through and to sttest-rows-2.txt after a scan at the end. The runner
    /// script checks those and the sidecar index.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class Driver : MonoBehaviour
    {
        private const string Tag = "[SaveTimelinesGameTests]";
        private const string Folder = "sttest";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        // Survives the scene loads that each step causes.
        private static int step;

        internal static bool Enabled
        {
            get { return Array.IndexOf(Environment.GetCommandLineArgs(), "-sttest") >= 0; }
        }

        private static string Root
        {
            get { return KSPUtil.ApplicationRootPath; }
        }

        private static string CraftPath
        {
            get { return Root + "sttest.craft"; }
        }

        private void Start()
        {
            if (!Enabled) return;
            StartCoroutine(Guarded());
        }

        private IEnumerator Guarded()
        {
            IEnumerator run = Run();
            while (true)
            {
                bool more;
                try
                {
                    more = run.MoveNext();
                }
                catch (Exception e)
                {
                    Fail("step " + step + " threw: " + e);
                    yield break;
                }
                if (!more) yield break;
                yield return run.Current;
            }
        }

        /// <summary>A failed run must not look like one that is still going.</summary>
        private static void Fail(string message)
        {
            Debug.LogError(Tag + " FAILED: " + message);
            File.WriteAllText(Root + "sttest-failed.txt", message + "\n");
            Application.Quit();
        }

        /// <summary>
        /// Keeps the 3D scene behind the dialogs from being drawn. The run
        /// uses software GL, where one frame of the Space Center takes
        /// seconds, and nothing under test is in it. The cameras stay enabled
        /// with nothing to draw, because game code looks them up.
        /// </summary>
        private void LateUpdate()
        {
            if (!Enabled) return;
            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera c = cameras[i];
                if (c.cullingMask == 0 || c.name.StartsWith("UI")) continue;
                c.cullingMask = 0;
                c.clearFlags = CameraClearFlags.Nothing;
            }
        }

        private static void Log(string message)
        {
            Debug.Log(Tag + " " + message);
        }

        private IEnumerator Run()
        {
            GameScenes scene = HighLogic.LoadedScene;
            if (scene == GameScenes.MAINMENU)
            {
                yield return new WaitForSecondsRealtime(3f);
                if (step == 0) NewGame();
                else if (step == 10) Resume();
            }
            else if (scene == GameScenes.SPACECENTER && step > 0)
            {
                yield return new WaitForSecondsRealtime(4f);
                yield return SpaceCenter();
            }
            else if (scene == GameScenes.FLIGHT)
            {
                float deadline = Time.realtimeSinceStartup + 240f;
                while (!FlightGlobals.ready || FlightGlobals.ActiveVessel == null || FlightDriver.PostInitState == null)
                {
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Fail("flight never became ready at step " + step);
                        yield break;
                    }
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(3f);
                yield return Flight();
            }
            else if (scene == GameScenes.EDITOR && step == 9)
            {
                yield return new WaitForSecondsRealtime(4f);
                Log("editor, step " + step);
                yield return Save("S");
                step = 10;
                HighLogic.LoadScene(GameScenes.MAINMENU);
            }
        }

        private static void NewGame()
        {
            Log("creating game");
            step = 1;
            GameParameters parameters = GameParameters.GetDefaultParameters(Game.Modes.SANDBOX, GameParameters.Preset.Normal);
            HighLogic.CurrentGame = GamePersistence.CreateNewGame(Folder, Game.Modes.SANDBOX, parameters,
                "Squad/Flags/default", GameScenes.SPACECENTER, EditorFacility.None);
            GameEvents.onGameNewStart.Fire();
            HighLogic.CurrentGame.Start();
        }

        /// <summary>What the main menu does once a game is picked to resume.</summary>
        private static void Resume()
        {
            Log("resuming from the main menu");
            step = 11;
            MainMenu menu = FindObjectOfType<MainMenu>();
            typeof(MainMenu).GetMethod("OnLoadDialogFinished", Private).Invoke(menu, new object[] { Folder });
        }

        private IEnumerator SpaceCenter()
        {
            Log("space center, step " + step);
            switch (step)
            {
                case 1:
                    yield return Save("A");
                    yield return Save("B");
                    yield return Save("C");
                    step = 2;
                    LoadThroughPauseMenu("B");
                    break;
                case 2:
                    yield return Save("D");
                    yield return Save("E");
                    step = 3;
                    LoadThroughPauseMenu("B");
                    break;
                case 3:
                    yield return Save("F");
                    yield return ReportDialog("sttest-rows-1.txt", false);
                    step = 4;
                    Launch();
                    break;
                case 7:
                    step = 8;
                    Launch();
                    break;
                case 11:
                    yield return Save("M");
                    // Files the mod never saw being written, for the scan.
                    // Kept apart in time: which file is newest decides
                    // which branch is drawn at the top level, and the game's
                    // runtime reports file times in whole seconds.
                    yield return new WaitForSecondsRealtime(1.5f);
                    CopySave("A", "legacyA");
                    yield return new WaitForSecondsRealtime(1.5f);
                    CopySave("H", "legacyH");
                    yield return ReportDialog("sttest-rows-2.txt", true);
                    File.WriteAllText(Root + "sttest-done.txt", "done\n");
                    Log("done");
                    yield return new WaitForSecondsRealtime(float.Parse(Environment.GetEnvironmentVariable("STTEST_LINGER") ?? "8"));
                    Application.Quit();
                    break;
                default:
                    Fail("reached the space center at step " + step);
                    break;
            }
        }

        private IEnumerator Flight()
        {
            Log("flight, step " + step);
            switch (step)
            {
                case 4:
                    yield return QuickSave("G");
                    yield return QuickSave("H");
                    step = 5;
                    Log("quickloading 'G'");
                    typeof(QuickSaveLoad).GetMethod("quickLoad", Private)
                        .Invoke(QuickSaveLoad.fetch, new object[] { "G", HighLogic.SaveFolder });
                    break;
                case 5:
                    yield return QuickSave("I");
                    step = 6;
                    Log("reverting to launch");
                    FlightDriver.RevertToLaunch();
                    break;
                case 6:
                    yield return QuickSave("R");
                    step = 7;
                    GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                    HighLogic.LoadScene(GameScenes.SPACECENTER);
                    break;
                case 8:
                    yield return QuickSave("T");
                    step = 9;
                    Log("reverting to the editor");
                    FlightDriver.RevertToPrelaunch(EditorFacility.VAB);
                    break;
                default:
                    Fail("reached flight at step " + step);
                    break;
            }
        }

        private static IEnumerator Save(string name)
        {
            string written = GamePersistence.SaveGame(name, HighLogic.SaveFolder, SaveMode.OVERWRITE);
            if (written != name) Fail("asked to save '" + name + "', game wrote '" + written + "'");
            Log("saved '" + written + "'");
            // Sibling order comes from wall clock time, so keep saves apart.
            yield return new WaitForSecondsRealtime(0.25f);
        }

        /// <summary>
        /// Saves the way a named quicksave in flight does. Unlike a plain
        /// save, the file says to resume in flight, which is what makes
        /// loading it return to the vessel and not to the Space Center.
        /// </summary>
        private static IEnumerator QuickSave(string name)
        {
            typeof(QuickSaveLoad).GetMethod("doSave", Private)
                .Invoke(QuickSaveLoad.fetch, new object[] { name, "#autoLOC_174669" });
            if (!File.Exists(Root + "saves/" + HighLogic.SaveFolder + "/" + name + ".sfs"))
                Fail("quicksave '" + name + "' wrote no file");
            Log("quicksaved '" + name + "'");
            yield return new WaitForSecondsRealtime(0.25f);
        }

        private static void CopySave(string from, string to)
        {
            string dir = Root + "saves/" + HighLogic.SaveFolder + "/";
            File.Copy(dir + from + ".sfs", dir + to + ".sfs", true);
            File.SetLastWriteTimeUtc(dir + to + ".sfs", DateTime.UtcNow);
        }

        /// <summary>
        /// Loads the way picking a save in the pause menu's dialog does, by
        /// calling the method the dialog's callback calls.
        /// </summary>
        private static void LoadThroughPauseMenu(string name)
        {
            Log("loading '" + name + "'");
            KSCPauseMenu menu = KSCPauseMenu.Create(delegate { });
            typeof(KSCPauseMenu).GetMethod("quickLoad", Private).Invoke(menu, new object[] { name, HighLogic.SaveFolder });
        }

        /// <summary>
        /// Launch a one-part vessel the way the editor's launch button
        /// finally does. The craft is written here so the test install needs
        /// no editor session to produce it.
        /// </summary>
        private static void Launch()
        {
            Log("launching");
            File.WriteAllText(CraftPath,
                "ship = sttest\nversion = 1.12.5\ndescription = \ntype = VAB\nsize = 1,1,1\n" +
                "steamPublishedFileId = 0\npersistentId = 1234567\nrot = 0,0,0,1\n" +
                "missionFlag = Squad/Flags/default\nvesselType = Probe\n" +
                "PART\n{\n\tpart = probeCoreOcto.v2_4290000000\n\tpartName = Part\n\tpersistentId = 7654321\n" +
                "\tpos = 0,10,0\n\tattPos = 0,0,0\n\tattPos0 = 0,10,0\n\trot = 0,0,0,1\n\tattRot = 0,0,0,1\n" +
                "\tattRot0 = 0,0,0,1\n\tmir = 1,1,1\n\tsymMethod = Radial\n\tautostrutMode = Off\n" +
                "\trigidAttachment = False\n\tistg = -1\n\tresPri = 0\n\tdstg = 0\n\tsidx = -1\n\tsqor = -1\n" +
                "\tsepI = -1\n\tattm = 0\n\tsameVesselCollision = False\n\tmodCost = 0\n\tmodMass = 0\n" +
                "\tmodSize = 0,0,0\n\tEVENTS\n\t{\n\t}\n\tACTIONS\n\t{\n\t}\n\tPARTDATA\n\t{\n\t}\n}\n");
            FlightDriver.StartWithNewLaunch(CraftPath, "Squad/Flags/default", "LaunchPad", new VesselCrewManifest());
        }

        /// <summary>
        /// Open the load dialog, optionally press the scan button and wait
        /// for it, and write one line per row in display order.
        /// </summary>
        private IEnumerator ReportDialog(string file, bool scan)
        {
            LoadGameDialog dialog = LoadGameDialog.Create(delegate { }, HighLogic.SaveFolder, false, UISkinManager.GetSkin("MainMenuSkin"));
            yield return new WaitForSecondsRealtime(1.5f);

            Transform found = FindDeep(dialog.transform, "STScan");
            if (found == null)
            {
                Fail("the dialog has no scan button");
                yield break;
            }
            if (scan)
            {
                Button button = found.GetComponent<Button>();
                Log("pressing the scan button");
                button.onClick.Invoke();
                float deadline = Time.realtimeSinceStartup + 60f;
                yield return null;
                while (!button.interactable)
                {
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Fail("the scan never finished");
                        yield break;
                    }
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(1.5f);
            }

            WriteRows(dialog, file);

            // Long enough for the runner to take a screenshot.
            yield return new WaitForSecondsRealtime(4f);

            if (!scan)
            {
                // Collapse the group that D starts by clicking its line on
                // E, its last row.
                if (!ClickLine(dialog, "E", 1)) yield break;
                yield return null;
                yield return null;
                WriteRows(dialog, "sttest-collapsed.txt");
                yield return new WaitForSecondsRealtime(4f);

                // The collapse is remembered by a dialog opened afterwards.
                Destroy(dialog.gameObject);
                yield return null;
                dialog = LoadGameDialog.Create(delegate { }, HighLogic.SaveFolder, false, UISkinManager.GetSkin("MainMenuSkin"));
                yield return new WaitForSecondsRealtime(1.5f);
                WriteRows(dialog, "sttest-reopened.txt");

                // Clicking the arrow on D expands it again.
                if (!ClickLine(dialog, "D", 1)) yield break;
                yield return null;
                yield return null;
                WriteRows(dialog, "sttest-expanded.txt");
            }

            Destroy(dialog.gameObject);
            yield return null;
        }

        private static RectTransform Content(LoadGameDialog dialog)
        {
            return (RectTransform)typeof(LoadGameDialog).GetField("scrollListContent", Private).GetValue(dialog);
        }

        private static string NameOf(Transform row)
        {
            VerticalLayoutGroup labels = row.GetComponentInChildren<VerticalLayoutGroup>(true);
            return labels != null && labels.transform.childCount > 0
                ? labels.transform.GetChild(0).GetComponent<TMP_Text>().text
                : "?";
        }

        /// <summary>
        /// One line per row in display order: label, left padding, number of
        /// line columns, whether the row is shown, whether it has an arrow
        /// in place of a line, and the hidden-save count shown with it.
        /// </summary>
        private static void WriteRows(LoadGameDialog dialog, string file)
        {
            RectTransform content = Content(dialog);
            var report = new StringBuilder();
            for (int i = 0; i < content.childCount; i++)
            {
                Transform row = content.GetChild(i);
                HorizontalLayoutGroup layout = row.GetComponentInChildren<HorizontalLayoutGroup>(true);
                int columns = 0;
                bool arrow = false;
                string count = "";
                for (int c = 0; c < row.childCount; c++)
                {
                    Transform child = row.GetChild(c);
                    if (!child.name.StartsWith("STRail")) continue;
                    columns++;
                    foreach (Image image in child.GetComponentsInChildren<Image>(true))
                        if (image.transform != child && image.sprite != null) arrow = true;
                    foreach (TMP_Text text in child.GetComponentsInChildren<TMP_Text>(true))
                        count = text.text;
                }
                report.Append(NameOf(row)).Append('\t')
                    .Append(layout != null ? layout.padding.left : -1).Append('\t')
                    .Append(columns).Append('\t')
                    .Append(row.gameObject.activeSelf ? 1 : 0).Append('\t')
                    .Append(arrow ? 1 : 0).Append('\t')
                    .Append(count).Append('\n');
            }
            File.WriteAllText(Root + file, report.ToString());
            Log("wrote " + file + ", " + content.childCount + " rows");
        }

        /// <summary>Click the line column at a level of the named save's row.</summary>
        private static bool ClickLine(LoadGameDialog dialog, string save, int level)
        {
            RectTransform content = Content(dialog);
            for (int i = 0; i < content.childCount; i++)
            {
                Transform row = content.GetChild(i);
                string label = NameOf(row);
                if (label != save && !label.StartsWith(save + " ")) continue;
                Transform column = row.Find("STRail" + level);
                if (column == null) break;
                Log("clicking the level " + level + " line on '" + save + "'");
                column.GetComponent<Button>().onClick.Invoke();
                return true;
            }
            Fail("no level " + level + " line on a row for '" + save + "'");
            return false;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindDeep(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}

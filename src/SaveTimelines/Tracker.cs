using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SaveTimelines
{
    /// <summary>
    /// Follows the game being played through saves, loads, and reverts, and
    /// keeps each save folder's sidecar index up to date. Everything here runs
    /// on the main thread, called from the Harmony patches.
    /// </summary>
    public static class Tracker
    {
        public const string SidecarName = "SaveTimelines.cfg";
        public const string ScanCacheName = "SaveTimelines.scan";

        // What the two files were called before the mod was renamed. A
        // folder that only has these is read from them once, and they are
        // removed when the files are next written under the current names.
        private const string OldSidecarName = "SaveHierarchy.cfg";
        private const string OldScanCacheName = "SaveHierarchy.scan";

        /// <summary>The scan in progress, or finished and not yet applied.</summary>
        public static ScanJob ActiveScan { get; private set; }

        /// <summary>The lineage a game state had when it was captured.</summary>
        private class Lineage
        {
            public string folder;
            public List<SaveRef> chain;
        }

        // The save the running game descends from, nearest first. A save
        // written now gets this as its ancestry.
        private static string headFolder;
        private static List<SaveRef> head = new List<SaveRef>();

        // A load chosen in a dialog but not yet through the upgrade pipeline,
        // which can still be cancelled.
        private static string pendingName;
        private static string pendingFolder;

        private static string indexFolder;
        private static SaveIndex index;

        // Revert snapshots are never files, so the lineage they were taken
        // with is remembered here for as long as the snapshot object lives.
        private static readonly ConditionalWeakTable<GameBackup, Lineage> snapshots =
            new ConditionalWeakTable<GameBackup, Lineage>();

        /// <summary>
        /// Folders the game uses for things other than a player's own saves.
        /// Their dialogs are separate and they are shared between games.
        /// </summary>
        public static bool IsTracked(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            string first = folder.Replace('\\', '/').Split('/')[0];
            return first != "scenarios" && first != "training" && first != "missions" && first != "test_missions";
        }

        private static string FolderPath(string folder)
        {
            return KSPUtil.ApplicationRootPath + "saves/" + folder + "/";
        }

        private static List<FileStamp> Scan(string folder)
        {
            var stamps = new List<FileStamp>();
            var dir = new DirectoryInfo(FolderPath(folder));
            if (!dir.Exists) return stamps;
            FileInfo[] files = dir.GetFiles("*.sfs");
            for (int i = 0; i < files.Length; i++)
                stamps.Add(new FileStamp(Path.GetFileNameWithoutExtension(files[i].Name), files[i].LastWriteTimeUtc.Ticks, files[i].Length));
            return stamps;
        }

        /// <summary>
        /// The index for a folder, reconciled against the files present. The
        /// most recently used folder's index is kept in memory.
        /// </summary>
        public static SaveIndex GetIndex(string folder)
        {
            if (index == null || indexFolder != folder)
            {
                string path = FolderPath(folder) + SidecarName;
                string old = FolderPath(folder) + OldSidecarName;
                if (File.Exists(path))
                {
                    index = SaveIndex.Parse(File.ReadAllText(path));
                }
                else if (File.Exists(old))
                {
                    index = SaveIndex.Parse(File.ReadAllText(old));
                    index.dirty = true;
                }
                else
                {
                    index = new SaveIndex();
                }
                indexFolder = folder;
            }
            index.Reconcile(Scan(folder));
            return index;
        }

        private static void Flush()
        {
            if (index == null || !index.dirty) return;
            string dir = FolderPath(indexFolder);
            if (!Directory.Exists(dir)) return;
            string path = dir + SidecarName;
            string temp = path + ".tmp";
            File.WriteAllText(temp, index.Serialize());
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
            index.dirty = false;
            if (File.Exists(dir + OldSidecarName)) File.Delete(dir + OldSidecarName);
        }

        private static FingerprintCache LoadScanCache(string folder)
        {
            string path = FolderPath(folder) + ScanCacheName;
            if (File.Exists(path)) return FingerprintCache.Parse(File.ReadAllText(path));
            string old = FolderPath(folder) + OldScanCacheName;
            return File.Exists(old) ? FingerprintCache.Parse(File.ReadAllText(old)) : new FingerprintCache();
        }

        /// <summary>
        /// Start reading every save in the folder that has no current
        /// fingerprint. Does nothing if a scan is already running.
        /// </summary>
        public static ScanJob StartScan(string folder)
        {
            if (ActiveScan != null) return ActiveScan;
            FingerprintCache cache = LoadScanCache(folder);
            List<FileStamp> files = Scan(folder);
            var stamps = new List<FileStamp>();
            var paths = new List<string>();
            for (int i = 0; i < files.Count; i++)
            {
                if (cache.Get(files[i]) != null) continue;
                stamps.Add(files[i]);
                paths.Add(FolderPath(folder) + files[i].name + ".sfs");
            }
            ActiveScan = new ScanJob(folder, stamps, paths);
            return ActiveScan;
        }

        /// <summary>
        /// Apply a finished scan: store its fingerprints, then guess a parent
        /// for every save that has none. Returns how many were placed.
        /// </summary>
        public static int FinishScan()
        {
            ScanJob job = ActiveScan;
            if (job == null || !job.Done) return 0;
            ActiveScan = null;
            try
            {
                FingerprintCache cache = LoadScanCache(job.folder);
                var errors = new List<string>();
                job.Collect(cache, errors);
                for (int i = 0; i < errors.Count; i++)
                    Debug.LogWarning("[SaveTimelines] Could not scan " + errors[i]);

                SaveIndex idx = GetIndex(job.folder);

                // Fingerprints of files that have since changed or gone are
                // dropped here, which also keeps the cache from growing.
                var current = new FingerprintCache();
                foreach (SaveEntry e in idx.entries.Values)
                {
                    var stamp = new FileStamp(e.name, e.mtime, e.size);
                    Fingerprint print = cache.Get(stamp);
                    if (print != null) current.Put(stamp, print);
                }
                File.WriteAllText(FolderPath(job.folder) + ScanCacheName, current.Serialize());
                if (File.Exists(FolderPath(job.folder) + OldScanCacheName)) File.Delete(FolderPath(job.folder) + OldScanCacheName);

                int placed = Inference.Apply(idx, delegate(string name)
                {
                    FingerprintCache.Item item;
                    return current.items.TryGetValue(name, out item) ? item.print : null;
                });
                Flush();
                Debug.Log("[SaveTimelines] Scan of '" + job.folder + "' read " + job.total + " saves and placed " + placed);
                return placed;
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveTimelines] Failed to apply scan: " + e);
                return 0;
            }
        }

        /// <summary>Called after the game has written a save file.</summary>
        public static void OnSaved(string name, string folder)
        {
            if (string.IsNullOrEmpty(name) || !IsTracked(folder)) return;
            try
            {
                var file = new FileInfo(FolderPath(folder) + name + ".sfs");
                if (!file.Exists) return;

                SaveIndex idx = GetIndex(folder);
                List<SaveRef> chain = headFolder == folder ? head : null;
                var stamp = new FileStamp(name, file.LastWriteTimeUtc.Ticks, file.Length);
                head = idx.RecordWrite(chain, DateTime.UtcNow.Ticks, stamp);
                headFolder = folder;
                Flush();
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveTimelines] Failed to record save '" + name + "': " + e);
            }
        }

        /// <summary>
        /// Called when a save file is about to become the running game.
        /// </summary>
        public static void OnLoaded(string name, string folder)
        {
            if (string.IsNullOrEmpty(name) || !IsTracked(folder))
            {
                Forget();
                return;
            }
            try
            {
                SaveIndex idx = GetIndex(folder);
                head = idx.Lineage(name);
                headFolder = folder;
                Flush();
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveTimelines] Failed to record load of '" + name + "': " + e);
            }
        }

        /// <summary>Remember whether the group a save starts is collapsed.</summary>
        public static void SetCollapsed(string folder, string name, bool collapsed)
        {
            try
            {
                SaveIndex idx = GetIndex(folder);
                SaveEntry e;
                if (!idx.entries.TryGetValue(name, out e) || e.collapsed == collapsed) return;
                e.collapsed = collapsed;
                idx.dirty = true;
                Flush();
            }
            catch (Exception ex)
            {
                Debug.LogError("[SaveTimelines] Failed to record collapse of '" + name + "': " + ex);
            }
        }

        public static void SetPendingLoad(string name, string folder)
        {
            pendingName = name;
            pendingFolder = folder;
        }

        public static void CommitPendingLoad()
        {
            if (pendingName == null) return;
            string name = pendingName, folder = pendingFolder;
            pendingName = pendingFolder = null;
            OnLoaded(name, folder);
        }

        /// <summary>The running game is no longer any save's descendant.</summary>
        public static void Forget()
        {
            head = new List<SaveRef>();
            headFolder = null;
            pendingName = pendingFolder = null;
        }

        public static void OnSnapshot(GameBackup backup)
        {
            if (backup == null) return;
            snapshots.Remove(backup);
            snapshots.Add(backup, new Lineage { folder = headFolder, chain = new List<SaveRef>(head) });
        }

        /// <summary>
        /// Called when a revert snapshot is about to become the running game.
        /// References in the restored lineage may be stale by now; they are
        /// filtered out when the next save is recorded.
        /// </summary>
        public static void OnRevert(GameBackup backup)
        {
            Lineage lineage;
            if (backup == null || !snapshots.TryGetValue(backup, out lineage)) return;
            headFolder = lineage.folder;
            head = new List<SaveRef>(lineage.chain);
        }
    }
}

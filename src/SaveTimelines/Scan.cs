using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SaveTimelines
{
    /// <summary>
    /// Reads fingerprints from save files on worker threads. A worker
    /// touches only the file system and <see cref="Fingerprint.Read"/>: no
    /// Unity or game API, and no logging, both of which are main thread only.
    /// Everything it produces is collected by the main thread afterwards.
    /// </summary>
    public class ScanJob
    {
        public readonly string folder;
        public readonly int total;

        private readonly List<FileStamp> stamps;
        private readonly List<string> paths;
        private readonly Fingerprint[] results;
        private readonly List<string> errors = new List<string>();
        private int completed;
        private int next;
        private int running;
        private volatile bool done;

        public bool Done { get { return done; } }
        public int Completed { get { return Thread.VolatileRead(ref completed); } }

        public ScanJob(string folder, List<FileStamp> stamps, List<string> paths)
        {
            this.folder = folder;
            this.stamps = stamps;
            this.paths = paths;
            total = stamps.Count;
            results = new Fingerprint[total];

            if (total == 0)
            {
                done = true;
                return;
            }

            // Files are independent, so several are read at once. Kept to
            // half the cores and at most four, because the game is still
            // running its own threads while the dialog is open.
            running = Math.Max(1, Math.Min(Math.Min(4, Environment.ProcessorCount / 2), total));
            for (int i = 0; i < running; i++)
            {
                var thread = new Thread(Run);
                thread.IsBackground = true;
                thread.Priority = ThreadPriority.BelowNormal;
                thread.Name = "SaveTimelines scan " + i;
                thread.Start();
            }
        }

        private void Run()
        {
            while (true)
            {
                int i = Interlocked.Increment(ref next) - 1;
                if (i >= total) break;
                try
                {
                    using (var stream = new FileStream(paths[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan))
                        results[i] = Fingerprint.Read(stream);
                }
                catch (Exception e)
                {
                    lock (errors) errors.Add(stamps[i].name + ": " + e.Message);
                }
                Interlocked.Increment(ref completed);
            }
            if (Interlocked.Decrement(ref running) == 0) done = true;
        }

        /// <summary>Main thread, after <see cref="Done"/>.</summary>
        public void Collect(FingerprintCache cache, List<string> errorsOut)
        {
            for (int i = 0; i < total; i++)
                if (results[i] != null)
                    cache.Put(stamps[i], results[i]);
            lock (errors) errorsOut.AddRange(errors);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SaveTimelines
{
    /// <summary>
    /// A summary of the append-only history inside one save file: for each
    /// event log the game keeps, a running hash after every entry. Two saves
    /// share history up to entry N of a log exactly when their running hashes
    /// at N are equal, so "is this save's history a prefix of that one's" is
    /// a single comparison per log.
    /// </summary>
    public class Fingerprint
    {
        /// <summary>Game time of the save, or -1 if it has no flight state.</summary>
        public double ut = -1;

        /// <summary>
        /// One array per event log. The key is the log's name ("contracts",
        /// "tech", "launches"). Element i of the array is the hash of that
        /// log's first i + 1 entries, so the array has one element per entry
        /// and the last element summarizes the whole log.
        /// </summary>
        public readonly Dictionary<string, ulong[]> logs = new Dictionary<string, ulong[]>(StringComparer.Ordinal);

        /// <summary>
        /// Vessels in the flight state, by id, with the game time each was
        /// launched. Unlike the logs this is not append-only: vessels are
        /// recovered and destroyed, and a vessel split off from another
        /// inherits its launch time. It is weaker evidence, and the only
        /// evidence a sandbox save has.
        /// </summary>
        public readonly Dictionary<string, double> vessels = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>
        /// Things that began at a known game time, by source and then by
        /// name: active strategies (which in RP-1 includes appointed leaders)
        /// with their start dates, programs with the date they were accepted,
        /// and strategies that have ended with the date they ended. The
        /// "ended" source is present, possibly empty, whenever the save has
        /// that record at all. The date
        /// is when the thing came to exist, so anything a later save holds
        /// from before this save was made must be in this save too.
        /// </summary>
        public readonly Dictionary<string, Dictionary<string, double>> events =
            new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);

        /// <summary>
        /// Work in progress that only moves forward, by its unique id:
        /// launch complex construction, facility upgrades, and vessels being
        /// integrated, as RP-1 stores them.
        /// </summary>
        public readonly Dictionary<string, double> progress = new Dictionary<string, double>(StringComparer.Ordinal);

        // Sources in which an entry, once made, is never removed.
        private static readonly string[] permanentSources = { "programs" };

        /// <summary>
        /// True when dated events or progress show that this save cannot be
        /// an ancestor of <paramref name="later"/>. A source that is empty on
        /// the side that would have to hold an entry is treated as unknown,
        /// since saves are sometimes written with a scenario's data missing.
        /// </summary>
        public bool Contradicts(Fingerprint later)
        {
            foreach (KeyValuePair<string, Dictionary<string, double>> source in later.events)
            {
                Dictionary<string, double> mine;
                if (!events.TryGetValue(source.Key, out mine) || mine.Count == 0) continue;
                foreach (KeyValuePair<string, double> e in source.Value)
                {
                    // It began before this save was made, so it was already
                    // there, with the same start date.
                    if (e.Value >= ut) continue;
                    double had;
                    if (!mine.TryGetValue(e.Key, out had) || had != e.Value) return true;
                }
            }

            for (int i = 0; i < permanentSources.Length; i++)
            {
                Dictionary<string, double> mine, theirs;
                if (!events.TryGetValue(permanentSources[i], out mine)) continue;
                if (!later.events.TryGetValue(permanentSources[i], out theirs) || theirs.Count == 0) continue;
                foreach (KeyValuePair<string, double> e in mine)
                {
                    double has;
                    if (!theirs.TryGetValue(e.Key, out has) || has != e.Value) return true;
                }
            }

            // A strategy active here and not in the later save must have
            // ended in between, and where endings are recorded at all, that
            // one has to be among them.
            Dictionary<string, double> active, activeLater, endedLater;
            if (events.TryGetValue("strategies", out active)
                && later.events.TryGetValue("strategies", out activeLater) && activeLater.Count > 0
                && later.events.TryGetValue("ended", out endedLater))
            {
                foreach (KeyValuePair<string, double> e in active)
                {
                    double still;
                    if (activeLater.TryGetValue(e.Key, out still) && still == e.Value) continue;
                    double endedAt;
                    if (!endedLater.TryGetValue(e.Key, out endedAt) || endedAt < ut) return true;
                }
            }

            foreach (KeyValuePair<string, double> p in progress)
            {
                double theirs;
                if (later.progress.TryGetValue(p.Key, out theirs) && p.Value > theirs * (1 + 1e-9) + 1e-9) return true;
            }
            return false;
        }

        /// <summary>
        /// True when the vessels suggest that this save and
        /// <paramref name="later"/> are on different branches: each has a
        /// vessel the other lacks, launched after the newest vessel they
        /// share and before this save was made. One side alone proves
        /// nothing, since a vessel missing from the later save may have been
        /// recovered, and one missing from the earlier save may have split
        /// off since.
        /// </summary>
        public bool DivergesFrom(Fingerprint later)
        {
            if (vessels.Count == 0 || later.vessels.Count == 0) return false;

            double shared = double.NegativeInfinity;
            foreach (KeyValuePair<string, double> v in vessels)
                if (later.vessels.ContainsKey(v.Key) && v.Value > shared)
                    shared = v.Value;

            bool mine = false;
            foreach (KeyValuePair<string, double> v in vessels)
            {
                if (v.Value > shared && !later.vessels.ContainsKey(v.Key))
                {
                    mine = true;
                    break;
                }
            }
            if (!mine) return false;

            foreach (KeyValuePair<string, double> v in later.vessels)
                if (v.Value > shared && v.Value <= ut && !vessels.ContainsKey(v.Key))
                    return true;
            return false;
        }

        public int Total
        {
            get
            {
                int n = 0;
                foreach (ulong[] log in logs.Values) n += log.Length;
                return n;
            }
        }

        /// <summary>
        /// True when every log of this save is a prefix of the same log in
        /// <paramref name="later"/>. A log that is empty in the later save
        /// says nothing either way: saves do get written with a scenario's
        /// data missing, and a log cannot really lose all its entries.
        /// </summary>
        public bool IsPrefixOf(Fingerprint later)
        {
            foreach (KeyValuePair<string, ulong[]> log in logs)
            {
                int n = log.Value.Length;
                if (n == 0) continue;
                ulong[] other;
                if (!later.logs.TryGetValue(log.Key, out other) || other.Length == 0) continue;
                if (other.Length < n) return false;
                if (other[n - 1] != log.Value[n - 1]) return false;
            }
            return true;
        }

        /// <summary>
        /// How much of this save's history can be checked against
        /// <paramref name="later"/>: its entries in the logs the later save
        /// also has.
        /// </summary>
        public int SharedTotal(Fingerprint later)
        {
            int n = 0;
            foreach (KeyValuePair<string, ulong[]> log in logs)
            {
                ulong[] other;
                if (later.logs.TryGetValue(log.Key, out other) && other.Length > 0) n += log.Value.Length;
            }
            return n;
        }

        /// <summary>
        /// Where one event log lives in a save file: under GAME, in the
        /// SCENARIO with the given name, optionally inside one container
        /// node, as child nodes each identified by one value.
        /// </summary>
        private class LogSpec
        {
            public string id;
            public string scenario;
            public string container;
            public string entry;
            public string key;

            private byte[] bytes;

            /// <summary>The start of a line that sets this log's key.</summary>
            public byte[] keyBytes
            {
                get { return bytes ?? (bytes = Encoding.ASCII.GetBytes(key + " = ")); }
            }
        }

        // Logs that only grow, in a fixed order, with entries that differ
        // between timelines. A save that lacks one simply has no entries in it.
        private static readonly LogSpec[] specs =
        {
            // Stock, career only. Contract GUIDs are random.
            new LogSpec { id = "contracts", scenario = "ContractSystem", container = "CONTRACTS", entry = "CONTRACT_FINISHED", key = "guid" },
            // Stock, career and science. Ids are not random, so this narrows
            // candidates more than it identifies a timeline.
            new LogSpec { id = "tech", scenario = "ResearchAndDevelopment", container = null, entry = "Tech", key = "id" },
            // RP-1. Each launch has a random id.
            new LogSpec { id = "launches", scenario = "CareerLog", container = "_launchedVessels", entry = null, key = "LaunchID" },
        };

        /// <summary>
        /// A node, anywhere inside one SCENARIO, that describes one dated
        /// event or one piece of work in progress.
        /// </summary>
        private class ItemSpec
        {
            public string scenario;
            public string node;
            // The values holding the item's name and its date or progress.
            // With both null, every value of the node is an item of its own,
            // named by its key, and -1 means it has not happened.
            public string idKey;
            public string valueKey;
            // The event source to file it under, or null for progress.
            public string source;
        }

        private static readonly ItemSpec[] items =
        {
            // Stock strategies. RP-1 appoints leaders as strategies, so this
            // covers who is appointed and since when. Removed when ended.
            new ItemSpec { scenario = "StrategySystem", node = "STRATEGY", idKey = "name", valueKey = "date", source = "strategies" },
            // RP-1 programs, which move from active to completed and stay.
            new ItemSpec { scenario = "ProgramHandler", node = "ACTIVEPROGRAM", idKey = "name", valueKey = "acceptedUT", source = "programs" },
            new ItemSpec { scenario = "ProgramHandler", node = "COMPLETEDPROGRAM", idKey = "name", valueKey = "acceptedUT", source = "programs" },
            // RP-1: when each strategy ended, which is what leader
            // reappointment cooldowns count from.
            new ItemSpec { scenario = "ProgramHandler", node = "ActivatedStrategies", source = "ended" },
            // RP-1 construction and integration.
            new ItemSpec { scenario = "SpaceCenterManagement", node = "LCConstructionProject", idKey = "modId", valueKey = "progress" },
            new ItemSpec { scenario = "SpaceCenterManagement", node = "FacilityUpgradeProject", idKey = "uid", valueKey = "progress" },
            new ItemSpec { scenario = "SpaceCenterManagement", node = "VesselProject", idKey = "shipID", valueKey = "progress" },
        };

        private static bool HasItems(string scenario)
        {
            for (int i = 0; i < items.Length; i++)
                if (items[i].scenario == scenario) return true;
            return false;
        }

        private void AddItem(ItemSpec spec, string id, double value)
        {
            if (spec.source == null)
            {
                progress[id] = value;
                return;
            }
            Dictionary<string, double> source;
            if (!events.TryGetValue(spec.source, out source))
                events[spec.source] = source = new Dictionary<string, double>(StringComparer.Ordinal);
            source[id] = value;
        }

        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>
        /// Read a fingerprint from a save file without building a node tree.
        /// Uses no game or Unity types and no shared state, so it is safe to
        /// call from a worker thread.
        ///
        /// Nearly all of a save is vessel part data, nested deeper than
        /// anything read here. The file is therefore scanned as bytes, and a
        /// line only becomes a string where its text can matter: at the top
        /// few levels, or when it is the value that identifies a log entry.
        /// Below that, lines are only checked for being a brace. Every byte
        /// tested is ASCII, which never occurs inside a multi-byte UTF-8
        /// character, so nothing needs decoding to find the structure.
        /// </summary>
        public static Fingerprint Read(Stream stream)
        {
            var print = new Fingerprint();
            var hashes = new Dictionary<string, List<ulong>>(StringComparer.Ordinal);
            var running = new Dictionary<string, ulong>(StringComparer.Ordinal);

            // Names of the enclosing nodes, kept only for the levels that are
            // ever looked at. depth counts every level.
            var names = new string[NamedLevels];
            int depth = 0;
            string previous = "";
            // Set while inside a SCENARIO whose name has been read.
            string scenario = null;
            // Set while inside a node that is an entry of this log and whose
            // identifying value has not been seen yet.
            LogSpec pending = null;
            int pendingDepth = 0;
            // The vessel being read, when directly inside one.
            string vesselId = null;
            double vesselLaunch = double.NaN;
            // Set while inside a scenario that has item nodes, which are
            // looked for at any depth; such scenarios are small, so every
            // line in them is decoded.
            bool wide = false;
            ItemSpec item = null;
            int itemDepth = 0;
            string itemId = null;
            double itemValue = double.NaN;

            var buffer = new byte[1 << 20];
            int length = 0;
            bool end = false;
            bool first = true;

            while (true)
            {
                while (!end && length < buffer.Length)
                {
                    int got = stream.Read(buffer, length, buffer.Length - length);
                    if (got <= 0) end = true;
                    else length += got;
                }

                int pos = 0;
                if (first)
                {
                    first = false;
                    if (length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF) pos = 3;
                }

                while (pos < length)
                {
                    int newline = Array.IndexOf(buffer, (byte)'\n', pos, length - pos);
                    int lineEnd;
                    if (newline >= 0) lineEnd = newline;
                    else if (end) lineEnd = length;
                    else break;

                    int s = pos, e = lineEnd;
                    pos = newline >= 0 ? newline + 1 : length;
                    while (s < e && IsSpace(buffer[s])) s++;
                    while (e > s && IsSpace(buffer[e - 1])) e--;
                    if (s == e) continue;

                    if (e - s == 1 && buffer[s] == (byte)'{')
                    {
                        if (depth < NamedLevels) names[depth] = previous;
                        depth++;
                        if (depth == 2 && names[0] == "GAME")
                        {
                            scenario = null;
                            wide = false;
                        }
                        if (wide && item == null)
                        {
                            for (int i = 0; i < items.Length; i++)
                            {
                                if (items[i].scenario != scenario || items[i].node != previous) continue;
                                item = items[i];
                                itemDepth = depth;
                                itemId = null;
                                itemValue = double.NaN;
                                break;
                            }
                        }
                        if (depth == 3 && previous == "VESSEL" && names[1] == "FLIGHTSTATE")
                        {
                            vesselId = null;
                            vesselLaunch = double.NaN;
                        }
                        if (scenario != null && pending == null && depth <= NamedLevels)
                        {
                            for (int i = 0; i < specs.Length; i++)
                            {
                                LogSpec spec = specs[i];
                                if (spec.scenario != scenario) continue;
                                int entryDepth = spec.container == null ? 3 : 4;
                                if (depth != entryDepth) continue;
                                if (spec.container != null && names[2] != spec.container) continue;
                                if (spec.entry != null && previous != spec.entry) continue;
                                pending = spec;
                                pendingDepth = depth;
                                break;
                            }
                        }
                        continue;
                    }
                    if (e - s == 1 && buffer[s] == (byte)'}')
                    {
                        if (depth > 0) depth--;
                        if (pending != null && depth < pendingDepth) pending = null;
                        if (item != null && depth < itemDepth)
                        {
                            if (itemId != null && !double.IsNaN(itemValue)) print.AddItem(item, itemId, itemValue);
                            item = null;
                        }
                        if (depth < 2)
                        {
                            scenario = null;
                            wide = false;
                        }
                        continue;
                    }

                    if (wide && depth > 2)
                    {
                        string text = Encoding.UTF8.GetString(buffer, s, e - s);
                        previous = text;
                        if (item == null || depth != itemDepth) continue;
                        int eq = text.IndexOf(" = ", StringComparison.Ordinal);
                        if (eq < 0) continue;
                        string key = text.Substring(0, eq);
                        double number;
                        if (item.idKey == null)
                        {
                            // Seeing any value at all shows the record is
                            // there, even if nothing in it has happened yet.
                            if (!print.events.ContainsKey(item.source))
                                print.events[item.source] = new Dictionary<string, double>(StringComparer.Ordinal);
                            if (double.TryParse(text.Substring(eq + 3), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && number != -1)
                                print.AddItem(item, key, number);
                        }
                        else if (key == item.idKey)
                        {
                            if (itemId == null) itemId = text.Substring(eq + 3);
                        }
                        else if (key == item.valueKey && double.IsNaN(itemValue))
                        {
                            if (!double.TryParse(text.Substring(eq + 3), NumberStyles.Float, CultureInfo.InvariantCulture, out itemValue))
                                itemValue = double.NaN;
                        }
                        continue;
                    }

                    // A line this deep can be neither a node name nor a
                    // value that is looked at, unless it is the one value
                    // being waited for.
                    if (depth >= NamedLevels)
                    {
                        if (pending != null && depth == pendingDepth)
                        {
                            string found = ValueOf(buffer, s, e, pending.keyBytes);
                            if (found != null)
                            {
                                AddEntry(pending.id, found, running, hashes);
                                pending = null;
                            }
                        }
                        continue;
                    }

                    string line = Encoding.UTF8.GetString(buffer, s, e - s);
                    previous = line;
                    if (depth < 2 || names[0] != "GAME") continue;
                    if (depth == 2)
                    {
                        if (names[1] == "SCENARIO" && scenario == null && line.StartsWith("name = ", StringComparison.Ordinal))
                        {
                            scenario = line.Substring(7);
                            wide = HasItems(scenario);
                        }
                        else if (names[1] == "FLIGHTSTATE" && print.ut < 0 && line.StartsWith("UT = ", StringComparison.Ordinal))
                            double.TryParse(line.Substring(5), NumberStyles.Float, CultureInfo.InvariantCulture, out print.ut);
                        continue;
                    }
                    if (names[1] == "FLIGHTSTATE" && names[2] == "VESSEL")
                    {
                        if (vesselId == null && line.StartsWith("pid = ", StringComparison.Ordinal))
                            vesselId = line.Substring(6);
                        else if (double.IsNaN(vesselLaunch) && line.StartsWith("lct = ", StringComparison.Ordinal)
                            && double.TryParse(line.Substring(6), NumberStyles.Float, CultureInfo.InvariantCulture, out vesselLaunch)
                            && vesselId != null)
                            print.vessels[vesselId] = vesselLaunch;
                        continue;
                    }
                    if (pending != null && depth == pendingDepth)
                    {
                        string found = ValueOf(buffer, s, e, pending.keyBytes);
                        if (found != null)
                        {
                            AddEntry(pending.id, found, running, hashes);
                            pending = null;
                        }
                    }
                }

                if (pos >= length && end) break;

                // Keep the unfinished line and read on. A line longer than
                // the whole buffer needs a bigger one.
                int rest = length - pos;
                if (pos == 0 && rest == buffer.Length)
                    Array.Resize(ref buffer, buffer.Length * 2);
                else if (rest > 0)
                    Buffer.BlockCopy(buffer, pos, buffer, 0, rest);
                length = rest;
            }

            foreach (KeyValuePair<string, List<ulong>> log in hashes)
                print.logs[log.Key] = log.Value.ToArray();
            return print;
        }

        // Entries are at most this deep, and so is everything else read.
        private const int NamedLevels = 4;

        private static bool IsSpace(byte b)
        {
            return b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r';
        }

        /// <summary>
        /// The value of the line in buffer[s..e) if it is "key = value" for
        /// the given key, otherwise null.
        /// </summary>
        private static string ValueOf(byte[] buffer, int s, int e, byte[] key)
        {
            if (e - s <= key.Length) return null;
            for (int i = 0; i < key.Length; i++)
                if (buffer[s + i] != key[i]) return null;
            return Encoding.UTF8.GetString(buffer, s + key.Length, e - s - key.Length);
        }

        private static void AddEntry(string id, string value, Dictionary<string, ulong> running, Dictionary<string, List<ulong>> hashes)
        {
            ulong h;
            if (!running.TryGetValue(id, out h)) h = FnvOffset;
            for (int i = 0; i < value.Length; i++)
            {
                h = (h ^ (byte)value[i]) * FnvPrime;
                h = (h ^ (byte)(value[i] >> 8)) * FnvPrime;
            }
            h = (h ^ 0xff) * FnvPrime;
            running[id] = h;
            List<ulong> list;
            if (!hashes.TryGetValue(id, out list)) hashes[id] = list = new List<ulong>();
            list.Add(h);
        }
    }

    /// <summary>
    /// Fingerprints already computed for a save folder, so that a later scan
    /// only has to read files that are new or have changed. Persisted beside
    /// the index. Losing it costs a rescan and nothing else.
    /// </summary>
    public class FingerprintCache
    {
        public class Item
        {
            public long mtime;
            public long size;
            public Fingerprint print;
        }

        // Bumped whenever a fingerprint gains or changes a field, so that
        // fingerprints read by an older version are read again.
        private const string Version = "3";

        public readonly Dictionary<string, Item> items = new Dictionary<string, Item>(StringComparer.Ordinal);

        public Fingerprint Get(FileStamp stamp)
        {
            Item item;
            if (items.TryGetValue(stamp.name, out item) && item.mtime == stamp.mtime && item.size == stamp.size)
                return item.print;
            return null;
        }

        public void Put(FileStamp stamp, Fingerprint print)
        {
            items[stamp.name] = new Item { mtime = stamp.mtime, size = stamp.size, print = print };
        }

        public string Serialize()
        {
            var names = new List<string>(items.Keys);
            names.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            sb.Append("version = ").Append(Version).Append('\n');
            for (int i = 0; i < names.Count; i++)
            {
                Item item = items[names[i]];
                sb.Append("SCAN\n{\n");
                sb.Append("\tname = ").Append(names[i]).Append('\n');
                sb.Append("\tmtime = ").Append(item.mtime.ToString(CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("\tsize = ").Append(item.size.ToString(CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("\tut = ").Append(item.print.ut.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                var ids = new List<string>(item.print.logs.Keys);
                ids.Sort(StringComparer.Ordinal);
                for (int j = 0; j < ids.Count; j++)
                {
                    sb.Append("\tlog = ").Append(ids[j]);
                    ulong[] log = item.print.logs[ids[j]];
                    for (int k = 0; k < log.Length; k++)
                        sb.Append(' ').Append(log[k].ToString("x16", CultureInfo.InvariantCulture));
                    sb.Append('\n');
                }
                var pids = new List<string>(item.print.vessels.Keys);
                pids.Sort(StringComparer.Ordinal);
                for (int j = 0; j < pids.Count; j++)
                    sb.Append("\tvessel = ").Append(pids[j]).Append(' ')
                        .Append(item.print.vessels[pids[j]].ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                var sources = new List<string>(item.print.events.Keys);
                sources.Sort(StringComparer.Ordinal);
                for (int j = 0; j < sources.Count; j++)
                {
                    Dictionary<string, double> source = item.print.events[sources[j]];
                    // A source can be present and empty, which is not the
                    // same as absent.
                    if (source.Count == 0) sb.Append("\tsource = ").Append(sources[j]).Append('\n');
                    var eventIds = new List<string>(source.Keys);
                    eventIds.Sort(StringComparer.Ordinal);
                    for (int k = 0; k < eventIds.Count; k++)
                        sb.Append("\tevent = ").Append(sources[j]).Append(' ')
                            .Append(source[eventIds[k]].ToString("R", CultureInfo.InvariantCulture)).Append(' ').Append(eventIds[k]).Append('\n');
                }
                var works = new List<string>(item.print.progress.Keys);
                works.Sort(StringComparer.Ordinal);
                for (int j = 0; j < works.Count; j++)
                    sb.Append("\tprogress = ").Append(item.print.progress[works[j]].ToString("R", CultureInfo.InvariantCulture))
                        .Append(' ').Append(works[j]).Append('\n');
                sb.Append("}\n");
            }
            return sb.ToString();
        }

        public static FingerprintCache Parse(string text)
        {
            var cache = new FingerprintCache();
            if (string.IsNullOrEmpty(text)) return cache;

            string name = null;
            Item item = null;
            string[] lines = text.Split('\n');
            if (lines[0].TrimEnd('\r') != "version = " + Version) return cache;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                string trimmed = line.Trim();
                if (trimmed == "{")
                {
                    name = null;
                    item = new Item { print = new Fingerprint() };
                    continue;
                }
                if (trimmed == "}")
                {
                    if (item != null && !string.IsNullOrEmpty(name)) cache.items[name] = item;
                    item = null;
                    continue;
                }
                if (item == null) continue;

                string body = line.TrimStart('\t', ' ');
                int eq = body.IndexOf(" = ", StringComparison.Ordinal);
                if (eq < 0) continue;
                string key = body.Substring(0, eq);
                string value = body.Substring(eq + 3);
                switch (key)
                {
                    case "name": name = value; break;
                    case "mtime": long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out item.mtime); break;
                    case "size": long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out item.size); break;
                    case "ut": double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out item.print.ut); break;
                    case "log":
                        string[] parts = value.Split(' ');
                        var log = new ulong[parts.Length - 1];
                        bool ok = true;
                        for (int k = 1; k < parts.Length; k++)
                            ok &= ulong.TryParse(parts[k], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out log[k - 1]);
                        if (ok) item.print.logs[parts[0]] = log;
                        break;
                    case "source":
                        if (!item.print.events.ContainsKey(value))
                            item.print.events[value] = new Dictionary<string, double>(StringComparer.Ordinal);
                        break;
                    case "event":
                    {
                        // source, date, then the name, which may hold spaces.
                        string[] f = value.Split(new[] { ' ' }, 3);
                        double when;
                        if (f.Length == 3 && double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out when))
                        {
                            Dictionary<string, double> source;
                            if (!item.print.events.TryGetValue(f[0], out source))
                                item.print.events[f[0]] = source = new Dictionary<string, double>(StringComparer.Ordinal);
                            source[f[2]] = when;
                        }
                        break;
                    }
                    case "progress":
                    {
                        string[] f = value.Split(new[] { ' ' }, 2);
                        double amount;
                        if (f.Length == 2 && double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
                            item.print.progress[f[1]] = amount;
                        break;
                    }
                    case "vessel":
                        int space = value.LastIndexOf(' ');
                        double launch;
                        if (space > 0 && double.TryParse(value.Substring(space + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out launch))
                            item.print.vessels[value.Substring(0, space)] = launch;
                        break;
                }
            }
            return cache;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SaveTimelines
{
    /// <summary>
    /// A reference to one generation of a save name. The name alone identifies
    /// a file slot; <see cref="created"/> tells generations of that slot apart,
    /// so a reference held in memory can be recognized as stale after the name
    /// has been overwritten.
    /// </summary>
    public struct SaveRef
    {
        public string name;
        public long created;

        public SaveRef(string name, long created)
        {
            this.name = name;
            this.created = created;
        }
    }

    /// <summary>What the file system says about one save file.</summary>
    public struct FileStamp
    {
        public string name;
        public long mtime;
        public long size;

        public FileStamp(string name, long mtime, long size)
        {
            this.name = name;
            this.mtime = mtime;
            this.size = size;
        }
    }

    public class SaveEntry
    {
        public string name;

        /// <summary>
        /// Wall clock ticks (UTC) when the save was recorded. For saves that
        /// were found on disk rather than seen being written, the file's mtime.
        /// </summary>
        public long created;

        public long mtime;
        public long size;

        /// <summary>
        /// True when the write was observed, so <see cref="ancestors"/> is a
        /// record rather than an absence of information.
        /// </summary>
        public bool tracked;

        /// <summary>
        /// True when <see cref="ancestors"/> was guessed from the save's
        /// contents by a scan rather than recorded.
        /// </summary>
        public bool inferred;

        /// <summary>
        /// True when the player has collapsed the group this save is the
        /// first row of. Ignored while the save does not start a group.
        /// </summary>
        public bool collapsed;

        /// <summary>Names of ancestor saves, nearest first.</summary>
        public List<string> ancestors = new List<string>();
    }

    /// <summary>
    /// The lineage of every save in one save folder, keyed by save name
    /// (file name without extension). Persisted as a sidecar file. Contains no
    /// game or Unity types, so it can be unit tested without the game.
    /// </summary>
    public class SaveIndex
    {
        public readonly Dictionary<string, SaveEntry> entries =
            new Dictionary<string, SaveEntry>(StringComparer.Ordinal);

        /// <summary>Set whenever the index differs from what was last serialized.</summary>
        public bool dirty;

        /// <summary>
        /// Bring the index in line with the files actually present. Entries for
        /// files that are gone or no longer match their recorded stamp are
        /// dropped, and their names are removed from every ancestor list, so a
        /// name reused later can never be mistaken for the old save. Files with
        /// no entry are added as untracked.
        /// </summary>
        public void Reconcile(IList<FileStamp> files)
        {
            var present = new Dictionary<string, FileStamp>(StringComparer.Ordinal);
            for (int i = 0; i < files.Count; i++)
                present[files[i].name] = files[i];

            List<string> stale = null;
            foreach (SaveEntry e in entries.Values)
            {
                FileStamp f;
                if (!present.TryGetValue(e.name, out f) || f.mtime != e.mtime || f.size != e.size)
                {
                    if (stale == null) stale = new List<string>();
                    stale.Add(e.name);
                }
            }

            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                    entries.Remove(stale[i]);
                Strip(stale);
                dirty = true;
            }

            foreach (FileStamp f in present.Values)
            {
                if (entries.ContainsKey(f.name)) continue;
                entries[f.name] = new SaveEntry
                {
                    name = f.name,
                    created = f.mtime,
                    mtime = f.mtime,
                    size = f.size,
                    tracked = false,
                };
                dirty = true;
            }
        }

        /// <summary>
        /// The chain of references starting at <paramref name="name"/> and
        /// continuing through its recorded ancestors, nearest first. Empty if
        /// the name is unknown.
        /// </summary>
        public List<SaveRef> Lineage(string name)
        {
            var chain = new List<SaveRef>();
            SaveEntry e;
            if (!entries.TryGetValue(name, out e)) return chain;
            chain.Add(new SaveRef(e.name, e.created));
            for (int i = 0; i < e.ancestors.Count; i++)
            {
                SaveEntry a;
                if (entries.TryGetValue(e.ancestors[i], out a))
                    chain.Add(new SaveRef(a.name, a.created));
            }
            return chain;
        }

        /// <summary>
        /// The references in <paramref name="chain"/> that still point at the
        /// generation they were taken from, in the same order.
        /// </summary>
        public List<SaveRef> Validate(IList<SaveRef> chain)
        {
            var valid = new List<SaveRef>();
            if (chain == null) return valid;
            for (int i = 0; i < chain.Count; i++)
            {
                SaveEntry e;
                if (entries.TryGetValue(chain[i].name, out e) && e.created == chain[i].created)
                    valid.Add(chain[i]);
            }
            return valid;
        }

        /// <summary>
        /// Record that <paramref name="stamp"/>.name has just been written by a
        /// game whose lineage was <paramref name="head"/>. Whatever previously
        /// had that name is gone, so the name is dropped from the new save's own
        /// ancestry and from every other save's. Returns the lineage of the new
        /// save, which is the caller's new head.
        /// </summary>
        public List<SaveRef> RecordWrite(IList<SaveRef> head, long now, FileStamp stamp)
        {
            List<SaveRef> valid = Validate(head);
            for (int i = valid.Count - 1; i >= 0; i--)
                if (valid[i].name == stamp.name)
                    valid.RemoveAt(i);

            entries.Remove(stamp.name);
            Strip(new List<string> { stamp.name });

            var entry = new SaveEntry
            {
                name = stamp.name,
                created = now,
                mtime = stamp.mtime,
                size = stamp.size,
                tracked = true,
            };
            for (int i = 0; i < valid.Count; i++)
                entry.ancestors.Add(valid[i].name);
            entries[stamp.name] = entry;
            dirty = true;

            var lineage = new List<SaveRef>(valid.Count + 1);
            lineage.Add(new SaveRef(entry.name, entry.created));
            lineage.AddRange(valid);
            return lineage;
        }

        private void Strip(List<string> names)
        {
            foreach (SaveEntry e in entries.Values)
                for (int i = e.ancestors.Count - 1; i >= 0; i--)
                    if (names.Contains(e.ancestors[i]))
                        e.ancestors.RemoveAt(i);
        }

        // The sidecar uses the same node syntax as the game's own files, so it
        // can be read by eye or by ConfigNode, but it is parsed here by hand to
        // keep this class free of game types.

        public string Serialize()
        {
            var names = new List<string>(entries.Keys);
            names.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                SaveEntry e = entries[names[i]];
                sb.Append("SAVE\n{\n");
                sb.Append("\tname = ").Append(e.name).Append('\n');
                sb.Append("\tcreated = ").Append(e.created.ToString(CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("\tmtime = ").Append(e.mtime.ToString(CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("\tsize = ").Append(e.size.ToString(CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("\ttracked = ").Append(e.tracked ? "True" : "False").Append('\n');
                if (e.inferred) sb.Append("\tinferred = True\n");
                if (e.collapsed) sb.Append("\tcollapsed = True\n");
                for (int j = 0; j < e.ancestors.Count; j++)
                    sb.Append("\tancestor = ").Append(e.ancestors[j]).Append('\n');
                sb.Append("}\n");
            }
            return sb.ToString();
        }

        public static SaveIndex Parse(string text)
        {
            var index = new SaveIndex();
            if (string.IsNullOrEmpty(text)) return index;

            SaveEntry current = null;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                string trimmed = line.Trim();
                if (trimmed == "{")
                {
                    current = new SaveEntry();
                    continue;
                }
                if (trimmed == "}")
                {
                    if (current != null && !string.IsNullOrEmpty(current.name))
                        index.entries[current.name] = current;
                    current = null;
                    continue;
                }
                if (current == null) continue;

                // Only the indentation is trimmed from the value, so a name
                // with trailing spaces survives a round trip.
                string body = line.TrimStart('\t', ' ');
                int eq = body.IndexOf(" = ", StringComparison.Ordinal);
                if (eq < 0) continue;
                string key = body.Substring(0, eq);
                string value = body.Substring(eq + 3);
                switch (key)
                {
                    case "name": current.name = value; break;
                    case "created": long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out current.created); break;
                    case "mtime": long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out current.mtime); break;
                    case "size": long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out current.size); break;
                    case "tracked": current.tracked = value == "True"; break;
                    case "inferred": current.inferred = value == "True"; break;
                    case "collapsed": current.collapsed = value == "True"; break;
                    case "ancestor": current.ancestors.Add(value); break;
                }
            }
            return index;
        }
    }
}

using System;
using System.Collections.Generic;

namespace SaveTimelines
{
    /// <summary>
    /// Guesses a parent for each save that has none, from fingerprints. A
    /// save's parent is taken to be the save with the most history that is
    /// still entirely contained in its own, among saves that are not later
    /// in game time and whose file is not newer.
    ///
    /// This is a guess. Saves with no logged event between them can only be
    /// told apart by the vessels in flight, which is weaker evidence, so a
    /// save can still be placed after a sibling on another branch. File dates
    /// are trusted, so a save restored from a backup can lose its children to
    /// another parent. Guessed links are flagged, and are recomputed by every
    /// scan.
    /// </summary>
    public static class Inference
    {
        private class Candidate
        {
            public SaveEntry entry;
            public Fingerprint print;
        }

        /// <summary>
        /// Assign guessed ancestors to every parentless entry that has a
        /// fingerprint. Returns how many entries were given a parent.
        /// </summary>
        public static int Apply(SaveIndex index, Func<string, Fingerprint> prints)
        {
            // Earlier guesses are thrown away first, so that a scan does not
            // build on its own previous output.
            foreach (SaveEntry e in index.entries.Values)
            {
                if (!e.inferred) continue;
                e.inferred = false;
                e.ancestors.Clear();
                index.dirty = true;
            }

            var all = new List<Candidate>();
            foreach (SaveEntry e in index.entries.Values)
            {
                Fingerprint p = prints(e.name);
                if (p != null) all.Add(new Candidate { entry = e, print = p });
            }
            // Ascending by game time. A parent is only ever looked for among
            // the saves sorted before its child, which rules out two saves
            // each being chosen as the other's parent, and means a parent's
            // own ancestry is settled before it is copied.
            all.Sort(Compare);

            int assigned = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Candidate orphan = all[i];
                if (HasLiveAncestor(index, orphan.entry)) continue;

                Candidate best = null;
                int bestShared = -1;
                for (int j = i - 1; j >= 0; j--)
                {
                    Candidate c = all[j];
                    // A parent exists before its child is written.
                    if (c.entry.created > orphan.entry.created) continue;
                    if (!c.print.IsPrefixOf(orphan.print)) continue;
                    if (c.print.DivergesFrom(orphan.print)) continue;
                    if (c.print.Contradicts(orphan.print)) continue;
                    if (c.entry.ancestors.Contains(orphan.entry.name)) continue;

                    // Most shared history wins. Walking down from the
                    // latest game time makes that the tie-break.
                    int shared = c.print.SharedTotal(orphan.print);
                    if (shared <= bestShared) continue;
                    best = c;
                    bestShared = shared;
                }
                if (best == null) continue;

                orphan.entry.ancestors.Clear();
                orphan.entry.ancestors.Add(best.entry.name);
                for (int k = 0; k < best.entry.ancestors.Count; k++)
                    if (best.entry.ancestors[k] != orphan.entry.name)
                        orphan.entry.ancestors.Add(best.entry.ancestors[k]);
                orphan.entry.inferred = true;
                index.dirty = true;
                assigned++;
            }
            return assigned;
        }

        private static bool HasLiveAncestor(SaveIndex index, SaveEntry e)
        {
            for (int i = 0; i < e.ancestors.Count; i++)
                if (e.ancestors[i] != e.name && index.entries.ContainsKey(e.ancestors[i]))
                    return true;
            return false;
        }

        private static int Compare(Candidate a, Candidate b)
        {
            int c = a.print.ut.CompareTo(b.print.ut);
            if (c != 0) return c;
            c = a.entry.created.CompareTo(b.entry.created);
            if (c != 0) return c;
            return string.CompareOrdinal(a.entry.name, b.entry.name);
        }
    }
}

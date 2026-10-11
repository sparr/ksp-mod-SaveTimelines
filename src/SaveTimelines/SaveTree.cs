using System;
using System.Collections.Generic;

namespace SaveTimelines
{
    /// <summary>One line of the load dialog, in display order.</summary>
    public struct TreeRow
    {
        public string name;

        /// <summary>Nesting level; 0 is the top level.</summary>
        public int depth;

        /// <summary>
        /// Identifies the timeline group the row belongs to, numbered in
        /// display order. -1 for saves with no known lineage.
        /// </summary>
        public int group;

        /// <summary>True for the first row of its group.</summary>
        public bool groupStart;

        /// <summary>True for the last row of its group.</summary>
        public bool groupEnd;

        /// <summary>
        /// The groups the row is inside, outermost first, ending with its
        /// own; one per level, so the length is depth + 1. Null for saves
        /// with no known lineage. Rows of one group share the array.
        /// </summary>
        public int[] path;

        /// <summary>True when the row's position comes from a guessed parent.</summary>
        public bool inferred;
    }

    /// <summary>
    /// Turns the parent links in a <see cref="SaveIndex"/> into the ordered,
    /// indented list the load dialog shows.
    ///
    /// A chain of saves, each the parent of the next, is one group and shares a
    /// depth. Where a save has several children, the one whose descendants
    /// include the most recently written save continues the group. Each other
    /// child starts a group of its own, nested one level under that save and
    /// listed directly after it, most recently played first. The newest save
    /// in a tree is therefore always at the top level, with all its ancestors.
    /// </summary>
    public static class SaveTree
    {
        private struct Frame
        {
            public SaveEntry node;
            public int depth;
            public int group;
            public bool start;

            // For a row that starts a group, the path of the row it hangs
            // from. Otherwise the row's own path.
            public int[] path;
        }

        public static List<TreeRow> Build(SaveIndex index)
        {
            var children = new Dictionary<string, List<SaveEntry>>(StringComparer.Ordinal);
            var roots = new List<SaveEntry>();
            foreach (SaveEntry e in index.entries.Values)
            {
                string parent = null;
                for (int i = 0; i < e.ancestors.Count; i++)
                {
                    if (e.ancestors[i] != e.name && index.entries.ContainsKey(e.ancestors[i]))
                    {
                        parent = e.ancestors[i];
                        break;
                    }
                }
                if (parent == null)
                {
                    roots.Add(e);
                    continue;
                }
                List<SaveEntry> list;
                if (!children.TryGetValue(parent, out list))
                    children[parent] = list = new List<SaveEntry>();
                list.Add(e);
            }

            // For every save, when the newest save among it and its
            // descendants was written. Visiting parents before children and
            // then walking that order backwards settles each child before
            // its parent.
            var newest = new Dictionary<string, long>(StringComparer.Ordinal);
            var order = new List<SaveEntry>(index.entries.Count);
            var pendingNodes = new Stack<SaveEntry>(roots);
            while (pendingNodes.Count > 0)
            {
                SaveEntry e = pendingNodes.Pop();
                if (newest.ContainsKey(e.name)) continue;
                newest[e.name] = e.created;
                order.Add(e);
                List<SaveEntry> kids;
                if (children.TryGetValue(e.name, out kids))
                    for (int k = 0; k < kids.Count; k++)
                        pendingNodes.Push(kids[k]);
            }
            for (int i = order.Count - 1; i >= 0; i--)
            {
                List<SaveEntry> kids;
                if (!children.TryGetValue(order[i].name, out kids)) continue;
                long latest = newest[order[i].name];
                for (int k = 0; k < kids.Count; k++)
                {
                    long n;
                    if (newest.TryGetValue(kids[k].name, out n) && n > latest) latest = n;
                }
                newest[order[i].name] = latest;
            }

            Comparison<SaveEntry> byNewest = delegate(SaveEntry x, SaveEntry y)
            {
                long nx, ny;
                newest.TryGetValue(x.name, out nx);
                newest.TryGetValue(y.name, out ny);
                int c = nx.CompareTo(ny);
                return c != 0 ? c : ByCreated(x, y);
            };
            foreach (List<SaveEntry> list in children.Values)
                list.Sort(byNewest);

            // Saves that were never seen being written and have no descendants
            // carry no lineage at all. They are listed after the trees, in the
            // order the stock dialog would use.
            var trees = new List<SaveEntry>();
            var loose = new List<SaveEntry>();
            for (int i = 0; i < roots.Count; i++)
            {
                if (!roots[i].tracked && !roots[i].inferred && !children.ContainsKey(roots[i].name))
                    loose.Add(roots[i]);
                else
                    trees.Add(roots[i]);
            }
            trees.Sort(ByCreated);

            var rows = new List<TreeRow>(index.entries.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int nextGroup = 0;
            var stack = new Stack<Frame>();

            for (int t = 0; t < trees.Count; t++)
            {
                stack.Push(new Frame { node = trees[t], depth = 0, group = -1, start = true });
                while (stack.Count > 0)
                {
                    Frame f = stack.Pop();
                    if (!seen.Add(f.node.name)) continue;
                    int group = f.group;
                    int[] path = f.path;
                    if (f.start)
                    {
                        group = nextGroup++;
                        path = new int[f.depth + 1];
                        if (f.path != null) Array.Copy(f.path, path, f.depth);
                        path[f.depth] = group;
                    }
                    rows.Add(new TreeRow { name = f.node.name, depth = f.depth, group = group, groupStart = f.start, inferred = f.node.inferred, path = path });

                    List<SaveEntry> kids;
                    if (!children.TryGetValue(f.node.name, out kids)) continue;

                    // Sorted by most recent activity, so the last child is
                    // the one that continues this group.
                    int next = kids.Count - 1;

                    // Pushed in the reverse of display order: the continuing
                    // child comes after every other child's group has been
                    // listed, and those are listed newest first.
                    stack.Push(new Frame { node = kids[next], depth = f.depth, group = group, start = false, path = path });
                    for (int k = 0; k < kids.Count; k++)
                        if (k != next)
                            stack.Push(new Frame { node = kids[k], depth = f.depth + 1, group = -1, start = true, path = path });
                }
            }

            var lastOfGroup = new int[nextGroup];
            for (int i = 0; i < rows.Count; i++)
                lastOfGroup[rows[i].group] = i;
            for (int g = 0; g < nextGroup; g++)
            {
                TreeRow last = rows[lastOfGroup[g]];
                last.groupEnd = true;
                rows[lastOfGroup[g]] = last;
            }

            // Anything not reached is either loose or part of a cycle, which a
            // damaged index could contain. Either way it still has to be
            // listed, or the save would be impossible to load.
            foreach (SaveEntry e in index.entries.Values)
                if (!seen.Contains(e.name) && !loose.Contains(e))
                    loose.Add(e);
            loose.Sort(ByName);
            for (int i = 0; i < loose.Count; i++)
                rows.Add(new TreeRow { name = loose[i].name, depth = 0, group = -1, groupStart = false });

            return rows;
        }

        private static int ByCreated(SaveEntry a, SaveEntry b)
        {
            int c = a.created.CompareTo(b.created);
            return c != 0 ? c : string.CompareOrdinal(a.name, b.name);
        }

        private static int ByName(SaveEntry a, SaveEntry b)
        {
            return NaturalCompare(a.name, b.name);
        }

        /// <summary>
        /// Case-insensitive comparison that orders runs of digits by value, so
        /// "save 9" sorts before "save 10".
        /// </summary>
        public static int NaturalCompare(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string da = a.Substring(si, i - si).TrimStart('0');
                    string db = b.Substring(sj, j - sj).TrimStart('0');
                    if (da.Length != db.Length) return da.Length.CompareTo(db.Length);
                    int c = string.CompareOrdinal(da, db);
                    if (c != 0) return c;
                }
                else
                {
                    char ca = char.ToUpperInvariant(a[i]);
                    char cb = char.ToUpperInvariant(b[j]);
                    if (ca != cb) return ca.CompareTo(cb);
                    i++;
                    j++;
                }
            }
            int rest = (a.Length - i).CompareTo(b.Length - j);
            return rest != 0 ? rest : string.CompareOrdinal(a, b);
        }
    }
}

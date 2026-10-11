using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SaveTimelines.Tests
{
    /// <summary>
    /// Plays the part of the game: keeps a head lineage and a set of files,
    /// and feeds the index the same calls the hooks make.
    /// </summary>
    internal class FakeGame
    {
        public readonly SaveIndex index = new SaveIndex();
        public readonly Dictionary<string, FileStamp> files = new Dictionary<string, FileStamp>();
        public List<SaveRef> head = new List<SaveRef>();
        private long clock = 1000;

        public void Save(string name)
        {
            long now = clock++;
            var stamp = new FileStamp(name, now, 100 + now);
            files[name] = stamp;
            head = index.RecordWrite(head, now, stamp);
        }

        public void Load(string name)
        {
            Reconcile();
            head = index.Lineage(name);
        }

        /// <summary>A file appearing without the hooks seeing it.</summary>
        public void Drop(string name)
        {
            long now = clock++;
            files[name] = new FileStamp(name, now, 100 + now);
        }

        public void Delete(string name)
        {
            files.Remove(name);
        }

        public void Reconcile()
        {
            index.Reconcile(files.Values.ToList());
        }

        public string Tree()
        {
            Reconcile();
            return string.Join(" ", SaveTree.Build(index).Select(r => new string('>', r.depth) + r.name));
        }
    }

    public class SaveTreeTests
    {
        [Fact]
        public void ConsecutiveSavesAreSiblings()
        {
            var g = new FakeGame();
            g.Save("A"); g.Save("B"); g.Save("C");
            Assert.Equal("A B C", g.Tree());
        }

        [Fact]
        public void ForkDemotesTheOldContinuation()
        {
            var g = new FakeGame();
            g.Save("A"); g.Save("B"); g.Save("C");
            g.Load("B");
            g.Save("D"); g.Save("E");
            Assert.Equal("A B >C D E", g.Tree());
        }

        [Fact]
        public void SecondForkListsChildGroupsNewestFirst()
        {
            var g = new FakeGame();
            g.Save("A"); g.Save("B"); g.Save("C");
            g.Load("B");
            g.Save("D"); g.Save("E");
            g.Load("B");
            g.Save("F");
            Assert.Equal("A B >D >E >C F", g.Tree());

            List<TreeRow> rows = SaveTree.Build(g.index);
            TreeRow d = rows.Single(r => r.name == "D");
            TreeRow e = rows.Single(r => r.name == "E");
            TreeRow c = rows.Single(r => r.name == "C");
            Assert.True(d.groupStart);
            Assert.False(e.groupStart);
            Assert.True(c.groupStart);
            Assert.Equal(d.group, e.group);
            Assert.NotEqual(d.group, c.group);

            // A, B and F are one top-level group; D and E end theirs at E.
            TreeRow a = rows.Single(r => r.name == "A");
            TreeRow b = rows.Single(r => r.name == "B");
            TreeRow f = rows.Single(r => r.name == "F");
            Assert.True(a.groupStart);
            Assert.False(a.groupEnd);
            Assert.False(b.groupEnd);
            Assert.True(f.groupEnd);
            Assert.False(d.groupEnd);
            Assert.True(e.groupEnd);
            Assert.True(c.groupStart && c.groupEnd);
            Assert.Equal(new[] { a.group }, a.path);
            Assert.Equal(new[] { a.group, d.group }, d.path);
            Assert.Equal(new[] { a.group, d.group }, e.path);
            Assert.Equal(new[] { a.group, c.group }, c.path);
        }

        [Fact]
        public void PathsNameEverySurroundingGroup()
        {
            var g = new FakeGame();
            g.Save("A"); g.Save("B"); g.Save("C");
            g.Load("B");
            g.Save("D"); g.Save("E");
            g.Load("D");
            g.Save("H");
            g.Load("A");
            g.Save("Z");
            // A >B >>C >D >>E >H Z
            List<TreeRow> rows = SaveTree.Build(g.index);
            Assert.All(rows, r => Assert.Equal(r.depth + 1, r.path.Length));
            Assert.All(rows, r => Assert.Equal(r.group, r.path[r.depth]));
            TreeRow b = rows.Single(r => r.name == "B");
            TreeRow e = rows.Single(r => r.name == "E");
            TreeRow h = rows.Single(r => r.name == "H");
            Assert.Equal(b.group, e.path[1]);
            Assert.Equal(b.group, h.group);
            Assert.True(h.groupEnd);
            Assert.True(e.groupStart && e.groupEnd);

            g.Drop("loose");
            g.Reconcile();
            Assert.Null(SaveTree.Build(g.index).Single(r => r.name == "loose").path);
        }

        [Fact]
        public void SavingOnAnOldBranchMakesItPrimary()
        {
            var g = new FakeGame();
            g.Save("A"); g.Save("B"); g.Save("C");
            g.Load("B");
            g.Save("D");
            Assert.Equal("A B >C D", g.Tree());
            // Going back to the abandoned branch and playing on promotes it.
            g.Load("C");
            g.Save("G");
            Assert.Equal("A B >D C G", g.Tree());
        }

        [Fact]
        public void NewestSaveIsAlwaysAtTheTopLevel()
        {
            var g = new FakeGame();
            g.Save("A"); g.Save("B"); g.Save("C");
            g.Load("B");
            g.Save("D"); g.Save("E");
            g.Load("D");
            g.Save("H");
            g.Load("A");
            g.Save("Z");
            // Deep in an old branch, then one more save there.
            g.Load("C");
            g.Save("N");
            List<TreeRow> rows = SaveTree.Build(g.index);
            Assert.Equal(0, rows.Single(r => r.name == "N").depth);
            Assert.Equal(0, rows.Single(r => r.name == "C").depth);
            Assert.Equal(0, rows.Single(r => r.name == "B").depth);
            // Z was played more recently than the D branch, so it is listed
            // first of the two, although D was forked later.
            Assert.Equal("A >Z B >D >>E >H C N", g.Tree());
        }

        [Fact]
        public void DeadEndForkDoesNotDemoteALongBranch()
        {
            var g = new FakeGame();
            g.Save("A"); g.Save("B");
            g.Load("A");
            g.Save("stub");
            g.Load("B");
            g.Save("C");
            Assert.Equal("A >stub B C", g.Tree());
        }

        [Fact]
        public void NestedForks()
        {
            var g = new FakeGame();
            g.Save("A"); g.Save("B"); g.Save("C");
            g.Load("B");
            g.Save("D"); g.Save("E");
            g.Load("D");
            g.Save("H");
            g.Load("A");
            g.Save("Z");
            Assert.Equal("A >B >>C >D >>E >H Z", g.Tree());
        }

        [Fact]
        public void OverwritingTheHeadTakesItsParent()
        {
            var g = new FakeGame();
            g.Save("A");
            g.Save("quicksave");
            g.Save("quicksave");
            g.Save("quicksave");
            Assert.Equal("A quicksave", g.Tree());
            Assert.Equal(new[] { "A" }, g.index.entries["quicksave"].ancestors);
        }

        [Fact]
        public void OverwritingAnAncestorDoesNotMakeACycle()
        {
            var g = new FakeGame();
            g.Save("X");
            g.Save("quicksave");
            g.Save("Y");
            g.Save("quicksave");
            Assert.Equal("X Y quicksave", g.Tree());
            Assert.Equal(new[] { "X" }, g.index.entries["Y"].ancestors);
            Assert.Equal(new[] { "Y", "X" }, g.index.entries["quicksave"].ancestors);
        }

        [Fact]
        public void OverwriteOnAnotherBranchDetachesOldDescendants()
        {
            var g = new FakeGame();
            g.Save("X");
            g.Save("quicksave");
            g.Save("Y");
            g.Load("X");
            g.Save("quicksave");
            // Y descended from the old quicksave, which no longer exists, so
            // it hangs from X. It is older than the new quicksave.
            Assert.Equal("X >Y quicksave", g.Tree());
        }

        [Fact]
        public void DeletedAncestorIsSkipped()
        {
            var g = new FakeGame();
            g.Save("A"); g.Save("B"); g.Save("C");
            g.Delete("B");
            Assert.Equal("A C", g.Tree());
        }

        [Fact]
        public void NameReusedOutsideTheHooksIsNotAParent()
        {
            var g = new FakeGame();
            g.Save("A"); g.Save("B"); g.Save("C");
            g.Delete("B");
            g.Drop("B");
            // The new B is some unrelated file. C must not attach to it.
            Assert.Equal("A C B", g.Tree());
            Assert.False(g.index.entries["B"].tracked);
        }

        [Fact]
        public void StaleHeadFromASnapshotResolvesToSurvivors()
        {
            var g = new FakeGame();
            g.Save("A");
            g.Save("persistent");
            List<SaveRef> snapshot = new List<SaveRef>(g.head);
            g.Save("Q");
            g.Save("persistent");
            // Reverting restores the lineage captured at launch. The
            // persistent it names has since been overwritten.
            g.head = snapshot;
            g.Save("R");
            Assert.Equal(new[] { "A" }, g.index.entries["R"].ancestors);
            Assert.Equal("A >Q >persistent R", g.Tree());
        }

        [Fact]
        public void UntrackedSavesFollowTheTreesInNaturalOrder()
        {
            var g = new FakeGame();
            g.Drop("old 10"); g.Drop("old 9"); g.Drop("Old 1");
            g.Save("A"); g.Save("B");
            Assert.Equal("A B Old 1 old 9 old 10", g.Tree());
            Assert.All(SaveTree.Build(g.index).Where(r => r.name.ToLower().StartsWith("old")), r => Assert.Equal(-1, r.group));
        }

        [Fact]
        public void LoadingAnUntrackedSaveMakesItAParent()
        {
            var g = new FakeGame();
            g.Drop("legacy"); g.Drop("other");
            g.Load("legacy");
            g.Save("A");
            Assert.Equal("legacy A other", g.Tree());
        }

        [Fact]
        public void DamagedIndexWithACycleStillListsEverySave()
        {
            var g = new FakeGame();
            g.Save("A"); g.Save("B");
            g.index.entries["A"].ancestors.Add("B");
            List<string> names = SaveTree.Build(g.index).Select(r => r.name).ToList();
            Assert.Equal(2, names.Count);
            Assert.Contains("A", names);
            Assert.Contains("B", names);
        }
    }

    public class SaveIndexTests
    {
        [Fact]
        public void RoundTrip()
        {
            var g = new FakeGame();
            g.Save("A"); g.Save("name with = sign"); g.Save("C ");
            g.Drop("loose");
            g.Reconcile();

            SaveIndex copy = SaveIndex.Parse(g.index.Serialize());
            Assert.Equal(g.index.Serialize(), copy.Serialize());
            Assert.Equal(4, copy.entries.Count);
            Assert.Equal(new[] { "name with = sign", "A" }, copy.entries["C "].ancestors);
            Assert.False(copy.entries["A"].collapsed);
            g.index.entries["A"].collapsed = true;
            Assert.True(SaveIndex.Parse(g.index.Serialize()).entries["A"].collapsed);
            Assert.True(copy.entries["A"].tracked);
            Assert.False(copy.entries["loose"].tracked);
            Assert.Equal(g.index.entries["A"].created, copy.entries["A"].created);
        }

        [Fact]
        public void ParseToleratesGarbage()
        {
            Assert.Empty(SaveIndex.Parse(null).entries);
            Assert.Empty(SaveIndex.Parse("nonsense\n{\n\tcreated = x\n}\n}").entries);
        }

        [Fact]
        public void ChangedFileLosesItsLineage()
        {
            var g = new FakeGame();
            g.Save("A"); g.Save("B"); g.Save("C");
            g.Drop("B");
            g.Reconcile();
            Assert.Empty(g.index.entries["B"].ancestors);
            Assert.Equal(new[] { "A" }, g.index.entries["C"].ancestors);
        }

        [Fact]
        public void ReconcileIsCleanWhenNothingChanged()
        {
            var g = new FakeGame();
            g.Save("A"); g.Save("B");
            g.Reconcile();
            g.index.dirty = false;
            g.Reconcile();
            Assert.False(g.index.dirty);
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace SaveTimelines.Tests
{
    internal static class FakeSave
    {
        /// <summary>
        /// Save file text in the game's layout: values before child nodes,
        /// tab indentation, and a FLIGHTSTATE full of things to skip.
        /// </summary>
        public static string Text(double ut, string[] contracts = null, string[] tech = null, string[] launches = null, string[] active = null, string[] vessels = null, string extra = null)
        {
            var sb = new StringBuilder();
            sb.Append("GAME\n{\n\tversion = 1.12.5\n\tTitle = t\n");
            if (extra != null) sb.Append(extra);
            sb.Append("\tSCENARIO\n\t{\n\t\tname = ContractSystem\n\t\tscene = 7, 8, 5, 6\n\t\tCONTRACTS\n\t\t{\n");
            foreach (string g in active ?? new string[0])
                sb.Append("\t\t\tCONTRACT\n\t\t\t{\n\t\t\t\tguid = ").Append(g).Append("\n\t\t\t\tPARAM\n\t\t\t\t{\n\t\t\t\t\tguid = nested\n\t\t\t\t}\n\t\t\t}\n");
            foreach (string g in contracts ?? new string[0])
                sb.Append("\t\t\tCONTRACT_FINISHED\n\t\t\t{\n\t\t\t\tguid = ").Append(g).Append("\n\t\t\t\tstate = Completed\n\t\t\t\tPARAM\n\t\t\t\t{\n\t\t\t\t\tguid = nested\n\t\t\t\t}\n\t\t\t}\n");
            sb.Append("\t\t}\n\t}\n");
            sb.Append("\tSCENARIO\n\t{\n\t\tname = ResearchAndDevelopment\n\t\tsci = 5\n");
            foreach (string t in tech ?? new string[0])
                sb.Append("\t\tTech\n\t\t{\n\t\t\tid = ").Append(t).Append("\n\t\t\tstate = Available\n\t\t}\n");
            sb.Append("\t\tScience\n\t\t{\n\t\t\tid = crewReport@KerbinSrfLanded\n\t\t}\n\t}\n");
            sb.Append("\tSCENARIO\n\t{\n\t\tname = CareerLog\n\t\t_launchedVessels\n\t\t{\n");
            foreach (string l in launches ?? new string[0])
                sb.Append("\t\t\tLaunchEvent\n\t\t\t{\n\t\t\t\tVesselName = v\n\t\t\t\tLaunchID = ").Append(l).Append("\n\t\t\t}\n");
            sb.Append("\t\t}\n\t\t_failures\n\t\t{\n\t\t\tFailureEvent\n\t\t\t{\n\t\t\t\tLaunchID = notALaunch\n\t\t\t}\n\t\t}\n\t}\n");
            sb.Append("\tFLIGHTSTATE\n\t{\n\t\tversion = 1.12.5\n\t\tUT = ").Append(ut.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append("\n");
            // Each vessel is "id@launchTime".
            foreach (string v in vessels ?? new string[0])
            {
                string[] parts = v.Split('@');
                sb.Append("\t\tVESSEL\n\t\t{\n\t\t\tpid = ").Append(parts[0]).Append("\n\t\t\tname = n\n\t\t\tUT = 999999\n\t\t\tmet = 1\n\t\t\tlct = ").Append(parts[1])
                    .Append("\n\t\t\tPART\n\t\t\t{\n\t\t\t\tname = x\n\t\t\t\tpid = inPart\n\t\t\t\tlct = 5\n\t\t\t\tguid = inPart\n\t\t\t\tid = inPart\n\t\t\t}\n\t\t}\n");
            }
            sb.Append("\t}\n}\n");
            return sb.ToString();
        }

        public static Fingerprint Print(double ut, string[] contracts = null, string[] tech = null, string[] launches = null, string[] active = null, string[] vessels = null, string extra = null)
        {
            return Read(Text(ut, contracts, tech, launches, active, vessels, extra));
        }

        /// <summary>
        /// RP-1 style scenario text. Strategies and programs are "name@date",
        /// ended strategies "name@date" too, work items "id@progress".
        /// </summary>
        public static string Rp1(string[] strategies = null, string[] programs = null, string[] done = null, string[] ended = null, string[] construction = null, string[] ships = null)
        {
            var sb = new StringBuilder();
            sb.Append("\tSCENARIO\n\t{\n\t\tname = StrategySystem\n\t\tSTRATEGIES\n\t\t{\n");
            foreach (string x in strategies ?? new string[0])
                sb.Append("\t\t\tSTRATEGY\n\t\t\t{\n\t\t\t\tname = ").Append(x.Split('@')[0]).Append("\n\t\t\t\tdate = ").Append(x.Split('@')[1]).Append("\n\t\t\t\tdateDeactivated = -1\n\t\t\t}\n");
            sb.Append("\t\t}\n\t}\n");
            sb.Append("\tSCENARIO\n\t{\n\t\tname = ProgramHandler\n\t\tActivatedStrategies\n\t\t{\n\t\t\tstillGoing = -1\n");
            foreach (string x in ended ?? new string[0])
                sb.Append("\t\t\t").Append(x.Split('@')[0]).Append(" = ").Append(x.Split('@')[1]).Append("\n");
            sb.Append("\t\t}\n");
            foreach (string x in programs ?? new string[0])
                sb.Append("\t\tACTIVEPROGRAM\n\t\t{\n\t\t\tname = ").Append(x.Split('@')[0]).Append("\n\t\t\tfracElapsed = 0.5\n\t\t\tacceptedUT = ").Append(x.Split('@')[1]).Append("\n\t\t\tcompletedUT = 0\n\t\t}\n");
            foreach (string x in done ?? new string[0])
                sb.Append("\t\tCOMPLETEDPROGRAM\n\t\t{\n\t\t\tname = ").Append(x.Split('@')[0]).Append("\n\t\t\tacceptedUT = ").Append(x.Split('@')[1]).Append("\n\t\t\tcompletedUT = 99\n\t\t}\n");
            sb.Append("\t}\n");
            sb.Append("\tSCENARIO\n\t{\n\t\tname = SpaceCenterManagement\n\t\tKSCs\n\t\t{\n\t\t\tLCSpaceCenter\n\t\t\t{\n\t\t\t\tLaunchComplexes\n\t\t\t\t{\n\t\t\t\t\tLaunchComplex\n\t\t\t\t\t{\n\t\t\t\t\t\tBuildList\n\t\t\t\t\t\t{\n");
            foreach (string x in ships ?? new string[0])
                sb.Append("\t\t\t\t\t\t\tVesselProject\n\t\t\t\t\t\t\t{\n\t\t\t\t\t\t\t\tprogress = ").Append(x.Split('@')[1]).Append("\n\t\t\t\t\t\t\t\tshipName = s\n\t\t\t\t\t\t\t\tshipID = ").Append(x.Split('@')[0])
                    .Append("\n\t\t\t\t\t\t\t\tShipNode\n\t\t\t\t\t\t\t\t{\n\t\t\t\t\t\t\t\t\tprogress = 777\n\t\t\t\t\t\t\t\t\tshipID = inner\n\t\t\t\t\t\t\t\t}\n\t\t\t\t\t\t\t}\n");
            sb.Append("\t\t\t\t\t\t}\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t\tLCConstructions\n\t\t\t\t{\n");
            foreach (string x in construction ?? new string[0])
                sb.Append("\t\t\t\t\tLCConstructionProject\n\t\t\t\t\t{\n\t\t\t\t\t\tmodId = ").Append(x.Split('@')[0]).Append("\n\t\t\t\t\t\tprogress = ").Append(x.Split('@')[1]).Append("\n\t\t\t\t\t}\n");
            sb.Append("\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n");
            return sb.ToString();
        }

        public static Fingerprint Read(string text)
        {
            return Fingerprint.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        }
    }

    public class FingerprintTests
    {
        [Fact]
        public void ReadsOnlyTheLogEntries()
        {
            Fingerprint p = FakeSave.Print(1234.5, new[] { "c1", "c2" }, new[] { "start", "basicRocketry", "x" }, new[] { "l1" }, new[] { "a1" });
            Assert.Equal(1234.5, p.ut);
            Assert.Equal(2, p.logs["contracts"].Length);
            Assert.Equal(3, p.logs["tech"].Length);
            Assert.Single(p.logs["launches"]);
            Assert.Equal(6, p.Total);
        }

        [Fact]
        public void EmptySaveHasNoLogs()
        {
            Fingerprint p = FakeSave.Read("GAME\n{\n\tversion = 1\n}\n");
            Assert.Equal(-1, p.ut);
            Assert.Equal(0, p.Total);
        }

        /// <summary>A stream that hands out a few bytes at a time.</summary>
        private class Trickle : MemoryStream
        {
            public Trickle(byte[] data) : base(data) { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return base.Read(buffer, offset, System.Math.Min(count, 7));
            }
        }

        [Fact]
        public void LayoutVariationsReadTheSame()
        {
            string text = FakeSave.Text(77.5, new[] { "c1", "c\u00e9" }, new[] { "start" }, new[] { "l1", "l2" }, new[] { "a1" }, new[] { "v1@10.5" });
            Fingerprint want = FakeSave.Read(text);
            Assert.Equal(5, want.Total);

            var variants = new Dictionary<string, byte[]>
            {
                { "windows line endings", Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n")) },
                { "byte order mark", new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray() },
                { "no final newline", Encoding.UTF8.GetBytes(text.TrimEnd('\n')) },
                { "spaces for tabs", Encoding.UTF8.GetBytes(text.Replace("\t", "  ")) },
                // Longer than the read buffer, so the buffer has to grow, and
                // placed where its text would be taken for a value if split.
                { "very long line", Encoding.UTF8.GetBytes(text.Replace("\tTitle = t\n", "\tTitle = " + new string('x', 3 << 20) + "\n")) },
            };
            foreach (KeyValuePair<string, byte[]> v in variants)
            {
                foreach (Stream stream in new Stream[] { new MemoryStream(v.Value), new Trickle(v.Value) })
                {
                    if (stream is Trickle && v.Key == "very long line") continue;
                    Fingerprint got = Fingerprint.Read(stream);
                    Assert.True(want.ut == got.ut, v.Key);
                    Assert.True(want.vessels.Count == got.vessels.Count && got.vessels["v1"] == 10.5, v.Key);
                    foreach (string id in want.logs.Keys)
                        Assert.True(want.logs[id].SequenceEqual(got.logs[id]), v.Key + " " + id);
                }
            }
        }

        [Fact]
        public void PrefixRelation()
        {
            Fingerprint early = FakeSave.Print(10, new[] { "c1" }, new[] { "start" }, new[] { "l1" });
            Fingerprint late = FakeSave.Print(20, new[] { "c1", "c2" }, new[] { "start", "t2" }, new[] { "l1", "l2" });
            Fingerprint fork = FakeSave.Print(20, new[] { "c1", "c2" }, new[] { "start", "t2" }, new[] { "l1", "other" });
            Fingerprint none = FakeSave.Print(0);

            Assert.True(early.IsPrefixOf(late));
            Assert.True(early.IsPrefixOf(fork));
            Assert.False(late.IsPrefixOf(early));
            Assert.False(late.IsPrefixOf(fork));
            Assert.False(fork.IsPrefixOf(late));
            Assert.True(none.IsPrefixOf(early));
            Assert.True(late.IsPrefixOf(late));
            // Nothing in the later save to check against.
            Assert.True(late.IsPrefixOf(none));
            Assert.Equal(0, late.SharedTotal(none));
            Assert.Equal(3, early.SharedTotal(late));
        }

        [Fact]
        public void SameEntriesInAnotherOrderDoNotMatch()
        {
            Fingerprint a = FakeSave.Print(1, new[] { "c1", "c2" });
            Fingerprint b = FakeSave.Print(1, new[] { "c2", "c1" });
            Assert.False(a.IsPrefixOf(b));
        }

        [Fact]
        public void ReadsVessels()
        {
            Fingerprint p = FakeSave.Print(100, vessels: new[] { "v1@10.5", "v2@40" });
            Assert.Equal(2, p.vessels.Count);
            Assert.Equal(10.5, p.vessels["v1"]);
            Assert.Equal(40, p.vessels["v2"]);
            Assert.Equal(0, p.Total);
        }

        [Fact]
        public void VesselDivergence()
        {
            Fingerprint fork = FakeSave.Print(50, vessels: new[] { "base@10", "mine@40" });
            Fingerprint later = FakeSave.Print(90, vessels: new[] { "base@10", "theirs@45", "late@80" });
            Assert.True(fork.DivergesFrom(later));

            // A vessel recovered before the later save: one side only.
            Fingerprint recovered = FakeSave.Print(90, vessels: new[] { "base@10", "late@80" });
            Assert.False(fork.DivergesFrom(recovered));

            // A vessel in the later save that this one lacks, but launched
            // after this save was made.
            Fingerprint early = FakeSave.Print(30, vessels: new[] { "base@10", "mine@20" });
            Fingerprint after = FakeSave.Print(90, vessels: new[] { "base@10", "new@60" });
            Assert.False(early.DivergesFrom(after));

            // Debris split from a shared launch inherits its launch time,
            // which is not later than the newest shared vessel.
            Fingerprint whole = FakeSave.Print(50, vessels: new[] { "ship@40", "gone@45" });
            Fingerprint staged = FakeSave.Print(90, vessels: new[] { "ship@40", "debris@40" });
            Assert.False(whole.DivergesFrom(staged));

            Assert.False(FakeSave.Print(50).DivergesFrom(later));
        }

        [Fact]
        public void ReadsEventsAndProgress()
        {
            Fingerprint p = FakeSave.Print(100, new[] { "c1" }, vessels: new[] { "v1@10.5" }, extra: FakeSave.Rp1(
                strategies: new[] { "leaderA@40.5", "ProgramX@17.25" }, programs: new[] { "ProgramX@17.25" }, done: new[] { "ProgramOld@3" },
                ended: new[] { "leaderB@60" }, construction: new[] { "lc-1@0.25" }, ships: new[] { "ship-1@1500.5" }));
            Assert.Equal(40.5, p.events["strategies"]["leaderA"]);
            Assert.Equal(17.25, p.events["strategies"]["ProgramX"]);
            Assert.Equal(2, p.events["programs"].Count);
            Assert.Equal(3, p.events["programs"]["ProgramOld"]);
            Assert.Equal(new[] { "leaderB" }, p.events["ended"].Keys);
            Assert.Empty(FakeSave.Print(1, extra: FakeSave.Rp1()).events["ended"]);
            Assert.False(FakeSave.Print(1).events.ContainsKey("ended"));
            Assert.Equal(60, p.events["ended"]["leaderB"]);
            Assert.Equal(2, p.progress.Count);
            Assert.Equal(0.25, p.progress["lc-1"]);
            Assert.Equal(1500.5, p.progress["ship-1"]);
            // Reading these does not disturb what was read before.
            Assert.Single(p.logs["contracts"]);
            Assert.Equal(10.5, p.vessels["v1"]);
            Assert.Equal(100, p.ut);
        }

        [Fact]
        public void EventContradictions()
        {
            Fingerprint Make(double ut, string[] strategies = null, string[] programs = null, string[] ended = null)
            {
                return FakeSave.Print(ut, extra: FakeSave.Rp1(strategies: strategies, programs: programs, ended: ended));
            }
            Fingerprint later = Make(100, new[] { "leaderA@40", "ProgramX@10" }, new[] { "ProgramX@10" }, new[] { "leaderB@70" });

            // Holds everything the later save has from before it was made.
            Assert.False(Make(50, new[] { "leaderA@40", "ProgramX@10" }, new[] { "ProgramX@10" }).Contradicts(later));
            // Made before the leader was appointed.
            Assert.False(Make(30, new[] { "ProgramX@10" }, new[] { "ProgramX@10" }).Contradicts(later));
            // Made after the appointment date, without the leader.
            Assert.True(Make(50, new[] { "ProgramX@10" }, new[] { "ProgramX@10" }).Contradicts(later));
            // The same leader, appointed at another time.
            Assert.True(Make(50, new[] { "leaderA@35", "ProgramX@10" }, new[] { "ProgramX@10" }).Contradicts(later));
            // A leader who has since left is not evidence either way.
            Assert.False(Make(50, new[] { "leaderA@40", "leaderB@20", "ProgramX@10" }, new[] { "ProgramX@10" }).Contradicts(later));
            // A leader who is gone from the later save with no record of
            // having left.
            Assert.True(Make(50, new[] { "leaderA@40", "leaderC@20", "ProgramX@10" }, new[] { "ProgramX@10" }).Contradicts(later));
            // A program the later save never accepted.
            Assert.True(Make(50, new[] { "leaderA@40", "ProgramX@10" }, new[] { "ProgramX@10", "ProgramY@45" }).Contradicts(later));
            // Made after a strategy ended, without that being recorded.
            Assert.True(Make(80, new[] { "leaderA@40", "ProgramX@10" }, new[] { "ProgramX@10" }, new[] { "other@5" }).Contradicts(later));
            // No program or strategy data at all says nothing.
            Assert.False(FakeSave.Print(50).Contradicts(later));
            Assert.False(Make(50, new[] { "leaderA@40", "ProgramX@10" }, new[] { "ProgramX@10" }).Contradicts(FakeSave.Print(100)));
        }

        [Fact]
        public void ProgressContradictions()
        {
            Fingerprint Make(string[] construction = null, string[] ships = null)
            {
                return FakeSave.Print(10, extra: FakeSave.Rp1(construction: construction, ships: ships));
            }
            Fingerprint later = Make(new[] { "lc@0.5" }, new[] { "ship@200" });
            Assert.False(Make(new[] { "lc@0.5" }, new[] { "ship@200" }).Contradicts(later));
            Assert.False(Make(new[] { "lc@0.25" }, new[] { "ship@100" }).Contradicts(later));
            Assert.True(Make(new[] { "lc@0.75" }).Contradicts(later));
            Assert.True(Make(null, new[] { "ship@201" }).Contradicts(later));
            // Finished or not yet started work is absent on one side.
            Assert.False(Make(new[] { "gone@9" }).Contradicts(later));
            Assert.False(Make().Contradicts(later));
        }

        [Fact]
        public void CacheRoundTripAndInvalidation()
        {
            var cache = new FingerprintCache();
            var stamp = new FileStamp("a save", 5, 6);
            cache.Put(stamp, FakeSave.Print(12.25, new[] { "c1", "c2" }, new[] { "start" }, vessels: new[] { "v1@10.5" },
                extra: FakeSave.Rp1(strategies: new[] { "leader with spaces@4.5" }, programs: new[] { "P@1" }, ended: new[] { "e@2" }, ships: new[] { "ship id@7.5" })));
            cache.Put(new FileStamp("empty", 1, 2), FakeSave.Print(0));
            cache.Put(new FileStamp("nothing ended", 3, 4), FakeSave.Print(0, extra: FakeSave.Rp1()));

            FingerprintCache copy = FingerprintCache.Parse(cache.Serialize());
            Assert.Equal(cache.Serialize(), copy.Serialize());
            Fingerprint p = copy.Get(stamp);
            Assert.NotNull(p);
            Assert.Equal(12.25, p.ut);
            Assert.Equal(cache.Get(stamp).logs["contracts"], p.logs["contracts"]);
            Assert.Equal(10.5, p.vessels["v1"]);
            Assert.Equal(4.5, p.events["strategies"]["leader with spaces"]);
            Assert.Equal(1, p.events["programs"]["P"]);
            Assert.Equal(2, p.events["ended"]["e"]);
            Assert.Equal(7.5, p.progress["ship id"]);
            Assert.Empty(copy.Get(new FileStamp("nothing ended", 3, 4)).events["ended"]);
            Assert.False(copy.Get(new FileStamp("empty", 1, 2)).events.ContainsKey("ended"));
            Assert.Empty(FingerprintCache.Parse("SCAN\n{\n\tname = old format\n\tmtime = 1\n\tsize = 1\n}\n").items);
            Assert.Null(copy.Get(new FileStamp("a save", 5, 7)));
            Assert.Null(copy.Get(new FileStamp("missing", 5, 6)));
        }
    }

    public class InferenceTests
    {
        private readonly FakeGame g = new FakeGame();
        private readonly Dictionary<string, Fingerprint> prints = new Dictionary<string, Fingerprint>();

        private void Legacy(string name, double ut, params string[] launches)
        {
            g.Drop(name);
            prints[name] = FakeSave.Print(ut, launches: launches);
        }

        private int Scan()
        {
            g.Reconcile();
            return Inference.Apply(g.index, n => prints.TryGetValue(n, out Fingerprint p) ? p : null);
        }

        [Fact]
        public void ChainAndForkAreRecovered()
        {
            Legacy("start", 0);
            Legacy("one", 10, "l1");
            Legacy("two", 20, "l1", "l2");
            Legacy("three", 30, "l1", "l2", "l3");
            Legacy("alt", 25, "l1", "x2");
            Legacy("alt2", 40, "l1", "x2", "x3");

            Assert.Equal(5, Scan());
            Assert.Equal(new[] { "one", "start" }, g.index.entries["two"].ancestors);
            Assert.Equal(new[] { "one", "start" }, g.index.entries["alt"].ancestors);
            Assert.Equal(new[] { "alt", "one", "start" }, g.index.entries["alt2"].ancestors);
            Assert.True(g.index.entries["two"].inferred);
            Assert.False(g.index.entries["start"].inferred);
            // alt was written last of one's two children, so it continues
            // the group and the older branch nests.
            Assert.Equal("start one >two >three alt alt2", g.Tree());
        }

        [Fact]
        public void RecordedLineageIsNotReplaced()
        {
            Legacy("old", 1);
            g.Save("A"); g.Save("B");
            prints["A"] = FakeSave.Print(5, launches: new[] { "l1" });
            prints["B"] = FakeSave.Print(6, launches: new[] { "l1" });
            // A was the first save the mod saw, so nothing was recorded
            // before it and the scan may place it. B has a recorded parent.
            Assert.Equal(1, Scan());
            Assert.Equal(new[] { "old" }, g.index.entries["A"].ancestors);
            Assert.True(g.index.entries["A"].inferred);
            Assert.Equal(new[] { "A" }, g.index.entries["B"].ancestors);
            Assert.False(g.index.entries["B"].inferred);
            Assert.Equal("old A B", g.Tree());
        }

        [Fact]
        public void OrphanAttachesToATrackedSave()
        {
            g.Save("A"); g.Save("B");
            prints["A"] = FakeSave.Print(5, launches: new[] { "l1" });
            prints["B"] = FakeSave.Print(9, launches: new[] { "l1", "l2" });
            Legacy("restored", 12, "l1", "l2", "l3");
            Assert.Equal(1, Scan());
            Assert.Equal(new[] { "B", "A" }, g.index.entries["restored"].ancestors);
        }

        [Fact]
        public void NewestFileContinuesEvenWhenItsParentIsGuessed()
        {
            g.Save("A"); g.Save("B");
            prints["A"] = FakeSave.Print(5);
            prints["B"] = FakeSave.Print(9);
            // A copy of A made after B. Its file is the newest in the folder.
            Legacy("copy", 5);
            Assert.Equal(1, Scan());
            Assert.Equal(new[] { "A" }, g.index.entries["copy"].ancestors);
            Assert.Equal("A >B copy", g.Tree());
        }

        [Fact]
        public void LeaderAppointmentSeparatesOtherwiseIdenticalSaves()
        {
            void Rp1(string name, double ut, params string[] strategies)
            {
                g.Drop(name);
                prints[name] = FakeSave.Print(ut, extra: FakeSave.Rp1(strategies: strategies));
            }
            Rp1("start", 5, "P@1");
            Rp1("with leader", 30, "P@1", "leaderA@20");
            // Later in game time, on a branch where nobody was appointed.
            Rp1("without", 40, "P@1");
            Rp1("end", 60, "P@1", "leaderA@20");

            Scan();
            Assert.Equal("start", g.index.entries["without"].ancestors[0]);
            Assert.Equal("with leader", g.index.entries["end"].ancestors[0]);
        }

        [Fact]
        public void LaterGameTimeIsNeverAParent()
        {
            Legacy("early", 50, "l1");
            Legacy("late", 100, "l1");
            Scan();
            Assert.Empty(g.index.entries["early"].ancestors);
            Assert.Equal(new[] { "early" }, g.index.entries["late"].ancestors);
        }

        [Fact]
        public void NewerFileIsNeverAParent()
        {
            Legacy("start", 10);
            Legacy("later", 60);
            // Written last, at an earlier game time: someone loaded "start"
            // and saved again. It cannot be the parent of "later".
            Legacy("redo", 30);
            Scan();
            Assert.Equal(new[] { "start" }, g.index.entries["later"].ancestors);
            Assert.Equal(new[] { "start" }, g.index.entries["redo"].ancestors);
        }

        [Fact]
        public void SaveMissingALogStillFindsItsNeighbors()
        {
            g.Drop("a"); prints["a"] = FakeSave.Print(10, new[] { "c1" }, launches: new[] { "l1" });
            g.Drop("b"); prints["b"] = FakeSave.Print(20, new[] { "c1", "c2" }, launches: new[] { "l1", "l2" });
            // Written with no contract data at all.
            g.Drop("broken"); prints["broken"] = FakeSave.Print(25, launches: new[] { "l1", "l2" });
            g.Drop("c"); prints["c"] = FakeSave.Print(30, new[] { "c1", "c2", "c3" }, launches: new[] { "l1", "l2" });
            Scan();
            Assert.Equal("b", g.index.entries["broken"].ancestors[0]);
            // c has more checkable history in common with b than with the
            // save whose contracts are missing.
            Assert.Equal("b", g.index.entries["c"].ancestors[0]);
        }

        [Fact]
        public void RescanStartsFromScratch()
        {
            Legacy("start", 0);
            Legacy("one", 10, "l1");
            Legacy("two", 20, "l1", "l2");
            Scan();
            Assert.Equal(new[] { "one", "start" }, g.index.entries["two"].ancestors);

            prints["one"] = FakeSave.Print(10, launches: new[] { "other" });
            Scan();
            Assert.Equal(new[] { "start" }, g.index.entries["two"].ancestors);
        }

        [Fact]
        public void SavesWithoutFingerprintsAreLeftAlone()
        {
            Legacy("a", 1);
            g.Drop("unread");
            Assert.Equal(0, Scan());
            Assert.Equal("a unread", g.Tree());
        }

        [Fact]
        public void NoCyclesWhenEverythingTies()
        {
            Legacy("a", 5); Legacy("b", 5); Legacy("c", 5);
            Scan();
            List<TreeRow> rows = SaveTree.Build(g.index);
            Assert.Equal(3, rows.Count);
            Assert.Equal(3, rows.Select(r => r.name).Distinct().Count());
            Assert.All(rows, r => Assert.Equal(0, r.depth));
        }
    }
}

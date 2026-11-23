using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ChessNEA.Engine;

namespace ChessNEA.Bench
{
    internal sealed class SearchSample
    {
        public int Run { get; set; }
        public double ElapsedMs { get; set; }
        public long AllocatedBytes { get; set; }
        public int Gen0 { get; set; }
        public int Gen1 { get; set; }
        public int Gen2 { get; set; }
        /// <summary>Time the runtime spent paused for garbage collection during the search (GC.GetTotalPauseDuration).</summary>
        public double GcPauseMs { get; set; }
    }

    internal sealed class Counters
    {
        public long Nodes { get; set; }
        public long LeafEvaluations { get; set; }
        public long TerminalNodes { get; set; }
        public long DrawRuleNodes { get; set; }
        public long InteriorNodes { get; set; }
        public long Cutoffs { get; set; }
        public long FirstMoveCutoffs { get; set; }
        public long LegalMovesGenerated { get; set; }

        public static Counters From(SearchStats s) => new()
        {
            Nodes = s.Nodes, LeafEvaluations = s.LeafEvaluations, TerminalNodes = s.TerminalNodes,
            DrawRuleNodes = s.DrawRuleNodes, InteriorNodes = s.InteriorNodes, Cutoffs = s.Cutoffs, FirstMoveCutoffs = s.FirstMoveCutoffs,
            LegalMovesGenerated = s.LegalMovesGenerated,
        };

        public bool SameAs(Counters o) => Nodes == o.Nodes && LeafEvaluations == o.LeafEvaluations
            && TerminalNodes == o.TerminalNodes && DrawRuleNodes == o.DrawRuleNodes && Cutoffs == o.Cutoffs && FirstMoveCutoffs == o.FirstMoveCutoffs
            && LegalMovesGenerated == o.LegalMovesGenerated;
    }

    internal sealed class SearchBenchResult
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string Fen { get; set; }
        public int Depth { get; set; }
        public int Score { get; set; }
        public string BestMove { get; set; }
        public Counters Counters { get; set; }
        public List<SearchSample> Samples { get; set; } = new();
        public double MedianMs { get; set; }
        public double MinMs { get; set; }
        public double MaxMs { get; set; }
        public long MedianAllocatedBytes { get; set; }
        public double MedianGcPauseMs { get; set; }
        public double NodesPerSecond { get; set; }
        public bool CountersIdenticalAcrossRuns { get; set; } = true;
        public bool ResultIdenticalAcrossRuns { get; set; } = true;
        public string Error { get; set; }
    }

    internal sealed class BenchSummary
    {
        public int Positions { get; set; }
        public long TotalNodes { get; set; }
        public long TotalLeafEvaluations { get; set; }
        public double SumOfMedianMs { get; set; }
        public double NodesPerSecond { get; set; }
        public long TotalMedianAllocatedBytes { get; set; }
        public double SumOfMedianGcPauseMs { get; set; }
        public string Aggregation { get; set; } =
            "Totals are sums over positions; SumOfMedianMs sums each position's median; NodesPerSecond = TotalNodes / SumOfMedianMs.";
    }

    internal sealed class MicroResult
    {
        public string Name { get; set; }
        public string Fen { get; set; }
        public long CallsPerRepetition { get; set; }
        public List<double> NsPerCall { get; set; } = new();
        public List<double> BytesPerCall { get; set; } = new();
        public double MedianNsPerCall { get; set; }
        public double MedianBytesPerCall { get; set; }
    }

    /// <summary>
    /// Timing, allocation and work counters of Bot.Search on the versioned suite.
    /// Each run starts from a freshly parsed position; there is no transposition table,
    /// so there is no cold/warm cache state carried between runs.
    /// </summary>
    internal static class Bench
    {
        /// <summary>Creates the Bot used for every run (lets the caller switch search features).</summary>
        public static Func<Bot> MakeBot = () => new Bot();

        public static List<SearchBenchResult> RunSearch(Suite suite, int depth, int warmups, int runs)
        {
            var results = new List<SearchBenchResult>();
            foreach (var sp in suite.Positions)
            {
                var res = new SearchBenchResult { Name = sp.Name, Category = sp.Category, Fen = sp.Fen, Depth = depth };
                try
                {
                    for (int i = 0; i < warmups; i++) SearchOnce(sp.Fen, depth);
                    for (int i = 0; i < runs; i++)
                    {
                        var (sample, score, move, counters) = SearchOnce(sp.Fen, depth);
                        sample.Run = i + 1;
                        res.Samples.Add(sample);
                        if (i == 0) { res.Score = score; res.BestMove = move; res.Counters = counters; }
                        else
                        {
                            res.CountersIdenticalAcrossRuns &= counters.SameAs(res.Counters);
                            res.ResultIdenticalAcrossRuns &= score == res.Score && move == res.BestMove;
                        }
                    }
                    var ms = res.Samples.Select(s => s.ElapsedMs).OrderBy(x => x).ToList();
                    res.MedianMs = Median(ms);
                    res.MinMs = ms.First();
                    res.MaxMs = ms.Last();
                    res.MedianAllocatedBytes = (long)Median(res.Samples.Select(s => (double)s.AllocatedBytes).OrderBy(x => x).ToList());
                    res.MedianGcPauseMs = Median(res.Samples.Select(s => s.GcPauseMs).OrderBy(x => x).ToList());
                    res.NodesPerSecond = res.Counters.Nodes / (res.MedianMs / 1000.0);
                }
                catch (Exception e) { res.Error = e.ToString(); }
                results.Add(res);
                Console.WriteLine($"  {sp.Name,-17} nodes {res.Counters?.Nodes,10:N0}  median {res.MedianMs,9:F1} ms  " +
                                  $"alloc {res.MedianAllocatedBytes,14:N0} B  gc {res.MedianGcPauseMs,7:F1} ms  score {res.Score,9} {res.BestMove}" +
                                  (res.Error != null ? "  ERROR" : "") + (res.CountersIdenticalAcrossRuns ? "" : "  COUNTERS-DIFFER"));
            }
            return results;
        }

        public static BenchSummary Summarise(List<SearchBenchResult> results)
        {
            var ok = results.Where(r => r.Error == null).ToList();
            var s = new BenchSummary
            {
                Positions = ok.Count,
                TotalNodes = ok.Sum(r => r.Counters.Nodes),
                TotalLeafEvaluations = ok.Sum(r => r.Counters.LeafEvaluations),
                SumOfMedianMs = ok.Sum(r => r.MedianMs),
                TotalMedianAllocatedBytes = ok.Sum(r => r.MedianAllocatedBytes),
                SumOfMedianGcPauseMs = ok.Sum(r => r.MedianGcPauseMs),
            };
            s.NodesPerSecond = s.TotalNodes / (s.SumOfMedianMs / 1000.0);
            return s;
        }

        static (SearchSample, int, string, Counters) SearchOnce(string fen, int depth)
        {
            var position = Position.FromFen(fen); // fresh state for every run
            var bot = MakeBot();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            TimeSpan p0 = GC.GetTotalPauseDuration();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            var r = bot.Search(position, depth);
            sw.Stop();
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            TimeSpan p1 = GC.GetTotalPauseDuration();
            var sample = new SearchSample
            {
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                AllocatedBytes = a1 - a0,
                Gen0 = GC.CollectionCount(0) - g0,
                Gen1 = GC.CollectionCount(1) - g1,
                Gen2 = GC.CollectionCount(2) - g2,
                GcPauseMs = (p1 - p0).TotalMilliseconds,
            };
            return (sample, r.Score, r.Move.ToString(), Counters.From(bot.Stats));
        }

        public static List<MicroResult> RunMicro(int repetitions)
        {
            var bot = MakeBot(); // the same configuration as the search benchmark and its metadata
            var position = Position.Start();
            return new List<MicroResult>
            {
                Micro("Bot.Evaluate", Position.StartFen, 200_000, repetitions, () => bot.Evaluate(position)),
                Micro("Position.GenerateLegalMoves", Position.StartFen, 20_000, repetitions, () => position.GenerateLegalMoves().Count),
            };
        }

        static MicroResult Micro(string name, string fen, long calls, int reps, Func<int> f)
        {
            var res = new MicroResult { Name = name, Fen = fen, CallsPerRepetition = calls };
            int sink = 0;
            for (long i = 0; i < calls / 4; i++) sink += f(); // warmup
            for (int r = 0; r < reps; r++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                for (long i = 0; i < calls; i++) sink += f();
                sw.Stop();
                long a1 = GC.GetAllocatedBytesForCurrentThread();
                res.NsPerCall.Add(sw.Elapsed.TotalMilliseconds * 1e6 / calls);
                res.BytesPerCall.Add((double)(a1 - a0) / calls);
            }
            if (sink == 42) Console.WriteLine(); // keep results observable to the JIT
            res.MedianNsPerCall = Median(res.NsPerCall.OrderBy(x => x).ToList());
            res.MedianBytesPerCall = Median(res.BytesPerCall.OrderBy(x => x).ToList());
            Console.WriteLine($"  {name}: median {res.MedianNsPerCall:F0} ns/call, {res.MedianBytesPerCall:F1} B/call");
            return res;
        }

        public static double Median(List<double> sorted)
        {
            int n = sorted.Count;
            if (n == 0) return double.NaN;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }
    }
}

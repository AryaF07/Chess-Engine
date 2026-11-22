using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ChessNEA.Repro
{
    internal sealed class SearchSample
    {
        public int Run { get; set; }
        public double ElapsedMs { get; set; }
        public long AllocatedBytes { get; set; }
        public int Gen0 { get; set; }
        public int Gen1 { get; set; }
        public int Gen2 { get; set; }
        public int Score { get; set; }
        public string BestMove { get; set; }
    }

    internal sealed class SearchBenchResult
    {
        public string Name { get; set; }
        public string Fen { get; set; }
        public int Depth { get; set; }
        public int Warmups { get; set; }
        public List<SearchSample> Samples { get; set; } = new();
        public double MedianMs { get; set; }
        public double MinMs { get; set; }
        public double MaxMs { get; set; }
        public long MedianAllocatedBytes { get; set; }
        public string Error { get; set; }
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
    /// Baseline measurements of the ORIGINAL engine (known rule defects, see Repros).
    /// These numbers describe how the code behaved at commit 396744d; they are not a
    /// corrected performance baseline and must not be compared directly with results
    /// from a version with different move generation.
    /// </summary>
    internal static class Bench
    {
        public static readonly (string name, string fen)[] SearchSuite =
        {
            ("after-1.e4", "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1"),
            ("italian", "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3"),
            ("rook-endgame", "8/5pk1/6p1/8/8/6P1/5PK1/3R4 b - - 0 1"),
        };

        public static List<SearchBenchResult> RunSearch(int depth, int warmups, int runs)
        {
            var results = new List<SearchBenchResult>();
            foreach (var (name, fen) in SearchSuite)
            {
                var res = new SearchBenchResult { Name = name, Fen = fen, Depth = depth, Warmups = warmups };
                try
                {
                    for (int i = 0; i < warmups; i++) SearchOnce(fen, depth);
                    for (int i = 0; i < runs; i++)
                    {
                        var s = SearchOnce(fen, depth);
                        s.Run = i + 1;
                        res.Samples.Add(s);
                    }
                    var ms = res.Samples.Select(s => s.ElapsedMs).OrderBy(x => x).ToList();
                    res.MedianMs = Median(ms);
                    res.MinMs = ms.First();
                    res.MaxMs = ms.Last();
                    res.MedianAllocatedBytes = (long)Median(res.Samples.Select(s => (double)s.AllocatedBytes).OrderBy(x => x).ToList());
                }
                catch (Exception e) { res.Error = e.ToString(); }
                results.Add(res);
                Console.WriteLine($"  {name}: median {res.MedianMs:F1} ms, alloc {res.MedianAllocatedBytes:N0} B {(res.Error != null ? "ERROR" : "")}");
            }
            return results;
        }

        static SearchSample SearchOnce(string fen, int depth)
        {
            // Fresh position each run: the original search leaks King.hasMoved (R01),
            // so re-using a board would change the position between runs.
            var board = Positions.FromFen(fen);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            var r = board.bot.Minimax(board, depth, -Repros.Large, Repros.Large);
            sw.Stop();
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            return new SearchSample
            {
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                AllocatedBytes = a1 - a0,
                Gen0 = GC.CollectionCount(0) - g0,
                Gen1 = GC.CollectionCount(1) - g1,
                Gen2 = GC.CollectionCount(2) - g2,
                Score = r.evaluation,
                BestMove = Positions.MoveName(r.move),
            };
        }

        public static List<MicroResult> RunMicro(int repetitions)
        {
            var list = new List<MicroResult>();
            var board = Positions.FromFen(Positions.StartFen);
            list.Add(Micro("Bot.evaluate", Positions.StartFen, 200_000, repetitions, () => board.bot.evaluate()));
            list.Add(Micro("Bot.findmoves(white)", Positions.StartFen, 20_000, repetitions, () => board.bot.findmoves(true).Count));
            return list;
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

        static double Median(List<double> sorted)
        {
            int n = sorted.Count;
            if (n == 0) return double.NaN;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }
    }
}

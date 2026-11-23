using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using ChessNEA.Engine;

namespace ChessNEA.Bench
{
    /// <summary>
    /// Headless runner for ChessNEA.Engine.
    ///   perft   [--out DIR]                              published perft suite; exit 1 on any mismatch
    ///   divide  --fen FEN --depth N                      perft count below each root move
    ///   bench   [--suite FILE] [--depth N] [--runs N] [--warmups N] [--ordering on|off] [--repetition on|off] [--out DIR]
    ///   compare [--suite FILE] [--depth N] [--ordering off|on] [--repetition on|off] [--out DIR]   alpha-beta vs. unpruned minimax; exit 1 if scores differ
    ///   --repetition off reproduces runs recorded before search repetition draws existed (results/phase2, results/phase4).
    ///   export  [--suite FILE] [--count N] [--seed N] --file PATH   random positions for an external move-generator check
    ///   allocprofile [--suite FILE] [--depth N] [--out DIR]          sampled allocation by type for each suite position
    /// JSON results include environment metadata (commit, dirty flag, runtime, CPU, build, GC, suite hash).
    /// </summary>
    internal static class Program
    {
        static int Main(string[] args)
        {
            string cmd = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "help";
            string outDir = Arg(args, "--out", Path.Combine("results", "latest"));
            string suitePath = Arg(args, "--suite", Path.Combine("bench", "suite-v1.txt"));
            switch (cmd)
            {
                case "perft": return RunPerft(outDir);
                case "divide": return RunDivide(Arg(args, "--fen", Position.StartFen), int.Parse(Arg(args, "--depth", "3")));
                case "bench":
                    return RunBench(outDir, Suite.Load(suitePath), int.Parse(Arg(args, "--depth", "4")),
                                    int.Parse(Arg(args, "--runs", "7")), int.Parse(Arg(args, "--warmups", "1")),
                                    Arg(args, "--ordering", "on") != "off", Arg(args, "--repetition", "on") != "off");
                case "compare":
                    return RunCompare(outDir, Suite.Load(suitePath), int.Parse(Arg(args, "--depth", "3")),
                                      Arg(args, "--ordering", "off") == "on", Arg(args, "--repetition", "on") != "off");
                case "export":
                    return RunExport(Suite.Load(suitePath), int.Parse(Arg(args, "--count", "3000")),
                                     int.Parse(Arg(args, "--seed", "1")), Arg(args, "--file", Path.Combine("results", "latest", "random-positions.json")));
                case "allocprofile": return RunAllocProfile(outDir, Suite.Load(suitePath), int.Parse(Arg(args, "--depth", "4")));
                default:
                    Console.WriteLine("usage: ChessNEA.Bench perft|divide|bench|compare|export|allocprofile [options]  (see Program.cs)");
                    return 2;
            }
        }

        static int RunPerft(string outDir)
        {
            var env = EnvironmentInfo(null);
            Console.WriteLine("== Perft (published reference counts) ==");
            var rows = Perft.RunSuite();
            foreach (var r in rows)
                Console.WriteLine($"  {r.Name,-10} d{r.Depth}: expected {r.Expected,9} observed {r.Observed,9} {(r.Match ? "OK" : "MISMATCH")}  ({r.ElapsedMs:F1} ms)");
            Save(outDir, "perft.json", env, rows);
            int bad = rows.Count(r => !r.Match);
            Console.WriteLine(bad == 0 ? "all perft counts match" : $"{bad} perft mismatches");
            return bad == 0 ? 0 : 1;
        }

        static int RunDivide(string fen, int depth)
        {
            var p = Position.FromFen(fen);
            long total = 0;
            foreach (var (move, nodes) in Perft.Divide(p, depth).OrderBy(t => t.move.ToString(), StringComparer.Ordinal))
            {
                Console.WriteLine($"{move}: {nodes}");
                total += nodes;
            }
            Console.WriteLine($"\nmoves: {Perft.Divide(p, 1).Count}  nodes: {total}");
            return 0;
        }

        static int RunBench(string outDir, Suite suite, int depth, int runs, int warmups, bool ordering, bool repetition)
        {
            Bench.MakeBot = () => new Bot { UseMoveOrdering = ordering, UseRepetitionDraws = repetition };
            var env = EnvironmentInfo(suite);
            env["benchDepth"] = depth.ToString();
            env["benchRuns"] = runs.ToString();
            env["benchWarmups"] = warmups.ToString();
            env["alphaBeta"] = "True";
            env["moveOrdering"] = ordering.ToString();
            env["repetitionDraws"] = repetition.ToString();
            if (Debugger.IsAttached) Console.WriteLine("WARNING: debugger attached; timings are not representative.");
            Console.WriteLine($"== Micro measurements (start position) ==");
            var micro = Bench.RunMicro(5);
            Save(outDir, "micro.json", env, micro);
            Console.WriteLine($"== Search, depth {depth}, {warmups} warm-up + {runs} runs per position ==");
            var results = Bench.RunSearch(suite, depth, warmups, runs);
            var summary = Bench.Summarise(results);
            Save(outDir, $"search_depth{depth}.json", env, new { summary, positions = results });
            Console.WriteLine($"  total nodes {summary.TotalNodes:N0}, sum of medians {summary.SumOfMedianMs:F1} ms, " +
                              $"{summary.NodesPerSecond:N0} nodes/s, allocated {summary.TotalMedianAllocatedBytes:N0} B, GC pauses {summary.SumOfMedianGcPauseMs:F1} ms");
            return results.Any(r => r.Error != null || !r.CountersIdenticalAcrossRuns || !r.ResultIdenticalAcrossRuns) ? 1 : 0;
        }

        static int RunCompare(string outDir, Suite suite, int depth, bool ordering, bool repetition)
        {
            var env = EnvironmentInfo(suite);
            env["compareDepth"] = depth.ToString();
            env["compareConfig"] = Compare.ConfigDescription(ordering, repetition);
            Console.WriteLine($"== Alpha-beta vs. minimax, depth {depth}, move ordering {(ordering ? "on" : "off")}, repetition draws {(repetition ? "on" : "off")} ==");
            var rows = Compare.Run(suite, depth, ordering, repetition);
            Save(outDir, $"compare_depth{depth}.json", env, new { summary = Compare.Summarise(rows), positions = rows });
            int bad = rows.Count(r => !r.ScoresEqual);
            Console.WriteLine(bad == 0 ? "all root scores equal" : $"{bad} positions with different root scores");
            return bad == 0 ? 0 : 1;
        }

        static int RunExport(Suite suite, int count, int seed, string file)
        {
            var positions = RandomPositions.Generate(suite, count, seed);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            File.WriteAllText(file, JsonSerializer.Serialize(new { seed, suite = suite.Name, suiteSha256 = suite.Sha256, positions },
                                                             new JsonSerializerOptions { WriteIndented = false }));
            Console.WriteLine($"wrote {positions.Count} positions to {file}");
            return 0;
        }

        static int RunAllocProfile(string outDir, Suite suite, int depth)
        {
            var env = EnvironmentInfo(suite);
            env["profileDepth"] = depth.ToString();
            env["method"] = "GCAllocationTick events (about one per 100 KB allocated), summed per type";
            var all = new Dictionary<string, List<AllocTypeRow>>();
            var totals = new Dictionary<string, long>();
            foreach (var sp in suite.Positions)
            {
                var rows = AllocProfile.Run(sp.Fen, depth);
                all[sp.Name] = rows;
                foreach (var r in rows) totals[r.Type] = totals.GetValueOrDefault(r.Type) + r.SampledBytes;
            }
            long sum = totals.Values.Sum();
            var suiteRows = totals.OrderByDescending(kv => kv.Value)
                .Select(kv => new AllocTypeRow { Type = kv.Key, SampledBytes = kv.Value, Share = sum == 0 ? 0 : (double)kv.Value / sum }).ToList();
            Console.WriteLine($"== Sampled allocation by type, suite total, depth {depth} ==");
            foreach (var r in suiteRows.Take(12)) Console.WriteLine($"  {r.Share,7:P1}  {r.SampledBytes,15:N0} B  {r.Type}");
            Save(outDir, $"allocprofile_depth{depth}.json", env, new { suite = suiteRows, positions = all });
            return 0;
        }

        static string Arg(string[] args, string name, string def)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : def;
        }

        static void Save(string dir, string file, Dictionary<string, string> env, object data)
        {
            Directory.CreateDirectory(dir);
            var doc = new { environment = env, data };
            File.WriteAllText(Path.Combine(dir, file), JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        }

        static Dictionary<string, string> EnvironmentInfo(Suite suite)
        {
            var d = new Dictionary<string, string>
            {
                ["timestampUtc"] = DateTime.UtcNow.ToString("o"),
                ["gitCommit"] = Git("rev-parse HEAD"),
                ["gitDirty"] = (Git("status --porcelain --untracked-files=no").Length > 0).ToString(),
                ["engineUnderTest"] = "ChessNEA.Engine (Engine/)",
                ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["targetFramework"] = AppContext.TargetFrameworkName ?? "",
                ["os"] = RuntimeInformation.OSDescription,
                ["arch"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["cpu"] = CpuName(),
                ["logicalProcessors"] = Environment.ProcessorCount.ToString(),
#if DEBUG
                ["configuration"] = "Debug",
#else
                ["configuration"] = "Release",
#endif
                ["debuggerAttached"] = Debugger.IsAttached.ToString(),
                ["serverGC"] = GCSettings.IsServerGC.ToString(),
                ["gcLatencyMode"] = GCSettings.LatencyMode.ToString(),
                ["tieredCompilation"] = AppContext.TryGetSwitch("System.Runtime.TieredCompilation", out bool tc) ? tc.ToString() : "default",
            };
            if (suite != null)
            {
                d["suite"] = suite.Name;
                d["suiteSha256"] = suite.Sha256;
                d["suitePositions"] = suite.Positions.Count.ToString();
            }
            return d;
        }

        static string CpuName()
        {
            try
            {
                if (!OperatingSystem.IsWindows()) return RuntimeInformation.OSDescription;
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "unknown";
            }
            catch { return "unknown"; }
        }

        static string Git(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("git", args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                string s = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                return s;
            }
            catch { return "unknown"; }
        }
    }
}

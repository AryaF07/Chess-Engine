using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ChessNEA.Repro
{
    /// <summary>
    /// Usage: ChessNEA.Repro [repro|perft|bench|all] [--out DIR] [--depth N] [--runs N]
    /// Writes JSON results (with environment metadata) to DIR (default results/phase0).
    /// </summary>
    internal static class Program
    {
        static int Main(string[] args)
        {
            string cmd = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "all";
            string outDir = Arg(args, "--out", Path.Combine("results", "phase0"));
            int depth = int.Parse(Arg(args, "--depth", "4"));
            int runs = int.Parse(Arg(args, "--runs", "7"));
            Directory.CreateDirectory(outDir);
            var env = EnvironmentInfo();
            Console.WriteLine(string.Join(Environment.NewLine, env.Select(kv => $"{kv.Key}: {kv.Value}")));
            Console.WriteLine();

            if (cmd is "repro" or "all")
            {
                Console.WriteLine("== Reproductions ==");
                var results = Repros.RunAll();
                foreach (var r in results)
                {
                    Console.WriteLine($"[{r.Status}] {r.Id} {r.Title}");
                    if (r.Position != null) Console.WriteLine($"    position: {r.Position}");
                    if (r.Expected != null) Console.WriteLine($"    expected: {r.Expected}");
                    Console.WriteLine($"    observed: {r.Observed}");
                }
                Save(outDir, "repro.json", env, results);
                Console.WriteLine();
            }
            if (cmd is "perft" or "all")
            {
                Console.WriteLine("== Perft with the original move pipeline ==");
                var rows = Perft.RunSuite();
                foreach (var r in rows)
                    Console.WriteLine($"  {r.Name,-10} {r.Mode,-14} d{r.Depth}: expected {r.Expected,8} observed {(r.Error ?? r.Observed.ToString()),8} {(r.Match ? "OK" : "MISMATCH")}  ({r.ElapsedMs:F1} ms)");
                Save(outDir, "perft.json", env, rows);

                // Root divide for the first mismatching case, to show where counts diverge.
                var divide = new Dictionary<string, object>();
                foreach (var (name, fen, _) in Perft.Suite)
                {
                    var board = Positions.FromFen(fen);
                    divide[name] = Perft.Divide(board, 2).Select(t => new { t.move, t.nodes }).ToList();
                }
                Save(outDir, "divide_depth2.json", env, divide);
                Console.WriteLine();
            }
            if (cmd is "bench" or "all")
            {
                if (Debugger.IsAttached) Console.WriteLine("WARNING: debugger attached; timings are not representative.");
                Console.WriteLine($"== Micro measurements (start position) ==");
                var micro = Bench.RunMicro(5);
                Save(outDir, "micro.json", env, micro);
                Console.WriteLine($"== Full search, depth {depth}, {runs} runs ==");
                var search = Bench.RunSearch(depth, warmups: 1, runs: runs);
                Save(outDir, $"search_depth{depth}.json", env, search);
            }
            return 0;
        }

        static string Arg(string[] args, string name, string def)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : def;
        }

        static void Save(string dir, string file, Dictionary<string, string> env, object data)
        {
            var doc = new { environment = env, data };
            File.WriteAllText(Path.Combine(dir, file), JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        }

        static Dictionary<string, string> EnvironmentInfo()
        {
            var d = new Dictionary<string, string>
            {
                ["timestampUtc"] = DateTime.UtcNow.ToString("o"),
                ["gitCommit"] = Git("rev-parse HEAD"),
                ["gitDirty"] = (Git("status --porcelain").Length > 0).ToString(),
                ["engineUnderTest"] = "original engine code (Bot.cs, Board.cs, Pieces/*) unmodified from 396744d",
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
            return d;
        }

        static string CpuName()
        {
            try
            {
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

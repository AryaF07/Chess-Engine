using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ChessNEA.Engine;

namespace ChessNEA.Bench
{
    internal sealed class CompareRow
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public int Depth { get; set; }
        public int MinimaxScore { get; set; }
        public string MinimaxMove { get; set; }
        public Counters Minimax { get; set; }
        public double MinimaxMs { get; set; }
        public int AlphaBetaScore { get; set; }
        public string AlphaBetaMove { get; set; }
        public Counters AlphaBeta { get; set; }
        public double AlphaBetaMs { get; set; }
        public bool ScoresEqual => MinimaxScore == AlphaBetaScore;
        public bool MovesEqual => MinimaxMove == AlphaBetaMove;
        public double NodeRatio => (double)AlphaBeta.Nodes / Minimax.Nodes;
        public double LeafRatio => Minimax.LeafEvaluations == 0 ? double.NaN : (double)AlphaBeta.LeafEvaluations / Minimax.LeafEvaluations;
    }

    internal sealed class CompareSummary
    {
        public long MinimaxNodes { get; set; }
        public long AlphaBetaNodes { get; set; }
        public long MinimaxLeafEvaluations { get; set; }
        public long AlphaBetaLeafEvaluations { get; set; }
        public double MinimaxMs { get; set; }
        public double AlphaBetaMs { get; set; }
        public int ScoresEqual { get; set; }
        public int MovesEqual { get; set; }
        public int Positions { get; set; }
        public string Aggregation { get; set; } = "Sums over positions; one timed run per variant after one untimed warm-up of each.";
    }

    /// <summary>
    /// Same position, depth, rules, evaluation and move order; only pruning differs.
    /// Alpha-beta with a full root window must return the same root score as minimax.
    /// Node counts are deterministic; times are single runs and only indicative.
    /// </summary>
    internal static class Compare
    {
        public static List<CompareRow> Run(Suite suite, int depth, bool ordering, bool repetition)
        {
            var rows = new List<CompareRow>();
            foreach (var sp in suite.Positions)
            {
                var row = new CompareRow { Name = sp.Name, Category = sp.Category, Depth = depth };
                (row.MinimaxScore, row.MinimaxMove, row.Minimax, row.MinimaxMs) = Measure(sp.Fen, depth, alphaBeta: false, ordering, repetition);
                (row.AlphaBetaScore, row.AlphaBetaMove, row.AlphaBeta, row.AlphaBetaMs) = Measure(sp.Fen, depth, alphaBeta: true, ordering, repetition);
                rows.Add(row);
                Console.WriteLine($"  {sp.Name,-17} minimax {row.Minimax.Nodes,10:N0} nodes {row.MinimaxMs,9:F1} ms | " +
                                  $"alpha-beta {row.AlphaBeta.Nodes,9:N0} nodes {row.AlphaBetaMs,8:F1} ms | " +
                                  $"ratio {row.NodeRatio:P1} | scores {(row.ScoresEqual ? "equal" : "DIFFER")}");
            }
            return rows;
        }

        /// <summary>
        /// The configuration compared: a plain fixed-depth search on both sides, differing only in
        /// pruning. Every option is set explicitly. With ordering off and repetition draws off this is
        /// the configuration of the recorded minimax vs. alpha-beta comparison
        /// (results/phase2/compare_depth4.json): moves in generation order, no search repetition draws.
        /// </summary>
        internal static Bot MakeBot(bool alphaBeta, bool ordering, bool repetition = true) => new Bot
        {
            UseAlphaBeta = alphaBeta, UseMoveOrdering = ordering, UseRepetitionDraws = repetition,
        };

        internal static string ConfigDescription(bool ordering, bool repetition) =>
            $"both sides: UseMoveOrdering={ordering}{(ordering ? " (static MVV-LVA)" : " (generation order)")}, " +
            $"UseRepetitionDraws={repetition}, original material + piece-square evaluation; only UseAlphaBeta differs";

        static (int, string, Counters, double) Measure(string fen, int depth, bool alphaBeta, bool ordering, bool repetition)
        {
            MakeBot(alphaBeta, ordering, repetition).Search(Position.FromFen(fen), Math.Min(depth, 2)); // warm-up
            var bot = MakeBot(alphaBeta, ordering, repetition);
            var position = Position.FromFen(fen);
            GC.Collect();
            var sw = Stopwatch.StartNew();
            var r = bot.Search(position, depth);
            sw.Stop();
            return (r.Score, r.Move.ToString(), Counters.From(bot.Stats), sw.Elapsed.TotalMilliseconds);
        }

        public static CompareSummary Summarise(List<CompareRow> rows) => new()
        {
            Positions = rows.Count,
            MinimaxNodes = rows.Sum(r => r.Minimax.Nodes),
            AlphaBetaNodes = rows.Sum(r => r.AlphaBeta.Nodes),
            MinimaxLeafEvaluations = rows.Sum(r => r.Minimax.LeafEvaluations),
            AlphaBetaLeafEvaluations = rows.Sum(r => r.AlphaBeta.LeafEvaluations),
            MinimaxMs = rows.Sum(r => r.MinimaxMs),
            AlphaBetaMs = rows.Sum(r => r.AlphaBetaMs),
            ScoresEqual = rows.Count(r => r.ScoresEqual),
            MovesEqual = rows.Count(r => r.MovesEqual),
        };
    }
}

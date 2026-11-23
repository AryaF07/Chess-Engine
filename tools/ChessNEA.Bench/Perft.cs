using System.Collections.Generic;
using System.Diagnostics;
using ChessNEA.Engine;

namespace ChessNEA.Bench
{
    internal sealed class PerftRow
    {
        public string Name { get; set; }
        public string Fen { get; set; }
        public int Depth { get; set; }
        public long Expected { get; set; }
        public long Observed { get; set; }
        public double ElapsedMs { get; set; }
        public bool Match => Expected == Observed;
    }

    /// <summary>Perft (leaf count of the legal-move tree) through Position.MakeMove/UnmakeMove.</summary>
    internal static class Perft
    {
        // Reference counts: Chess Programming Wiki, "Perft Results".
        public static readonly (string name, string fen, long[] expected)[] Suite =
        {
            ("startpos", Position.StartFen, new long[] { 20, 400, 8902, 197281, 4865609 }),
            ("kiwipete", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", new long[] { 48, 2039, 97862, 4085603 }),
            ("position3", "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", new long[] { 14, 191, 2812, 43238, 674624 }),
            ("position4", "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", new long[] { 6, 264, 9467, 422333 }),
            ("position5", "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", new long[] { 44, 1486, 62379, 2103487 }),
            ("position6", "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", new long[] { 46, 2079, 89890, 3894594 }),
        };

        public static List<PerftRow> RunSuite()
        {
            var rows = new List<PerftRow>();
            foreach (var (name, fen, expected) in Suite)
                for (int d = 1; d <= expected.Length; d++)
                {
                    var p = Position.FromFen(fen);
                    var sw = Stopwatch.StartNew();
                    long n = Count(p, d);
                    rows.Add(new PerftRow { Name = name, Fen = fen, Depth = d, Expected = expected[d - 1], Observed = n, ElapsedMs = sw.Elapsed.TotalMilliseconds });
                }
            return rows;
        }

        public static long Count(Position p, int depth)
        {
            List<Move> moves = p.GenerateLegalMoves();
            if (depth == 1) return moves.Count;
            long nodes = 0;
            foreach (Move m in moves)
            {
                Undo u = p.MakeMove(m);
                nodes += Count(p, depth - 1);
                p.UnmakeMove(m, u);
            }
            return nodes;
        }

        /// <summary>Perft count below each root move (depth 1 gives 1 per move).</summary>
        public static List<(Move move, long nodes)> Divide(Position p, int depth)
        {
            var result = new List<(Move, long)>();
            foreach (Move m in p.GenerateLegalMoves())
            {
                if (depth <= 1) { result.Add((m, 1)); continue; }
                Undo u = p.MakeMove(m);
                result.Add((m, Count(p, depth - 1)));
                p.UnmakeMove(m, u);
            }
            return result;
        }
    }
}

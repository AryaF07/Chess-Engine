using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ChessNEA.Repro
{
    internal sealed class PerftRow
    {
        public string Name { get; set; }
        public string Fen { get; set; }
        public int Depth { get; set; }
        public long Expected { get; set; }
        public long Observed { get; set; }
        public string Mode { get; set; }
        public double ElapsedMs { get; set; }
        public string Error { get; set; }
        public bool Match => Error == null && Expected == Observed;
    }

    /// <summary>
    /// Perft = number of leaf nodes of the full legal-move tree to a fixed depth.
    /// Here it is computed with the original engine's own pipeline
    /// (Bot.findmoves -> Bot.makeMove -> recurse -> Bot.undoMove), so mismatches
    /// against published reference counts measure defects in that pipeline.
    /// Mode "raw" runs the engine exactly as written. Mode "flags-restored" additionally
    /// saves and restores King/Rook.hasMoved and Pawn.movedtwoSquares around each move,
    /// isolating the hasMoved leak (R01) from the other rule defects.
    /// </summary>
    internal static class Perft
    {
        public static readonly (string name, string fen, long[] expected)[] Suite =
        {
            // Reference counts: the Chess Programming Wiki "Perft Results" page.
            ("startpos", Positions.StartFen, new long[] { 20, 400, 8902, 197281 }),
            ("kiwipete", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", new long[] { 48, 2039, 97862 }),
            ("position3", "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", new long[] { 14, 191, 2812, 43238 }),
        };

        public static List<PerftRow> RunSuite()
        {
            var rows = new List<PerftRow>();
            foreach (var (name, fen, expected) in Suite)
                foreach (string mode in new[] { "raw", "flags-restored" })
                    for (int d = 1; d <= expected.Length; d++)
                    {
                        var row = new PerftRow { Name = name, Fen = fen, Depth = d, Expected = expected[d - 1], Mode = mode };
                        var board = Positions.FromFen(fen); // fresh position for every count
                        var sw = Stopwatch.StartNew();
                        try { row.Observed = Count(board, d, mode == "flags-restored"); }
                        catch (Exception e) { row.Error = $"{e.GetType().Name}: {e.Message}"; }
                        row.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                        rows.Add(row);
                    }
            return rows;
        }

        public static long Count(Board board, int depth, bool restoreFlags)
        {
            List<Bot.Move> moves = board.bot.findmoves(board.turn);
            if (depth == 1) return moves.Count;
            long nodes = 0;
            foreach (var m in moves)
            {
                var saved = restoreFlags ? SaveFlags(board) : null;
                board.bot.makeMove(board, m);
                nodes += Count(board, depth - 1, restoreFlags);
                board.bot.undoMove(board, m);
                if (saved != null) RestoreFlags(saved);
            }
            return nodes;
        }

        static List<(Piece p, bool moved, bool two)> SaveFlags(Board board)
        {
            var list = new List<(Piece, bool, bool)>();
            foreach (Piece p in board.ChessBoard)
                if (p is King || p is Rook || p is Pawn)
                    list.Add((p, p is King k ? k.hasMoved : p is Rook r && r.hasMoved, p is Pawn pw && pw.movedtwoSquares));
            return list;
        }

        static void RestoreFlags(List<(Piece p, bool moved, bool two)> saved)
        {
            foreach (var (p, moved, two) in saved)
            {
                if (p is King k) k.hasMoved = moved;
                else if (p is Rook r) r.hasMoved = moved;
                else if (p is Pawn pw) pw.movedtwoSquares = two;
            }
        }

        /// <summary>Root divide: perft count below each root move, for locating a mismatch.</summary>
        public static List<(string move, long nodes)> Divide(Board board, int depth)
        {
            var result = new List<(string, long)>();
            foreach (var m in board.bot.findmoves(board.turn))
            {
                board.bot.makeMove(board, m);
                long n = depth <= 1 ? 1 : Count(board, depth - 1, false);
                board.bot.undoMove(board, m);
                result.Add((Positions.MoveName(m), n));
            }
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using ChessNEA.Engine;

namespace ChessNEA.Bench
{
    internal sealed class ExportedPosition
    {
        public string Fen { get; set; }
        public List<string> LegalMoves { get; set; }
        public long Perft2 { get; set; }
        /// <summary>Status computed from the FEN alone (no move history): what an external checker can verify.</summary>
        public string Status { get; set; }
        /// <summary>Status in the random game, including history-dependent results (threefold repetition).</summary>
        public string StatusInGame { get; set; }
    }

    /// <summary>
    /// Positions reached by seeded random play from each suite position, with this engine's
    /// legal moves (UCI, sorted), perft(2) and game status, for comparison with an independent
    /// move generator (tools/oracle/compare.py). Random play reaches castling, en passant,
    /// promotions and endgames far more often than the start position does.
    /// </summary>
    internal static class RandomPositions
    {
        public static List<ExportedPosition> Generate(Suite suite, int count, int seed)
        {
            var rng = new Random(seed);
            var seen = new HashSet<string>();
            var output = new List<ExportedPosition>();
            int attempts = 0;
            while (output.Count < count && attempts++ < count * 20)
            {
                var start = suite.Positions[rng.Next(suite.Positions.Count)];
                var p = Position.FromFen(start.Fen);
                int plies = rng.Next(0, 120);
                for (int i = 0; i < plies; i++)
                {
                    var moves = p.GenerateLegalMoves();
                    if (moves.Count == 0) break;
                    // Prefer captures, promotions and castling half of the time to reach special cases more often.
                    var special = moves.Where(m => m.Kind != MoveKind.Normal || p[m.ToRow, m.ToCol] != null).ToList();
                    var pool = special.Count > 0 && rng.Next(2) == 0 ? special : moves;
                    p.MakeMove(pool[rng.Next(pool.Count)]);
                }
                string fen = p.ToFen();
                if (!seen.Add(fen)) continue;
                var legal = p.GenerateLegalMoves();
                output.Add(new ExportedPosition
                {
                    Fen = fen,
                    LegalMoves = legal.Select(m => m.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToList(),
                    Perft2 = legal.Count == 0 ? 0 : Perft.Count(p, 2),
                    Status = Position.FromFen(fen).GetStatus().ToString(),
                    StatusInGame = p.GetStatus().ToString(),
                });
            }
            return output;
        }
    }
}

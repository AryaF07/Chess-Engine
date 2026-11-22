using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;

namespace ChessNEA.Repro
{
    /// <summary>
    /// Builds positions for the original engine without the GUI.
    /// The engine stores each piece's square inside its sprite rectangle
    /// (X = 165 + 60*col, Y = 5 + 60*row), with row 0 = rank 8 and col 0 = file a,
    /// so every placement here must set that rectangle consistently.
    /// </summary>
    internal static class Positions
    {
        public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        public static (int col, int row) Sq(string square)
        {
            int col = square[0] - 'a';
            int rank = square[1] - '0';
            return (col, 8 - rank);
        }

        public static string SqName(int col, int row) => $"{(char)('a' + col)}{8 - row}";

        public static Rectangle Rect(int col, int row) => new Rectangle(165 + 60 * col, 5 + 60 * row, 50, 50);

        /// <summary>Creates a Board (which also sets the static Piece/Bot board references) and empties it.</summary>
        public static Board EmptyBoard(bool whiteToMove)
        {
            var board = new Board();
            board.ChessBoard = new Piece[8, 8];
            board.turn = whiteToMove;
            board.bot = new Bot();
            board.botGame = true;
            return board;
        }

        /// <summary>Places a piece given a FEN letter (upper case = white) on a square such as "e1".</summary>
        public static Piece Place(Board board, char fenLetter, string square)
        {
            var (col, row) = Sq(square);
            return Place(board, fenLetter, col, row);
        }

        public static Piece Place(Board board, char fenLetter, int col, int row)
        {
            bool white = char.IsUpper(fenLetter);
            Rectangle r = Rect(col, row);
            Piece p = char.ToLowerInvariant(fenLetter) switch
            {
                'p' => new Pawn(board, white, r),
                'n' => new Knight(board, white, r),
                'b' => new Bishop(board, white, r),
                'r' => new Rook(board, white, r),
                'q' => new Queen(board, white, r),
                'k' => new King(board, white, r),
                _ => throw new ArgumentException($"Unknown piece letter '{fenLetter}'")
            };
            board.ChessBoard[row, col] = p;
            return p;
        }

        /// <summary>
        /// Minimal FEN loader for diagnostics only: piece placement, side to move,
        /// castling field (mapped onto King/Rook.hasMoved) and en-passant field
        /// (mapped onto Pawn.movedtwoSquares). Move counters are ignored because
        /// the original engine has no equivalent state. A complete FEN import/export
        /// belongs to Phase 2.
        /// </summary>
        public static Board FromFen(string fen)
        {
            string[] parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var board = EmptyBoard(parts.Length < 2 || parts[1] == "w");
            string[] ranks = parts[0].Split('/');
            if (ranks.Length != 8) throw new ArgumentException("FEN needs 8 ranks");
            for (int row = 0; row < 8; row++)
            {
                int col = 0;
                foreach (char c in ranks[row])
                {
                    if (char.IsDigit(c)) { col += c - '0'; continue; }
                    Place(board, c, col, row);
                    col++;
                }
                if (col != 8) throw new ArgumentException($"FEN rank {8 - row} does not have 8 files");
            }

            string castling = parts.Length > 2 ? parts[2] : "-";
            MarkCastling(board, true, castling.Contains('K'), castling.Contains('Q'));
            MarkCastling(board, false, castling.Contains('k'), castling.Contains('q'));

            string ep = parts.Length > 3 ? parts[3] : "-";
            if (ep != "-")
            {
                var (epCol, epRow) = Sq(ep);
                // The pawn that just double-pushed sits one row beyond the target square
                // from the mover's point of view: white target on rank 6 -> black pawn on rank 5.
                int pawnRow = epRow == 2 ? 3 : 4;
                if (board.ChessBoard[pawnRow, epCol] is Pawn pawn) pawn.movedtwoSquares = true;
            }
            return board;
        }

        static void MarkCastling(Board board, bool white, bool kingSide, bool queenSide)
        {
            int homeRow = white ? 7 : 0;
            if (board.ChessBoard[homeRow, 4] is King king && king.IsWhite == white)
                king.hasMoved = !(kingSide || queenSide);
            if (board.ChessBoard[homeRow, 7] is Rook rh && rh.IsWhite == white) rh.hasMoved = !kingSide;
            if (board.ChessBoard[homeRow, 0] is Rook ra && ra.IsWhite == white) ra.hasMoved = !queenSide;
            // Any king or rook elsewhere has, by definition, moved.
            foreach (Piece p in board.ChessBoard)
            {
                if (p is King k && k.IsWhite == white && !(board.ChessBoard[homeRow, 4] == k)) k.hasMoved = true;
                if (p is Rook r && r.IsWhite == white && board.ChessBoard[homeRow, 0] != r && board.ChessBoard[homeRow, 7] != r) r.hasMoved = true;
            }
        }

        public static string MoveName(Bot.Move m)
        {
            if (m.piece == null) return "(none)";
            return $"{Letter(m.piece)}{SqName(m.prevcol, m.prevrow)}-{SqName(m.nextcol, m.nextrow)}";
        }

        public static char Letter(Piece p)
        {
            char c = p switch
            {
                Pawn => 'p', Knight => 'n', Bishop => 'b', Rook => 'r', Queen => 'q', King => 'k',
                _ => '?'
            };
            return p.IsWhite ? char.ToUpperInvariant(c) : c;
        }

        /// <summary>Text diagram of the board array (rank 8 at the top).</summary>
        public static string Diagram(Board board)
        {
            var sb = new StringBuilder();
            for (int row = 0; row < 8; row++)
            {
                sb.Append(8 - row).Append(' ');
                for (int col = 0; col < 8; col++)
                {
                    Piece p = board.ChessBoard[row, col];
                    sb.Append(p == null ? '.' : Letter(p)).Append(' ');
                }
                sb.AppendLine();
            }
            sb.Append("  a b c d e f g h");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Complete observable state of the original engine's position: the board array
    /// (by reference), each piece's rectangle-encoded square, the castling and
    /// double-push flags, and the side to move. Used to test make/undo restoration.
    /// </summary>
    internal sealed class Snapshot
    {
        readonly Piece[,] cells = new Piece[8, 8];
        readonly Dictionary<Piece, (Rectangle pos, bool moved, bool twoSquares)> pieceState = new();
        readonly bool turn;

        public Snapshot(Board board)
        {
            turn = board.turn;
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                {
                    Piece p = board.ChessBoard[r, c];
                    cells[r, c] = p;
                    if (p != null) pieceState[p] = (p.Position, Moved(p), p is Pawn pw && pw.movedtwoSquares);
                }
        }

        static bool Moved(Piece p) => p switch { King k => k.hasMoved, Rook r => r.hasMoved, _ => false };

        public List<string> Diff(Board board)
        {
            var diffs = new List<string>();
            if (board.turn != turn) diffs.Add($"turn {turn} -> {board.turn}");
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if (!ReferenceEquals(cells[r, c], board.ChessBoard[r, c]))
                        diffs.Add($"square {Positions.SqName(c, r)} changed occupant");
            foreach (var (p, s) in pieceState)
            {
                string name = $"{Positions.Letter(p)}@{Positions.SqName((s.pos.X - 165) / 60, (s.pos.Y - 5) / 60)}";
                if (p.Position != s.pos) diffs.Add($"{name} rectangle {s.pos} -> {p.Position}");
                if (Moved(p) != s.moved) diffs.Add($"{name} hasMoved {s.moved} -> {Moved(p)}");
                bool two = p is Pawn pw && pw.movedtwoSquares;
                if (two != s.twoSquares) diffs.Add($"{name} movedtwoSquares {s.twoSquares} -> {two}");
            }
            return diffs;
        }
    }
}

using System.Text;

namespace ChessNEA.Engine;

/// <summary>
/// Complete chess position: piece placement (8x8 array of piece objects, row 0 = rank 8),
/// side to move, castling rights, en-passant square, halfmove clock and fullmove number.
/// All changes go through MakeMove/UnmakeMove, which restore every field exactly.
/// No static state: several positions can exist at once.
/// </summary>
public sealed class Position
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    readonly Piece?[,] squares = new Piece?[8, 8];
    King whiteKing = null!;
    King blackKing = null!;

    public Piece? this[int row, int col] => squares[row, col];
    public bool WhiteToMove { get; private set; }
    public CastlingRights Castling { get; private set; }
    /// <summary>Square (row*8+col) a pawn skipped over on the previous move's double push, or -1.</summary>
    public int EnPassantSquare { get; private set; } = -1;
    /// <summary>Plies since the last capture or pawn move (fifty-move rule).</summary>
    public int HalfmoveClock { get; private set; }
    public int FullmoveNumber { get; private set; } = 1;

    public King KingOf(bool white) => white ? whiteKing : blackKing;

    /// <summary>Zobrist key of the current position (see <see cref="Zobrist"/>), updated incrementally.</summary>
    public ulong Key { get; private set; }

    // The en-passant file's contribution to Key (0 when no en-passant capture is available).
    ulong enPassantKeyPart;

    // Keys of the earlier positions of this game and of the current search path, oldest first.
    readonly List<ulong> history = new(256);

    /// <summary>Number of earlier positions recorded in the history (for tests and diagnostics).</summary>
    public int HistoryLength => history.Count;

    Position() { }

    public static Position Start() => FromFen(StartFen);

    // ---------------------------------------------------------------- FEN

    public static Position FromFen(string fen)
    {
        string[] f = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 4 || f.Length > 6) throw new FormatException("FEN needs 4 to 6 fields");
        var p = new Position();

        string[] ranks = f[0].Split('/');
        if (ranks.Length != 8) throw new FormatException("FEN placement needs 8 ranks");
        for (int row = 0; row < 8; row++)
        {
            int col = 0;
            foreach (char c in ranks[row])
            {
                if (c >= '1' && c <= '8') { col += c - '0'; continue; }
                PieceType type = char.ToLowerInvariant(c) switch
                {
                    'p' => PieceType.Pawn, 'n' => PieceType.Knight, 'b' => PieceType.Bishop,
                    'r' => PieceType.Rook, 'q' => PieceType.Queen, 'k' => PieceType.King,
                    _ => throw new FormatException($"Invalid piece letter '{c}'"),
                };
                if (col > 7) throw new FormatException($"Rank {8 - row} has more than 8 files");
                p.Put(Piece.Create(type, char.IsUpper(c), row, col));
                col++;
            }
            if (col != 8) throw new FormatException($"Rank {8 - row} does not have 8 files");
        }

        int whiteKings = 0, blackKings = 0;
        foreach (Piece piece in p.Pieces())
        {
            if (piece is King k)
            {
                if (k.IsWhite) { whiteKings++; p.whiteKing = k; } else { blackKings++; p.blackKing = k; }
            }
            if (piece is Pawn && (piece.Row == 0 || piece.Row == 7))
                throw new FormatException("Pawn on the first or last rank");
        }
        if (whiteKings != 1 || blackKings != 1) throw new FormatException("Each side needs exactly one king");

        p.WhiteToMove = f[1] switch
        {
            "w" => true,
            "b" => false,
            _ => throw new FormatException("Side to move must be 'w' or 'b'"),
        };

        if (f[2] != "-")
        {
            foreach (char c in f[2])
            {
                CastlingRights r = c switch
                {
                    'K' => CastlingRights.WhiteKingside, 'Q' => CastlingRights.WhiteQueenside,
                    'k' => CastlingRights.BlackKingside, 'q' => CastlingRights.BlackQueenside,
                    _ => throw new FormatException($"Invalid castling flag '{c}'"),
                };
                p.Castling |= r;
            }
        }
        if (!p.CastlingRightsConsistent()) throw new FormatException("Castling rights need king and rook on their home squares");

        if (f[3] != "-")
        {
            var (row, col) = Square.Parse(f[3]);
            int expectedRow = p.WhiteToMove ? 2 : 5;
            if (row != expectedRow) throw new FormatException("En-passant square on the wrong rank");
            // The opponent's pawn must have just moved two squares past the target: it stands one row
            // beyond it, and both the target and the square it started from are empty.
            int pawnRow = p.WhiteToMove ? row + 1 : row - 1;
            int originRow = p.WhiteToMove ? row - 1 : row + 1;
            if (!(p.squares[pawnRow, col] is Pawn pushed && pushed.IsWhite != p.WhiteToMove))
                throw new FormatException("En-passant square without the double-pushed pawn beside it");
            if (p.squares[row, col] != null || p.squares[originRow, col] != null)
                throw new FormatException("En-passant square or the pawn's starting square is occupied");
            p.EnPassantSquare = row * 8 + col;
        }

        p.HalfmoveClock = f.Length > 4 ? int.Parse(f[4]) : 0;
        p.FullmoveNumber = f.Length > 5 ? int.Parse(f[5]) : 1;
        if (p.HalfmoveClock < 0 || p.FullmoveNumber < 1) throw new FormatException("Invalid move counters");

        if (p.IsInCheck(!p.WhiteToMove)) throw new FormatException("Side not to move is in check");
        p.enPassantKeyPart = p.EnPassantKeyPart();
        p.Key = p.ComputeKey();
        return p;
    }

    bool CastlingRightsConsistent()
    {
        bool Ok(CastlingRights right, int row, int rookCol, bool white) =>
            (Castling & right) == 0
            || (squares[row, 4] is King k && k.IsWhite == white && squares[row, rookCol] is Rook r && r.IsWhite == white);
        return Ok(CastlingRights.WhiteKingside, 7, 7, true) && Ok(CastlingRights.WhiteQueenside, 7, 0, true)
            && Ok(CastlingRights.BlackKingside, 0, 7, false) && Ok(CastlingRights.BlackQueenside, 0, 0, false);
    }

    public string ToFen()
    {
        var sb = new StringBuilder();
        for (int row = 0; row < 8; row++)
        {
            int empty = 0;
            for (int col = 0; col < 8; col++)
            {
                Piece? piece = squares[row, col];
                if (piece == null) { empty++; continue; }
                if (empty > 0) { sb.Append(empty); empty = 0; }
                sb.Append(piece.FenChar);
            }
            if (empty > 0) sb.Append(empty);
            if (row < 7) sb.Append('/');
        }
        sb.Append(WhiteToMove ? " w " : " b ");
        if (Castling == CastlingRights.None) sb.Append('-');
        if ((Castling & CastlingRights.WhiteKingside) != 0) sb.Append('K');
        if ((Castling & CastlingRights.WhiteQueenside) != 0) sb.Append('Q');
        if ((Castling & CastlingRights.BlackKingside) != 0) sb.Append('k');
        if ((Castling & CastlingRights.BlackQueenside) != 0) sb.Append('q');
        sb.Append(' ').Append(EnPassantSquare < 0 ? "-" : Square.Name(EnPassantSquare / 8, EnPassantSquare % 8));
        sb.Append(' ').Append(HalfmoveClock).Append(' ').Append(FullmoveNumber);
        return sb.ToString();
    }

    public override string ToString() => ToFen();

    // ---------------------------------------------------------------- make / unmake

    void Put(Piece piece) => squares[piece.Row, piece.Col] = piece;

    void Relocate(Piece piece, int row, int col)
    {
        squares[piece.Row, piece.Col] = null;
        piece.Row = row;
        piece.Col = col;
        squares[row, col] = piece;
    }

    /// <summary>
    /// Rights lost when a move starts or ends on a square: moving the king loses both of that
    /// side's rights; moving a rook from, or capturing on, a corner loses that corner's right.
    /// </summary>
    static CastlingRights RightsKeptAfterTouching(int row, int col) => (row, col) switch
    {
        (7, 4) => ~(CastlingRights.WhiteKingside | CastlingRights.WhiteQueenside),
        (7, 7) => ~CastlingRights.WhiteKingside,
        (7, 0) => ~CastlingRights.WhiteQueenside,
        (0, 4) => ~(CastlingRights.BlackKingside | CastlingRights.BlackQueenside),
        (0, 7) => ~CastlingRights.BlackKingside,
        (0, 0) => ~CastlingRights.BlackQueenside,
        _ => CastlingRights.All,
    };

    /// <summary>
    /// Plays a pseudo-legal move for the side to move and returns what UnmakeMove needs.
    /// It does not check legality (the own king may be left in check); GenerateLegalMoves filters that.
    /// </summary>
    public Undo MakeMove(Move m)
    {
        Piece mover = squares[m.FromRow, m.FromCol] ?? throw new InvalidOperationException($"No piece on {Square.Name(m.FromRow, m.FromCol)}");
        if (mover.IsWhite != WhiteToMove) throw new InvalidOperationException($"{mover} does not belong to the side to move");

        Piece? captured = m.Kind == MoveKind.EnPassant ? squares[m.FromRow, m.ToCol] : squares[m.ToRow, m.ToCol];
        var undo = new Undo(captured, m.Kind == MoveKind.Promotion ? mover : null,
                            Castling, EnPassantSquare, HalfmoveClock, FullmoveNumber, Key, enPassantKeyPart);
        history.Add(Key);
        ulong key = Key ^ Zobrist.PieceKey(mover, m.FromRow, m.FromCol);

        if (captured != null)
        {
            key ^= Zobrist.PieceKey(captured, captured.Row, captured.Col);
            squares[captured.Row, captured.Col] = null;
        }
        Relocate(mover, m.ToRow, m.ToCol);

        switch (m.Kind)
        {
            case MoveKind.CastleKingside:
            {
                Piece rook = squares[m.FromRow, 7]!;
                key ^= Zobrist.PieceKey(rook, m.FromRow, 7) ^ Zobrist.PieceKey(rook, m.FromRow, 5);
                Relocate(rook, m.FromRow, 5);
                break;
            }
            case MoveKind.CastleQueenside:
            {
                Piece rook = squares[m.FromRow, 0]!;
                key ^= Zobrist.PieceKey(rook, m.FromRow, 0) ^ Zobrist.PieceKey(rook, m.FromRow, 3);
                Relocate(rook, m.FromRow, 3);
                break;
            }
            case MoveKind.Promotion:
                squares[m.ToRow, m.ToCol] = Piece.Create(m.Promotion, mover.IsWhite, m.ToRow, m.ToCol);
                break;
        }
        key ^= Zobrist.PieceKey(squares[m.ToRow, m.ToCol]!, m.ToRow, m.ToCol); // the mover, or the promoted piece

        CastlingRights oldRights = Castling;
        Castling &= RightsKeptAfterTouching(m.FromRow, m.FromCol) & RightsKeptAfterTouching(m.ToRow, m.ToCol);
        key ^= Zobrist.CastlingKey(oldRights) ^ Zobrist.CastlingKey(Castling);
        EnPassantSquare = m.Kind == MoveKind.DoublePush ? (m.FromRow + m.ToRow) / 2 * 8 + m.FromCol : -1;
        HalfmoveClock = mover is Pawn || captured != null ? 0 : HalfmoveClock + 1;
        if (!WhiteToMove) FullmoveNumber++;
        WhiteToMove = !WhiteToMove;
        key ^= Zobrist.BlackToMove;
        key ^= enPassantKeyPart;            // remove the previous position's en-passant contribution
        enPassantKeyPart = EnPassantKeyPart();
        key ^= enPassantKeyPart;            // add this position's
        Key = key;
        return undo;
    }

    /// <summary>
    /// The en-passant file's key when a LEGAL en-passant capture exists for the side to move,
    /// otherwise 0. The same key decides repetitions, so an en-passant right that cannot be used
    /// (no capturing pawn, or the capture would expose the own king, e.g. a pin along a file or a
    /// rank opened by removing both pawns) must not make a position different.
    /// </summary>
    ulong EnPassantKeyPart()
    {
        if (EnPassantSquare < 0) return 0;
        int epRow = EnPassantSquare / 8, epCol = EnPassantSquare % 8;
        int pawnRow = WhiteToMove ? epRow + 1 : epRow - 1; // capturing pawns and the victim stand here
        for (int dc = -1; dc <= 1; dc += 2)
            if (Square.OnBoard(pawnRow, epCol + dc) && squares[pawnRow, epCol + dc] is Pawn pw && pw.IsWhite == WhiteToMove
                && EnPassantCaptureIsLegal(pawnRow, epCol + dc, epRow, epCol))
                return Zobrist.EnPassantKey(epCol);
        return 0;
    }

    /// <summary>
    /// Plays the en-passant capture on the board array only (capturer to the target square, victim
    /// removed), tests whether the mover's king is attacked, and restores the array. Attack tests
    /// read only the array, so pieces' Row/Col, the key and the history are not touched; no move
    /// generation or hashing is involved.
    /// </summary>
    bool EnPassantCaptureIsLegal(int fromRow, int fromCol, int toRow, int toCol)
    {
        Piece capturer = squares[fromRow, fromCol]!;
        Piece? victim = squares[fromRow, toCol];
        squares[fromRow, fromCol] = null;
        squares[fromRow, toCol] = null;
        squares[toRow, toCol] = capturer;
        King own = KingOf(WhiteToMove);
        bool legal = !IsSquareAttacked(own.Row, own.Col, !WhiteToMove);
        squares[toRow, toCol] = null;
        squares[fromRow, toCol] = victim;
        squares[fromRow, fromCol] = capturer;
        return legal;
    }

    /// <summary>Computes the Zobrist key from scratch (used to verify the incremental key).</summary>
    public ulong ComputeKey()
    {
        ulong key = 0;
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 8; col++)
                if (squares[row, col] is Piece p) key ^= Zobrist.PieceKey(p, row, col);
        key ^= Zobrist.CastlingKey(Castling);
        if (!WhiteToMove) key ^= Zobrist.BlackToMove;
        key ^= EnPassantKeyPart();
        return key;
    }

    /// <summary>
    /// How many earlier positions in the history equal the current one. Only positions with the
    /// same side to move since the last capture or pawn move (the halfmove clock) can repeat.
    /// </summary>
    public int RepetitionCount()
    {
        int count = 0;
        int limit = Math.Min(HalfmoveClock, history.Count);
        for (int i = 2; i <= limit; i += 2)
            if (history[history.Count - i] == Key) count++;
        return count;
    }

    /// <summary>True when the current position has occurred at least twice before (third occurrence).</summary>
    public bool IsThreefoldRepetition => RepetitionCount() >= 2;

    /// <summary>Reverses MakeMove(m) exactly, given the Undo it returned.</summary>
    public void UnmakeMove(Move m, Undo undo)
    {
        WhiteToMove = !WhiteToMove;
        Castling = undo.Castling;
        EnPassantSquare = undo.EnPassantSquare;
        HalfmoveClock = undo.HalfmoveClock;
        FullmoveNumber = undo.FullmoveNumber;
        Key = undo.Key;
        enPassantKeyPart = undo.EnPassantKeyPart;
        history.RemoveAt(history.Count - 1);

        switch (m.Kind)
        {
            case MoveKind.CastleKingside:
                Relocate(squares[m.FromRow, 5]!, m.FromRow, 7);
                break;
            case MoveKind.CastleQueenside:
                Relocate(squares[m.FromRow, 3]!, m.FromRow, 0);
                break;
            case MoveKind.Promotion:
                squares[m.ToRow, m.ToCol] = undo.PromotedPawn; // pawn object still has Row/Col = destination
                break;
        }

        Relocate(squares[m.ToRow, m.ToCol]!, m.FromRow, m.FromCol);
        if (undo.Captured != null) Put(undo.Captured); // its Row/Col were never changed
    }

    // ---------------------------------------------------------------- attacks and legality

    static readonly (int, int)[] KnightJumps = { (2, -1), (-1, 2), (-2, -1), (-1, -2), (2, 1), (1, 2), (-2, 1), (1, -2) };
    static readonly (int, int)[] Straight = { (0, 1), (1, 0), (0, -1), (-1, 0) };
    static readonly (int, int)[] Diagonal = { (-1, 1), (1, 1), (-1, -1), (1, -1) };

    /// <summary>True if any piece of the given colour attacks the square (ignoring pins).</summary>
    public bool IsSquareAttacked(int row, int col, bool byWhite)
    {
        // Pawns: a white pawn attacks diagonally towards row 0, so it sits one row below (row + 1).
        int pawnRow = byWhite ? row + 1 : row - 1;
        for (int dc = -1; dc <= 1; dc += 2)
            if (Square.OnBoard(pawnRow, col + dc) && squares[pawnRow, col + dc] is Pawn p && p.IsWhite == byWhite)
                return true;

        foreach (var (dr, dc) in KnightJumps)
            if (Square.OnBoard(row + dr, col + dc) && squares[row + dr, col + dc] is Knight n && n.IsWhite == byWhite)
                return true;

        // Enemy king (missing from the original IsKingInCheck).
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
                if ((dr != 0 || dc != 0) && Square.OnBoard(row + dr, col + dc)
                    && squares[row + dr, col + dc] is King k && k.IsWhite == byWhite)
                    return true;

        foreach (var (dr, dc) in Straight)
        {
            Piece? first = FirstPieceAlong(row, col, dr, dc);
            if (first != null && first.IsWhite == byWhite && (first is Rook || first is Queen)) return true;
        }
        foreach (var (dr, dc) in Diagonal)
        {
            Piece? first = FirstPieceAlong(row, col, dr, dc);
            if (first != null && first.IsWhite == byWhite && (first is Bishop || first is Queen)) return true;
        }
        return false;
    }

    Piece? FirstPieceAlong(int row, int col, int dr, int dc)
    {
        for (int r = row + dr, c = col + dc; Square.OnBoard(r, c); r += dr, c += dc)
            if (squares[r, c] != null) return squares[r, c];
        return null;
    }

    public bool IsInCheck(bool white)
    {
        King k = KingOf(white);
        return IsSquareAttacked(k.Row, k.Col, !white);
    }

    /// <summary>Pieces in board-scan order (row 0..7, col 0..7), as the original loops visited them.</summary>
    public IEnumerable<Piece> Pieces()
    {
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 8; col++)
                if (squares[row, col] is Piece p) yield return p;
    }

    public List<Move> GeneratePseudoLegalMoves()
    {
        var moves = new List<Move>();
        AddPseudoLegalMoves(moves);
        return moves;
    }

    void AddPseudoLegalMoves(List<Move> moves)
    {
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 8; col++)
                if (squares[row, col] is Piece p && p.IsWhite == WhiteToMove)
                    p.AddPseudoLegalMoves(this, moves);
    }

    /// <summary>Pseudo-legal moves that do not leave the mover's king attacked (make, test, unmake).</summary>
    public List<Move> GenerateLegalMoves()
    {
        var legal = new List<Move>();
        GenerateLegalMoves(legal);
        return legal;
    }

    // Scratch list for pseudo-legal moves, reused by every GenerateLegalMoves call on this position.
    // Safe because legality testing (make, IsInCheck, unmake) never generates moves itself.
    readonly List<Move> pseudoScratch = new(64);

    /// <summary>
    /// Clears <paramref name="legal"/> and fills it with the legal moves, in the same order as
    /// GenerateLegalMoves(). Reusing the caller's list avoids allocating new lists for every node.
    /// Not reentrant: a Position must not generate moves from two threads at once.
    /// </summary>
    public void GenerateLegalMoves(List<Move> legal)
    {
        legal.Clear();
        pseudoScratch.Clear();
        AddPseudoLegalMoves(pseudoScratch);
        bool mover = WhiteToMove;
        foreach (Move m in pseudoScratch)
        {
            Undo u = MakeMove(m);
            if (!IsInCheck(mover)) legal.Add(m);
            UnmakeMove(m, u);
        }
    }

    /// <summary>Finds the legal move written in UCI notation (e.g. "e2e4", "e7e8q"), or null.</summary>
    public Move? FindLegalMove(string uci)
    {
        foreach (Move m in GenerateLegalMoves())
            if (m.ToString() == uci) return m;
        return null;
    }

    // ---------------------------------------------------------------- game end

    /// <summary>
    /// Material with which no sequence of legal moves can checkmate: K v K, K+minor v K,
    /// and positions whose only non-king pieces are bishops all on squares of one colour.
    /// Positions that are dead for other reasons (for example fully blocked pawns) are not detected.
    /// </summary>
    public bool IsInsufficientMaterial()
    {
        int minors = 0, knights = 0, lightBishops = 0, darkBishops = 0;
        foreach (Piece? p in squares)
        {
            if (p == null) continue;
            switch (p.Type)
            {
                case PieceType.King: break;
                case PieceType.Knight: knights++; minors++; break;
                case PieceType.Bishop:
                    minors++;
                    if ((p.Row + p.Col) % 2 == 0) lightBishops++; else darkBishops++; // a8 (0,0) is a light square
                    break;
                default: return false; // pawn, rook or queen
            }
        }
        if (minors <= 1) return true;
        return knights == 0 && (lightBishops == 0 || darkBishops == 0);
    }

    /// <summary>
    /// Fifty-move rule: 100 plies without a capture or pawn move. FIDE makes this a claimable draw
    /// (automatic after 75 moves); like most online play, this engine applies it automatically.
    /// </summary>
    public bool IsFiftyMoveDraw => HalfmoveClock >= 100;

    /// <summary>
    /// Result for the side to move. Checkmate and stalemate take precedence over the draw rules,
    /// so a checkmate delivered on the hundredth ply still wins. Threefold repetition is detected from the key history.
    /// </summary>
    public GameStatus GetStatus()
    {
        if (GenerateLegalMoves().Count == 0)
            return IsInCheck(WhiteToMove) ? GameStatus.Checkmate : GameStatus.Stalemate;
        if (IsThreefoldRepetition) return GameStatus.ThreefoldRepetition;
        if (IsFiftyMoveDraw) return GameStatus.FiftyMoveRule;
        if (IsInsufficientMaterial()) return GameStatus.InsufficientMaterial;
        return GameStatus.Ongoing;
    }
}

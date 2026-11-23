namespace ChessNEA.Engine;

public enum PieceType : byte { None, Pawn, Knight, Bishop, Rook, Queen, King }

public enum MoveKind : byte { Normal, DoublePush, EnPassant, CastleKingside, CastleQueenside, Promotion }

/// <summary>
/// A move between two squares. Squares use the original board orientation:
/// row 0 = rank 8, row 7 = rank 1, col 0 = file a.
/// The kind says which extra state MakeMove must change (rook for castling,
/// captured pawn for en passant, new piece for promotion).
/// </summary>
public readonly record struct Move(int FromRow, int FromCol, int ToRow, int ToCol,
                                   MoveKind Kind = MoveKind.Normal, PieceType Promotion = PieceType.None)
{
    /// <summary>The default value (a8a8) means "no move".</summary>
    public bool IsNone => FromRow == ToRow && FromCol == ToCol;

    public override string ToString()
    {
        if (IsNone) return "0000";
        string s = Square.Name(FromRow, FromCol) + Square.Name(ToRow, ToCol);
        return Promotion == PieceType.None ? s : s + Piece.Letter(Promotion);
    }
}

public static class Square
{
    public static string Name(int row, int col) => $"{(char)('a' + col)}{8 - row}";

    public static (int Row, int Col) Parse(string name)
    {
        if (name.Length != 2 || name[0] < 'a' || name[0] > 'h' || name[1] < '1' || name[1] > '8')
            throw new FormatException($"Invalid square '{name}'");
        return (8 - (name[1] - '0'), name[0] - 'a');
    }

    public static bool OnBoard(int row, int col) => (uint)row < 8 && (uint)col < 8;
}

[Flags]
public enum CastlingRights : byte
{
    None = 0,
    WhiteKingside = 1,
    WhiteQueenside = 2,
    BlackKingside = 4,
    BlackQueenside = 8,
    All = 15,
}

public enum GameStatus { Ongoing, Checkmate, Stalemate, FiftyMoveRule, InsufficientMaterial, ThreefoldRepetition }

/// <summary>
/// Everything MakeMove changes that cannot be recomputed from the move itself.
/// UnmakeMove uses it to restore the position exactly.
/// </summary>
public readonly struct Undo
{
    public Undo(Piece? captured, Piece? promotedPawn, CastlingRights castling, int enPassantSquare, int halfmoveClock, int fullmoveNumber,
                ulong key, ulong enPassantKeyPart)
    {
        Key = key;
        EnPassantKeyPart = enPassantKeyPart;
        Captured = captured;
        PromotedPawn = promotedPawn;
        Castling = castling;
        EnPassantSquare = enPassantSquare;
        HalfmoveClock = halfmoveClock;
        FullmoveNumber = fullmoveNumber;
    }

    /// <summary>Captured piece (for en passant, the pawn beside the destination). It keeps its own Row/Col.</summary>
    public Piece? Captured { get; }
    /// <summary>For promotions, the pawn object that was replaced.</summary>
    public Piece? PromotedPawn { get; }
    public CastlingRights Castling { get; }
    public int EnPassantSquare { get; }
    public int HalfmoveClock { get; }
    public int FullmoveNumber { get; }
    /// <summary>Zobrist key before the move.</summary>
    public ulong Key { get; }
    public ulong EnPassantKeyPart { get; }
}

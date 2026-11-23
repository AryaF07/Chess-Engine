namespace ChessNEA.Engine;

/// <summary>
/// A chess piece with an explicit logical square. Rendering coordinates are derived
/// by the GUI from Row/Col; the engine never reads pixel positions.
/// </summary>
public abstract class Piece
{
    protected Piece(bool isWhite, int row, int col)
    {
        IsWhite = isWhite;
        Row = row;
        Col = col;
    }

    public bool IsWhite { get; }
    public int Row { get; internal set; }
    public int Col { get; internal set; }
    public abstract PieceType Type { get; }

    /// <summary>Material value, positive for white and negative for black (as in the original engine).</summary>
    public int PieceValue => IsWhite ? BaseValue : -BaseValue;
    protected abstract int BaseValue { get; }

    /// <summary>Adds moves that follow this piece's movement rules; they may leave the own king in check.</summary>
    public abstract void AddPseudoLegalMoves(Position position, List<Move> moves);

    /// <summary>Piece-square table value for the current square (original tables, unchanged).</summary>
    public abstract int Evaluate();

    public static Piece Create(PieceType type, bool isWhite, int row, int col) => type switch
    {
        PieceType.Pawn => new Pawn(isWhite, row, col),
        PieceType.Knight => new Knight(isWhite, row, col),
        PieceType.Bishop => new Bishop(isWhite, row, col),
        PieceType.Rook => new Rook(isWhite, row, col),
        PieceType.Queen => new Queen(isWhite, row, col),
        PieceType.King => new King(isWhite, row, col),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static char Letter(PieceType type) => type switch
    {
        PieceType.Pawn => 'p', PieceType.Knight => 'n', PieceType.Bishop => 'b',
        PieceType.Rook => 'r', PieceType.Queen => 'q', PieceType.King => 'k',
        _ => '?',
    };

    /// <summary>FEN letter: upper case for white.</summary>
    public char FenChar => IsWhite ? char.ToUpperInvariant(Letter(Type)) : Letter(Type);

    protected void AddStep(Position position, List<Move> moves, int row, int col)
    {
        if (!Square.OnBoard(row, col)) return;
        Piece? target = position[row, col];
        if (target == null || target.IsWhite != IsWhite) moves.Add(new Move(Row, Col, row, col));
    }

    protected void AddSlide(Position position, List<Move> moves, int dRow, int dCol)
    {
        for (int row = Row + dRow, col = Col + dCol; Square.OnBoard(row, col); row += dRow, col += dCol)
        {
            Piece? target = position[row, col];
            if (target == null)
            {
                moves.Add(new Move(Row, Col, row, col));
                continue;
            }
            if (target.IsWhite != IsWhite) moves.Add(new Move(Row, Col, row, col));
            break;
        }
    }

    public override string ToString() => $"{FenChar}{Square.Name(Row, Col)}";
}

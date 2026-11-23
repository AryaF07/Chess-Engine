namespace ChessNEA.Engine;

public sealed class Pawn : Piece
{
    public Pawn(bool isWhite, int row, int col) : base(isWhite, row, col) { }
    public override PieceType Type => PieceType.Pawn;
    protected override int BaseValue => 100;

    static readonly PieceType[] PromotionChoices = { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight };

    public override void AddPseudoLegalMoves(Position position, List<Move> moves)
    {
        int dir = IsWhite ? -1 : 1;            // white moves towards row 0 (rank 8)
        int startRow = IsWhite ? 6 : 1;
        int ahead = Row + dir;
        if (!Square.OnBoard(ahead, Col)) return; // cannot happen in a legal position (pawns promote)

        if (position[ahead, Col] == null)
        {
            AddPawnMove(moves, ahead, Col);
            if (Row == startRow && position[ahead + dir, Col] == null)
                moves.Add(new Move(Row, Col, ahead + dir, Col, MoveKind.DoublePush));
        }
        for (int dc = -1; dc <= 1; dc += 2)
        {
            int col = Col + dc;
            if (!Square.OnBoard(ahead, col)) continue;
            Piece? target = position[ahead, col];
            if (target != null && target.IsWhite != IsWhite) AddPawnMove(moves, ahead, col);
        }
        // En passant: the destination is the square the enemy pawn skipped over on its double push.
        for (int dc = -1; dc <= 1; dc += 2)
        {
            int col = Col + dc;
            if (Square.OnBoard(ahead, col) && position.EnPassantSquare == ahead * 8 + col)
                moves.Add(new Move(Row, Col, ahead, col, MoveKind.EnPassant));
        }
    }

    void AddPawnMove(List<Move> moves, int toRow, int toCol)
    {
        if (toRow == 0 || toRow == 7)
        {
            foreach (PieceType p in PromotionChoices)
                moves.Add(new Move(Row, Col, toRow, toCol, MoveKind.Promotion, p));
        }
        else
        {
            moves.Add(new Move(Row, Col, toRow, toCol));
        }
    }

    // Piece-square tables, created once per type (Phase 3; previously rebuilt on every Evaluate call).
    static readonly int[,] PawnWhite = {
        {   0,   0,   0,   0,   0,   0,   0,   0 },
        {  50,  50,  50,  50,  50,  50,  50,  50 },
        {  10,  10,  20,  30,  30,  20,  10,  10 },
        {   5,   5,  10,  25,  25,  10,   5,   5 },
        {   0,   0,   0,  20,  20,   0,   0,   0 },
        {   5,  -5, -10,   0,   0, -10,  -5,   5 },
        {   5,  10,  10, -20, -20,  10,  10,   5 },
        {   0,   0,   0,   0,   0,   0,   0,   0 }
    };

    static readonly int[,] PawnBlack = {
        {   0,   0,   0,   0,   0,   0,   0,   0 },
        {  -5, -10, -10,  20,  20, -10, -10,  -5 },
        {  -5,   5,  10,   0,   0,  10,   5,  -5 },
        {   0,   0,   0, -20, -20,   0,   0,   0 },
        {  -5,  -5, -10, -25, -25, -10,  -5,  -5 },
        { -10, -10, -20, -30, -30, -20, -10, -10 },
        { -50, -50, -50, -50, -50, -50, -50, -50 },
        {   0,   0,   0,   0,   0,   0,   0,   0 }
    };

    public override int Evaluate() => IsWhite ? PawnWhite[Row, Col] : PawnBlack[Row, Col];
}

namespace ChessNEA.Engine;

public sealed class King : Piece
{
    public King(bool isWhite, int row, int col) : base(isWhite, row, col) { }
    public override PieceType Type => PieceType.King;
    protected override int BaseValue => 20000;

    public override void AddPseudoLegalMoves(Position position, List<Move> moves)
    {
        // Same order as the original: for each row offset, columns left, centre, right.
        for (int dRow = -1; dRow <= 1; dRow++)
            for (int dCol = -1; dCol <= 1; dCol++)
                if (dRow != 0 || dCol != 0)
                    AddStep(position, moves, Row + dRow, Col + dCol);

        AddCastling(position, moves);
    }

    /// <summary>
    /// Castling needs: the right (king and that rook unmoved, rook not captured), empty squares
    /// between king and rook, the king not in check, and the square the king crosses not attacked.
    /// The landing square is checked by the general legality filter like any other king move.
    /// </summary>
    void AddCastling(Position position, List<Move> moves)
    {
        int home = IsWhite ? 7 : 0;
        if (Row != home || Col != 4) return;
        CastlingRights queenSide = IsWhite ? CastlingRights.WhiteQueenside : CastlingRights.BlackQueenside;
        CastlingRights kingSide = IsWhite ? CastlingRights.WhiteKingside : CastlingRights.BlackKingside;
        if ((position.Castling & (queenSide | kingSide)) == 0) return;

        bool enemy = !IsWhite;
        if (position.IsSquareAttacked(home, 4, enemy)) return; // cannot castle out of check

        if ((position.Castling & queenSide) != 0
            && position[home, 0] is Rook qr && qr.IsWhite == IsWhite
            && position[home, 1] == null && position[home, 2] == null && position[home, 3] == null
            && !position.IsSquareAttacked(home, 3, enemy))
            moves.Add(new Move(home, 4, home, 2, MoveKind.CastleQueenside));

        if ((position.Castling & kingSide) != 0
            && position[home, 7] is Rook kr && kr.IsWhite == IsWhite
            && position[home, 5] == null && position[home, 6] == null
            && !position.IsSquareAttacked(home, 5, enemy))
            moves.Add(new Move(home, 4, home, 6, MoveKind.CastleKingside));
    }

    // Piece-square tables, created once per type (Phase 3; previously rebuilt on every Evaluate call).
    static readonly int[,] KingWhite = {
        { -30, -40, -40, -50, -50, -40, -40, -30 },
        { -30, -40, -40, -50, -50, -40, -40, -30 },
        { -30, -40, -40, -50, -50, -40, -40, -30 },
        { -30, -40, -40, -50, -50, -40, -40, -30 },
        { -20, -30, -30, -40, -40, -30, -30, -20 },
        { -10, -20, -20, -20, -20, -20, -20, -10 },
        {  20,  20,   0,   0,   0,   0,  20,  20 },
        {  20,  30,  10,   0,   0,  10,  30,  20 }
    };

    static readonly int[,] KingBlack = {
        { -20, -30, -10,   0,   0, -10, -30, -20 },
        { -20, -20,   0,   0,   0,   0, -20, -20 },
        {  10,  20,  20,  20,  20,  20,  20,  10 },
        {  20,  30,  30,  40,  40,  30,  30,  20 },
        {  30,  40,  40,  50,  50,  40,  40,  30 },
        {  30,  40,  40,  50,  50,  40,  40,  30 },
        {  30,  40,  40,  50,  50,  40,  40,  30 },
        {  30,  40,  40,  50,  50,  40,  40,  30 }
    };

    public override int Evaluate() => IsWhite ? KingWhite[Row, Col] : KingBlack[Row, Col];
}

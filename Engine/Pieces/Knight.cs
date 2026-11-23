namespace ChessNEA.Engine;

public sealed class Knight : Piece
{
    public Knight(bool isWhite, int row, int col) : base(isWhite, row, col) { }
    public override PieceType Type => PieceType.Knight;
    protected override int BaseValue => 320;

    // Same jump order as the original nested loops (j = -1, 1; i = 2, -2).
    static readonly (int dRow, int dCol)[] Jumps =
    {
        (2, -1), (-1, 2), (-2, -1), (-1, -2),
        (2, 1), (1, 2), (-2, 1), (1, -2),
    };

    public override void AddPseudoLegalMoves(Position position, List<Move> moves)
    {
        foreach (var (dRow, dCol) in Jumps) AddStep(position, moves, Row + dRow, Col + dCol);
    }

    // Piece-square tables, created once per type (Phase 3; previously rebuilt on every Evaluate call).
    static readonly int[,] KnightWhite = {
        { -50, -40, -30, -30, -30, -30, -40, -50 },
        { -40, -20,   0,   0,   0,   0, -20, -40 },
        { -30,   0,  10,  15,  15,  10,   0, -30 },
        { -30,   5,  15,  20,  20,  15,   5, -30 },
        { -30,   0,  15,  20,  20,  15,   0, -30 },
        { -30,   5,  10,  15,  15,  10,   5, -30 },
        { -40, -20,   0,   0,   5,   5,   0, -40 },
        { -50, -40, -30, -30, -30, -30, -40, -50 }
    };

    static readonly int[,] KnightBlack = {
        {  50,  40,  30,  30,  30,  30,  40,  50 },
        {  40,  20,   0,   0,  -5,  -5,   0,  40 },
        {  30,  -5, -10, -15, -15, -10,  -5,  30 },
        {  30,   0, -15, -20, -20, -15,   0,  30 },
        {  30,  -5, -15, -20, -20, -15,  -5,  30 },
        {  30,   0, -10, -15, -15, -10,   0,  30 },
        {  40,  20,   0,   0,   0,   0,  20,  40 },
        {  50,  40,  30,  30,  30,  30,  40,  50 }
    };

    public override int Evaluate() => IsWhite ? KnightWhite[Row, Col] : KnightBlack[Row, Col];
}

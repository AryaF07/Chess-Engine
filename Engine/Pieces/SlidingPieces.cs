namespace ChessNEA.Engine;

// Direction order matches the original findMoves loops, so moves are generated in the same order.

public sealed class Rook : Piece
{
    public Rook(bool isWhite, int row, int col) : base(isWhite, row, col) { }
    public override PieceType Type => PieceType.Rook;
    protected override int BaseValue => 500;

    public override void AddPseudoLegalMoves(Position position, List<Move> moves)
    {
        AddSlide(position, moves, 0, 1);   // right
        AddSlide(position, moves, 1, 0);   // down (towards rank 1)
        AddSlide(position, moves, 0, -1);  // left
        AddSlide(position, moves, -1, 0);  // up (towards rank 8)
    }

    // Piece-square tables, created once per type (Phase 3; previously rebuilt on every Evaluate call).
    static readonly int[,] RookWhite = {
        {   0,   0,   0,   0,   0,   0,   0,   0 },
        {   5,  10,  10,  10,  10,  10,  10,   5 },
        {  -5,   0,   0,   0,   0,   0,   0,  -5 },
        {  -5,   0,   0,   0,   0,   0,   0,  -5 },
        {  -5,   0,   0,   0,   0,   0,   0,  -5 },
        {  -5,   0,   0,   0,   0,   0,   0,  -5 },
        {  -5,   0,   0,   0,   0,   0,   0,  -5 },
        {   0,   0,   0,   5,   5,   0,   0,   0 }
    };

    static readonly int[,] RookBlack = {
        {   0,   0,   0,  -5,  -5,   0,   0,   0 },
        {   5,   0,   0,   0,   0,   0,   0,   5 },
        {   5,   0,   0,   0,   0,   0,   0,   5 },
        {   5,   0,   0,   0,   0,   0,   0,   5 },
        {   5,   0,   0,   0,   0,   0,   0,   5 },
        {   5,   0,   0,   0,   0,   0,   0,   5 },
        {  -5, -10, -10, -10, -10, -10, -10,  -5 },
        {   0,   0,   0,   0,   0,   0,   0,   0 }
    };

    public override int Evaluate() => IsWhite ? RookWhite[Row, Col] : RookBlack[Row, Col];
}

public sealed class Bishop : Piece
{
    public Bishop(bool isWhite, int row, int col) : base(isWhite, row, col) { }
    public override PieceType Type => PieceType.Bishop;
    protected override int BaseValue => 330;

    public override void AddPseudoLegalMoves(Position position, List<Move> moves)
    {
        AddSlide(position, moves, -1, 1);   // up-right
        AddSlide(position, moves, 1, 1);    // down-right
        AddSlide(position, moves, -1, -1);  // up-left
        AddSlide(position, moves, 1, -1);   // down-left
    }

    // Piece-square tables, created once per type (Phase 3; previously rebuilt on every Evaluate call).
    static readonly int[,] BishopWhite = {
        { -20, -10, -10, -10, -10, -10, -10, -20 },
        { -10,   0,   0,   0,   0,   0,   0, -10 },
        { -10,   0,   5,  10,  10,   5,   0, -10 },
        { -10,   5,   5,  10,  10,   5,   5, -10 },
        { -10,   0,  10,  10,  10,  10,   0, -10 },
        { -10,  10,  10,  10,  10,  10,  10, -10 },
        { -10,   5,   0,   0,   0,   0,   5, -10 },
        { -20, -10, -10, -10, -10, -10, -10, -20 }
    };

    static readonly int[,] BishopBlack = {
        {  20,  10,  10,  10,  10,  10,  10,  20 },
        {  10,  -5,   0,   0,   0,   0,  -5,  10 },
        {  10, -10, -10, -10, -10, -10, -10,  10 },
        {  10,   0, -10, -10, -10, -10,   0,  10 },
        {  10,  -5,  -5, -10, -10,  -5,  -5,  10 },
        {  10,   0,  -5, -10, -10,  -5,   0,  10 },
        {  10,   0,   0,   0,   0,   0,   0,  10 },
        {  20,  10,  10,  10,  10,  10,  10,  20 }
    };

    public override int Evaluate() => IsWhite ? BishopWhite[Row, Col] : BishopBlack[Row, Col];
}

public sealed class Queen : Piece
{
    public Queen(bool isWhite, int row, int col) : base(isWhite, row, col) { }
    public override PieceType Type => PieceType.Queen;
    protected override int BaseValue => 900;

    public override void AddPseudoLegalMoves(Position position, List<Move> moves)
    {
        AddSlide(position, moves, -1, 1);
        AddSlide(position, moves, 1, 1);
        AddSlide(position, moves, -1, -1);
        AddSlide(position, moves, 1, -1);
        AddSlide(position, moves, 0, 1);
        AddSlide(position, moves, 1, 0);
        AddSlide(position, moves, 0, -1);
        AddSlide(position, moves, -1, 0);
    }

    // Piece-square tables, created once per type (Phase 3; previously rebuilt on every Evaluate call).
    static readonly int[,] QueenWhite = {
        { -20, -10, -10,  -5,  -5, -10, -10, -20 },
        { -10,   0,   0,   0,   0,   0,   0, -10 },
        { -10,   0,   5,   5,   5,   5,   0, -10 },
        {  -5,   0,   5,   5,   5,   5,   0,  -5 },
        {   0,   0,   5,   5,   5,   5,   0,  -5 },
        { -10,   5,   5,   5,   5,   5,   0, -10 },
        { -10,   0,   5,   0,   0,   0,   0, -10 },
        { -20, -10, -10,  -5,  -5, -10, -10, -20 }
    };

    static readonly int[,] QueenBlack = {
        {  20,  10,  10,   5,   5,  10,  10,  20 },
        {  10,   0,  -5,   0,   0,   0,   0,  10 },
        {  10,  -5,  -5,  -5,  -5,  -5,   0,  10 },
        {   0,   0,  -5,  -5,  -5,  -5,   0,   5 },
        {   5,   0,  -5,  -5,  -5,  -5,   0,   5 },
        {  10,   0,  -5,  -5,  -5,  -5,   0,  10 },
        {  10,   0,   0,   0,   0,   0,   0,  10 },
        {  20,  10,  10,   5,   5,  10,  10,  20 }
    };

    public override int Evaluate() => IsWhite ? QueenWhite[Row, Col] : QueenBlack[Row, Col];
}

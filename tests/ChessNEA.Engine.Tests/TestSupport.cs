namespace ChessNEA.Engine.Tests;

/// <summary>Records everything observable about a position so make/unmake restoration can be checked.</summary>
internal sealed class PositionState
{
    readonly string fen;
    readonly ulong key;
    readonly int historyLength;
    readonly Piece?[,] occupants = new Piece?[8, 8];

    public PositionState(Position p)
    {
        fen = p.ToFen();
        key = p.Key;
        historyLength = p.HistoryLength;
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                occupants[r, c] = p[r, c];
    }

    /// <summary>Returns a description of the first difference, or null if identical.</summary>
    public string? Difference(Position p)
    {
        if (p.ToFen() != fen) return $"FEN {fen} -> {p.ToFen()}";
        if (p.Key != key) return $"key {key:X16} -> {p.Key:X16}";
        if (p.Key != p.ComputeKey()) return "incremental key differs from recomputed key";
        if (p.HistoryLength != historyLength) return $"history length {historyLength} -> {p.HistoryLength}";
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                Piece? piece = p[r, c];
                if (!ReferenceEquals(piece, occupants[r, c])) return $"different piece object on {Square.Name(r, c)}";
                if (piece != null && (piece.Row != r || piece.Col != c))
                    return $"{piece} stored on {Square.Name(r, c)} but its Row/Col disagree";
            }
        return null;
    }
}

internal static class Perft
{
    public static long Count(Position p, int depth)
    {
        List<Move> moves = p.GenerateLegalMoves();
        if (depth == 1) return moves.Count;
        long nodes = 0;
        foreach (Move m in moves)
        {
            Undo u = p.MakeMove(m);
            nodes += Count(p, depth - 1);
            p.UnmakeMove(m, u);
        }
        return nodes;
    }

    public static string Divide(Position p, int depth)
    {
        var lines = new List<string>();
        foreach (Move m in p.GenerateLegalMoves())
        {
            Undo u = p.MakeMove(m);
            lines.Add($"{m}: {(depth <= 1 ? 1 : Count(p, depth - 1))}");
            p.UnmakeMove(m, u);
        }
        return string.Join(Environment.NewLine, lines);
    }
}

internal static class Helpers
{
    public static Move Legal(Position p, string uci)
        => p.FindLegalMove(uci) ?? throw new Xunit.Sdk.XunitException($"{uci} is not legal in {p.ToFen()}");

    public static bool IsLegal(Position p, string uci) => p.FindLegalMove(uci) != null;

    public static void Play(Position p, params string[] ucis)
    {
        foreach (string u in ucis) p.MakeMove(Legal(p, u));
    }
}

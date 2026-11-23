namespace ChessNEA.Engine.Tests;

/// <summary>
/// Leaf counts of the legal-move tree compared with published reference values
/// (Chess Programming Wiki, "Perft Results"). Together these positions exercise
/// castling, en passant, promotion (including under-promotion), checks and pins.
/// </summary>
public class PerftTests
{
    public const string Kiwipete = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";
    public const string Position3 = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1";
    public const string Position4 = "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1";
    public const string Position4Mirrored = "r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1";
    public const string Position5 = "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8";
    public const string Position6 = "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10";

    [Theory]
    [InlineData(Position.StartFen, 1, 20L)]
    [InlineData(Position.StartFen, 2, 400L)]
    [InlineData(Position.StartFen, 3, 8902L)]
    [InlineData(Position.StartFen, 4, 197281L)]
    [InlineData(Kiwipete, 1, 48L)]
    [InlineData(Kiwipete, 2, 2039L)]
    [InlineData(Kiwipete, 3, 97862L)]
    [InlineData(Position3, 1, 14L)]
    [InlineData(Position3, 2, 191L)]
    [InlineData(Position3, 3, 2812L)]
    [InlineData(Position3, 4, 43238L)]
    [InlineData(Position3, 5, 674624L)]
    [InlineData(Position4, 1, 6L)]
    [InlineData(Position4, 2, 264L)]
    [InlineData(Position4, 3, 9467L)]
    [InlineData(Position4Mirrored, 3, 9467L)]
    [InlineData(Position5, 1, 44L)]
    [InlineData(Position5, 2, 1486L)]
    [InlineData(Position5, 3, 62379L)]
    [InlineData(Position6, 1, 46L)]
    [InlineData(Position6, 2, 2079L)]
    [InlineData(Position6, 3, 89890L)]
    public void Perft_matches_reference(string fen, int depth, long expected)
    {
        var p = Position.FromFen(fen);
        var before = new PositionState(p);
        long nodes = Perft.Count(p, depth);
        Assert.True(expected == nodes,
            $"perft({depth}) of {fen}: expected {expected}, got {nodes}\n{Perft.Divide(Position.FromFen(fen), depth)}");
        Assert.Null(before.Difference(p));
    }

    [Theory]
    [InlineData(Position.StartFen, 5, 4865609L)]
    [InlineData(Kiwipete, 4, 4085603L)]
    [InlineData(Position4, 4, 422333L)]
    [InlineData(Position5, 4, 2103487L)]
    [InlineData(Position6, 4, 3894594L)]
    [Trait("Category", "Slow")]
    public void Perft_matches_reference_deep(string fen, int depth, long expected)
    {
        var p = Position.FromFen(fen);
        Assert.Equal(expected, Perft.Count(p, depth));
    }
}

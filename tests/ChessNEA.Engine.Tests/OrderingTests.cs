namespace ChessNEA.Engine.Tests;

public class OrderingTests
{
    [Fact]
    public void Mvv_lva_prefers_valuable_victims_and_cheap_attackers()
    {
        // White: pawn d4 can take the queen on e5; queen h2 can take the pawn on h7; knight b1 has quiet moves.
        var p = Position.FromFen("4k3/7p/8/4q3/3P4/8/7Q/KN6 w - - 0 1");
        int pawnTakesQueen = Bot.OrderKey(p, Helpers.Legal(p, "d4e5"));
        int queenTakesPawn = Bot.OrderKey(p, Helpers.Legal(p, "h2h7"));
        int quiet = Bot.OrderKey(p, Helpers.Legal(p, "b1c3"));
        Assert.True(pawnTakesQueen > queenTakesPawn);
        Assert.True(queenTakesPawn > quiet);
        Assert.Equal(0, quiet);
    }

    [Fact]
    public void Promotions_rank_above_quiet_moves_and_queen_above_underpromotion()
    {
        var p = Position.FromFen("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1");
        int queen = Bot.OrderKey(p, Helpers.Legal(p, "b7b8q"));
        int knight = Bot.OrderKey(p, Helpers.Legal(p, "b7b8n"));
        int quiet = Bot.OrderKey(p, Helpers.Legal(p, "e1d2"));
        Assert.True(queen > knight && knight > quiet);
    }

    [Fact]
    public void En_passant_counts_as_a_pawn_capture()
    {
        var p = Position.FromFen("7k/8/8/3pP3/8/8/8/K7 w - d6 0 2");
        Assert.True(Bot.OrderKey(p, Helpers.Legal(p, "e5d6")) > 0);
    }

    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData(PerftTests.Kiwipete)]
    [InlineData(PerftTests.Position4)]
    [InlineData(PerftTests.Position5)]
    [InlineData(PerftTests.Position6)]
    [InlineData("r2qkb1r/pp2nppp/3p4/2pNN1B1/2BnP3/3P4/PPP2PPP/R2bK2R w KQkq - 1 10")]
    [InlineData("8/5pk1/6p1/8/8/6P1/5PK1/3R4 b - - 0 1")]
    public void Ordering_never_changes_the_root_score(string fen)
    {
        var ordered = new Bot();
        var unordered = new Bot { UseMoveOrdering = false };
        int a = ordered.Search(Position.FromFen(fen), 3).Score;
        int b = unordered.Search(Position.FromFen(fen), 3).Score;
        Assert.Equal(b, a);
    }

    /// <summary>
    /// With search repetition draws off (they were added later), the search is exactly the one
    /// recorded in results/phase4/1-ordering-nodes (commit 51a63ce): same node counts, ordered and unordered.
    /// </summary>
    [Theory]
    [InlineData(Position.StartFen, true, 6308L)]
    [InlineData(PerftTests.Kiwipete, true, 6908L)]
    [InlineData("8/5pk1/6p1/8/8/6P1/5PK1/3R4 b - - 0 1", true, 7210L)]
    [InlineData("1K1k4/1P6/8/8/8/8/r7/2R5 w - - 0 1", true, 705L)]
    [InlineData("1K1k4/1P6/8/8/8/8/r7/2R5 w - - 0 1", false, 1103L)]
    [InlineData("8/8/4k3/8/2P5/4K3/8/8 w - - 0 1", false, 1111L)]
    public void Repetition_draws_off_reproduces_the_recorded_node_counts(string fen, bool ordering, long nodes)
    {
        var bot = new Bot { UseMoveOrdering = ordering, UseRepetitionDraws = false };
        bot.Search(Position.FromFen(fen), 4);
        Assert.Equal(nodes, bot.Stats.Nodes);
    }

    [Fact]
    public void Ordering_without_pruning_visits_the_same_tree()
    {
        var ordered = new Bot { UseAlphaBeta = false };
        var unordered = new Bot { UseAlphaBeta = false, UseMoveOrdering = false };
        ordered.Search(Position.FromFen(PerftTests.Kiwipete), 2);
        unordered.Search(Position.FromFen(PerftTests.Kiwipete), 2);
        Assert.Equal(unordered.Stats.Nodes, ordered.Stats.Nodes);
    }
}

namespace ChessNEA.Engine.Tests;

public class SearchCounterTests
{
    /// <summary>
    /// Without pruning, a depth-d search visits every node of the legal-move tree to depth d,
    /// so (when no terminal or draw positions occur) Nodes = perft(0) + ... + perft(d) and
    /// LeafEvaluations = perft(d). This ties the search counters to the verified perft counts.
    /// </summary>
    [Fact]
    public void Unpruned_minimax_visits_exactly_the_perft_tree()
    {
        var p = Position.Start();
        var bot = new Bot { UseAlphaBeta = false };
        bot.Search(p, 3);
        Assert.Equal(1 + 20 + 400 + 8902, bot.Stats.Nodes);
        Assert.Equal(8902, bot.Stats.LeafEvaluations);
        Assert.Equal(0, bot.Stats.Cutoffs);
        Assert.Equal(0, bot.Stats.TerminalNodes);
        Assert.Equal(1 + 20 + 400, bot.Stats.InteriorNodes);
        Assert.Equal(20 + 400 + 8902 + 197281, bot.Stats.LegalMovesGenerated); // leaves generate moves too
    }

    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData(PerftTests.Kiwipete)]
    [InlineData(PerftTests.Position4)]
    [InlineData(PerftTests.Position5)]
    [InlineData("k7/8/8/8/8/1r6/r7/7K b - - 0 1")]
    [InlineData("8/5pk1/6p1/8/8/6P1/5PK1/3R4 b - - 0 1")]
    public void Alpha_beta_returns_the_minimax_score_with_no_more_nodes(string fen)
    {
        var minimax = new Bot { UseAlphaBeta = false };
        var alphaBeta = new Bot();
        int m = minimax.Search(Position.FromFen(fen), 3).Score;
        int a = alphaBeta.Search(Position.FromFen(fen), 3).Score;
        Assert.Equal(m, a);
        Assert.True(alphaBeta.Stats.Nodes <= minimax.Stats.Nodes);
    }

    [Fact]
    public void Node_categories_add_up()
    {
        var bot = new Bot();
        bot.Search(Position.FromFen(PerftTests.Kiwipete), 3);
        var s = bot.Stats;
        Assert.True(s.InteriorNodes > 0);
        Assert.Equal(s.Nodes, s.InteriorNodes + s.LeafEvaluations + s.TerminalNodes + s.DrawRuleNodes);
    }

    [Fact]
    public void Counters_reset_for_each_search_and_are_deterministic()
    {
        var bot = new Bot();
        bot.Search(Position.Start(), 3);
        long first = bot.Stats.Nodes;
        bot.Search(Position.Start(), 3);
        Assert.Equal(first, bot.Stats.Nodes);
    }

    [Fact]
    public void Mate_and_draw_nodes_are_counted()
    {
        var bot = new Bot { UseAlphaBeta = false };
        bot.Search(Position.FromFen("k7/8/8/8/8/1r6/r7/7K b - - 0 1"), 2);
        Assert.True(bot.Stats.TerminalNodes > 0); // b3b1 mates
        bot.Search(Position.FromFen("8/8/4k3/8/8/3NK3/8/8 w - - 0 1"), 2);
        Assert.Equal(1, bot.Stats.DrawRuleNodes); // root is a dead position
        Assert.Equal(1, bot.Stats.Nodes);
    }
}

public class BenchSuiteTests
{
    static string SuitePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "bench", "suite-v1.txt");
    }

    [Fact]
    public void Suite_v1_positions_are_valid_unique_and_cover_all_categories()
    {
        var lines = File.ReadAllLines(SuitePath()).Where(l => l.Trim().Length > 0 && !l.StartsWith('#')).ToList();
        Assert.Equal(15, lines.Count);
        var names = new HashSet<string>();
        var categories = new HashSet<string>();
        foreach (string line in lines)
        {
            string[] parts = line.Split(';');
            Assert.Equal(3, parts.Length);
            Assert.True(names.Add(parts[0]), $"duplicate name {parts[0]}");
            categories.Add(parts[1]);
            var p = Position.FromFen(parts[2]);
            Assert.Equal(parts[2], p.ToFen());
            Assert.Equal(GameStatus.Ongoing, p.GetStatus());
        }
        Assert.Equal(new[] { "endgame", "middlegame", "opening", "tactical" }, categories.OrderBy(c => c));
    }
}

using ChessNEA.Bench;

namespace ChessNEA.Engine.Tests;

/// <summary>Regression tests for the review's benchmark findings 4 and 6.</summary>
public class CompareConfigurationTests
{
    /// <summary>Finding 4: the "unpruned minimax" side must make no cutoffs.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plain_minimax_baseline_makes_no_cutoffs(bool ordering)
    {
        var bot = Compare.MakeBot(alphaBeta: false, ordering);
        bot.Search(Position.Start(), 4);
        Assert.Equal(0, bot.Stats.Cutoffs);
    }

    [Fact]
    public void Plain_minimax_baseline_visits_the_full_perft_tree()
    {
        var bot = Compare.MakeBot(alphaBeta: false, ordering: false);
        bot.Search(Position.Start(), 3);
        Assert.Equal(1 + 20 + 400 + 8902, bot.Stats.Nodes);
    }

    [Theory]
    [InlineData(Position.StartFen, false)]
    [InlineData(PerftTests.Kiwipete, false)]
    [InlineData("8/5pk1/6p1/8/8/6P1/5PK1/3R4 b - - 0 1", false)]
    [InlineData(PerftTests.Kiwipete, true)]
    public void Both_sides_of_the_comparison_return_the_same_root_score(string fen, bool ordering)
        => Assert.Equal(Compare.MakeBot(false, ordering).Search(Position.FromFen(fen), 3).Score,
                        Compare.MakeBot(true, ordering).Search(Position.FromFen(fen), 3).Score);
}

[Collection("Bench static configuration")]
public class MicroConfigurationTests
{
    /// <summary>Finding 6: the micro measurements must use the configured bot, not a default one.</summary>
    [Fact]
    public void Micro_measurements_use_the_configured_bot()
    {
        var saved = Bench.Bench.MakeBot;
        int created = 0;
        try
        {
            Bench.Bench.MakeBot = () => { created++; return new Bot { UseMoveOrdering = false }; };
            Bench.Bench.RunMicro(1);
            Assert.True(created > 0);
        }
        finally { Bench.Bench.MakeBot = saved; }
    }
}

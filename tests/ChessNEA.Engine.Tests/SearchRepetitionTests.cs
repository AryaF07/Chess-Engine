namespace ChessNEA.Engine.Tests;

public class SearchRepetitionTests
{
    [Fact]
    public void Returning_to_an_earlier_position_scores_as_a_draw()
    {
        var p = Position.Start();
        Helpers.Play(p, "g1f3", "g8f6", "f3g1");
        Helpers.Play(p, "f6g8"); // back to the starting position, which is in the history
        var bot = new Bot();
        Assert.Equal(0, bot.Minimax(p, 3, 1, -Bot.Infinity, Bot.Infinity).Score);
        Assert.Equal(1, bot.Stats.Nodes);
    }

    [Fact]
    public void The_root_itself_is_searched_even_if_it_repeats()
    {
        var p = Position.Start();
        Helpers.Play(p, "g1f3", "g8f6", "f3g1", "f6g8");
        var (_, move) = new Bot().Search(p, 2);
        Assert.False(move.IsNone);
    }

    [Fact]
    public void Repetition_draws_can_be_switched_off()
    {
        var p = Position.Start();
        Helpers.Play(p, "g1f3", "g8f6", "f3g1", "f6g8");
        var bot = new Bot { UseRepetitionDraws = false };
        bot.Minimax(p, 1, 1, -Bot.Infinity, Bot.Infinity);
        Assert.True(bot.Stats.Nodes > 1);
    }
}

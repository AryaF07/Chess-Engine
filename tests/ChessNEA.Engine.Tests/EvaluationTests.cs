namespace ChessNEA.Engine.Tests;

public class EvaluationTests
{
    /// <summary>Colour-mirrored position: ranks reversed, colours swapped, side to move swapped.</summary>
    static string Mirror(string fen)
    {
        string[] f = fen.Split(' ');
        string Swap(string s) => new(s.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray());
        string placement = string.Join('/', f[0].Split('/').Reverse().Select(Swap));
        string side = f[1] == "w" ? "b" : "w";
        string castling = f[2] == "-" ? "-" : new string(Swap(f[2]).OrderBy(c => "KQkq".IndexOf(c)).ToArray());
        string ep = f[3] == "-" ? "-" : $"{f[3][0]}{(char)('1' + '8' - f[3][1])}";
        return $"{placement} {side} {castling} {ep} {f[4]} {f[5]}";
    }

    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData(PerftTests.Kiwipete)]
    [InlineData(PerftTests.Position3)]
    [InlineData(PerftTests.Position4)]
    [InlineData("8/8/4k3/8/2P5/4K3/8/8 w - - 0 1")]
    [InlineData("1K1k4/1P6/8/8/8/8/r7/2R5 w - - 0 1")]
    [InlineData("8/5pk1/6p1/8/8/6P1/5PK1/3R4 b - - 0 1")]
    public void Mirrored_position_has_the_negated_evaluation(string fen)
    {
        var bot = new Bot();
        Assert.Equal(-bot.Evaluate(Position.FromFen(fen)), bot.Evaluate(Position.FromFen(Mirror(fen))));
    }
}

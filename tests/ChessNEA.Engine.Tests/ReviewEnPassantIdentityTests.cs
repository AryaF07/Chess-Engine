namespace ChessNEA.Engine.Tests;

/// <summary>
/// Regression tests for the external review of commit 79a01d8. Each test was first run
/// against the unfixed code and failed (see docs/phase6-review-fixes.md).
/// </summary>
public class EnPassantIdentityTests
{
    /// <summary>
    /// Finding 3: e5xd6 e.p. is illegal (it would expose the white king on e1 to the rook on e8), so the
    /// d6 right changes nothing about the position. After two king/king cycles the position occurs
    /// for the third time and the game is drawn.
    /// </summary>
    [Fact]
    public void Pinned_en_passant_right_does_not_prevent_threefold_repetition()
    {
        var p = Position.FromFen("k3r3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
        Assert.False(Helpers.IsLegal(p, "e5d6"));
        Helpers.Play(p, "e1f1", "a8b8", "f1e1", "b8a8", "e1f1", "a8b8", "f1e1", "b8a8");
        Assert.Equal(2, p.RepetitionCount());
        Assert.Equal(GameStatus.ThreefoldRepetition, p.GetStatus());
    }

    [Theory]
    [InlineData("k3r3/8/8/3pP3/8/8/8/4K3 w - d6 0 1")]   // pinned along the file
    [InlineData("7k/8/8/K2pP2r/8/8/8/8 w - d6 0 2")]      // both pawns leave the rank: discovered rook check
    public void Illegal_en_passant_right_does_not_change_the_key(string fen)
    {
        string noEp = string.Join(' ', fen.Split(' ').Select((f, i) => i == 3 ? "-" : f));
        Assert.Equal(Position.FromFen(noEp).Key, Position.FromFen(fen).Key);
    }

    [Fact]
    public void Legal_en_passant_right_changes_the_key()
        => Assert.NotEqual(Position.FromFen("7k/8/8/3pP3/8/8/8/K7 w - - 0 2").Key,
                           Position.FromFen("7k/8/8/3pP3/8/8/8/K7 w - d6 0 2").Key);

    [Theory]
    [InlineData("k3r3/8/8/3pP3/8/8/8/4K3 w - d6 0 1", 4)]
    [InlineData("7k/8/8/K2pP2r/8/8/8/8 w - d6 0 2", 4)]
    [InlineData("4k3/3p4/8/4P3/8/8/8/4K2R b K - 0 1", 4)]
    public void Incremental_key_matches_recomputed_key_with_en_passant_rights(string fen, int depth)
    {
        static void Walk(Position p, int d)
        {
            Assert.Equal(p.ComputeKey(), p.Key);
            if (d == 0) return;
            foreach (Move m in p.GenerateLegalMoves())
            {
                Undo u = p.MakeMove(m);
                Walk(p, d - 1);
                p.UnmakeMove(m, u);
            }
        }
        Walk(Position.FromFen(fen), depth);
    }
}

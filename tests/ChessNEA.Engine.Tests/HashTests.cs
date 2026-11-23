namespace ChessNEA.Engine.Tests;

public class HashTests
{
    static long WalkCheckingKeys(Position p, int depth)
    {
        Assert.Equal(p.ComputeKey(), p.Key);
        if (depth == 0) return 1;
        long n = 0;
        foreach (Move m in p.GenerateLegalMoves())
        {
            ulong before = p.Key;
            int historyBefore = p.HistoryLength;
            Undo u = p.MakeMove(m);
            n += WalkCheckingKeys(p, depth - 1);
            p.UnmakeMove(m, u);
            Assert.Equal(before, p.Key);
            Assert.Equal(historyBefore, p.HistoryLength);
        }
        return n;
    }

    /// <summary>The incremental key equals the key recomputed from scratch at every node of the tree.</summary>
    [Theory]
    [InlineData(Position.StartFen, 4)]
    [InlineData(PerftTests.Kiwipete, 3)]
    [InlineData(PerftTests.Position3, 4)]
    [InlineData(PerftTests.Position4, 3)]
    [InlineData(PerftTests.Position5, 3)]
    [InlineData("r3k2r/8/8/2pP4/8/8/8/R3K2R w KQkq c6 0 1", 3)]
    public void Incremental_key_matches_recomputed_key_everywhere(string fen, int depth)
        => WalkCheckingKeys(Position.FromFen(fen), depth);

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void Incremental_key_matches_in_random_games(int seed)
    {
        var rng = new Random(seed);
        var p = Position.Start();
        for (int ply = 0; ply < 300; ply++)
        {
            var moves = p.GenerateLegalMoves();
            if (moves.Count == 0) break;
            p.MakeMove(moves[rng.Next(moves.Count)]);
            Assert.Equal(p.ComputeKey(), p.Key);
        }
    }

    [Fact]
    public void Transpositions_have_equal_keys()
    {
        var a = Position.Start();
        Helpers.Play(a, "g1f3", "g8f6", "b1c3");
        var b = Position.Start();
        Helpers.Play(b, "b1c3", "g8f6", "g1f3");
        Assert.Equal(a.Key, b.Key);
        Assert.Equal(Position.FromFen(a.ToFen()).Key, a.Key);
    }

    [Fact]
    public void Side_to_move_and_castling_rights_change_the_key()
    {
        ulong w = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1").Key;
        ulong b = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1").Key;
        ulong noK = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w Qkq - 0 1").Key;
        Assert.NotEqual(w, b);
        Assert.NotEqual(w, noK);
    }

    [Fact]
    public void En_passant_square_counts_only_when_a_capture_is_possible()
    {
        // e3 set, black pawn on d4 can capture legally: the key includes the e-file.
        ulong usable = Position.FromFen("4k3/8/8/8/3pP3/8/8/4K3 b - e3 0 1").Key;
        ulong none = Position.FromFen("4k3/8/8/8/3pP3/8/8/4K3 b - - 0 1").Key;
        Assert.NotEqual(usable, none);
        // e3 set but no black pawn beside e4: same key as without the square.
        ulong unusable = Position.FromFen("4k3/8/8/8/4P3/8/8/4K3 b - e3 0 1").Key;
        ulong plain = Position.FromFen("4k3/8/8/8/4P3/8/8/4K3 b - - 0 1").Key;
        Assert.Equal(plain, unusable);
    }

    [Fact]
    public void No_key_collisions_among_distinct_random_positions()
    {
        var rng = new Random(7);
        var seen = new Dictionary<ulong, string>();
        int distinct = 0;
        while (distinct < 20000)
        {
            var p = Position.Start();
            int plies = rng.Next(1, 80);
            for (int i = 0; i < plies; i++)
            {
                var moves = p.GenerateLegalMoves();
                if (moves.Count == 0) break;
                p.MakeMove(moves[rng.Next(moves.Count)]);
            }
            // Identity for hashing: placement, side to move, castling rights and the en-passant square
            // only when it is usable (the move counters are not part of the key).
            string[] f = p.ToFen().Split(' ');
            ulong withoutEp = Position.FromFen($"{f[0]} {f[1]} {f[2]} - 0 1").Key;
            string identity = $"{f[0]} {f[1]} {f[2]} {(p.Key == withoutEp ? "-" : f[3])}";
            if (seen.TryGetValue(p.Key, out var other)) Assert.Equal(other, identity);
            else { seen[p.Key] = identity; distinct++; }
        }
    }
}

public class RepetitionTests
{
    [Fact]
    public void Knight_shuffle_reaches_threefold_repetition_on_the_third_occurrence()
    {
        var p = Position.Start();
        Helpers.Play(p, "g1f3", "g8f6", "f3g1", "f6g8");
        Assert.Equal(1, p.RepetitionCount());
        Assert.Equal(GameStatus.Ongoing, p.GetStatus());
        Helpers.Play(p, "g1f3", "g8f6", "f3g1", "f6g8");
        Assert.Equal(2, p.RepetitionCount());
        Assert.Equal(GameStatus.ThreefoldRepetition, p.GetStatus());
    }

    [Fact]
    public void A_pawn_move_ends_the_repetition_window()
    {
        var p = Position.Start();
        Helpers.Play(p, "g1f3", "g8f6", "f3g1", "f6g8", "e2e4", "e7e5");
        Helpers.Play(p, "g1f3", "g8f6", "f3g1", "f6g8");
        Assert.Equal(1, p.RepetitionCount()); // only the position after 1.e4 e5 counts
    }

    [Fact]
    public void Lost_castling_rights_make_a_different_position()
    {
        var p = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        Helpers.Play(p, "e1f1", "e8f8", "f1e1", "f8e8"); // same placement, rights gone
        Assert.Equal(0, p.RepetitionCount());
    }
}

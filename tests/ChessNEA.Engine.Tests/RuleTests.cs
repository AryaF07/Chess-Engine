namespace ChessNEA.Engine.Tests;

public class FenTests
{
    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData(PerftTests.Kiwipete)]
    [InlineData(PerftTests.Position3)]
    [InlineData(PerftTests.Position4)]
    [InlineData(PerftTests.Position5)]
    [InlineData("rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3")]
    public void Round_trips(string fen) => Assert.Equal(fen, Position.FromFen(fen).ToFen());

    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP w KQkq - 0 1")]        // 7 ranks
    [InlineData("rnbqkbnr/pppppppp/9/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")] // 9 files
    [InlineData("rnbq1bnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQ - 0 1")]   // no black king
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x KQkq - 0 1")] // side to move
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBN1 w KQkq - 0 1")] // K right without rook
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq e3 0 1")]// e.p. square on wrong rank for side
    public void Rejects_invalid(string fen) => Assert.Throws<FormatException>(() => Position.FromFen(fen));

    [Fact]
    public void Rejects_side_not_to_move_in_check()
        => Assert.Throws<FormatException>(() => Position.FromFen("4k3/8/8/8/8/8/8/4R1K1 w - - 0 1"));
}

public class MakeUnmakeTests
{
    public static IEnumerable<object[]> Fens() => new[]
    {
        Position.StartFen, PerftTests.Kiwipete, PerftTests.Position3, PerftTests.Position4,
        PerftTests.Position5, PerftTests.Position6,
        "4k3/1P6/8/8/8/8/6p1/4K3 w - - 0 1",                // promotions both sides
        "r3k2r/8/8/2pP4/8/8/8/R3K2R w KQkq c6 0 1",          // castling + en passant
    }.Select(f => new object[] { f });

    /// <summary>Every legal move, and every reply to it, is undone exactly (two plies deep).</summary>
    [Theory]
    [MemberData(nameof(Fens))]
    public void Every_move_and_reply_is_restored_exactly(string fen)
    {
        var p = Position.FromFen(fen);
        var root = new PositionState(p);
        foreach (Move m in p.GenerateLegalMoves())
        {
            Undo u = p.MakeMove(m);
            var afterMove = new PositionState(p);
            foreach (Move reply in p.GenerateLegalMoves())
            {
                Undo u2 = p.MakeMove(reply);
                p.UnmakeMove(reply, u2);
                Assert.True(afterMove.Difference(p) == null, $"{m} {reply}: {afterMove.Difference(p)}");
            }
            p.UnmakeMove(m, u);
            Assert.True(root.Difference(p) == null, $"{m}: {root.Difference(p)}");
        }
    }

    /// <summary>Deterministic pseudo-random games of up to 200 plies, then unwound move by move.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Random_games_unwind_to_the_start(int seed)
    {
        var rng = new Random(seed);
        var p = Position.Start();
        var history = new Stack<(Move, Undo, PositionState)>();
        for (int ply = 0; ply < 200; ply++)
        {
            var moves = p.GenerateLegalMoves();
            if (moves.Count == 0) break;
            Move m = moves[rng.Next(moves.Count)];
            var state = new PositionState(p);
            history.Push((m, p.MakeMove(m), state));
        }
        while (history.Count > 0)
        {
            var (m, u, state) = history.Pop();
            p.UnmakeMove(m, u);
            Assert.Null(state.Difference(p));
        }
        Assert.Equal(Position.StartFen, p.ToFen());
    }
}

public class CastlingTests
{
    [Fact]
    public void Castling_into_check_is_illegal()
        => Assert.False(Helpers.IsLegal(Position.FromFen("k5r1/8/8/8/8/8/8/4K2R w K - 0 1"), "e1g1"));

    [Fact]
    public void Queenside_castling_allowed_when_only_b1_is_attacked()
        => Assert.True(Helpers.IsLegal(Position.FromFen("1r5k/8/8/8/8/8/8/R3K3 w Q - 0 1"), "e1c1"));

    [Fact]
    public void Queenside_castling_blocked_by_piece_on_b1()
        => Assert.False(Helpers.IsLegal(Position.FromFen("7k/8/8/8/8/8/8/RN2K3 w Q - 0 1"), "e1c1"));

    [Fact]
    public void King_move_removes_both_rights_and_unmake_restores_them()
    {
        var p = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        Move m = Helpers.Legal(p, "e1e2");
        Undo u = p.MakeMove(m);
        Assert.Equal(CastlingRights.BlackKingside | CastlingRights.BlackQueenside, p.Castling);
        p.UnmakeMove(m, u);
        Assert.Equal(CastlingRights.All, p.Castling);
    }

    [Fact]
    public void Rook_move_and_rook_capture_remove_the_matching_right()
    {
        var p = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        Helpers.Play(p, "h1h8"); // white rook leaves h1 and captures on h8
        Assert.Equal(CastlingRights.WhiteQueenside | CastlingRights.BlackQueenside, p.Castling);
    }

    [Fact]
    public void Rook_that_left_and_returned_cannot_castle()
    {
        var p = Position.FromFen("4k3/8/8/8/8/8/8/4K2R w K - 0 1");
        Helpers.Play(p, "h1h2", "e8d8", "h2h1", "d8e8");
        Assert.False(Helpers.IsLegal(p, "e1g1"));
    }

    [Fact]
    public void Castling_with_an_enemy_rook_in_the_corner_is_impossible()
        => Assert.Throws<FormatException>(() => Position.FromFen("4k3/8/8/8/8/8/8/4K2r w K - 0 1"));
}

public class EnPassantTests
{
    [Fact]
    public void En_passant_that_exposes_the_king_on_the_rank_is_illegal()
        => Assert.False(Helpers.IsLegal(Position.FromFen("7k/8/8/K2pP2r/8/8/8/8 w - d6 0 2"), "e5d6"));

    [Fact]
    public void En_passant_is_legal_otherwise()
        => Assert.True(Helpers.IsLegal(Position.FromFen("7k/8/8/3pP3/8/8/8/K7 w - d6 0 2"), "e5d6"));

    [Fact]
    public void Double_push_sets_the_square_behind_the_pawn()
    {
        var p = Position.Start();
        Helpers.Play(p, "e2e4");
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", p.ToFen());
    }
}

public class PromotionTests
{
    [Fact]
    public void Capture_promotion_generates_four_choices_per_target()
    {
        var p = Position.FromFen("1n2k3/P7/8/8/8/8/8/4K3 w - - 0 1");
        var promos = p.GenerateLegalMoves().Where(m => m.Kind == MoveKind.Promotion).Select(m => m.ToString()).OrderBy(s => s).ToArray();
        Assert.Equal(new[] { "a7a8b", "a7a8n", "a7a8q", "a7a8r", "a7b8b", "a7b8n", "a7b8q", "a7b8r" }, promos);
    }

    [Fact]
    public void Underpromotion_to_knight_can_give_check()
    {
        var p = Position.FromFen("8/5P1k/8/8/8/8/8/K7 w - - 0 1");
        Helpers.Play(p, "f7f8n");
        Assert.IsType<Knight>(p[0, 5]);
        Assert.True(p.IsInCheck(false));
    }
}

public class StatusAndClockTests
{
    [Fact]
    public void Fools_mate_is_checkmate()
    {
        var p = Position.Start();
        Helpers.Play(p, "f2f3", "e7e5", "g2g4", "d8h4");
        Assert.Equal(GameStatus.Checkmate, p.GetStatus());
    }

    [Fact]
    public void Stalemate_is_detected()
        => Assert.Equal(GameStatus.Stalemate, Position.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1").GetStatus());

    [Fact]
    public void Halfmove_clock_counts_and_resets()
    {
        var p = Position.Start();
        Helpers.Play(p, "g1f3", "g8f6");
        Assert.Equal(2, p.HalfmoveClock);
        Assert.Equal(2, p.FullmoveNumber);
        Helpers.Play(p, "e2e4");
        Assert.Equal(0, p.HalfmoveClock);
    }

    [Fact]
    public void Fifty_move_rule_triggers_at_one_hundred_plies()
    {
        Assert.Equal(GameStatus.Ongoing, Position.FromFen("4k3/8/8/8/8/8/8/R3K3 w - - 99 80").GetStatus());
        var p = Position.FromFen("4k3/8/8/8/8/8/8/R3K3 w - - 99 80");
        Helpers.Play(p, "a1a2");
        Assert.Equal(GameStatus.FiftyMoveRule, p.GetStatus());
    }

    [Fact]
    public void Checkmate_on_the_hundredth_ply_wins()
    {
        var p = Position.FromFen("7k/8/6K1/8/8/8/8/R7 w - - 99 80");
        Helpers.Play(p, "a1a8");
        Assert.Equal(GameStatus.Checkmate, p.GetStatus());
    }

    [Fact]
    public void Insufficient_material_status()
        => Assert.Equal(GameStatus.InsufficientMaterial, Position.FromFen("8/8/4k3/8/8/3NK3/8/8 w - - 0 1").GetStatus());
}

public class SearchTests
{
    [Fact]
    public void Search_does_not_change_the_position()
    {
        var p = Position.FromFen(PerftTests.Kiwipete.Replace(" w ", " b "));
        var before = new PositionState(p);
        new Bot().Search(p, 3);
        Assert.Null(before.Difference(p));
    }

    [Fact]
    public void Search_at_a_checkmated_root_returns_no_move_and_the_mate_score()
    {
        // White to move is mated at the root (ply 0): +Infinity from black's (the maximiser's) view.
        var p = Position.Start();
        Helpers.Play(p, "f2f3", "e7e5", "g2g4", "d8h4");
        var (score, move) = new Bot().Search(p, 2);
        Assert.True(move.IsNone);
        Assert.Equal(Bot.Infinity, score);
    }

    [Fact]
    public void Evaluation_of_the_start_position_is_zero()
        => Assert.Equal(0, new Bot().Evaluate(Position.Start()));
}

namespace ChessNEA.Engine.Tests;

/// <summary>
/// One test per defect reproduced in Phase 0 (docs/phase0-baseline.md, R01–R12),
/// asserting the correct behaviour on the same positions.
/// </summary>
public class RegressionTests
{
    [Fact] // R01: the search changed King.hasMoved permanently
    public void R01_search_restores_the_complete_position()
    {
        var p = Position.FromFen("rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2");
        var before = new PositionState(p);
        new Bot().Search(p, 2);
        Assert.Null(before.Difference(p));
        Assert.Equal(CastlingRights.All, p.Castling);
    }

    [Fact] // R02: castling in search moved only the king
    public void R02_castling_moves_the_rook_and_unmake_restores_it()
    {
        var p = Position.FromFen("4k2r/8/8/8/8/8/8/4K3 b k - 0 1");
        var before = new PositionState(p);
        Move m = Helpers.Legal(p, "e8g8");
        Assert.Equal(MoveKind.CastleKingside, m.Kind);
        Undo u = p.MakeMove(m);
        Assert.IsType<King>(p[0, 6]);
        Assert.IsType<Rook>(p[0, 5]);
        Assert.Null(p[0, 7]);
        p.UnmakeMove(m, u);
        Assert.Null(before.Difference(p));
    }

    [Fact] // R03
    public void R03_no_kingside_castling_through_an_attacked_square()
        => Assert.False(Helpers.IsLegal(Position.FromFen("k4r2/8/8/8/8/8/8/4K2R w K - 0 1"), "e1g1"));

    [Fact] // R04
    public void R04_no_queenside_castling_out_of_check()
        => Assert.False(Helpers.IsLegal(Position.FromFen("4r2k/8/8/8/8/8/8/R3K3 w Q - 0 1"), "e1c1"));

    [Fact] // R04 control
    public void R04c_no_kingside_castling_out_of_check()
        => Assert.False(Helpers.IsLegal(Position.FromFen("k3r3/8/8/8/8/8/8/4K2R w K - 0 1"), "e1g1"));

    [Fact] // R05: IsKingInCheck ignored the enemy king
    public void R05_king_cannot_move_next_to_the_enemy_king()
    {
        var p = Position.FromFen("8/8/8/8/8/4k3/8/4K3 w - - 0 1");
        var moves = p.GenerateLegalMoves().Select(m => m.ToString()).OrderBy(s => s);
        Assert.Equal(new[] { "e1d1", "e1f1" }, moves);
    }

    [Fact] // R06: after a king capture the search dereferenced a null king
    public void R06_search_with_nearby_kings_completes()
    {
        var p = Position.FromFen("8/7p/8/4k3/8/4K3/P7/8 b - - 0 1");
        var before = new PositionState(p);
        var (score, move) = new Bot().Search(p, 4);
        Assert.False(move.IsNone);
        Assert.Null(before.Difference(p));
    }

    [Fact] // R07: the bot's promoted queen got pixel X = column index
    public void R07_bot_promotion_creates_a_queen_on_the_promotion_square()
    {
        var p = Position.FromFen("7k/8/8/8/8/8/1p6/R6K b - - 0 1");
        var (_, move) = new Bot().Search(p, 4);
        Assert.Equal("b2a1q", move.ToString());
        p.MakeMove(move);
        Piece q = Assert.IsType<Queen>(p[7, 0]);
        Assert.Equal((7, 0), (q.Row, q.Col));
        new Bot().Evaluate(p); // the original threw IndexOutOfRangeException here
    }

    [Fact] // R08: the search did not model promotion
    public void R08_promotion_is_generated_for_all_four_pieces_and_scored_as_a_queen()
    {
        var p = Position.FromFen("7k/8/8/8/8/8/1p6/7K b - - 0 1");
        var promotions = p.GenerateLegalMoves().Where(m => m.Kind == MoveKind.Promotion).Select(m => m.ToString()).OrderBy(s => s);
        Assert.Equal(new[] { "b2b1b", "b2b1n", "b2b1q", "b2b1r" }, promotions);
        var (_, best) = new Bot().Search(p, 1);
        Assert.Equal("b2b1q", best.ToString());
    }

    [Fact] // R09: en passant in search left the captured pawn
    public void R09_en_passant_removes_the_captured_pawn_and_unmake_restores_it()
    {
        var p = Position.FromFen("k7/8/8/8/3pP3/8/8/7K b - e3 0 1");
        var before = new PositionState(p);
        Move m = Helpers.Legal(p, "d4e3");
        Assert.Equal(MoveKind.EnPassant, m.Kind);
        Undo u = p.MakeMove(m);
        Assert.Null(p[4, 4]);
        Assert.IsType<Pawn>(p[5, 4]);
        p.UnmakeMove(m, u);
        Assert.Null(before.Difference(p));
    }

    [Fact] // R10: the bot's double push did not enable en passant
    public void R10_double_push_enables_en_passant_for_one_move_only()
    {
        var p = Position.FromFen("rnbqkbnr/pppppppp/8/4P3/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1");
        Helpers.Play(p, "d7d5");
        Assert.True(Helpers.IsLegal(p, "e5d6"));
        Helpers.Play(p, "g1f3", "g8f6");
        Assert.False(Helpers.IsLegal(p, "e5d6"));
    }

    [Fact] // R11: mate scoring used remaining depth and preferred slower mates
    public void R11_search_plays_the_mate_in_one()
    {
        var p = Position.FromFen("k7/8/8/8/8/1r6/r7/7K b - - 0 1");
        var (score, move) = new Bot().Search(p, 4);
        Assert.Equal("b3b1", move.ToString());
        Assert.Equal(Bot.Infinity - 1, score);
    }

    [Theory] // R12: dead-position (insufficient material) detection
    [InlineData("k7/8/8/8/8/8/8/K7 w - - 0 1", true)]
    [InlineData("k7/8/8/8/8/8/8/KB6 w - - 0 1", true)]
    [InlineData("k7/8/8/8/8/8/8/KN6 w - - 0 1", true)]
    [InlineData("kb6/8/8/8/8/8/8/K1B5 w - - 0 1", true)]   // same-colour bishops
    [InlineData("k7/8/8/8/8/8/8/KBN5 w - - 0 1", false)]
    [InlineData("kb6/8/8/8/8/8/8/KB6 w - - 0 1", false)]   // opposite-colour bishops
    [InlineData("k7/8/8/8/8/8/8/KNN5 w - - 0 1", false)]
    [InlineData("kn6/8/8/8/8/8/8/KB6 w - - 0 1", false)]
    [InlineData("k7/8/8/4p3/4P3/8/8/K7 w - - 0 1", false)] // blocked pawns: not detected as dead
    public void R12_insufficient_material(string fen, bool expected)
        => Assert.Equal(expected, Position.FromFen(fen).IsInsufficientMaterial());
}

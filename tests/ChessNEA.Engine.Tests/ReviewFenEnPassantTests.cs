namespace ChessNEA.Engine.Tests;

/// <summary>
/// Regression tests for the external review of commit 79a01d8. Each test was first run
/// against the unfixed code and failed (see docs/phase6-review-fixes.md).
/// </summary>
public class FenEnPassantValidationTests
{
    /// <summary>Additional finding: an en-passant target needs the double-pushed pawn behind it and an empty path.</summary>
    [Theory]
    [InlineData("4k3/8/8/4P3/8/8/8/4K3 w - d6 0 1")]        // no black pawn on d5
    [InlineData("4k3/8/3n4/3pP3/8/8/8/4K3 w - d6 0 1")]     // target square d6 occupied
    [InlineData("4k3/3n4/8/3pP3/8/8/8/4K3 w - d6 0 1")]     // d7 occupied: the pawn cannot have come from there
    [InlineData("4k3/8/8/8/4p3/8/8/4K3 b - d3 0 1")]        // no white pawn on d4
    public void Inconsistent_en_passant_target_is_rejected(string fen)
        => Assert.Throws<FormatException>(() => Position.FromFen(fen));

    [Fact]
    public void Consistent_en_passant_target_is_accepted()
        => Assert.Equal("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1", Position.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1").ToFen());
}

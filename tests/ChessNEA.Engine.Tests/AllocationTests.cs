namespace ChessNEA.Engine.Tests;

/// <summary>
/// Allocation regression tests. Allocated bytes on the current thread are deterministic
/// (unlike timings), so these assertions are stable on any machine.
/// </summary>
public class AllocationTests
{
    /// <summary>Bytes of one SearchStats object: the only allocation a warm search should make.</summary>
    internal static long StatsObjectBytes()
    {
        // Stored in a static field so the object escapes: the .NET 10 JIT may otherwise prove that a
        // discarded object never escapes and not allocate it on the heap at all (measured 0 bytes).
        statsSink = new SearchStats(); // warm-up (type loading)
        long before = GC.GetAllocatedBytesForCurrentThread();
        statsSink = new SearchStats();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    static SearchStats? statsSink;

    static long AllocatedBy(Action action)
    {
        action(); // warm-up: JIT compilation and first-use allocations happen here
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void Evaluate_allocates_nothing()
    {
        var bot = new Bot();
        var p = Position.FromFen(PerftTests.Kiwipete);
        Assert.Equal(0, AllocatedBy(() => bot.Evaluate(p)));
    }

    [Fact]
    public void Generating_into_a_reused_list_allocates_nothing()
    {
        var p = Position.FromFen(PerftTests.Kiwipete);
        var list = new List<Move>(64);
        Assert.Equal(0, AllocatedBy(() => p.GenerateLegalMoves(list)));
        Assert.Equal(48, list.Count);
    }

    [Fact]
    public void Reused_list_gives_the_same_moves_in_the_same_order()
    {
        var p = Position.FromFen(PerftTests.Kiwipete);
        var list = new List<Move> { default, default }; // stale content must be cleared
        p.GenerateLegalMoves(list);
        Assert.Equal(p.GenerateLegalMoves(), list);
    }

    [Fact]
    public void Repeated_searches_with_reused_move_lists_give_identical_results()
    {
        var bot = new Bot();
        var first = bot.Search(Position.FromFen(PerftTests.Kiwipete), 3);
        long nodes = bot.Stats.Nodes;
        bot.Search(Position.Start(), 4); // different position in between
        var again = bot.Search(Position.FromFen(PerftTests.Kiwipete), 3);
        Assert.Equal(first, again);
        Assert.Equal(nodes, bot.Stats.Nodes);
    }

    [Fact]
    public void Search_without_promotions_allocates_only_its_stats_object()
    {
        // After the first search has grown the per-ply lists, a search allocates just the
        // SearchStats object. (Promotions would also allocate the new piece.)
        var bot = new Bot();
        var p = Position.Start(); // created outside the measurement: FEN parsing allocates
        long bytes = AllocatedBy(() => bot.Search(p, 3));
        Assert.Equal(StatsObjectBytes(), bytes); // exactly one SearchStats object, whatever its size
    }
}

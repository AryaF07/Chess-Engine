namespace ChessNEA.Engine;

/// <summary>
/// Fixed-depth alpha-beta search, ported from the original Bot with the same structure:
/// black is the maximising player and Evaluate() returns the negated white-positive sum
/// of material and piece-square values. Changes from the original:
///  * moves are applied with Position.MakeMove/UnmakeMove (complete, reversible state);
///  * mate scores use the distance from the root (ply), so shorter mates score higher;
///  * fifty-move, insufficient-material and repeated positions score as draws (0).
/// Moves at interior nodes are ordered (captures by MVV-LVA and promotions first, see
/// <see cref="UseMoveOrdering"/>); the depth-0 behaviour (moves are generated before the depth
/// test, so leaves can detect mate and stalemate) is unchanged.
/// Counters in <see cref="Stats"/> describe the work done by the last Search call.
/// </summary>
public sealed class Bot
{
    /// <summary>
    /// When false, the search never cuts off and becomes plain minimax with identical rules,
    /// evaluation and move order (used to measure what alpha-beta pruning saves).
    /// </summary>
    public bool UseAlphaBeta { get; init; } = true;

    /// <summary>
    /// When true (default), interior nodes search captures first, ordered by
    /// most-valuable-victim / least-valuable-attacker, and promotions before quiet moves.
    /// Ordering changes which nodes alpha-beta visits but never the score it returns.
    /// When false, moves are searched in generation order.
    /// </summary>
    public bool UseMoveOrdering { get; init; } = true;

    /// <summary>
    /// When true (default), a position that already occurred earlier in the game or on the current
    /// search path (since the last capture or pawn move) scores as a draw, except at the root.
    /// </summary>
    public bool UseRepetitionDraws { get; init; } = true;

    /// <summary>Work counters of the most recent <see cref="Search"/> call.</summary>
    public SearchStats Stats { get; private set; } = new();

    // One reusable move list per ply (index = distance from the root). A node fills only its own
    // ply's list, and its children use deeper lists, so a parent's list is never overwritten while
    // it is still being iterated. The lists are kept between searches.
    List<Move>[] moveLists = Array.Empty<List<Move>>();

    List<Move> MoveListFor(int ply)
    {
        if (ply >= moveLists.Length)
        {
            int old = moveLists.Length;
            Array.Resize(ref moveLists, Math.Max(ply + 1, old * 2));
            for (int i = old; i < moveLists.Length; i++) moveLists[i] = new List<Move>(64);
        }
        return moveLists[ply];
    }

    /// <summary>Same magnitude as the original 'largenumber'. Mate in n plies scores ±(Infinity − n).</summary>
    public const int Infinity = 99999999;
    public const int DefaultDepth = 4;

    public static bool IsMateScore(int score) => Math.Abs(score) > Infinity - 1000;

    /// <summary>Static evaluation from black's point of view (positive = good for black).</summary>
    public int Evaluate(Position position)
    {
        int evaluation = 0;
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 8; col++)
            {
                Piece? piece = position[row, col];
                if (piece == null) continue;
                evaluation += piece.PieceValue + piece.Evaluate();
            }
        return -evaluation;
    }

    /// <summary>Fixed-depth search.</summary>
    public (int Score, Move Move) Search(Position position, int depth = DefaultDepth)
    {
        Stats = new SearchStats();
        return Minimax(position, depth, 0, -Infinity, Infinity);
    }

    /// <param name="ply">Distance from the root; used to prefer shorter mates.</param>
    public (int Score, Move Move) Minimax(Position position, int depth, int ply, int alpha, int beta)
    {
        Stats.Nodes++;

        // A repeated position is a draw. This depends on the path, not just the position.
        if (ply > 0 && UseRepetitionDraws && position.RepetitionCount() > 0)
        {
            Stats.DrawRuleNodes++;
            return (0, default);
        }

        List<Move> moves = MoveListFor(ply);
        position.GenerateLegalMoves(moves);
        Stats.LegalMovesGenerated += moves.Count;
        if (moves.Count == 0)
        {
            Stats.TerminalNodes++;
            if (position.IsInCheck(position.WhiteToMove))
                return position.WhiteToMove ? (Infinity - ply, default) : (-Infinity + ply, default);
            return (0, default); // stalemate
        }
        if (position.IsFiftyMoveDraw || position.IsInsufficientMaterial())
        {
            Stats.DrawRuleNodes++;
            return (0, default);
        }
        if (depth == 0)
        {
            Stats.LeafEvaluations++;
            return (Evaluate(position), default);
        }
        if (UseMoveOrdering) OrderMoves(position, moves);

        Move bestMove = default;
        if (!position.WhiteToMove) // maximising player (black, the bot)
        {
            int maxEval = -Infinity;
            int searched = 0;
            foreach (Move move in moves)
            {
                searched++;
                Undo undo = position.MakeMove(move);
                int eval = Minimax(position, depth - 1, ply + 1, alpha, beta).Score;
                position.UnmakeMove(move, undo);
                if (eval > maxEval)
                {
                    maxEval = eval;
                    bestMove = move;
                }
                alpha = Math.Max(alpha, eval);
                if (UseAlphaBeta && beta <= alpha)
                {
                    Stats.Cutoffs++;
                    if (searched == 1) Stats.FirstMoveCutoffs++;
                    break;
                }
            }
            return (maxEval, bestMove);
        }
        else // minimising player (white)
        {
            int minEval = Infinity;
            int searched = 0;
            foreach (Move move in moves)
            {
                searched++;
                Undo undo = position.MakeMove(move);
                int eval = Minimax(position, depth - 1, ply + 1, alpha, beta).Score;
                position.UnmakeMove(move, undo);
                if (eval < minEval)
                {
                    minEval = eval;
                    bestMove = move;
                }
                beta = Math.Min(beta, eval);
                if (UseAlphaBeta && beta <= alpha)
                {
                    Stats.Cutoffs++;
                    if (searched == 1) Stats.FirstMoveCutoffs++;
                    break;
                }
            }
            return (minEval, bestMove);
        }
    }

    // ------------------------------------------------------------------ move ordering

    // Sort keys for the node currently being ordered. One buffer suffices: a node finishes
    // sorting before it searches any child, and children overwrite it only after that.
    readonly int[] orderKeys = new int[256]; // more than the maximum number of legal moves (218)

    /// <summary>
    /// Orders moves in place, highest key first; equal keys keep their generation order
    /// (stable insertion sort), so quiet moves stay in the original order.
    /// </summary>
    void OrderMoves(Position position, List<Move> moves)
    {
        Span<Move> span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(moves);
        for (int i = 0; i < span.Length; i++) orderKeys[i] = OrderKey(position, span[i]);
        for (int i = 1; i < span.Length; i++)
        {
            Move m = span[i];
            int key = orderKeys[i];
            int j = i - 1;
            while (j >= 0 && orderKeys[j] < key)
            {
                span[j + 1] = span[j];
                orderKeys[j + 1] = orderKeys[j];
                j--;
            }
            span[j + 1] = m;
            orderKeys[j + 1] = key;
        }
    }

    /// <summary>
    /// Captures: 1,000,000 + 10 × victim value − attacker value (MVV-LVA), so taking a queen
    /// with a pawn comes first and taking a pawn with a queen comes last among captures.
    /// Promotions add 500,000 + the new piece's value. Quiet moves score 0.
    /// </summary>
    public static int OrderKey(Position position, Move m)
    {
        int key = 0;
        Piece attacker = position[m.FromRow, m.FromCol]!;
        Piece? victim = m.Kind == MoveKind.EnPassant ? position[m.FromRow, m.ToCol] : position[m.ToRow, m.ToCol];
        if (victim != null) key += 1_000_000 + 10 * Math.Abs(victim.PieceValue) - Math.Abs(attacker.PieceValue);
        if (m.Kind == MoveKind.Promotion) key += 500_000 + PieceTypeValue(m.Promotion);
        return key;
    }

    static int PieceTypeValue(PieceType t) => t switch
    {
        PieceType.Pawn => 100, PieceType.Knight => 320, PieceType.Bishop => 330,
        PieceType.Rook => 500, PieceType.Queen => 900, _ => 0,
    };
}


/// <summary>
/// Counters for one search. Definitions:
///  Nodes               positions visited by Minimax, root included (one per call);
///  LeafEvaluations     static evaluations at depth 0 (non-terminal, non-draw leaves);
///  TerminalNodes       positions with no legal move (checkmate or stalemate);
///  DrawRuleNodes       positions scored 0 by the repetition, fifty-move or insufficient-material rule;
///  Cutoffs             move loops ended early because beta &lt;= alpha;
///  FirstMoveCutoffs    cutoffs caused by the first move searched;
///  LegalMovesGenerated sum of legal-move counts over all visited positions.
/// Every visited node is exactly one of: interior (searched its moves), leaf evaluation,
/// terminal or draw-rule node.
/// </summary>
public sealed class SearchStats
{
    public long Nodes { get; internal set; }
    public long LeafEvaluations { get; internal set; }
    public long TerminalNodes { get; internal set; }
    public long DrawRuleNodes { get; internal set; }
    public long Cutoffs { get; internal set; }
    /// <summary>Cutoffs caused by the first move searched (a measure of move-ordering quality).</summary>
    public long FirstMoveCutoffs { get; internal set; }
    public long LegalMovesGenerated { get; internal set; }
    public long InteriorNodes => Nodes - LeafEvaluations - TerminalNodes - DrawRuleNodes;
}

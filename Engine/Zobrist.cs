namespace ChessNEA.Engine;

/// <summary>
/// Zobrist hashing keys. A position's key is the XOR of one random 64-bit number per
/// (piece, square), one for the castling-rights combination, one for the en-passant file when
/// an en-passant capture is available, and one when black is to move. Because XOR is its own
/// inverse, a move updates the key by XOR-ing out what changed and XOR-ing in the new state.
/// The numbers come from a fixed-seed generator, so keys are the same in every run.
/// </summary>
public static class Zobrist
{
    static readonly ulong[] pieceSquare = new ulong[12 * 64];
    static readonly ulong[] castling = new ulong[16];
    static readonly ulong[] enPassantFile = new ulong[8];
    public static readonly ulong BlackToMove;

    static Zobrist()
    {
        ulong state = 0x5EED_C0FF_EE15_2026UL;
        ulong Next()
        {
            // SplitMix64: a small, well-distributed generator, fully determined by the seed.
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        for (int i = 0; i < pieceSquare.Length; i++) pieceSquare[i] = Next();
        for (int i = 0; i < castling.Length; i++) castling[i] = Next();
        for (int i = 0; i < enPassantFile.Length; i++) enPassantFile[i] = Next();
        BlackToMove = Next();
    }

    static int PieceIndex(PieceType type, bool white) => ((int)type - 1) * 2 + (white ? 0 : 1);

    public static ulong PieceKey(Piece p, int row, int col) => pieceSquare[PieceIndex(p.Type, p.IsWhite) * 64 + row * 8 + col];

    public static ulong CastlingKey(CastlingRights rights) => castling[(int)rights];

    public static ulong EnPassantKey(int col) => enPassantFile[col];
}

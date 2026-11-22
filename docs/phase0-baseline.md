# Phase 0 — Baseline of the original engine

Engine code examined: `master` at `396744dd46eba75495eafed84a4f9bd1bfe667d6`.
Phase 0 changes only the build files and adds a separate console runner. `Bot.cs`, `Board.cs`, `Game1.cs` and `Pieces/*` are unchanged.

## Environment

| Item | Value |
|---|---|
| CPU | AMD Ryzen 7 5700X 8-Core Processor (16 logical processors) |
| OS | Windows 11 Home, build 10.0.26300, x64 |
| SDK | .NET SDK 11.0.100 |
| Installed runtimes | Microsoft.NETCore.App 10.0.15, 11.0.0 (no .NET 8 runtime installed) |
| Project target | `net8.0-windows`, `RollForward=Major`, `TieredCompilation=false` |
| Runtime used | .NET 10.0.15 (default roll-forward); .NET 11.0.0 (`DOTNET_ROLL_FORWARD=LatestMajor`) |
| Build | Release, no debugger attached, workstation GC |
| Packages | MonoGame.Framework.WindowsDX 3.8.2.1105, MonoGame.Content.Builder.Task 3.8.2.1105 |

The project targets .NET 8. Because that runtime is not installed, every result here comes from .NET 10 or .NET 11 running the net8.0 build.

## Build and run

```powershell
dotnet build ChessNEA.csproj -c Release          # game (restores MonoGame packages and mgcb tools)
dotnet build tools/ChessNEA.Repro -c Release     # headless runner
dotnet tools/ChessNEA.Repro/bin/Release/net8.0-windows/ChessNEA.Repro.dll all --out results/phase0/default-rollforward
$env:DOTNET_ROLL_FORWARD = "LatestMajor"
dotnet tools/ChessNEA.Repro/bin/Release/net8.0-windows/ChessNEA.Repro.dll all --out results/phase0/net11-latestmajor
```

The runner takes `repro`, `perft`, `bench` or `all`. It writes JSON with environment metadata (commit, dirty flag, runtime, CPU, configuration, GC mode, tiering) to the output directory.

The game builds with one warning (WFAC010, high-DPI settings in `app.manifest`). It starts, and the window ("ChessNEA") responds. No game was played through the GUI during Phase 0.

## Architecture

```
Program.cs ── Game1 (MonoGame Game: menus, screens, content loading, Draw/Update loop)
                 └── Board  (position + rules + mouse input + rendering + clocks)
                       ├── Piece[8,8] ChessBoard     (row 0 = rank 8, col 0 = file a)
                       │     └── Pawn/Knight/Bishop/Rook/Queen/King : Piece
                       │           findMoves()  pseudo-legal moves into legalmoves (List<Point>)
                       │           evaluate()   piece-square value (array built per call)
                       │           Update/Draw  click detection and sprite drawing
                       └── Bot      (Move struct, findmoves, makeMove/undoMove, evaluate, Minimax)
```

**Position state.** `Board.ChessBoard` holds piece references. Each piece's square is stored only in its sprite rectangle `Piece.Position`, with X = 165 + 60·col and Y = 5 + 60·row. Every rule function recovers the square as `((X − 165)/60, (Y − 5)/60)`. Other state is spread across flags: `King.hasMoved`, `Rook.hasMoved`, `Pawn.movedtwoSquares`, `Board.turn` (true = white to move), and in `Board` the fields `numberofmoves`, `pawnmoved` and `piececaptured`. `Piece.board` and `Bot.board` are static. They are assigned in the `Board` constructor, so only one board can be active at a time.

**Two separate move-application paths.**
1. GUI path (`Board.Update`): moves pieces when a highlighted square is clicked. Handles castling (moves the rook), en passant (removes the pawn), the promotion menu, and the `hasMoved`, `movedtwoSquares`, capture and pawn-move flags. Nothing can undo it.
2. Search path (`Bot.makeMove` / `Bot.undoMove`): moves one piece and puts back one captured piece. Sets `King.hasMoved = true` and toggles `turn`. Handles no other special state.

**Legality.** `Piece.findMoves` generates pseudo-legal moves. `Bot.findmoves`, `Board.highlightSquares` and `Board.Checkmate` each do the same filtering on their own: place the piece on the destination, call `Board.IsKingInCheck`, then restore the piece.

**Search.** `Bot.move()` calls `Minimax(board, 4, −99999999, 99999999)`, an alpha-beta search to fixed depth 4. It returns unclamped best values (fail-soft). Black (the bot) is the maximiser, because `Bot.evaluate()` returns the negated white-positive sum. Moves are searched in board-scan order with no move ordering. Every node generates all legal moves before it tests `depth == 0`, so leaf nodes can detect checkmate and stalemate. Mate scores are `±(99999999 − depth)`, where `depth` is the remaining depth.

**Evaluation.** Material (`pieceValue`: P 100, N 320, B 330, R 500, Q 900, K 20000, negative for black) plus one piece-square table per piece type and colour. Each table is a local `int[,]` built inside every `evaluate()` call. The black tables are exact vertical mirrors of the white tables: all 6 pairs were checked and none differ.

**Timing.** The only existing timing is `Board.Update` wrapping `bot.move()` in a `Stopwatch` and printing with `Debug.WriteLine`. `Debug.WriteLine` is `[Conditional("DEBUG")]`, so Release builds print nothing.

### Headless route

`tools/ChessNEA.Repro` is a console project that references `ChessNEA.csproj`. It builds positions by constructing a `Board`, clearing `ChessBoard` and placing pieces with consistent rectangles. A minimal diagnostic FEN reader handles placement, side to move, castling (mapped to `hasMoved`) and en passant (mapped to `movedtwoSquares`). The runner then calls the engine's own public and internal methods, without a graphics device. To make this work, `ChessNEA.csproj` excludes `tools/**` from its compile glob and declares `InternalsVisibleTo ChessNEA.Repro`. The piece classes are `internal`.

## Reproductions (executed)

Raw output: `results/phase0/*/repro.json`. The output is identical on .NET 10.0.15 and .NET 11.0.0.

| ID | Defect | Observed | Status |
|---|---|---|---|
| R01 | Search leaks `King.hasMoved` | Depth-2 search after 1.e4 e5 2.Nf3: both kings `hasMoved False → True`, so both sides lose castling rights | Reproduced |
| R02 | Search castling moves only the king | `makeMove` Ke8–g8: king on g8, rook still on h8 | Reproduced |
| R03 | Kingside castling through an attacked square | Black rook on f8: `findmoves(true)` returns Ke1–g1 | Reproduced |
| R04 | Queenside castling while in check | King in check from e8: `findmoves(true)` returns Ke1–c1 | Reproduced |
| R04c | Control: kingside castling while in check | Ke1–g1 rejected (loop re-tests e1 by accident) | Correct |
| R05 | `IsKingInCheck` ignores the enemy king | K e1 vs k e3: Kd2, Ke2, Kf2 generated | Reproduced |
| R06 | King capture crashes the search | Kings two squares apart, depth 2: `NullReferenceException` in `Board.IsKingInCheck` | Reproduced |
| R07 | Bot promotion stores the wrong X | `Bot.move()` bxa1: queen at a1 with `Position.X = 0` (expected 165); next `Bot.evaluate()` throws `IndexOutOfRangeException` | Reproduced |
| R08 | Search does not model promotion | Static score after each black move: b2–b1 = 100, king moves = 150–160 | Reproduced |
| R09 | Search en passant keeps the captured pawn | `makeMove` d4xe3 e.p.: white pawn still on e4 | Reproduced |
| R10 | Bot double push not recorded | After bot d7–d5, white e5xd6 e.p. not generated | Reproduced |
| R11 | Mate scoring prefers slower mates | Rb3–b1 is mate in one (score 99999996 = L−3); root chose Ka8–b8 with 99999998 = L−1 | Reproduced |
| R12a–c | Insufficient material: KvK, KBvK, KB v KB same colour | `true` | Correct |
| R12d | KBN v K | `true` (mate can be forced) | Reproduced |
| R12e | KB v KB, opposite-colour bishops | `true` (a mate position exists, so not dead) | Reproduced |
| R12f | KNN v K | `true` (a mate position exists, so not dead) | Reproduced |
| R12g | K+P v K+P with blocked pawns | `true` | Reproduced |

Causes, from the source:

- R03/R04: `King.findMoves` sets `Position = new Rectangle(165 + (60 * col ± j), …)`, which offsets by j **pixels**. Integer division maps both kingside probes to e1 and both queenside probes to d1. Kingside therefore never tests f1. Queenside never tests e1, and its `board.check == false` guard has no effect because `Board.check` is never set to `true` anywhere.
- R06 follows from R05: once kings can stand next to each other, a king can be captured, and `IsKingInCheck` then dereferences a null king.

## Static findings (not executed)

- **Fifty-move rule** (`Board.Draw`, `Board.Update`): `numberofmoves` counts plies, triggers at 50 (the rule needs 100 plies), and resets only every 50 plies, not on a pawn move or capture. `pawnmoved` and `piececaptured` are never set by bot moves. The draw flag is assigned inside `Draw`, the rendering code.
- **Rook state for the bot**: `Bot.makeMove` never sets `Rook.hasMoved`, so a bot rook that moved and came back keeps castling rights. `King.findMoves` checks `ChessBoard[row, 0] is Rook` / `[row, 7] is Rook` without checking the rook's colour.
- **`Board.Checkmate(bool colour)`** ignores its parameter and uses `turn`.
- **Clock expiry**: shown in `Draw`, but `Update` keeps accepting moves.
- **Search en passant flags**: `movedtwoSquares` is only cleared by the GUI, so inside the search an en-passant capture stays available at every ply below the root.
- **Depth-0 move generation**: `Minimax` calls `findmoves` before testing `depth == 0`. It is needed today for leaf mate/stalemate detection, so moving the test would change results.
- **Development history**: `origin/dev` searches depth 3 with no pruning, scores terminal positions with the static evaluation, and returns `int.MinValue` for a side with no moves. It is not a controlled baseline for alpha-beta.

## Perft through the original move pipeline

Perft counts the leaves of the full legal-move tree to a given depth. Here it uses `Bot.findmoves` → `makeMove` → recursion → `undoMove`. The expected counts are the published reference values. In the "flags-restored" mode, the harness saves and restores `hasMoved` and `movedtwoSquares` around each move, which isolates R01. Raw output: `results/phase0/*/perft.json`; depth-2 root divides are in `divide_depth2.json`.

| Position | Depth | Expected | Raw | Flags restored |
|---|---|---|---|---|
| start | 1–4 | 20 / 400 / 8,902 / 197,281 | match | match |
| Kiwipete | 1 | 48 | 48 | 48 |
| Kiwipete | 2 | 2,039 | 2,040 | 2,040 |
| Kiwipete | 3 | 97,862 | 93,483 | 97,968 |
| Position 3 | 1–2 | 14 / 191 | match | match |
| Position 3 | 3 | 2,812 | 2,810 | 2,810 |
| Position 3 | 4 | 43,238 | 43,087 | 43,087 |

The start position to depth 4 contains no castling, en passant or promotion, so a match there does not test those rules. For the other positions the deviations are consistent with R01–R04 and R09–R10, but they have not yet been attributed move by move. The published Position 3 depth-3 count includes 2 en-passant captures, and the engine is 2 short. That matches R10, but it is a hypothesis until a divide comparison against an independent generator (Phase 2).

## Measurements of the original engine

These figures describe the original engine at commit 396744d, which has the rule defects listed above. They are **not** a corrected performance baseline. Later phases change move generation, so these numbers must not be used to show speedups.

Method:
- Single process, one thread.
- Fresh position for every run, because of R01.
- `GC.Collect` before each sample.
- Allocation is measured with `GC.GetAllocatedBytesForCurrentThread`.
- Full search: `Minimax(board, 4, −L, L)`, 1 warm-up and 7 timed runs per position; median, minimum and maximum reported.
- Micro measurements: 5 repetitions after a warm-up; median reported.

Raw per-run data: `results/phase0/*/search_depth4.json` and `micro.json`.

| Measurement (start position) | .NET 10.0.15 | .NET 11.0.0 |
|---|---|---|
| `Bot.evaluate()` bytes per call | 9,472 | 9,472 |
| `Bot.evaluate()` ns per call (median) | 2,790 | 2,063 |
| `Bot.findmoves(true)` bytes per call | 2,048 | 2,048 |
| `Bot.findmoves(true)` ns per call (median) | 4,894 | 4,887 |

| Depth-4 search (black to move) | Score / move | Allocated per search | Gen0 GCs | .NET 10 median (min–max) ms | .NET 11 median (min–max) ms |
|---|---|---|---|---|---|
| after 1.e4 | −70 / Nb8–c6 | 318,945,864 B | 19 | 170.1 (167.8–179.1) | 153.1 (146.2–164.0) |
| Italian, `r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3` | −110 / Ng8–f6 | 197,022,136 B | 11 | 120.2 (118.7–124.3) | 104.0 (102.2–114.6) |
| rook endgame, `8/5pk1/6p1/8/8/6P1/5PK1/3R4 b - - 0 1` | −520 / Kg7–g8 | 18,171,080 B | 1 | 15.2 (15.0–15.6) | 13.1 (13.1–19.2) |

Across the 7 runs of each position, the allocated bytes, score and best move were identical, and no Gen1 or Gen2 collections occurred.

The 9,472 B per evaluation equals 32 pieces × 296 B. 296 B is the size of one `int[8,8]` on x64 (40 B header and bounds plus 256 B of data). This is consistent with one table allocation per `Piece.evaluate()` call. The 2,048 B per `findmoves` call at the start position is consistent with a `List<Move>` (32 B elements) growing through capacities 4, 8, 16 and 32.

Node counts are not available yet, because the original `Minimax` has no counters. Phase 2 adds them.

## Preliminary review leads: status

| Lead | Status |
|---|---|
| `Bot.Minimax()` is depth-limited alpha-beta | Confirmed (fail-soft returns, depth 4, no move ordering) |
| `makeMove` sets king state that `undoMove` does not restore | Confirmed, R01 |
| Search ignores castling, en passant, double push, promotion | Confirmed, R02, R09, R10, R08 |
| `IsKingInCheck` omits enemy-king attacks | Confirmed, R05; consequence R06 |
| `King.findMoves` mixes pixels and square offsets | Confirmed, R03, R04 (kingside castling through check; queenside castling out of check) |
| Promoted queen gets the wrong X | Confirmed, R07 (X = column index instead of pixel) |
| Mate scores favour slower wins | Confirmed, R11 |
| Insufficient material and fifty-move handling need correction | Confirmed, R12d–g executed; fifty-move by static reading |
| `evaluate()` builds tables on every call | Confirmed; 9,472 B per start-position evaluation measured on .NET 10 and 11 |
| Moves generated before the depth-0 test | Confirmed; also currently required for leaf mate detection |
| Static board references; squares derived from sprite rectangles | Confirmed |
| Timing is a stopwatch around a GUI move | Confirmed; output compiled out in Release |
| `dev` is not a controlled alpha-beta baseline | Confirmed |

## Prioritised issues

1. **Position update (blocks every later phase).** There is no single, complete, reversible make/undo. Covers R01, R02, R07, R08, R09 and R10, plus rook `hasMoved` and en-passant flag lifetime.
2. **King safety.** Enemy-king attacks are missing (R05), which leads to a crash (R06).
3. **Castling legality.** Covers R03, R04, the rook colour check, and `Board.check` never being set.
4. **Terminal scoring.** Mate distance (R11).
5. **Draw rules.** Dead-position detection (R12d–g); the fifty-move counter and the decision being made in `Draw`.
6. **Coupling.** Rules depend on sprite rectangles and static board references; game-state decisions are made in rendering code.
7. **Allocation and avoidable work.** Piece-square tables are allocated per evaluation; a new `List<Move>` is allocated per node.
8. **Measurement.** There are no node counters, and the only timing is compiled out in Release.

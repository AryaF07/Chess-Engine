# ChessNEA — chess engine with a MonoGame interface

A chess game in C# where you play White against a fixed-depth search engine. The engine is a separate, GUI-independent library. Its rules are checked against published perft counts and an independent move generator, and its search speed-ups are measured with a reproducible benchmark suite.

This `master` is the **simplified version**: the original game plus correct rules, testing, benchmarking and two measured search improvements (alpha-beta pruning and capture ordering). A more advanced engine is preserved on a separate branch (see [Advanced version](#advanced-version)).

## Features

- **Rules:** full legal move generation, including castling (through/out of check, rook moves and rights), en passant (including pins along the rank), promotion and under-promotion.
- **Game end:** checkmate, stalemate, the fifty-move rule, insufficient material, and threefold repetition (Zobrist keys with a move history).
- **Reversible state:** `MakeMove` returns an `Undo` record and `UnmakeMove` restores the position exactly, including castling rights, the en-passant square, the clocks and the hash key. FEN import and export are validated.
- **Evaluation:** the original material plus piece-square tables, from Black's point of view. The tables are created once, not on every call.
- **Search:**
  - fixed-depth minimax with alpha-beta pruning (fail-soft); the bot plays Black at depth 4;
  - mate scores prefer shorter mates;
  - fifty-move, insufficient-material and repeated positions score as draws;
  - captures are ordered by most-valuable-victim/least-valuable-attacker (MVV-LVA), and promotions come before quiet moves;
  - one reusable move list per ply, so a search allocates only its statistics object.
- **GUI:** MonoGame (WindowsDX). Two players can play with 1/3/10-minute clocks, or one player can play against the bot. It has legal-move highlighting, a promotion menu and game-end screens.

## Build and play

Requirements:
- Windows, for the MonoGame WindowsDX GUI. The engine, tests and tools also run elsewhere.
- The .NET 10 SDK. `global.json` pins 10.0.404 and allows later patch versions.

```powershell
dotnet run --project ChessNEA.csproj -c Release
```

Press play, then choose a 1/3/10-minute two-player game or the bot game, where you play White. Move by clicking a piece and then its destination. The MonoGame content tool (`mgcb`) is restored from `.config/dotnet-tools.json` during the build.

```powershell
dotnet test tests/ChessNEA.Engine.Tests -c Release
```

## Architecture

| Path | Contents |
|---|---|
| `Engine/` (`ChessNEA.Engine`, net10.0) | `Position` (8×8 board, legal moves, make/unmake, FEN, status, Zobrist key and history), `Move`, `Pieces/` (move patterns and piece-square tables), `Bot` (evaluation, search, ordering, `SearchStats` counters) |
| `Game1.cs`, `Board.cs` (net10.0-windows) | MonoGame screens and input; `Board` presents the game and asks `Bot.Search` for Black's move. All rules come from the engine library. |
| `tests/ChessNEA.Engine.Tests` | xUnit: rules, regressions, perft, hashing and repetition, ordering, search counters, allocations |
| `tools/ChessNEA.Bench` | Headless runner: `perft`, `divide`, `bench`, `compare`, `export`, `allocprofile`. Every JSON result records the commit, dirty flag, runtime, CPU, build and suite hash. |
| `tools/ab/` | Interleaved A/B timing of two commits or two option sets (`ab.sh`, `summarise.py`) |
| `tools/oracle/compare.py` | Compares legal moves, perft(2) and status with python-chess |
| `bench/suite-v1.txt` | Versioned benchmark suite of 15 positions (openings, middlegames, tactics, endgames) |
| `.github/workflows/ci.yml` | CI: tests, perft suite, a 3,000-position oracle check, GUI build |

## Results

All figures below come from saved result files. The environment for all of them was:
- an AMD Ryzen 7 5700X running Windows 11 (10.0.26300), x64;
- .NET 10.0.15, Release build, tiered compilation off, workstation GC;
- suite `bench/suite-v1.txt`, 15 positions, fixed depth 4.

The runs were recorded during development, at the commits named. Those commits are on the `advanced-engine` branch; the simplified search is the same code path without the later features. [Reproduction](#reproduction) shows that the simplified build gives identical node counts, scores and moves.

### Alpha-beta pruning vs. unpruned minimax

[`results/phase2/compare_depth4.json`](results/phase2/compare_depth4.json), commit `7a24243`. Both sides used the same rules, evaluation and move order (generation order); only pruning differed.

| | Minimax | Alpha-beta | Reduction |
|---|---|---|---|
| Nodes | 16,841,836 | 1,073,472 | 93.6% |
| Leaf evaluations | 16,382,972 | 1,006,418 | 93.9% |
| Time (sum over positions) | 132,541 ms | 8,615 ms | 93.5% |
| Equal root scores / moves | 15 / 15 | | |

The times are single runs per side after one warm-up, so they are indicative. They were measured on that commit's build, before the allocation improvements below; the time ratio was not re-measured on the simplified build. Node counts are deterministic and reproduce exactly.

### Capture ordering (MVV-LVA) vs. unordered alpha-beta

| | Unordered | Ordered | Reduction | Source |
|---|---|---|---|---|
| Nodes | 1,073,472 | 106,727 | 90.1% | [`results/phase4/1-ordering-nodes`](results/phase4/1-ordering-nodes) (`51a63ce`) |
| Time, sum of per-position medians | 5,398.7 ms | 400.7 ms | 92.6% | [`results/phase4/1-ordering-time/summary.json`](results/phase4/1-ordering-time/summary.json) |
| Equal root scores / best moves | 15 / 15 | | | both of the above |

The timing is an interleaved A/B run: commit `b517559` (unordered) vs. `51a63ce` (ordered), 3 rounds × (1 warm-up + 7 runs) per position. Per-round totals were 5,365–5,510 ms unordered and 394–403 ms ordered.

### Allocations

[`results/phase3/3-overall/summary.json`](results/phase3/3-overall/summary.json): interleaved A/B, `5c78cb2` → `81beecf`, unordered alpha-beta, same 1,073,472 nodes.

| | Before | After |
|---|---|---|
| Allocated bytes, sum of per-position medians | 12,918,974,912 | 4,441,688 |
| GC pause time | 31.4 ms | 0 ms |
| Time, sum of per-position medians | 8,037.1 ms | 5,387.7 ms (−33.0%) |

The two changes were piece-square tables created once instead of on every evaluation, and one reused move list per ply.

### Original engine baseline

[`docs/phase0-baseline.md`](docs/phase0-baseline.md) and [`results/phase0/`](results/phase0) record the original game's engine before this work: reproduced rule defects (castling, en passant, promotion, king safety, mate scoring) and its perft errors.

## Correctness coverage

Verified on the simplified build, commits `a936375` / `03fecf6` (same tree); outputs are in [`results/simplified-check/`](results/simplified-check):

| Check | Result |
|---|---|
| xUnit tests | 174 passed, 0 failed ([`tests-console.txt`](results/simplified-check/tests-console.txt)) |
| Published perft suite | 26/26 reference counts match: start position, Kiwipete and CPW positions 3–6 ([`perft/perft.json`](results/simplified-check/perft/perft.json)) |
| python-chess oracle | 0 differences in 20,000 seeded random positions: 346,571 legal moves, 7,336,915 perft(2) nodes and the FEN-derivable status ([`oracle.json`](results/simplified-check/oracle.json)) |
| Recorded node counts | `compare --ordering off --repetition off` reproduces `results/phase2/compare_depth4.json` with 0 differences in any counter, score or move. `bench --repetition off` reproduces both Phase 4 ordering totals. |
| GUI build | Succeeds ([`gui-build-console.txt`](results/simplified-check/gui-build-console.txt)) |

The regression tests cover:
- **castling:** through or out of check, the rook move and its unmake, lost and restored rights, and a rook that left and returned;
- **en passant:** a one-move window, removing the captured pawn, the pin along the rank, and the key and repetition effect of an unusable right;
- **promotion:** all four pieces, capture promotions, and checking under-promotion;
- **game end:** checkmate, stalemate, the fifty-move rule (including mate on the 100th ply), insufficient material, threefold repetition;
- **search:** the search reproduces the recorded node counts, and unpruned minimax visits exactly the perft tree.

## Reproduction

```powershell
dotnet build tools/ChessNEA.Bench -c Release
```

```powershell
dotnet run --project tools/ChessNEA.Bench -c Release -- perft --out results/latest
```

Minimax vs. alpha-beta, the configuration of `results/phase2/compare_depth4.json` (about 2 minutes):

```powershell
dotnet run --project tools/ChessNEA.Bench -c Release -- compare --depth 4 --ordering off --repetition off --out results/latest
```

Node counts without and with ordering (`results/phase4/1-ordering-nodes`):

```powershell
dotnet run --project tools/ChessNEA.Bench -c Release -- bench --depth 4 --runs 1 --warmups 0 --ordering off --repetition off --out results/latest/off
```

```powershell
dotnet run --project tools/ChessNEA.Bench -c Release -- bench --depth 4 --runs 1 --warmups 0 --ordering on --repetition off --out results/latest/on
```

Interleaved timing (Git Bash). These repeat the recorded A/B runs for ordering and for allocations; the commits are on `advanced-engine`:

```bash
tools/ab/ab.sh b517559 51a63ce results/latest/ordering-time 3 4 7 && python -I tools/ab/summarise.py results/latest/ordering-time --semantics score
tools/ab/ab.sh 5c78cb2 81beecf results/latest/alloc-time 3 4 7 && python -I tools/ab/summarise.py results/latest/alloc-time
```

To time ordering on the current build instead:
- set `BENCH_ARGS_A="--ordering off"` and `BENCH_ARGS_B="--ordering on"`;
- pass `HEAD` for both commits.

The results will differ in absolute time from the recorded ones (see the limitations).

Oracle (`python -m pip install chess`, python-chess 1.11.2 was used):

```powershell
dotnet run --project tools/ChessNEA.Bench -c Release -- export --count 20000 --seed 1 --file results/latest/random.json
```

```powershell
python -I tools/oracle/compare.py results/latest/random.json --out results/latest/oracle.json
```

`--repetition` defaults to `on`, the engine's normal rules. With it on, the totals differ by 4 nodes (1,073,476 and 106,731), in 3 of the 15 positions (rook-mate-in-one, pawn-endgame, lucena), with identical scores and moves ([`results/simplified-check/bench-ordering-*-repetition-on`](results/simplified-check)). The recorded runs predate search repetition draws.

## Limitations

- **Fixed depth 4, no time management.** There is no quiescence search, so evaluations at the horizon can be taken in the middle of an exchange. The bot always plays Black.
- **Simple evaluation:** material plus static piece-square tables. The king uses the middlegame table in every phase.
- **No transposition table.** The Zobrist key exists only for repetition detection.
- **Times from earlier builds.** The recorded times come from the commits named above. The simplified build adds Zobrist hashing and repetition tracking. When they were introduced on the advanced branch, they cost +14.1% search time at depth 5, measured in that branch's configuration at the time. The simplified build's absolute times therefore differ from the recorded ones; its node counts do not.
- **Indicative minimax times.** The minimax vs. alpha-beta times are single runs. The ordering and allocation times are interleaved medians on one machine.
- **Automatic draws.** The fifty-move rule and threefold repetition end the game automatically. FIDE makes them claimable, and automatic only at 75 moves or fivefold repetition.
- **What the oracle covers.** It checks only what a FEN encodes; repetition is covered by the engine's own tests.
- **Build warning.** The GUI build shows one existing MonoGame template warning about high-DPI settings in `app.manifest`.

## Advanced version

The branch `advanced-engine` (commit `8e37417`) keeps the full development history and the stronger engine:
- iterative deepening with principal-variation reuse and a time budget;
- killer moves;
- a Zobrist transposition table with bounds and mate-score adjustment;
- quiescence search;
- a tapered king evaluation;
- self-play match and fixed-time tools;
- fixes from an external review.

Its results (for example self-play strength gains) were measured on that engine, and none of them describe this version. The per-phase branches (`phase-0-baseline` … `phase-6-review-fixes`) remain as they were. The phase notes in `docs/` (`phase0-baseline.md`, `roadmap.md`) are historical: they describe that development, including features not present here.

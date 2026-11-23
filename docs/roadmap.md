# Roadmap

Phases run one at a time on their own branches, each with a review point at the end. All performance claims must come from saved results under `results/`.

| Phase | Scope | Changes from the original plan |
|---|---|---|
| 0 — Baseline | Architecture map, headless runner, reproductions, perft through the original pipeline, measurements of the original engine | Done (this branch) |
| 1 — Position state and rules | One `MakeMove`/`UnmakeMove` that records everything needed to undo a move: captured piece, castling rook, en-passant victim, promotion piece, previous flags, halfmove clock. Explicit logical square on each piece (the sprite rectangle derived for drawing only). Enemy-king attacks. Castling path and in-check tests. All four promotion choices in search. Mate-distance scoring (ply from root). Dead-position and fifty-move rules moved out of `Draw`. | **Adds a small unit-test project in Phase 1** (instead of waiting for Phase 2), so every fix lands with a regression test. The R01–R12 reproductions become tests. Threefold repetition is **deferred to Phase 5**, where Zobrist keys make it cheap and exact. |
| 2 — Testing and benchmarking | Complete FEN import/export. Perft and divide in the test project. Comparison against an independent generator. Deterministic random make/undo tests. Node and leaf counters. Alpha-beta vs. unpruned minimax at equal depth. Versioned benchmark suite. CI for correctness only. | The Phase 0 runner already provides timing, allocation and environment capture; Phase 2 extends it rather than starting over. |
| 3 — Allocations | Static piece-square tables; per-ply move buffers if profiling supports them | Unchanged |
| 4 — Search efficiency | Capture/promotion ordering, iterative deepening with PV reuse, time budget with guaranteed position restore; killers/history only after measurement | Unchanged |
| 5 — Transposition table | Zobrist hashing (incremental vs. recomputed), bounded table, bound types, mate-score normalisation, repetition handling | Now also carries threefold-repetition detection |
| 6 — Playing strength (optional) | Quiescence search, phase-dependent king tables, tactical test suite | Unchanged |
| 7 — Presentation | README, architecture overview, reproducible commands, evidence tables | Unchanged |

## Open decisions

- **Runtime.** The project targets .NET 8, which reached end of support in November 2026, and only .NET 10 and 11 runtimes are installed. The choices are to install the .NET 8 runtime (results then match the declared target) or to retarget to `net10.0-windows` (the current LTS) and record that change.
- **Test framework.** Phase 1 needs a test framework from NuGet (for example xUnit). Its packages have to be downloaded.

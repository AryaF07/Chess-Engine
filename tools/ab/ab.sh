#!/usr/bin/env bash
# Interleaved A/B benchmark of two commits on the same machine, runtime and settings.
# Each commit is built in its own git worktree; full suite benchmarks then alternate
# A, B, A, B, ... so slow drifts in machine state affect both sides equally.
#
# usage: tools/ab/ab.sh <commitA> <commitB> <outdir> [rounds=3] [depth=4] [runs=7]
# Extra bench options per side: BENCH_ARGS_A="--ordering off" BENCH_ARGS_B="--ordering on" (commits may be equal).
# then:  python -I tools/ab/summarise.py <outdir>
set -euo pipefail
A="$1"; B="$2"; OUT="$3"; ROUNDS="${4:-3}"; DEPTH="${5:-4}"; RUNS="${6:-7}"
ROOT="$(git rev-parse --show-toplevel)"
WT="${AB_WORKTREES:-$ROOT/../ab-worktrees}"
mkdir -p "$OUT" "$WT"
OUT="$(cd "$OUT" && pwd)"

for side in A B; do
  commit="${!side}"
  dir="$WT/$side"
  if [ -d "$dir" ]; then git -C "$ROOT" worktree remove --force "$dir"; fi
  git -C "$ROOT" worktree add --detach "$dir" "$commit" > /dev/null
  dotnet build "$dir/tools/ChessNEA.Bench" -c Release > "$OUT/build-$side.log" 2>&1
  extra="BENCH_ARGS_$side"
  echo "$side = $(git -C "$dir" rev-parse HEAD) ${!extra:-}" | tee -a "$OUT/commits.txt"
done

for ((i = 1; i <= ROUNDS; i++)); do
  for side in A B; do
    echo "round $i: $side"
    (cd "$WT/$side" && dotnet tools/ChessNEA.Bench/bin/Release/net10.0/ChessNEA.Bench.dll \
       bench --depth "$DEPTH" --runs "$RUNS" --warmups 1 $(eval echo "\${BENCH_ARGS_$side:-}") --out "$OUT/$side-round$i" > "$OUT/$side-round$i.log" 2>&1)
  done
done

for side in A B; do git -C "$ROOT" worktree remove --force "$WT/$side"; done
echo "done; summarise with: python -I tools/ab/summarise.py $OUT"

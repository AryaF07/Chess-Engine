"""Summarise an interleaved A/B benchmark directory written by tools/ab/ab.sh.

Per position, all timed samples of all rounds are pooled per side and reported as
median, minimum and maximum; allocation and GC pause are medians. The script also
checks that both sides searched the same tree (identical counters, scores and best
moves), which a semantics-preserving change must satisfy, and reports per-round
suite totals so drift between rounds is visible.

With --semantics score (for changes such as move ordering that legitimately change the
searched tree), only the root score must match; node counts may differ and best moves may
differ (reported, since two moves can share the best score).

usage: python -I tools/ab/summarise.py <outdir> [--semantics tree|score] [--json summary.json]
"""
import glob
import json
import os
import statistics
import sys


def load(outdir, side):
    rounds = []
    for d in sorted(glob.glob(os.path.join(outdir, f"{side}-round*"))):
        if os.path.isdir(d):
            files = glob.glob(os.path.join(d, "search_depth*.json"))
            if len(files) != 1:
                sys.exit(f"expected one search_depth*.json in {d}, found {len(files)}")
            doc = json.load(open(files[0], encoding="utf-8"))
            rounds.append(doc)
    return rounds


def main(argv):
    outdir = argv[1]
    semantics = argv[argv.index("--semantics") + 1] if "--semantics" in argv else "tree"
    A, B = load(outdir, "A"), load(outdir, "B")
    if not A or not B:
        sys.exit("missing rounds")
    names = [p["Name"] for p in A[0]["data"]["positions"]]
    rows, problems = [], []
    for name in names:
        def collect(rounds):
            ps = [next(p for p in r["data"]["positions"] if p["Name"] == name) for r in rounds]
            ms = [s["ElapsedMs"] for p in ps for s in p["Samples"]]
            alloc = [s["AllocatedBytes"] for p in ps for s in p["Samples"]]
            pause = [s.get("GcPauseMs", 0.0) for p in ps for s in p["Samples"]]
            gen0 = [s["Gen0"] for p in ps for s in p["Samples"]]
            gen1 = [s["Gen1"] for p in ps for s in p["Samples"]]
            gen2 = [s["Gen2"] for p in ps for s in p["Samples"]]
            sig = {(json.dumps(p["Counters"], sort_keys=True), p["Score"], p["BestMove"]) for p in ps}
            scores = {p["Score"] for p in ps}
            return dict(ms=ms, alloc=alloc, pause=pause, gen0=gen0, gen1=gen1, gen2=gen2, sig=sig, scores=scores,
                        counters=ps[0]["Counters"], score=ps[0]["Score"], move=ps[0]["BestMove"])
        a, b = collect(A), collect(B)
        if len(a["sig"]) != 1 or len(b["sig"]) != 1:
            problems.append(f"{name}: results differ between rounds")
        same_tree = a["sig"] == b["sig"]
        same_score = a["scores"] == b["scores"]
        if semantics == "tree" and not same_tree:
            problems.append(f"{name}: A and B searched different trees or returned different results")
        if not same_score:
            problems.append(f"{name}: root scores differ ({a['score']} vs {b['score']})")
        row = {
            "name": name, "nodes": a["counters"]["Nodes"], "sameTree": same_tree, "sameScore": same_score,
            "B_nodes": b["counters"]["Nodes"], "A_leaves": a["counters"]["LeafEvaluations"],
            "B_leaves": b["counters"]["LeafEvaluations"], "sameMove": a["move"] == b["move"],
            "score": a["score"], "move": a["move"], "B_move": b["move"],
            "A_medianMs": statistics.median(a["ms"]), "A_minMs": min(a["ms"]), "A_maxMs": max(a["ms"]),
            "B_medianMs": statistics.median(b["ms"]), "B_minMs": min(b["ms"]), "B_maxMs": max(b["ms"]),
            "A_allocBytes": statistics.median(a["alloc"]), "B_allocBytes": statistics.median(b["alloc"]),
            "A_gcPauseMs": statistics.median(a["pause"]), "B_gcPauseMs": statistics.median(b["pause"]),
            "A_gen0": statistics.median(a["gen0"]), "B_gen0": statistics.median(b["gen0"]),
            "A_gen1": max(a["gen1"]), "B_gen1": max(b["gen1"]), "A_gen2": max(a["gen2"]), "B_gen2": max(b["gen2"]),
            "samplesPerSide": len(a["ms"]),
        }
        row["timeRatio"] = row["B_medianMs"] / row["A_medianMs"]
        row["allocRatio"] = row["B_allocBytes"] / row["A_allocBytes"] if row["A_allocBytes"] else float("nan")
        rows.append(row)

    def total(key):
        return sum(r[key] for r in rows)
    summary = {
        "commits": open(os.path.join(outdir, "commits.txt"), encoding="utf-8").read().split("\n")[:2],
        "rounds": len(A),
        "semantics": semantics,
        "environmentA": A[0]["environment"], "environmentB": B[0]["environment"],
        "totals": {
            "nodes": total("nodes"), "B_nodes": total("B_nodes"),
            "A_sumMedianMs": total("A_medianMs"), "B_sumMedianMs": total("B_medianMs"),
            "A_allocBytes": total("A_allocBytes"), "B_allocBytes": total("B_allocBytes"),
            "A_gcPauseMs": total("A_gcPauseMs"), "B_gcPauseMs": total("B_gcPauseMs"),
        },
        "perRoundSumOfMediansMs": {
            "A": [r["data"]["summary"]["SumOfMedianMs"] for r in A],
            "B": [r["data"]["summary"]["SumOfMedianMs"] for r in B],
        },
        "aggregation": "Per position: median/min/max over all timed samples of all rounds; allocation, GC pause and Gen0 are medians, Gen1/Gen2 maxima. Totals are sums of per-position values.",
        "problems": problems,
        "positions": rows,
    }
    t = summary["totals"]
    print(f"rounds {len(A)}, samples per side per position {rows[0]['samplesPerSide']}")
    print(f"{'position':<17}{'A nodes':>10}{'B nodes':>10}  {'A ms':>9} {'B ms':>9} {'B/A':>6}   {'A alloc':>13} {'B alloc':>13}  tree  score  move")
    for r in rows:
        print(f"{r['name']:<17}{r['nodes']:>10,}{r['B_nodes']:>10,}  {r['A_medianMs']:>9.1f} {r['B_medianMs']:>9.1f} {r['timeRatio']:>6.3f}   "
              f"{r['A_allocBytes']:>13,.0f} {r['B_allocBytes']:>13,.0f}  {'same' if r['sameTree'] else 'diff'}  "
              f"{'same' if r['sameScore'] else 'DIFF'}  {r['move'] if r['sameMove'] else r['move'] + '->' + r['B_move']}")
    print(f"{'total':<17}{t['nodes']:>10,}{t['B_nodes']:>10,}  {t['A_sumMedianMs']:>9.1f} {t['B_sumMedianMs']:>9.1f} "
          f"{t['B_sumMedianMs'] / t['A_sumMedianMs']:>6.3f}   {t['A_allocBytes']:>13,.0f} {t['B_allocBytes']:>13,.0f}")
    print(f"GC pause (sum of medians): A {t['A_gcPauseMs']:.1f} ms, B {t['B_gcPauseMs']:.1f} ms")
    print("per-round sums of medians:", summary["perRoundSumOfMediansMs"])
    print("problems:", problems or "none")
    if "--json" in argv:
        json.dump(summary, open(argv[argv.index("--json") + 1], "w", encoding="utf-8"), indent=2)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

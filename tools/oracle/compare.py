"""Compare ChessNEA.Engine move generation with python-chess, an independent implementation.

Input: the JSON written by `ChessNEA.Bench export` (positions with this engine's sorted
legal moves in UCI, perft(2) and game status). For every position, python-chess computes
the same three things from the FEN alone. Any difference is printed and makes the script
exit with status 1. Writes a summary JSON when --out is given.

Setup (once):  python -m venv tools/oracle/.venv
               tools/oracle/.venv/Scripts/python -m pip install chess
Usage:         tools/oracle/.venv/Scripts/python -I tools/oracle/compare.py EXPORT.json [--out SUMMARY.json]
"""
import json
import sys
from collections import Counter

import chess


def perft(board, depth):
    if depth == 0:
        return 1
    if depth == 1:
        return board.legal_moves.count()
    nodes = 0
    for move in board.legal_moves:
        board.push(move)
        nodes += perft(board, depth - 1)
        board.pop()
    return nodes


def status(board):
    # Same precedence as Position.GetStatus: no legal moves first, then the draw rules.
    if not any(board.legal_moves):
        return "Checkmate" if board.is_check() else "Stalemate"
    if board.halfmove_clock >= 100:
        return "FiftyMoveRule"
    if board.is_insufficient_material():
        return "InsufficientMaterial"
    return "Ongoing"


def main(argv):
    path = argv[1]
    out = argv[argv.index("--out") + 1] if "--out" in argv else None
    data = json.load(open(path, encoding="utf-8"))
    positions = data["positions"]
    mismatches = []
    features = Counter()
    for p in positions:
        board = chess.Board(p["Fen"])
        oracle_moves = sorted(m.uci() for m in board.legal_moves)
        for m in board.legal_moves:
            if board.is_castling(m): features["castling moves"] += 1
            if board.is_en_passant(m): features["en-passant moves"] += 1
            if m.promotion: features["promotion moves"] += 1
        if board.is_check(): features["positions in check"] += 1
        oracle_status = status(board)
        features[f"status {oracle_status}"] += 1
        problems = []
        if oracle_moves != p["LegalMoves"]:
            missing = sorted(set(oracle_moves) - set(p["LegalMoves"]))
            extra = sorted(set(p["LegalMoves"]) - set(oracle_moves))
            problems.append(f"moves: missing {missing} extra {extra}")
        if oracle_moves:
            oracle_perft2 = perft(board, 2)
            if oracle_perft2 != p["Perft2"]:
                problems.append(f"perft2: engine {p['Perft2']} oracle {oracle_perft2}")
        if oracle_status != p["Status"]:
            problems.append(f"status: engine {p['Status']} oracle {oracle_status}")
        if problems:
            mismatches.append({"fen": p["Fen"], "problems": problems})

    total_moves = sum(len(p["LegalMoves"]) for p in positions)
    summary = {
        "oracle": f"python-chess {chess.__version__}",
        "input": path,
        "seed": data.get("seed"),
        "suite": data.get("suite"),
        "suiteSha256": data.get("suiteSha256"),
        "positions": len(positions),
        "legalMovesCompared": total_moves,
        "perft2NodesCompared": sum(p["Perft2"] for p in positions),
        "features": dict(sorted(features.items())),
        "mismatches": mismatches,
    }
    print(json.dumps({k: v for k, v in summary.items() if k != "mismatches"}, indent=2))
    for m in mismatches[:20]:
        print("MISMATCH", m["fen"], "|", "; ".join(m["problems"]))
    print(f"{len(mismatches)} mismatching positions out of {len(positions)}")
    if out:
        with open(out, "w", encoding="utf-8") as f:
            json.dump(summary, f, indent=2)
    return 1 if mismatches else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

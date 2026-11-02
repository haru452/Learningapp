"""CEFR-J Vocabulary Profile 1.5 の CSV を、アプリが読む CefrjWords.json に変換する。

出典: The CEFR-J Wordlist Version 1.5. Compiled by Yukio Tono, Tokyo University of Foreign Studies (Tono Laboratory, TUFS).
配布: https://github.com/openlanguageprofiles/olp-en-cefrj (研究・商用とも無料。出典の明記が必要)

  python build_cefrj_words.py --csv cefrj-vocabulary-profile-1.5.csv --out ../../Assets/Resources/CefrjWords.json

- 見出しに "/" で別形があるもの (a.m./A.M./am/AM) は最初の形
- 小文字・前後の空白なし。同じ (語, 品詞) は一番低いレベル。A1〜B2 だけ
- 出力は 1 行 1 語の JSON 配列 [{"w", "pos", "cefr"}] (語 → 品詞の順)
"""
import argparse
import csv
import json

LEVELS = ["A1", "A2", "B1", "B2"]


def read_csv(lines):
    """CSV の行 (ヘッダーつき) → dict のリスト"""
    return list(csv.DictReader(lines))


def build(rows):
    best = {}
    for r in rows:
        word = (r.get("headword") or "").split("/")[0].strip().lower()
        pos = (r.get("pos") or "").strip().lower()
        level = (r.get("CEFR") or "").strip().upper()
        if not word or level not in LEVELS:
            continue
        key = (word, pos)
        if key not in best or LEVELS.index(level) < LEVELS.index(best[key]):
            best[key] = level
    return [{"w": w, "pos": p, "cefr": best[(w, p)]} for (w, p) in sorted(best)]


def to_json(words):
    return "[\n" + ",\n".join(json.dumps(x, ensure_ascii=False) for x in words) + "\n]\n"


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--csv", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args(argv)
    with open(a.csv, encoding="utf-8-sig", newline="") as f:
        words = build(read_csv(f))
    with open(a.out, "w", encoding="utf-8", newline="\n") as f:
        f.write(to_json(words))
    print(f"{len(words)} words -> {a.out}")


if __name__ == "__main__":
    main()

"""
Tatoeba のエクスポートから、リピーティング練習に使う英文を選んで RepeatSentences.json を作る。

  python3 build_repeat_sentences.py \
      --sentences eng_sentences.tsv.bz2 --audio sentences_with_audio.tar.bz2 \
      --out ../../Assets/Resources/RepeatSentences.json --count 1000 --seed 42

選ぶ条件: 英語 / 録音がある / 録音のライセンス欄が空でない (空 = 再利用の許可がない) /
6〜15 語 / 120 文字以内 / `"` と `(` を含まない / 数字は 2 個まで / 同じ文は 1 回だけ /
Tom・Mary が出てくる文は全体の 15% まで。
決まった seed で選ぶので、毎回同じ結果になる。

※ 入力ファイルも出力 (RepeatSentences.json) も Tatoeba のデータなので git には入れない (.gitignore 済み)。
   文: CC BY 2.0 FR、録音: 録音した人ごと (主に CC BY-NC-ND)。個人の学習用 (営利目的でない) でだけ使う。
"""
import argparse
import bz2
import json
import os
import random
import re
import tarfile

MIN_WORDS, MAX_WORDS, MAX_CHARS, MAX_DIGITS = 6, 15, 120, 2
# Tatoeba には "Tom" と "Mary" の文がとても多い (そのままだと半分近く) ので、全体の 15% までにする
NAME_SHARE = 0.15
_NAME_RE = re.compile(r"\b(Tom|Mary)\b")


def has_common_name(text):
    return bool(_NAME_RE.search(text))


def usable_text(text):
    words = text.split()
    return (MIN_WORDS <= len(words) <= MAX_WORDS
            and len(text) <= MAX_CHARS
            and '"' not in text and "(" not in text
            and sum(c.isdigit() for c in text) <= MAX_DIGITS)


def usable_license(lic):
    lic = (lic or "").strip()
    return lic != "" and lic != "\\N"


def select_sentences(sentence_rows, audio_rows, count, seed):
    """sentence_rows: (id, lang, text)、audio_rows: (sentence_id, audio_id, username, license)。"""
    best_audio = {}
    for sid, aid, user, lic in audio_rows:
        if not usable_license(lic):
            continue
        sid, aid = int(sid), int(aid)
        if sid not in best_audio or aid < best_audio[sid][0]:
            best_audio[sid] = (aid, user, lic.strip())

    candidates, seen = [], set()
    for sid, lang, text in sentence_rows:
        sid = int(sid)
        text = (text or "").strip()
        if lang != "eng" or sid not in best_audio or not usable_text(text):
            continue
        key = re.sub(r"\s+", " ", text.lower())
        if key in seen:
            continue
        seen.add(key)
        aid, user, lic = best_audio[sid]
        candidates.append({"id": sid, "audioId": aid, "text": text, "author": user, "license": lic})

    candidates.sort(key=lambda s: s["id"])
    rng = random.Random(seed)
    rng.shuffle(candidates)

    max_named = int(count * NAME_SHARE)
    chosen, named = [], 0
    for s in candidates:
        if len(chosen) >= count:
            break
        if has_common_name(s["text"]):
            if named >= max_named:
                continue
            named += 1
        chosen.append(s)
    return chosen


def read_sentences(path):
    with bz2.open(path, "rt", encoding="utf-8") as f:
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= 3:
                yield parts[0], parts[1], parts[2]


def read_audio(path):
    with tarfile.open(path, "r:bz2") as tar:
        member = next(m for m in tar.getmembers() if m.name.endswith(".csv"))
        for raw in tar.extractfile(member):
            parts = raw.decode("utf-8").rstrip("\n").split("\t")
            if len(parts) >= 4 and parts[0].isdigit() and parts[1].isdigit():
                yield parts[0], parts[1], parts[2], parts[3]


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.strip().splitlines()[0])
    ap.add_argument("--sentences", required=True)
    ap.add_argument("--audio", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--count", type=int, default=1000)
    ap.add_argument("--seed", type=int, default=42)
    a = ap.parse_args(argv)

    chosen = select_sentences(read_sentences(a.sentences), list(read_audio(a.audio)), a.count, a.seed)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"sentences": chosen}, f, ensure_ascii=False, indent=0)
    print(f"wrote {len(chosen)} sentences to {a.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

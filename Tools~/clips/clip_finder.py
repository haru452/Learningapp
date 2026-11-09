"""単語帳の「動画の場面」を字幕から作る規則。C# 版 (VocabClipFinder.cs) と同じ結果にする。
fixtures/finder_cases.json を両方のテストで共有している。
"""
import math
import re

MAX_PER_WORD = 10
MAX_PER_VIDEO = 2
LEAD_MS = 1000


def tokens(text):
    """小文字にして、[a-z'] 以外で区切り、前後の ' を取る。"""
    out = []
    for t in re.split(r"[^a-z']+", (text or "").lower()):
        t = t.strip("'")
        if t:
            out.append(t)
    return out


def _round_ms(seconds):
    # C# の AwayFromZero に合わせる (Python の round は偶数丸めなので使わない)
    return int(math.floor(seconds * 1000 + 0.5))


def _contains_run(toks, target):
    n = len(target)
    return any(toks[i:i + n] == target for i in range(len(toks) - n + 1))


def _one_line(text):
    return (text or "").replace("\r\n", " ").replace("\n", " ").replace("\r", " ").strip()


def find_in_video(word, video_id, channel, source, lines):
    """単語 (空白を含めば連続した単語) が字幕の区切りに出たら場面にする。1 動画 MAX_PER_VIDEO まで。"""
    result = []
    target = tokens(word)
    if not target or not lines:
        return result
    for line in lines:
        if len(result) >= MAX_PER_VIDEO:
            break
        if not line or not _contains_run(tokens(line.get("text")), target):
            continue
        start = float(line["start"])
        result.append({
            "word": word, "video_id": video_id,
            "start_ms": max(0, _round_ms(start) - LEAD_MS),
            "end_ms": _round_ms(start + float(line["duration"])),
            "sentence": _one_line(line.get("text")),
            "channel": channel, "source": source,
        })
    return result


def merge(current, found):
    """current に found を足す。(video_id, start_ms) 重複は足さない。1 動画 2 つ、全体 10 まで。"""
    result, seen, per_video = [], set(), {}
    for lst in (current, found):
        for c in lst or []:
            if len(result) >= MAX_PER_WORD:
                return result
            key = (c["video_id"], c["start_ms"])
            if key in seen:
                continue
            seen.add(key)
            n = per_video.get(c["video_id"], 0)
            if n >= MAX_PER_VIDEO:
                continue
            per_video[c["video_id"]] = n + 1
            result.append(c)
    return result

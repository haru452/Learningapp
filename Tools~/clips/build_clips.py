"""YouTube のチャンネルから単語帳の「動画の場面」を作り、Supabase の vocab_clips に入れる。
  python build_clips.py --dry-run --max-videos 3   (out/clips.json に書くだけ)
  python build_clips.py                            (Supabase に書く)
キーは同じフォルダの .env に書く (.env.example を参照)。
"""
import argparse
import json
import os
import re
import sys
import time

import clip_finder

HERE = os.path.dirname(os.path.abspath(__file__))
WORDS_PATH = os.path.join(HERE, "..", "..", "Assets", "Resources", "CefrjWords.json")
CHANNELS_PATH = os.path.join(HERE, "channels.json")
DONE_PATH = os.path.join(HERE, ".done.json")
OUT_PATH = os.path.join(HERE, "out", "clips.json")
ENV_PATH = os.path.join(HERE, ".env")

LEVELS = {"A1", "A2", "B1", "B2"}
WORD_RE = re.compile(r"^[a-z][a-z' -]*$")
COLUMNS = ("word", "video_id", "start_ms", "end_ms", "sentence", "channel", "source")
BATCH = 500
TIMEOUT = 30
YT = "https://www.googleapis.com/youtube/v3"


# ---------- 純粋な関数 (通信しない) ----------

def load_words(path):
    """A1〜B2 の単語を小文字・重複なしで返す。'm や a.m. のような語は除く。"""
    with open(path, encoding="utf-8") as fp:
        data = json.load(fp)
    words = set()
    for e in data:
        w = (e.get("w") or "").strip().lower()
        if e.get("cefr") in LEVELS and WORD_RE.match(w):
            words.add(w)
    return words


def pick_transcript(transcripts):
    """英語 (en, en-*) の人が付けた字幕を優先し、無ければ自動字幕。英語が無ければ None。"""
    manual = auto = None
    for t in transcripts:
        lang = (t.language_code or "").lower()
        if lang != "en" and not lang.startswith("en-"):
            continue
        if t.is_generated:
            auto = auto or t
        else:
            manual = manual or t
    return manual or auto


def build_rows(words, videos, current):
    """動画ごとに場面を探し、単語ごとに current へ merge する。場面がある単語だけ返す。
    videos: [{video_id, channel, lines}]、current: {word: [場面]}。"""
    result = {}
    for w in words:
        merged = list(current.get(w) or [])
        for v in videos:
            if len(merged) >= clip_finder.MAX_PER_WORD:
                break
            found = clip_finder.find_in_video(w, v["video_id"], v["channel"], "channel", v["lines"])
            merged = clip_finder.merge(merged, found)
        if merged:
            result[w] = merged
    return result


def upload_payload(rows):
    """表の列だけにして source を channel にする。"""
    out = []
    for r in rows:
        d = {k: r[k] for k in COLUMNS if k in r}
        d["source"] = "channel"
        out.append(d)
    return out


def new_rows(built, current):
    """build_rows の結果から、current に無い場面だけを平らにして返す。"""
    out = []
    for w, lst in built.items():
        have = {(c["video_id"], c["start_ms"]) for c in current.get(w) or []}
        out.extend(c for c in lst if (c["video_id"], c["start_ms"]) not in have)
    return out


# ---------- 通信 ----------

def load_env(path):
    env = dict(os.environ)
    if os.path.exists(path):
        with open(path, encoding="utf-8") as fp:
            for line in fp:
                line = line.strip()
                if line and not line.startswith("#") and "=" in line:
                    k, v = line.split("=", 1)
                    if not env.get(k.strip()):
                        env[k.strip()] = v.strip().strip('"').strip("'")
    return env


def safe_err(e):
    """表示してよい形にする。RuntimeError は自作の文 (URL を含まない)、それ以外は例外の名前だけ。"""
    return str(e) if isinstance(e, RuntimeError) else type(e).__name__


def next_offset(status, row_count, offset, content_range):
    """次のページを読む位置。もう無ければ None。416・空のページで終わり、
    Content-Range の総数 (a-b/total) があればそれに届いたら終わり。"""
    if status == 416 or row_count == 0:
        return None
    nxt = offset + row_count
    m = re.search(r"/(\d+)$", content_range or "")
    if m and nxt >= int(m.group(1)):
        return None
    return nxt


def yt_get(requests, path, params):
    params = dict(params)
    key = params.pop("key")  # キーは URL に入れず (例外の表示に出るため)、ヘッダで送る
    r = requests.get(YT + path, params=params, headers={"X-Goog-Api-Key": key}, timeout=TIMEOUT)
    if r.status_code != 200:
        try:
            reason = r.json()["error"]["errors"][0]["reason"]
        except Exception:
            reason = r.text[:200]
        raise RuntimeError("YouTube API %d: %s" % (r.status_code, reason))
    return r.json()


def list_channel_videos(requests, key, channel_id, limit):
    """チャンネルのアップロード一覧から動画 ID を新しい順に limit 本。"""
    d = yt_get(requests, "/channels", {"part": "contentDetails", "id": channel_id, "key": key})
    items = d.get("items") or []
    if not items:
        raise RuntimeError("チャンネルが見つからない: " + channel_id)
    playlist = items[0]["contentDetails"]["relatedPlaylists"]["uploads"]
    ids, token = [], None
    while len(ids) < limit:
        params = {"part": "contentDetails", "playlistId": playlist, "maxResults": 50, "key": key}
        if token:
            params["pageToken"] = token
        d = yt_get(requests, "/playlistItems", params)
        ids.extend(i["contentDetails"]["videoId"] for i in d.get("items", []))
        token = d.get("nextPageToken")
        if not token:
            break
    return ids[:limit]


def fetch_lines(api, video_id):
    """字幕を clip_finder が使う辞書の並びにする。無ければ None。"""
    t = pick_transcript(api.list(video_id))
    if t is None:
        return None
    return [{"start": s.start, "duration": s.duration, "text": s.text} for s in t.fetch()]


def supa_headers(key, extra=None):
    h = {"apikey": key, "Authorization": "Bearer " + key, "Content-Type": "application/json"}
    h.update(extra or {})
    return h


def read_current(requests, url, key):
    """今 vocab_clips にある場面 (word, video_id, start_ms) を単語ごとに読む。"""
    current, offset, page = {}, 0, 1000
    endpoint = url + "/rest/v1/vocab_clips?select=word,video_id,start_ms&order=word,video_id,start_ms"
    while offset is not None:
        r = requests.get(endpoint,
                         headers=supa_headers(key, {"Range": "%d-%d" % (offset, offset + page - 1),
                                                    "Prefer": "count=exact"}),
                         timeout=TIMEOUT)
        if r.status_code not in (200, 206, 416):
            raise RuntimeError("Supabase 読み込み %d: %s" % (r.status_code, r.text[:200]))
        rows = r.json() if r.status_code != 416 else []
        for c in rows:
            current.setdefault(c["word"], []).append(c)
        offset = next_offset(r.status_code, len(rows), offset, r.headers.get("Content-Range"))
    return current


def upload(requests, url, key, payload):
    endpoint = url + "/rest/v1/vocab_clips?on_conflict=word,video_id,start_ms"
    headers = supa_headers(key, {"Prefer": "resolution=ignore-duplicates,return=minimal"})
    for i in range(0, len(payload), BATCH):
        r = requests.post(endpoint, headers=headers, data=json.dumps(payload[i:i + BATCH]), timeout=TIMEOUT)
        if r.status_code not in (200, 201, 204):
            raise RuntimeError("Supabase 書き込み %d: %s" % (r.status_code, r.text[:200]))
        print("  書き込み %d / %d" % (min(i + BATCH, len(payload)), len(payload)))


def load_json(path, default):
    if os.path.exists(path):
        with open(path, encoding="utf-8") as fp:
            return json.load(fp)
    return default


def save_json(path, data):
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8") as fp:
        json.dump(data, fp, ensure_ascii=False, indent=1)


def collect_videos(requests, api, key, channels, done, max_videos):
    """各チャンネルの未処理の動画の字幕を集める。失敗は飛ばす。done に処理した ID を足す。"""
    videos = []
    for ch in channels:
        try:
            ids = list_channel_videos(requests, key, ch["id"], max_videos)
        except Exception as e:
            print("[%s] 動画一覧を取れず飛ばす: %s" % (ch.get("name"), safe_err(e)))
            continue
        todo = [i for i in ids if i not in done]
        print("[%s] %d 本中 %d 本が未処理" % (ch.get("name"), len(ids), len(todo)))
        for n, vid in enumerate(todo, 1):
            try:
                lines = fetch_lines(api, vid)
            except Exception as e:
                print("  %s 字幕を取れず飛ばす: %s" % (vid, type(e).__name__))
                continue
            done.add(vid)
            if lines:
                videos.append({"video_id": vid, "channel": ch.get("name", ""), "lines": lines})
            if n % 20 == 0:
                print("  %d / %d" % (n, len(todo)))
            time.sleep(0.3)
    return videos


def main(argv=None):
    ap = argparse.ArgumentParser(description="チャンネルの字幕から単語帳の動画の場面を作る")
    ap.add_argument("--dry-run", action="store_true", help="Supabase に書かず out/clips.json に出す")
    ap.add_argument("--max-videos", type=int, default=200, help="チャンネルごとの動画数 (既定 200)")
    args = ap.parse_args(argv)

    env = load_env(ENV_PATH)
    need = ["YOUTUBE_API_KEY"] + ([] if args.dry_run else ["SUPABASE_URL", "SUPABASE_SERVICE_KEY"])
    missing = [k for k in need if not env.get(k)]
    if missing:
        print("エラー: %s が未設定です。%s に書いてください (.env.example を参照)。" % (", ".join(missing), ENV_PATH))
        return 1
    try:
        import requests
        from youtube_transcript_api import YouTubeTranscriptApi
    except ImportError as e:
        print("エラー: %s。pip install -r requirements.txt を実行してください。" % e)
        return 1

    try:
        words = load_words(WORDS_PATH)
        channels = load_json(CHANNELS_PATH, [])
        done = set(load_json(DONE_PATH, []))
        videos = collect_videos(requests, YouTubeTranscriptApi(), env["YOUTUBE_API_KEY"],
                                channels, done, args.max_videos)
        url, skey = env.get("SUPABASE_URL"), env.get("SUPABASE_SERVICE_KEY")
        current = {} if args.dry_run else read_current(requests, url, skey)
        built = build_rows(words, videos, current)
        payload = upload_payload(new_rows(built, current))
        print("字幕のある動画 %d 本から新しい場面 %d 個 (%d 語)" % (len(videos), len(payload), len(built)))
        if args.dry_run:
            save_json(OUT_PATH, payload)
            print("書き出し: " + OUT_PATH)
            return 0
        upload(requests, url, skey, payload)
        save_json(DONE_PATH, sorted(done))
    except Exception as e:  # 通信の例外は URL を含むので名前だけ出す
        print("エラー: %s" % safe_err(e))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

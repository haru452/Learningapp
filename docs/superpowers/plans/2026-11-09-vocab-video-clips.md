# 単語帳: 動画の場面 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 単語帳のカードの Videos ボタンで、その単語が出てくる YouTube の場面 (字幕の文つき) を流せるようにし、これから出る 20 単語の場面をいつも裏で準備しておく。

**Architecture:** 場面は Supabase の表 `vocab_clips` に集める。パソコンの Python スクリプトが指定チャンネルから作り (A)、無い単語はアプリが YouTube を検索して字幕から作る (B)。アプリは場面を単語ごとに端末へ保存し、`VocabClipPrefetcher` が 20 単語先まで埋め続ける。場面を作る規則は C# と Python で同じ見本のテストを通す。

**Tech Stack:** Unity 2022.3.62f3 (C#、async/await、`HttpUtil`、gree/unity-webview の `YouTubePlayerView`)、Supabase (PostgREST の RPC)、Python 3.13 (`youtube-transcript-api`、`requests`)、YouTube Data API v3。

**Spec:** `docs/superpowers/specs/2026-11-09-vocab-video-clips-design.md`

**リポジトリ:** Unity `C:\Users\haru0\club\class\es\My project` (`UNITY/`)、SQL `C:\Users\haru0\club\class\es\database_learnApp` (`WEB/`)。**コミットは `git commit -- <自分が変えたファイル>`**。push は利用者が頼んだときだけ。

## Global Constraints

- 返事・コミットの説明・コードのコメントは日本語。UI の文言は英語、絵文字を使わない。
- UI の文言 (そのまま使う): `Videos`、`Replay`、`Next`、`Close`、`Finding videos...`、`Searching YouTube...`、`No videos for this word yet.`、`Can't reach the internet.`、`Video search limit reached. Try again tomorrow.`、`No YouTube key. Add youtubeApiKey to ApiSecrets.json.`、場面の番号 `3 / 7` の形。
- 数: 1 語につき最大 10 場面、同じ動画から 2 場面まで、始まりは字幕の区切りの 1 秒前 (0 未満は 0)、B は端末で 1 日 30 回まで、B の 1 回は最大 10 本を検索して 5 本調べるか 3 場面で止める、見つからなかった単語は 30 日間 B をしない、準備する単語は 20 個。
- 1 ファイル 600 行以内。600 行を超えている `SupabaseService.cs` (833 行) と、超えそうな `VocabScreenBuilder.cs` (588 行) には書き足さず、partial の別ファイルにする。
- キーは `ApiSecrets.json` だけ。**利用者の `ApiSecrets.json` を上書きしない。** 新しい項目 `youtubeVocabApiKey` (空なら `youtubeApiKey`)。パソコンのスクリプトのキーは `Tools~/clips/.env` (git に入れない)。
- 字幕の文は git に入れない (テストの見本は自分で書いた短い文だけ)。`Tools~/clips/.done.json`・`.env`・出力ファイルは `.gitignore` に入れる。
- Unity の通信はメインのスレッドで動かすので、スレッドは使わず async/await で裏の処理をする。
- 端末のファイル: `persistentDataPath/vocab/clips/<SafeName(word)>.json`、`vocab/clip_search.json`、`vocab/next_new.json`。時刻は UTC の ISO 8601 (`"o"`)。

## 決めたこと (設計書から細かくしたところ)

- Supabase は表を直接読まず、RPC `get_vocab_clips` / `add_vocab_clips` を使う (今の `SupabaseService` は RPC の呼び方しか持っていないため。設計書の `word=in.(...)` と同じく 1 回で問い合わせる)。
- 「見つからなかった単語」は `vocab/clip_search.json` ではなく、場面の保存ファイル (`vocab/clips/<word>.json`) に「0 場面・調べた日時」として入れる。準備済みの判定が 1 か所で済む。`clip_search.json` は 1 日の回数と「上限の日」だけを持つ。

## テストの動かし方 (このパソコン)

`csc`・`dotnet`・`psql` はパスに無い。前の作業用スクリプト (`unitycheck.py` など) は残っていないので Task 0 で作り直す。以後のタスクの「Run:」はこのスクリプトを指す。

- `python <scratchpad>/unitycheck.py` → `errors: 0` (Assembly-CSharp と -Editor をコンパイルだけ)
- `python <scratchpad>/logictest.py` → `ALL LOGIC TESTS PASSED`
- `python <scratchpad>/sqltest.py WEB/supabase/tests/<x>_test.sql` → `ALL ... TESTS PASSED`
- `cd UNITY/Tools~/clips && python -m unittest test_build_clips` → `OK`

## Review Focus

1. **単語が別の語の一部・記号つき** (`calm` と `calmness`、`Calm,`、`calm-looking`、`don't`) → 単語として一致したときだけ場面にする。Task 1 のテストの見本に入れる。
2. **字幕が無い・英語が無い・自動字幕だけ・取り方が変わって全部失敗** → その動画を飛ばし、全部だめなら「0 場面」として保存 (同じ日に何度も検索しない)。Task 5 と Task 9 のテスト。
3. **準備の途中で利用者が同じ単語の Videos を押す・評価が続いて準備が重なる** → 同じ単語は 1 回だけ探し、同時に動く準備は 1 つ。Task 9 と Task 11 のテスト。
4. **YouTube の上限 (403 `quotaExceeded`) と端末の 30 回** → その日は B をしない。上限の日の翌日 (端末の日付) に戻る。Task 6 と Task 7 のテスト。
5. **ログインしていない・Supabase に届かない** → 一覧は飛ばして B だけ。見つけた場面は端末にだけ保存。Task 9 のテスト。

---

### Task 0: テストの道具を作り直す

**Files:** Create (scratchpad、git に入れない): `unitycheck.py`、`logictest.py`、`sqltest.py`

- [ ] **Step 1:** `unitycheck.py` を書く。Unity 2022.3.62f3 の `Editor/Data/DotNetSdkRoslyn/csc.dll` を Unity 同梱の dotnet (`Editor/Data/NetCoreRuntime/dotnet.exe`、無ければ `~/.dotnet/dotnet.exe`) で動かし、`Library/ScriptAssemblies` と `Editor/Data/Managed/UnityEngine/*.dll`、`Packages` の dll を参照して `Assets/Script/**/*.cs` → `acs.dll`、`Assets/Editor/**/*.cs` → `acse.dll` をコンパイルする。`-langversion:9 -nowarn:CS0618`。最後に `errors: <件数>`。
- [ ] **Step 2:** `logictest.py` を書く。`Tests~/LogicTests/*.cs` と、Program.cs の先頭のコメントに並べたテスト対象の .cs (無ければ `Assets/Script/CorrectionDiary/**/*.cs` のうち `using UnityEngine` を含まないもの) をコンパイルし、.NET 6 で実行する。
- [ ] **Step 3:** `sqltest.py` を書く。今ある SQL のテスト (`WEB/supabase/tests/stub.sql` + 対象の .sql + テストの .sql) を、Python の埋め込み Postgres (`pip install postgresql-wheel` を scratchpad の venv に入れる) で流す。
- [ ] **Step 4: 今のテストが通ることを確かめる**
  Run: 3 つとも。Expected: `errors: 0`、`ALL LOGIC TESTS PASSED`、`vocab_test.sql` が `ALL ... TESTS PASSED`。通らなければ道具を直す (コードは直さない)。

### Task 1: 場面を作る規則 `VocabClipFinder` (C#)

**Files:**
- Create: `Assets/Script/CorrectionDiary/Vocab/VocabClipFinder.cs` (Unity に依存しない)
- Create: `Tools~/clips/fixtures/finder_cases.json` (C# と Python が共有する見本)
- Create: `Tests~/LogicTests/VocabClipTests.cs`、Modify: `Tests~/LogicTests/Program.cs` (`VocabClipTests()` を呼ぶ)

**Interfaces:**
- Produces:
  - `public class TranscriptLine { public double Start; public double Duration; public string Text; }`
  - `public class VocabClip { public string Word, VideoId, Sentence, Channel, Source; public int StartMs, EndMs; }` (`Source` は `"channel"` / `"search"`)
  - `public static class VocabClipFinder`:
    - `const int MaxPerWord = 10, MaxPerVideo = 2, LeadMs = 1000;`
    - `static List<string> Tokens(string text)` — 小文字、`[a-z']` 以外で区切り、前後の `'` を取る
    - `static List<VocabClip> FindInVideo(string word, string videoId, string channel, string source, IList<TranscriptLine> lines)` — 単語 (空白を含めば連続した単語) が区切りの中に単語として出たら場面。1 動画 `MaxPerVideo` まで、出てきた順
    - `static List<VocabClip> Merge(IList<VocabClip> current, IList<VocabClip> found)` — `(VideoId, StartMs)` が同じものは足さない、1 動画 `MaxPerVideo`、全体 `MaxPerWord`

- [ ] **Step 1: 見本を書く** `finder_cases.json`: `[{ "name", "word", "lines": [{start, duration, text}], "expect": [{startMs, endMs, sentence}] }]`。最低限の見本: `calm` が `"I feel calm."` で 1 場面 / `"calmness helps"` で 0 場面 / `"Calm, please"` で 1 場面 / `"calm-looking"` で 1 場面 / `don't` が `"I don't know"` で 1 場面 / `start=0.4` で startMs=0 / `start=12.5, duration=2.0` で startMs=11500・endMs=14500 / 同じ動画に 3 回出ても 2 場面 / `look after` が `"look after it"` で 1 場面。文は自分で書いた短い文。
- [ ] **Step 2: 失敗するテストを書く** `VocabClipTests.cs` で見本を `System.Text.Json` で読み、`FindInVideo(case.word, "v1", "ch", "channel", lines)` が `expect` と同じ (件数・StartMs・EndMs・Sentence) を `Check`。`Merge` のテスト: 12 場面 (6 動画 × 2) を渡すと 10 件、同じ `(VideoId, StartMs)` を 2 回渡すと 1 件、1 動画 3 件は 2 件。
- [ ] **Step 3: 実行して失敗を確かめる** Run: `logictest.py`。Expected: `VocabClipFinder` が無いコンパイルエラー。
- [ ] **Step 4: 実装する**
- [ ] **Step 5: 実行して通る** Run: `logictest.py` → `ALL LOGIC TESTS PASSED`、`unitycheck.py` → `errors: 0`
- [ ] **Step 6: コミット** `git commit -- Assets/Script/CorrectionDiary/Vocab/VocabClipFinder.cs Tools~/clips/fixtures/finder_cases.json Tests~/LogicTests/VocabClipTests.cs Tests~/LogicTests/Program.cs` 「単語帳: 字幕から場面を作る規則」

### Task 2: Python の同じ規則

**Files:** Create: `Tools~/clips/clip_finder.py`、`Tools~/clips/test_build_clips.py`

**Interfaces:**
- Produces: `tokens(text: str) -> list[str]`、`find_in_video(word, video_id, channel, source, lines: list[dict]) -> list[dict]` (dict のキーは表の列名 `word, video_id, start_ms, end_ms, sentence, channel, source`)、`merge(current, found) -> list[dict]`。定数 `MAX_PER_WORD = 10`、`MAX_PER_VIDEO = 2`、`LEAD_MS = 1000`。

- [ ] **Step 1: 失敗するテスト** `test_build_clips.py` の `FinderCasesTest` が `fixtures/finder_cases.json` の全見本で `find_in_video` の `start_ms, end_ms, sentence` を照合。`merge` も Task 1 と同じ 3 つ。
- [ ] **Step 2: 実行して失敗** Run: `python -m unittest test_build_clips` → `ModuleNotFoundError: clip_finder`
- [ ] **Step 3: 実装** (C# と同じ規則。区切りは `re.split(r"[^a-z']+", text.lower())`)
- [ ] **Step 4: 通る** → `OK`
- [ ] **Step 5: コミット** 「単語帳: 場面を作る規則 (Python)」

### Task 3: Supabase の表と RPC

**Files:** Create: `WEB/supabase/vocab_clips.sql`、`WEB/supabase/tests/vocab_clips_test.sql`。書き換える前に WEB で `git status` と `git log -1` を見て、利用者の変更が無いか確かめる。

**Interfaces:**
- Produces:
  - 表 `public.vocab_clips` (設計書の列。一意 `(word, video_id, start_ms)`、`source in ('channel','search')`、`word` は小文字)。RLS: 直接の select/insert は誰にも許さない (RPC と service role だけ)
  - `get_vocab_clips(p_timezone text default null, p_words text[]) returns setof vocab_clips` (security definer、`auth.uid()` が null なら例外、`word = any(p_words)`、単語ごとに `id` 順で最大 10)
  - `add_vocab_clips(p_timezone text default null, p_clips jsonb) returns int` (security definer、ログイン必須、`source` は必ず `'search'` にして入れる、重なりは `on conflict do nothing`、1 回 30 行まで・1 語 10 場面を超える分は入れない。入れた行数を返す)
  - `p_timezone` は今の `RpcAsync` が必ず送るので受け取るだけ

- [ ] **Step 1: 失敗するテスト** `vocab_clips_test.sql` (`vocab_test.sql` と同じ書き方): ログインした人が `get` で読める / ログインしていないと例外 / `add` で `channel` と書いても `search` で入る / 同じ場面を 2 回 `add` しても 1 行 / 1 語に 12 場面足しても 10 行 / 表を直接 insert・delete できない / 2 回流しても壊れない。
- [ ] **Step 2: 失敗を確かめる** Run: `sqltest.py WEB/supabase/tests/vocab_clips_test.sql` → `vocab_clips` が無いエラー
- [ ] **Step 3: 実装** — **Step 4: 通る** → `ALL VOCAB_CLIPS TESTS PASSED`、`vocab_test.sql` も通る
- [ ] **Step 5: コミット** (WEB で) 「単語帳: 動画の場面の表 vocab_clips」。利用者に「Supabase の SQL Editor で `vocab_clips.sql` を流す」ことを最後の報告に書く。

### Task 4: パソコンのスクリプト `build_clips.py`

**Files:** Create: `Tools~/clips/build_clips.py`、`Tools~/clips/channels.json`、`Tools~/clips/requirements.txt` (`youtube-transcript-api>=1.2`、`requests`)、`Tools~/clips/README.md`、`Tools~/clips/.env.example`。Modify: `.gitignore` (`Tools~/clips/.env`、`Tools~/clips/.done.json`、`Tools~/clips/out/`)、`test_build_clips.py`

**Interfaces:**
- Consumes: Task 2 の `find_in_video`・`merge`。`Assets/Resources/CefrjWords.json` (A1〜B2 の単語)
- Produces (テストする純粋な関数):
  - `pick_transcript(transcripts) -> transcript | None` — 英語 (`en`, `en-*`) で人が付けたものを優先、無ければ自動字幕
  - `build_rows(words: set[str], videos: list[dict], current: dict[str, list]) -> dict[str, list[dict]]` — `videos` は `{video_id, channel, lines}`。単語ごとに `merge`
  - `upload_payload(rows) -> list[dict]` — 表の列だけ、`source="channel"`

- [ ] **Step 1: 失敗するテスト** `BuildRowsTest`: 2 本の動画・3 単語で期待どおりの行、すでに 10 場面ある単語には足さない。`PickTranscriptTest`: 人の `en-GB` と自動の `en` → 人の方、自動だけ → 自動、`ja` だけ → None。`UploadPayloadTest`: キーが 7 列だけで `source == "channel"`。
- [ ] **Step 2: 失敗** — **Step 3: 実装**
  - `main()`: `.env` から `YOUTUBE_API_KEY`・`SUPABASE_URL`・`SUPABASE_SERVICE_KEY`。`channels.json` の各チャンネルで `channels.list?part=contentDetails` → アップロードの再生リスト → `playlistItems.list` (50 本ずつ、`--max-videos` 既定 200)。`.done.json` にある動画は飛ばす
  - 字幕: `YouTubeTranscriptApi().list(video_id)` → `pick_transcript` → `fetch()`。例外はその動画を飛ばしてログ
  - 書き込み: `POST {SUPABASE_URL}/rest/v1/vocab_clips?on_conflict=word,video_id,start_ms`、ヘッダ `Prefer: resolution=ignore-duplicates`、service role キー。今の場面数は書き込む前に `GET /rest/v1/vocab_clips?select=word,video_id,start_ms` で読む
  - `--dry-run` で `out/clips.json` に書くだけ
  - `channels.json` の初期値: TED (`UCAuUUnT6oDeKwE6v1NGQxug`)、BBC Learning English (`UCHaHD477h-FeBbVh9Sh7syA`)
- [ ] **Step 4: 通る** → `OK`。`--dry-run` を 1 チャンネル 3 本で実際に動かして `out/clips.json` に場面が出ることを確かめる (キーがあれば。無ければ報告に書く)
- [ ] **Step 5: コミット** (`out/`・`.env`・`.done.json` は入れない) 「単語帳: チャンネルから場面の一覧を作るスクリプト」

### Task 5: スマホで字幕を取る `YouTubeTranscriptClient`

**Files:** Create: `Assets/Script/CorrectionDiary/YouTubeTranscriptClient.cs`、`Tests~/LogicTests/fixtures/player_response.json` (自分で書いた最小の形)、`Tests~/LogicTests/fixtures/timedtext.xml`。Modify: `VocabClipTests.cs`

**Interfaces:**
- Consumes: `HttpUtil.PostJsonAsync`・`GetAsync`、`TranscriptLine`
- Produces:
  - `public class YouTubeTranscriptClient { Task<List<TranscriptLine>> GetEnglishAsync(string videoId) }` — 取れなければ null (例外にしない。通信できないときだけ `HttpException` を外へ)
  - `internal static string PickTrackUrl(string playerJson)` — `captions.playerCaptionsTracklistRenderer.captionTracks` から英語の人の字幕、無ければ `kind == "asr"` の英語。`baseUrl` から `&fmt=...` を除く。無ければ null
  - `internal static List<TranscriptLine> ParseTimedText(string xml)` — `<text start dur>` を読み、`&amp;` などと改行を戻す
- 取り方 (`youtube-transcript-api` 1.x と同じ): `POST https://www.youtube.com/youtubei/v1/player` に `{"context":{"client":{"clientName":"ANDROID","clientVersion":"20.10.38"}},"videoId":"<id>"}` → `PickTrackUrl` → `GET` → `ParseTimedText`。定数にして 1 か所で変えられるようにする

- [ ] **Step 1: 失敗するテスト** `PickTrackUrl`: 人の `en` と自動の `en` → 人の URL / 自動だけ → 自動 / `ja` だけ → null / `captions` が無い → null / `&fmt=srv3` が取れる。`ParseTimedText`: 3 行の見本で Start・Duration・Text、`&#39;` → `'`、空の `<transcript/>` → 0 件。
- [ ] **Step 2: 失敗** — **Step 3: 実装** (JSON は `MiniJson.Parse` + `JsonGet.Str` で読む。XML は `System.Xml`) — **Step 4: 通る** (`logictest.py`、`unitycheck.py`)
- [ ] **Step 5: コミット** 「単語帳: スマホで YouTube の字幕を取る」

### Task 6: YouTube の単語検索と上限の見分け

**Files:** Modify: `Assets/Script/CorrectionDiary/YouTubeSearchClient.cs`、`ApiSecrets.cs` (`youtubeVocabApiKey`)、`ApiSecrets.example.json`、README のキーの表、`VocabClipTests.cs`

**Interfaces:**
- Produces:
  - `Task<List<Video>> SearchWordVideosAsync(string word)` — `search.list` (`type=video&maxResults=10&videoCaption=closedCaption&videoEmbeddable=true&relevanceLanguage=en&safeSearch=strict&q="<word>"`) → `videos.list?part=snippet` でチャンネル名。並びは検索の順のまま (シャッフルしない)
  - `public static bool IsQuotaExceeded(HttpException e)` — 403 かつ本文に `quotaExceeded` または `dailyLimitExceeded`
  - `ApiSecrets.Pick(secrets.youtubeVocabApiKey, secrets.youtubeApiKey)` で作る

- [ ] **Step 1: 失敗するテスト** `IsQuotaExceeded`: 403 + `"reason": "quotaExceeded"` → true / 403 + `forbidden` → false / 400 → false。
- [ ] **Step 2〜4:** 失敗 → 実装 → 通る
- [ ] **Step 5: コミット** 「単語帳: YouTube の単語検索とキー」

### Task 7: 1 日の回数 `VocabClipSearchLog`

**Files:** Create: `Assets/Script/CorrectionDiary/Vocab/VocabClipSearchLog.cs` (Unity に依存しない。保存は呼ぶ側)。Modify: `VocabClipTests.cs`

**Interfaces:**
- Produces: `public class VocabClipSearchLog { string Date; int Count; string BlockedDate; }` と
  - `const int DailyLimit = 30;`
  - `bool CanSearch(DateTime localNow)` — 日付が `Date` と違えば数え直し扱い。`BlockedDate == 今日` なら false
  - `void Used(DateTime localNow)` / `void Block(DateTime localNow)` (上限の返事が来た)
  - `string ToJson()` / `static VocabClipSearchLog FromJson(string)` (壊れた JSON は新しいもの)

- [ ] **Step 1: 失敗するテスト** 30 回 `Used` で `CanSearch` false / 翌日 true / `Block` した日は false・翌日 true / JSON の往復 / 壊れた JSON → 0 回。
- [ ] **Step 2〜4** — **Step 5: コミット** 「単語帳: 動画の検索の 1 日の回数」

### Task 8: Supabase の読み書き (partial)

**Files:** Modify: `SupabaseService.cs` (`public class` → `public partial class` の 1 行だけ)。Create: `Assets/Script/CorrectionDiary/SupabaseService.VocabClips.cs`。Modify: `VocabClipTests.cs`

**Interfaces:**
- Consumes: `RpcAsync(string function, string extraJsonFields)`、`HttpUtil.JsonEscape`
- Produces:
  - `Task<List<VocabClip>> GetVocabClipsAsync(IList<string> words)` — `get_vocab_clips`、`,"p_words":[...]`
  - `Task<int> AddVocabClipsAsync(IList<VocabClip> clips)` — `add_vocab_clips`、`,"p_clips":[...]`
  - `internal static string BuildClipsJson(IList<VocabClip> clips)` / `internal static string BuildWordsJson(IList<string> words)` / `internal static List<VocabClip> ParseClips(string json)` (`JsonUtility` ではなく `MiniJson.Parse` で読む。テストできるように)

- [ ] **Step 1: 失敗するテスト** `"` と改行を含む文がエスケープされる / 空 → `[]` / `ParseClips` が列名 (`video_id`, `start_ms` …) から読める / 往復で同じ。
- [ ] **Step 2〜4** — **Step 5: コミット** 「単語帳: vocab_clips の読み書き」

### Task 9: 端末の保存と場面を集める流れ `VocabClipSource`

**Files:** Create: `Assets/Script/CorrectionDiary/Vocab/VocabClipStore.cs`、`Assets/Script/CorrectionDiary/Vocab/VocabClipSource.cs`。Modify: `VocabClipTests.cs`

**Interfaces:**
- Consumes: Task 1・5・6・7・8
- Produces:
  - `public class VocabClipEntry { string Word; List<VocabClip> Clips; DateTime CheckedAt; }`、`VocabClipStore.Load(word) -> VocabClipEntry | null`、`Save(entry)`、`LoadLog()`、`SaveLog(log)` (`vocab/clips/`、`vocab/clip_search.json`)
  - `public static bool IsReady(VocabClipEntry e, DateTime nowUtc)` — 場面が 1 つ以上ある、または 0 場面で `CheckedAt` から 30 日以内
  - `public enum ClipStatus { Ok, None, Offline, Limit, NoKey }`
  - `public class VocabClipSource`:
    - コンストラクタは差し替えられる関数で受け取る (テストで通信を偽物にする): `Func<IList<string>, Task<List<VocabClip>>> getCloud`、`Func<IList<VocabClip>, Task> addCloud` (ログインしていなければ null)、`Func<string, Task<List<YouTubeSearchClient.Video>>> search` (キーが無ければ null)、`Func<string, Task<List<TranscriptLine>>> transcript`、`IVocabClipStorage storage` (`Load`/`Save`/`LoadLog`/`SaveLog`)、`Func<DateTime> now`
    - `Task<Dictionary<string, ClipStatus>> FillFromCloudAsync(IList<string> words)` — まとめて 1 回。見つかった単語は保存
    - `Task<(ClipStatus status, List<VocabClip> clips)> GetAsync(string word, bool allowSearch)` — 保存 → (無ければ) 雲 → (`allowSearch` かつ回数が残っていれば) B。**同じ単語を同時に頼まれたら、同じ Task を返す**
    - B: 検索 10 本 → 上から字幕 → `FindInVideo` → `Merge`、3 場面か 5 本で止める。0 場面なら 0 場面を保存。見つけたら `addCloud` (失敗はログだけ)
  - Unity の `VocabStore.Root` を使う実装 `VocabClipFileStorage : IVocabClipStorage`

- [ ] **Step 1: 失敗するテスト** (偽物の関数とメモリの storage で):
  - 保存にあれば通信しない
  - 雲にあれば B をしない・保存される
  - 雲に無く B で 2 本目から見つかる → 3 場面で止まり、5 本目まで字幕を取らない
  - 字幕が全部 null → `None`、0 場面が保存され、同じ日にもう一度頼んでも検索しない。31 日後は検索する
  - 検索が `quotaExceeded` → `Limit`、`log.BlockedDate` が今日
  - 30 回使ったあと → `Limit` (検索の関数を呼ばない)
  - 検索が `HttpException` (0, 通信なし) → `Offline`、何も保存しない
  - `addCloud` が null (ログインなし) → B の結果は保存だけで `Ok`
  - `search` が null (キー無し) で雲にも無い → `NoKey`
  - 同じ単語の `GetAsync` を 2 回同時 → 検索の関数は 1 回
- [ ] **Step 2〜4** — **Step 5: コミット** 「単語帳: 動画の場面を集める流れ」

### Task 10: 次に出る新しい単語を先に決める

**Files:** Modify: `Assets/Script/CorrectionDiary/Vocab/VocabQueue.cs`、`VocabStore.cs` (`LoadNextNew()` / `SaveNextNew(List<string>)`、`vocab/next_new.json`)、`VocabTodayView.cs` (`StartSessionAsync` の候補)。Modify: `Tests~/LogicTests/VocabTests.cs`

**Interfaces:**
- Produces: `VocabQueue.OrderCandidates(List<string> nextNew, List<string> randomCandidates, IEnumerable<string> deckWords) -> List<string>` — `nextNew` のうち単語帳にまだ無いものを先に、そのあとランダムの候補 (重なりなし)
- `StartSessionAsync` は `NewCandidates` の代わりに `OrderCandidates(VocabStore.LoadNextNew(), NewCandidates(...), deck)` を使う。回を始めたら、使った単語を `next_new.json` から除く。`next_new.json` を作るのは Task 11

- [ ] **Step 1: 失敗するテスト** 先の単語が先頭 / 単語帳にある先の単語は除く / 重ならない / `nextNew` が空なら今までと同じ。
- [ ] **Step 2〜4** — **Step 5: コミット** 「単語帳: 次の新しい単語を先に決めて使う」

### Task 11: 20 単語先まで準備する `VocabClipPrefetcher`

**Files:** Create: `Assets/Script/CorrectionDiary/Vocab/VocabClipPrefetcher.cs`。Modify: `VocabClipTests.cs`

**Interfaces:**
- Consumes: Task 9 の `VocabClipSource`・`IsReady`、Task 10 の `next_new.json`、`VocabQueue.NewCandidates`
- Produces:
  - `const int Target = 20;`
  - `public static List<string> UpcomingWords(IList<string> sessionRemaining, IList<VocabCard> deck, IList<string> nextNew, DateTime nowUtc)` — 順番: 今の回の残り → 学習中 (`Status == Learning`) → `Known` で `DueAt` の早い順 → `nextNew`。重なりなし、`Target` 個まで
  - `public static List<string> TopUpNextNew(IList<string> nextNew, IList<string> randomCandidates, int need)` — `next_new.json` に足す単語
  - `public class VocabClipPrefetcher { void Kick(); bool IsRunning; }` — `Kick()` は動いていなければ 1 つだけ動かす。中で: 足りない新しい単語を `next_new.json` に足す → `UpcomingWords` → `IsReady` でないものを `FillFromCloudAsync` → まだのものを順に `GetAsync(word, allowSearch: true)`。`Limit`・`Offline`・`NoKey` で止める。終わったときにもう一度数えて足りなければ続ける (動いている間の `Kick` を取りこぼさない)

- [ ] **Step 1: 失敗するテスト**
  - `UpcomingWords` の順番と重なりなし、20 個で切れる
  - 準備済みが 20 個なら何も呼ばない / 1 個欠けたら 1 語だけ探す
  - `Kick` を 3 回続けて呼んでも、同時に動くのは 1 つ・同じ単語は 1 回
  - `Limit` が返ったらそれ以上探さない
  - `TopUpNextNew`: 足りない分だけ、単語帳と重ならない
- [ ] **Step 2〜4** — **Step 5: コミット** 「単語帳: 20 単語先まで動画の場面を準備する」

### Task 12: プレーヤーで秒数を指定して流す

**Files:** Modify: `Assets/Script/CorrectionDiary/YouTubePlayerView.cs`

**Interfaces:**
- Produces: `public void Show(string videoId, double startSeconds, double endSeconds)` — IFrame の `playerVars` に `start` (切り捨て)・`end` (切り上げ)、`rel: 0`。終わりで `OnEnded`。Editor では `Application.OpenURL("https://www.youtube.com/watch?v=<id>&t=<start>s")`。`public void Replay()` — 同じ範囲をもう一度 (`seekTo(start)` + `playVideo`)。今の `Show(videoId)` は変えない

- [ ] **Step 1: 実装** (ロジックのテストは無し。HTML を作る部分を `internal static string BuildPlayerVars(double start, double end)` に分けて `logictest.py` でテスト: `12.4, 15.2` → `start:12,end:16`、`end <= start` なら `end` を付けない)
- [ ] **Step 2:** `logictest.py`・`unitycheck.py` が通る
- [ ] **Step 3: コミット** 「YouTube のプレーヤーで秒数を指定して流す」

### Task 13: 動画の画面とカードの Videos ボタン

**Files:** Create: `Assets/Script/CorrectionDiary/Vocab/VocabVideosView.cs`。Modify: `VocabTodayView.cs` (Videos ボタン、カードと評価の行を隠す・戻す)、`VocabManager.cs` (部品の受け渡し、`VocabClipSource`・`VocabClipPrefetcher` を作る、評価・Vocab を開いたときに `Kick()`)

**Interfaces:**
- Consumes: Task 9・11・12
- Produces:
  - `VocabVideosView.View { GameObject Panel; RectTransform VideoArea; TMP_Text SentenceText; TMP_Text ChannelText; TMP_Text CountText; Button Replay, Next, Close; }`
  - `VocabVideosView.Open(VocabCard card, Action onClose)` / `void Pause()` (プレーヤーを隠す。`Open` し直すときは同じ場面を表示するだけで流さない) / `bool IsOpen`
  - `VocabTodayView.View` に `Button VideosButton` を足す
- 動き (設計書の「画面と操作」と「エラー」の表どおり): 保存にあれば待たない / 無ければ `Finding videos...`、B に進んだら `Searching YouTube...` / 文の中の単語を `<b>` に (大文字小文字は無視、`VocabClipFinder.Tokens` と同じ区切り) / `OnError` で自動で Next、全部だめなら `No videos for this word yet.` / Close でカードへ (`_flipped` は変えない) / `VocabTodayView.Pause()` から `VocabVideosView.Pause()` を呼ぶ
- [ ] **Step 1: 実装**。文を太字にする部分は `internal static string Highlight(string sentence, string word)` に分けてテスト: `"I feel Calm."` + `calm` → `"I feel <b>Calm</b>."`、`calmness` は太字にしない、`<` を含む文はエスケープ
- [ ] **Step 2:** `logictest.py`・`unitycheck.py`
- [ ] **Step 3: コミット** 「単語帳: 動画の画面と Videos ボタン」

### Task 14: 画面の組み立て

**Files:** Create: `Assets/Editor/VocabScreenBuilder.Videos.cs` (`VocabScreenBuilder` を partial にして、動画の画面と Videos ボタンを作る部分をここに)。Modify: `VocabScreenBuilder.cs` (partial にする、Sound / Say it の行に Videos を足す呼び出し、`VocabManager` への割り当て)

- [ ] **Step 1: 実装** — 動画の画面: 上に 16:9 の `VideoArea` (何も重ねない)、その下に文・チャンネル名と `3 / 7`、`Replay` / `Next` / `Close` の行。色と大きさは今の Vocab のボタンに合わせる (`VocabStyler`)。**Update Vocabulary Screen** でも足されるようにする
- [ ] **Step 2:** `unitycheck.py` → `errors: 0`。どのファイルも 600 行以内 (`wc -l`)
- [ ] **Step 3: コミット** 「単語帳: 動画の画面を組み立てる」

### Task 15: 説明を書いて、手で確かめる準備

**Files:** Modify: `docs/features.md` (Vocab に動画の場面)、`docs/vocabulary-next.md` (2 番を「実装済み・確認待ち」に、手で確かめる手順)、README (キーと `Tools~/clips` の使い方)

- [ ] **Step 1:** 書く。手で確かめる手順は設計書の「手で確かめること」の 4 つ + 「評価を続けても止まらない・準備が裏で進む (Console の `[VocabClips]` ログ)」+ 「Supabase の SQL Editor で `vocab_clips.sql` を流す」+ 「`Tools~/clips` で `pip install -r requirements.txt` → `.env` → `python build_clips.py`」
- [ ] **Step 2:** 全部のテストをもう一度: `unitycheck.py`、`logictest.py`、`sqltest.py` (vocab と vocab_clips)、`python -m unittest test_build_clips`
- [ ] **Step 3: コミット** 「docs: 単語帳の動画の場面の使い方と確認の手順」

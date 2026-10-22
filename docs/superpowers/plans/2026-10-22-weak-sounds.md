# 苦手な発音と「今日の発音練習」動画 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 発音チェックの判定と言えなかった単語から苦手な音を数えて Supabase に貯め、アプリ・Web に出し、1 日 1 回、苦手 1 位の音の英語ネイティブの練習動画を Discord に送る。

**Architecture:** SQL (`sounds.sql`) に記録の表と集計の関数を足す。アプリは純粋なロジック (音の種類・つづりからの推測・RPC の JSON・今日送るかの判断・Discord の文) を MonoBehaviour から分けてテストし、発音チェック (お題) と言い直し・リピーティングの終わりから保存と「今日の発音練習」を呼ぶ。動画は既存の `YouTubeSearchClient` で探す。

**Tech Stack:** Unity 2022.3.62f3 / C# 9、Gemini API (JSON スキーマ)、YouTube Data API v3、Supabase (PostgreSQL, PostgREST RPC, RLS)、素の HTML + ES modules。

**Spec:** `docs/superpowers/specs/2026-10-22-weak-sounds-design.md` (My project リポジトリ)

**リポジトリ:** Unity `C:\Users\haru0\club\class\es\My project` (`UNITY/`)、Web + SQL `C:\Users\haru0\club\class\es\database_learnApp` (`WEB/`)。**コミットは必ず `git commit -- <自分が変えたファイル>`**。push は利用者。

## Global Constraints

- 音の種類 (固定 10): `th, r_l, v_b, f_h, s_sh, vowel, long_short, final_consonant, extra_vowel, stress` + `other` (Gemini の判定だけ)。表示・例・検索語はスペックの表のとおり。
- `other` の名前のそろえ方: 小文字 → カッコとその中身を取る → 語 `sound` / `sounds` を取る → 英字・数字・空白・`/` `-` だけ → 空白 1 つ → 前後を取る → 30 文字まで。空なら保存しない。検索語 `how to pronounce <名前> sound English pronunciation`。
- 重み: Gemini の判定 3、つづりからの推測 1。source: `check` / `read_aloud` / `repeat`。
- 推測の規則と除外語はスペック §1 のとおり (3 文字未満と `the this that these those there they then than with of for from` は数えない)。推測で作るのは `th v_b f_h r_l s_sh final_consonant` だけ。
- 保存: 1 回 20 件まで、word 40 文字まで、90 日・1 人 500 件まで。連続記録・カレンダーには数えない。
- 集計: `get_my_weak_sounds(p_days 30)` → `sound, label, points, previous_points (45 日超は null), words (新しい順・重なりなし 最大 5)`、points の多い順。
- 今日の発音練習: 1 日 1 回 (夜 3 時区切り)、1 日 2 回失敗したらその日はやめる、1 位の points ≥ 3 のときだけ、`SearchShortVideosAsync(query, 240, 1200)`、直近 30 日に送った動画は避ける (全部送っていたら一番上)、発音用の Discord (`pronunciationDiscordWebhookUrl` / `pronunciationDiscordChannelId`)。ログインなし・Discord なし・YouTube キーなしは何もしない (失敗に数えない)。
- 画面は待たない。UI 文字: アプリ英語、Web 日本語。Web は textContent。
- Gemini の使用回数は増やさない (発音チェックの返事に `sounds` を足すだけ)。

## Review Focus

1. Gemini が `sounds` を返さない・空・知らないコード・`other` で名前が空 → アドバイスはそのまま出て、知らないものは保存しない (Task 2・Task 3)。
2. 言えなかった単語が全部機能語・3 文字未満・推測できない語 → 何も保存しない (呼び出しもしない) (Task 2・Task 4)。
3. 同じ日に言い直し・発音チェック・リピーティングが次々終わる → 送るのは 1 回だけ (送っている最中に次が来ても 2 回送らない) (Task 3・Task 4)。
4. YouTube で 1 本も見つからない・全部 30 日以内に送った → 見つからないなら送らずに失敗 1 回、全部送った済みなら一番上 (Task 3)。
5. 古い Supabase (`sounds.sql` を実行していない) → 保存・集計の失敗は Console に出すだけで、練習は止めない (Task 4)。

---

### Task 1: SQL — 発音の記録と集計

**Files:** Create `WEB/supabase/sounds.sql`、`WEB/supabase/tests/sounds_test.sql`。Modify `WEB/README.md` (実行順に `sounds.sql`、表に 1 行)。

**Interfaces (Produces, `authenticated` のみ):**
- `save_pronunciation_issues(p_timezone text, p_issues jsonb, p_taken_at timestamptz default null) returns int`
- `get_my_weak_sounds(p_days int default 30, p_timezone text default 'Asia/Tokyo')` → `sound text, label text, points int, previous_points int, words jsonb`
- 内部: `_sound_label(p_name text) returns text` (名前のそろえ方。SQL 版)

- [ ] **Step 1: テスト** (`\i stub.sql` → schema → summary → topic → repeat → sounds): `_sound_label('Z sound (zoo)') = 'z'`、`_sound_label('The "W" Sound') = 'the w'`、`_sound_label('NG sounds') = 'ng'`、`_sound_label('(x)') = ''` / 25 件 → 20 / 知らない sound と空の名前の other は飛ばす / th の label は空 / weight 2 → 1 / word 50 文字 → 40 / source `x` → `repeat` / th×3(重み 3)・r_l×2(重み 1)・other `z`×1(3)・other `ng`×1(1) → 1 行目 `th, '', 9`、other は名前ごとに別の行 / 35 日前の行 (postgres で直接) → 30 日の `previous_points` に数える、90 日は null / words は新しい順・重なりなし 最大 5 / 95 日前の行は次の保存で消える / 510 件 → 500 / B には 0 行 / 直接 insert は `insufficient_privilege` / もう一度全部 `\i` しても壊れない → `ALL SOUNDS TESTS PASSED`
- [ ] **Step 2: 失敗を確認** (`runsql2.sh sounds_test.sql` → `sounds.sql` が無い)
- [ ] **Step 3: 実装** (`repeat.sql` と同じ作り: RLS、SECURITY DEFINER、`_practice_date`)
- [ ] **Step 4: 通る** + `repeat_test.sql`・`topic_test.sql` も PASS
- [ ] **Step 5: Commit** (パス指定)

### Task 2: Unity — 音の種類・つづりからの推測・RPC の JSON

**Files:** Create `UNITY/Assets/Script/CorrectionDiary/Sounds/SoundTypes.cs`、`SoundGuesser.cs`、`SoundRpc.cs`。Test: `Tests~/LogicTests/Program.cs` (+ `buildlib.sh` に追加)。

**Interfaces:**
- `SoundTypes`: `string[] All` (10)、`bool IsKnown(string code)` (10 + other)、`string Label(string code, string otherName)` (アプリ表示、other は `Other: <name>`)、`string Example(string code)`、`string SearchQuery(string code, string otherName)`、`string NormalizeOtherName(string name)`
- `public class SoundHit { string Code; string Label; string Word; }`
- `SoundGuesser.Guess(string word) : List<string>` (コード、重なりなし)、`SoundGuesser.FromMissedWords(IEnumerable<string> words) : List<SoundHit>` (除外語を落とし、推測できた語ごとに)、`SoundGuesser.Top(IEnumerable<SoundHit> hits, int n = 3) : List<string>` (多い順のコード)
- `public class WeakSound { string Sound; string Label; int Points; int? PreviousPoints; List<string> Words; }`
- `SoundRpc.IssuesJson(IList<SoundHit> hits, int weight, string source) : string`、`SoundRpc.ParseWeakSounds(string body) : List<WeakSound>` (MiniJson)

- [ ] **Step 1: テスト**: All が 10 個・`IsKnown("other")`・`IsKnown("x") == false` / `NormalizeOtherName` の 4 例 (SQL と同じ) / 検索語 (`th` はスペックの文、other `z` → `how to pronounce z sound English pronunciation`) / Guess: `think→[th]`、`very→[v_b]`、`phone→[f_h]`、`right→[r_l]`、`play→[r_l]`、`tree→[r_l]`、`car→[]`、`she→[s_sh]`、`sit→[s_sh]`、`walked→[final_consonant]`、`hand→[final_consonant]`、`three→[th]` (thr は r_l の規則に入らない) / FromMissedWords(`the, this, of, think, cat, Play!`) → `think→th`、`play→r_l` の 2 件 (句読点・大文字を無視) / Top / IssuesJson のキーと値 (`label` は other だけ) / ParseWeakSounds (`previous_points` null、words)
- [ ] **Step 2: 失敗** → **Step 3: 実装** → **Step 4: 通る** → **Step 5: Commit**

### Task 3: Unity — 発音チェックの判定・今日送るかの判断・Discord の文

**Files:** Modify `GeminiApiClient.cs` (発音チェックのスキーマと戻り値)、`Talk3D/DiscordClient.cs`。Create `Sounds/DailySoundPlanner.cs`。Test: `Program.cs`。

**Interfaces:**
- `PronunciationSchema` に `sounds`: `ARRAY` of `{code: enum(10 + other), otherName: STRING}` (最大 2 個と指示)。プロンプトに 10 種類の説明と「当てはまらないときだけ other + 短い英語の名前」。
- `public class PronunciationFeedback { string Comment; List<SoundHit> Sounds; }`、`CheckPronunciationAsync(string expectedText, byte[] wav) : Task<PronunciationFeedback>` (知らないコードと空の other は落とす。Word はその文の中で `SoundGuesser.Guess` が同じコードになる最初の語、無ければ空)
- `DailySoundPlanner.ShouldTry(DateTime lastSentLocal, DateTime lastFailLocal, int failsToday, DateTime nowLocal) : bool` (夜 3 時区切り)、`DailySoundPlanner.PickVideo(IList<YouTubeSearchClient.Video> found, IDictionary<string, DateTime> sentAt, DateTime nowUtc) : Video` (30 日以内を避ける、全部なら一番上、無ければ null)
- `DiscordClient.SoundPracticeMessage(DateTime date, WeakSound top, YouTubeSearchClient.Video video) : string` (スペックの形)、`SendSoundPracticeAsync(...)`

- [ ] **Step 1: テスト**: スキーマが JSON で code の enum = `SoundTypes.All + other` / ShouldTry (今日送った → false、昨日 23:00 に送り今日 03:00 → true、今日 2 回失敗 → false) / PickVideo (30 日以内を避ける、全部送った済み → 一番上、空 → null) / メッセージ (見出し、`Your weakest sound: TH (think / this) — 9 points in 30 days`、other は `Other: z`、words、タイトル (チャンネル)、URL)
- [ ] **Step 2〜5** (失敗 → 実装 → 通る → Commit)

### Task 4: Unity — 保存と表示のつなぎ込み・今日の発音練習

**Files:** Modify `SupabaseService.cs`、`TopicPracticeManager.cs`、`Repeat/RepeatPracticeManager.cs`。Create `Sounds/DailySoundPractice.cs`。

**Interfaces:**
- `SupabaseService.SavePronunciationIssuesAsync(IList<SoundHit> hits, int weight, string source, DateTime takenAtUtc) : Task` (空なら呼ばない、失敗は Console)
- `SupabaseService.GetWeakSoundsAsync(int days = 30) : Task<List<WeakSound>>` (失敗は空)
- `DailySoundPractice.TryAsync(SupabaseService supabase, string youtubeApiKey, DiscordClient pronDiscord) : Task` (static。PlayerPrefs: `sound_daily_sent_at`、`sound_daily_fail_at`、`sound_daily_fails`、`sound_daily_videos` (id → 送った日時)。実行中フラグで二重送信を防ぐ)
- お題: 発音チェックの結果に `Sound: <Label>, ...` を足し、`SavePronunciationIssuesAsync(sounds, 3, "check")`。言い直しの 1 文の結果で、一致しなかった語から `FromMissedWords` → `SavePronunciationIssuesAsync(hits, 1, "read_aloud")`。どちらのあとも `DailySoundPractice.TryAsync`。
- リピーティング: 各文の結果で一致しなかった語を集め、まとめで `Watch out: <Top 3 の Label>` を出し、`SavePronunciationIssuesAsync(hits, 1, "repeat")` → `DailySoundPractice.TryAsync`。発音用の DiscordClient は `ApiSecrets.pronunciationDiscord*` から作る。
- [ ] **Step 1: 実装** → **Step 2: Unity コンパイル 0 エラー・ロジックテスト PASS** → **Step 3: Commit** + Ruling (MonoBehaviour、実機で確認)

### Task 5: Web — 「苦手な発音」の欄

**Files:** Modify `WEB/index.html`、`WEB/src/streak-client.js`、`WEB/tests/web_test.py`。

**Interfaces:** `getWeakSounds(days)` → `[{sound, label, points, previousPoints, words}]`。「よくあるミス」ページの `#mis-list` の下に `<section id="snd-sec">` (`h3` 「苦手な発音」、`#snd-list .snd-row`)。期間ボタンで一緒に読み直す。表示名は `SOUND_LABELS` (スペックの Web の表示)、other は `その他: <label>`。

- [ ] **Step 1: テスト**: 行数・並び・other の表示・矢印 (`previous_points` null → 無し)・words・期間ボタンで `get_my_weak_sounds` が p_days 7/30/90 で呼ばれる・HTML として入らない・空のときの文
- [ ] **Step 2〜5**

### Task 6: 仕上げ

- [ ] READMEs (Unity: 苦手な発音と今日の発音練習、YouTube キーと発音用 Discord が必要)、全部のテスト、最後のレビュー、利用者への手順 (Supabase で `sounds.sql` を実行、APK、push)。

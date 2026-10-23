# YouTube シャドーイング Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repeat 画面のシャドーイングを、BBC の 5 文ずつから「YouTube の動画をタイトルで検索 → アプリの中で流しながら最後まで録音 → 文ごとの判定と Gemini のアドバイス」に置き換える。

**Architecture:** 純粋なロジック (お手本の読み取りと途中までの文、WAV の分割、検索結果の絞り込み、Gemini のスキーマ、Discord の文) をテストし、再生 (`YouTubePlayerView` に「再生が始まった」を足す)・検索欄 (`ShadowUi`)・流れ (`ShadowSession`)・お手本の保存 (`ShadowScriptStore`) を Unity 側に置く。BBC 用の部品は消す。

**Tech Stack:** Unity 2022.3.62f3 / C# 9、gree/unity-webview (YouTube IFrame API)、YouTube Data API v3、Gemini API (YouTube URL・音声 + JSON スキーマ)、Supabase、素の HTML。

**Spec:** `docs/superpowers/specs/2026-10-23-youtube-shadowing-design.md`

**リポジトリ:** `UNITY/` = My project、`WEB/` = database_learnApp。コミットは `git commit -- <自分が変えたファイル>`。push は利用者。

## Global Constraints

- 動画: 1〜15 分 (60〜900 秒)、候補は最大 5 件 (検索の順のまま)、埋め込みできるものだけ。表示「タイトル — チャンネル (m:ss)」。
- 録音: 動画の再生が始まったとき (IFrame の PLAYING) に開始、ENDED の 2 秒後か Stop で終了、上限は動画の長さ + 30 秒。
- 書き起こし: 録音を 180 秒ずつに分ける。お手本の全文は 6000 文字まで手がかりとして渡す。
- お手本: 1 文 40 語まで (超えた文は捨てる)、全体 1500 語まで、使える文 0 → 失敗。`persistentDataPath/shadow_scripts/<動画ID>.json`、最大 20 本。
- 途中で止めた: 録音秒数 ÷ 動画秒数 の割合までの文 + 1 文。
- アドバイス: 日本語で 3 つほどの箇条書き。失敗しても「Advice: not available」でまとめは出す。
- 保存: 1 回 = 1 行 (mode `shadow`、source `youtube`、voice `human`、replays 0、accuracy = 平均、英文・言った内容は空)。Discord に タイトル・URL・平均・アドバイス・文ごとの ✅/⚠️。
- 表示文 (英語): 「Preparing the script (first time only)...」「Tap ▶ on the video. Recording starts with the video.」「Shadowing... m:ss / m:ss」「Checking...」「No videos found (1-15 min). Try other words.」「YouTube API key is not set.」「Could not play the video in the app.」「I couldn't hear you. Tap Start to try again.」「Advice: not available」
- 消す: BbcFeed / ShadowEpisodeStore / ShadowChunker / ShadowFailure / MakeSegment / SegmentEpisodeAsync と切り出しのスキーマ / Slow ボタン / BBC の表示。起動時に `persistentDataPath/shadow/` と PlayerPrefs `shadow_progress_v1`・`shadow_slow_on` を消す。
- テストは架空の文だけ。動画の文字起こし・録音をプロジェクトのフォルダに作らない。

## Review Focus

1. 再生が始まらない (WebView が無い・エディタ・動画が埋め込み禁止・ネットが切れた) → 録音は始めず、Stop か「Could not play the video in the app.」で Idle (Task 3)。
2. 15 分の録音のメモリと送信: 録音の WAV (約 30 MB) を 180 秒ずつに分けて送る。1 つ失敗したら判定全体を失敗にして保存しない (Task 2 の `WavSplitter`、Task 3)。
3. 再生中に動画を一時停止した・広告が入った → 録音は続け、上限 (長さ + 30 秒) で止めて判定 (Task 3)。
4. 検索の結果が 0 / キーが無い / 通信なし → メッセージを出して Idle、選んである動画はそのまま (Task 3)。
5. 古いバージョンの BBC の練習の途中 (PlayerPrefs・MP3) が残っている → 起動時に消して、新しい流れだけになる (Task 3)。

---

### Task 1: SQL — source `youtube`

**Files:** Modify `WEB/supabase/repeat.sql` (source の check と保存関数に `youtube`)、`WEB/supabase/tests/repeat_test.sql`。

- [ ] Step 1: テスト: `{"accuracy":72,"source":"youtube","mode":"shadow"}` が source `youtube` で保存され、英文は空 → Step 2: 失敗を見る → Step 3: 実装 → Step 4: `runsql2.sh repeat_test.sql` と topic_test が通る → Step 5: Commit

### Task 2: Unity — 純粋なロジック

**Files:** Create `Shadow/ShadowScript.cs`、`Shadow/WavSplitter.cs`。Modify `YouTubeSearchClient.cs` (純粋な `PickTitleResults` と `FormatDuration`)、`GeminiApiClient.cs` (3 つの関数とスキーマ、切り出しのものを消す)、`Talk3D/DiscordClient.cs` (`ShadowMessage` の新しい形)。Delete `Shadow/BbcFeed.cs`、`ShadowChunker.cs`、`ShadowFailure.cs` (+ テスト)。Test: `Program.cs`、`buildlib.sh`。

**Interfaces:**
- `ShadowScript`: `class Script { string VideoId; string Title; List<string> Sentences; }`、`List<string> Parse(string json)` (`{"sentences":[{"text"}]}`、規則は Global Constraints)、`List<string> SentencesUpTo(IList<string> sentences, double fraction)` (fraction ≥ 0.98 → 全部、それ以外は ceil(n × fraction) + 1 まで、最低 1)
- `WavSplitter.Split(byte[] wav, int seconds) : List<byte[]>` (16bit PCM の WAV を、正しいヘッダー付きの複数の WAV に分ける。最後が 1 秒未満なら前に足す)
- `YouTubeSearchClient.PickTitleResults(IList<Video> videos, int minSeconds, int maxSeconds, int limit) : List<Video>`、`FormatDuration(int seconds) : string` ("6:05")
- `GeminiApiClient`: `const string VideoScriptSchema`、`Task<string> TranscribeVideoAsync(string youtubeUrl)` (JSON をそのまま)、`const string ShadowTranscriptSchema` (`transcript` string、`notes` string 配列)、`Task<(string transcript, List<string> notes)> TranscribeShadowingAsync(byte[] wav, string referenceExcerpt)`、`Task<string> ShadowAdviceAsync(IList<string> notes, IList<string> missedWords)` (日本語の箇条書き)
- `DiscordClient.ShadowMessage(DateTime date, string videoTitle, string videoUrl, int average, string advice, IList<ShadowScorer.SentenceScore> scores) : string`、`SendShadowResultsAsync(同じ引数)` — 見出し「🎧 **Shadowing - yyyy-MM-dd HH:mm**」、タイトル、URL (`<URL>` で埋め込みを出さない)、`Average: n%`、`Advice:` と本文、文ごとの ✅/⚠️ (80% 境)

- [ ] Step 1: テスト (ShadowScript: 空・41 語を捨てる・1500 語まで・壊れた JSON / SentencesUpTo 0.5・1.0・0 / WavSplitter: 7 分の架空の音 → 3 つ、各ヘッダーのデータ長、0.5 秒の余りは前に足す / PickTitleResults: 59 秒・901 秒を除く、順番、5 件まで / FormatDuration / 3 つのスキーマが JSON、書き起こしに transcript と notes / Discord の文)
- [ ] Step 2: 失敗 → Step 3: 実装 (BBC の部品と古いテストを消す) → Step 4: `buildlib.sh` が通る、Unity コンパイル 0 エラー → Step 5: Commit

### Task 3: Unity — 検索・再生しながらの録音・判定・画面

**Files:** Create `Shadow/ShadowScriptStore.cs`。Modify `YouTubePlayerView.cs` (`OnPlaying`・`OnError`、`SetVideoArea(RectTransform)`)、`YouTubeSearchClient.cs` (`SearchVideosByTitleAsync(string query)`: `videoDuration` を付けず `type=video&videoEmbeddable=true&relevanceLanguage=en&maxResults=15` → videos.list → `PickTitleResults(…, 60, 900, 5)`)、`AudioRecorder.cs` (`MaxSeconds` を設定できるように。Repeat では 60 に戻す)、`Shadow/ShadowUi.cs` (Slow を消し、検索欄 `TMP_DefaultControls.CreateInputField`・Search・候補ボタン・動画の表示枠)、`Shadow/ShadowSession.cs` (作り直し)、`Repeat/RepeatPracticeManager.cs`、`Repeat/RepeatAudioPlayer.cs` (`MakeSegment` を消す)。

**Interfaces:**
- `ShadowScriptStore.GetAsync(GeminiApiClient gemini, YouTubeSearchClient.Video video, CancellationToken ct) : Task<List<string>>` (保存があれば使う、無ければ `TranscribeVideoAsync` → `ShadowScript.Parse` → 保存。結果は待っている間にタブを移っても保存する)、`DeleteOldBbcData()` (起動時)
- `ShadowUi`: `event Action<string> SearchRequested`、`ShowCandidates(IList<Video>)`、`event Action<Video> VideoChosen`、`RectTransform VideoArea`、`SetInteractable(bool)`、`ConfirmEarphonesAsync()` (今のまま)。選んだ動画は PlayerPrefs `shadow_video_v1` (JSON)。
- `ShadowSession`: 状態 `Idle, Searching, Preparing, WaitingToPlay, Shadowing, Checking, Summary`。Start → script → イヤホン → `player.Show(id)` → `OnPlaying` (最初の 1 回) で録音開始 → `OnEnded` + 2 秒 / Stop / 上限 → `WavSplitter.Split(wav, 180)` → 各 `TranscribeShadowingAsync` → `ShadowScorer.Score(SentencesUpTo(...), heard)` → `ShadowAdviceAsync` → まとめ・保存 (1 行)・Discord。WaitingToPlay で 20 秒 `OnPlaying` が来なければ「Could not play the video in the app.」。`Cancel()` で再生を隠し録音を捨てる。
- `RepeatPracticeManager`: `YouTubePlayerView` と `YouTubeSearchClient` (キーは ApiSecrets の youtubeApiKey) を用意して渡す。Slow の処理を消す。`OnApplicationPause` は今のまま。

- [ ] Step 1: 実装 → Step 2: Unity コンパイル (UnityEditor 込み、`-define:UNITY_ANDROID` で `YouTubePlayerView` も) 0 エラー・ロジックテスト PASS → Step 3: Commit + Ruling (実機で確かめるもの)

### Task 4: 文書と Web の説明

**Files:** Modify `UNITY/README.md` (シャドーイングの節を YouTube に)、`UNITY/docs/publishing-notes.md` (公開してよい・載せないもの)、`UNITY/docs/superpowers/specs/2026-10-22-shadowing-design.md` (先頭に「置き換えられた」の一行)、`WEB/index.html` (使い方の説明、BBC の注記を消す)、`WEB/tests/web_test.py` (使い方に `YouTube` `Search` `Advice`)。

- [ ] Step 1: Web のテストを変えて失敗を見る → Step 2: 書き換え → Step 3: `web_test.py` が通る → Step 4: Commit

### Task 5: 仕上げ

- [ ] 全部のテスト、最後のレビュー (opus)、直す、利用者への手順 (Supabase で `repeat.sql`、APK、実機で: 検索・再生と同時の録音開始・最後まで・Stop・判定・アドバイス・My voice・Discord)。

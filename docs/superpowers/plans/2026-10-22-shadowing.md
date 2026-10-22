# シャドーイング (Repeat 画面のモード) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repeat 画面に Shadowing / Slow のスイッチを足し、BBC 6 Minute English の続いた 5 文で「聞く → シャドーイング」を練習・判定・保存できるようにする。

**Architecture:** 純粋なロジック (フィードの読み取り・5 文への切り分け・照合・イヤホンの種類・メッセージ) をテストし、音声の取得と保存 (`ShadowEpisodeStore`) とシャドーイングの流れ (`ShadowSession`) を MonoBehaviour 側に置く。スイッチとポップアップは、利用者がもう作った Repeat 画面に**実行時に足す** (エディタツールを作り直さなくてよい)。SQL は `repeat_results` に `mode` を足すだけ。

**Tech Stack:** Unity 2022.3.62f3 / C# 9 (`System.Xml`)、Gemini API (音声 + JSON スキーマ)、Android `AudioManager`、Supabase、素の HTML。

**Spec:** `docs/superpowers/specs/2026-10-22-shadowing-design.md`

**リポジトリ:** `UNITY/` = My project、`WEB/` = database_learnApp。**コミットは `git commit -- <自分が変えたファイル>`**。push は利用者。

## Global Constraints

- 5 文、1 回目は聞くだけ (Skip あり)、カウント 3-2-1 (各 0.8 秒)、2 回目で録音しながら流す。文の間 0.5 秒。最後の文の 1.5 秒後に録音を止める。
- Slow = 0.75 倍 (`TimeStretch`)、1 回目・2 回目とも。
- 切り出し: 4〜20 語の文、前後 0.2 秒、5 文ずつ (最後の半端は 3 文以上なら 1 かたまり、2 文以下は捨てる)。
- 回: 最初は一番新しい回 → 終わったらまだ終わっていない次に新しい回。MP3 と切り出しの JSON は `persistentDataPath/shadow/` に最大 3 回分。
- 判定: `TranscribeAsync` 1 回 (キー `geminiTtsApiKey`)。書き起こしが空 → 0% にせず Idle。
- イヤホンの種類: 3, 4, 7, 8, 22, 23, 26。調べられないときは「付いている」。
- 保存: `repeat_results.mode` (`repeat`/`shadow`、既定 `repeat`)、source `bbc`、voice `human`、replays 0。言えなかった文は `mode='repeat'` だけから。カレンダーは `repeat`。
- 表示文 (英語): ポップアップ「Please use earphones for shadowing. If the phone speaker is used, the microphone hears it and the result will be wrong.」/「Start anyway」「Cancel」、見出し「Listen first (n / 5)」「Shadow (n / 5)」、クレジット「Audio: BBC Learning English — 6 Minute English (<title>). Personal study only.」、失敗「Could not prepare the BBC audio. Please try again later.」、空「I couldn't hear you. Tap Start to try again.」
- BBC の音声・JSON・録音はプロジェクトのフォルダに作らない。テストは架空のデータだけ。

## Review Focus

1. フィードや MP3 が取れない (通信なし・URL が変わった) → 「Could not prepare…」で Idle、リピーティングはそのまま使える (Task 3)。
2. Gemini の切り出しが時間を返さない・壊れた JSON・使える文が 3 未満 → その回を飛ばして次の回、全部だめなら失敗表示 (Task 2・Task 3)。
3. シャドーイング中のタブ移動・Stop・電話着信で再生が止まる → 録音を捨てて Idle、保存しない (Task 3)。
4. 2 回目の録音が 60 秒を超える (AudioRecorder の上限) → 5 文 + 間 + 1.5 秒が 55 秒を超えるかたまりは使わない (Task 2 の `ShadowChunker` で長さの上限)。
5. 古い Supabase (`mode` 列なし) で保存 → 失敗は一時保存に回し、練習は止めない (Task 3)。

---

### Task 0: 確認 — BBC のフィード

- [ ] WebSearch / WebFetch で 6 Minute English のポッドキャストの RSS の URL と、各回に MP3 (enclosure) があることを確かめ、`BbcFeed.FeedUrl` に入れる値を ledger に書く。取れなければ止めて利用者に相談 (B 案以外を検討)。実機での取得は Task 5 で利用者が確かめる。

### Task 1: SQL — `mode` 列

**Files:** Modify `WEB/supabase/repeat.sql`、`WEB/supabase/tests/repeat_test.sql`。

- [ ] **Step 1: テスト**: mode なし → `repeat`、`shadow` で保存できる、`x` → `repeat`、source `bbc` が保存できる、shadow で 50% の文が `get_my_repeat_sentences` の missed に出ない、`get_my_repeat_results` に `mode`、`repeat.sql` をもう一度実行しても壊れない (列が既にある)
- [ ] **Step 2: 失敗** → **Step 3: 実装** (`alter table ... add column if not exists mode text not null default 'repeat'`、check 制約を付け直し、source の check に `bbc`、関数を作り直し) → **Step 4: 通る** (+ topic_test) → **Step 5: Commit**

### Task 2: Unity — 純粋なロジック

**Files:** Create `UNITY/Assets/Script/CorrectionDiary/Shadow/BbcFeed.cs`、`ShadowChunker.cs`、`ShadowScorer.cs`、`EarphoneCheck.cs`。Modify `Repeat/RepeatResult.cs` (`mode`)、`Repeat/RepeatRpc.cs` (`mode` を JSON に)、`GeminiApiClient.cs` (切り出し)、`Talk3D/DiscordClient.cs`。Test: `Program.cs` (+ `buildlib.sh` に Shadow/*.cs)。

**Interfaces:**
- `BbcFeed`: `const string FeedUrl` (Task 0 の値)、`class Episode { string Id; string Title; DateTime Published; string AudioUrl; }`、`List<Episode> Parse(string xml)` (新しい順、enclosure が無い回は除く、壊れていたら空。Id は guid、無ければ URL のファイル名)
- `ShadowChunker`: `class Line { string Text; string Speaker; double Start; double End; }`、`List<List<Line>> Chunk(IList<Line> lines, double audioSeconds)` (おかしい時間・語数で落とし、5 文ずつ、半端の規則、1 かたまりの長さ (各文 + 0.4 秒の余裕 + 間 0.5 秒 × 4 + 1.5 秒) が 55 秒を超えたら使わない)、`(double start, double end) Window(Line l, double audioSeconds)` (前後 0.2 秒、0〜長さに収める)、`List<Line> ParseGemini(string json)` (MiniJson)
- `GeminiApiClient.SegmentEpisodeAsync(byte[] audio, string mimeType) : Task<string>` (JSON をそのまま返す)、`const string ShadowSegmentSchema`
- `ShadowScorer`: `class SentenceScore { string Text; List<ReadAloudMatcher.Word> Words; int Accuracy; }`、`(List<SentenceScore> scores, int average, bool empty) Score(IList<string> sentences, string heard)`
- `EarphoneCheck.IsEarphone(int deviceType) : bool`
- `RepeatResult.mode` (`"repeat"` / `"shadow"`、空なら JSON に `repeat`)
- `DiscordClient.ShadowMessage(DateTime date, string episodeTitle, IList<RepeatResult> results) : string`、`SendShadowResultsAsync(...)`

- [ ] **Step 1: テスト** (架空の RSS 2 回分・壊れた XML / 時間のおかしい文・3 語・21 語・7 文 → 5 + 2 (捨てる)・8 文 → 5 + 3・55 秒超え / Window の端 / 照合: 2 文目を言わなかった → その文 0%、他は 100% / 空 / イヤホンの番号 / スキーマが JSON / RPC に mode / Discord の見出し「🎧 **Shadowing - …**」と回のタイトル)
- [ ] **Step 2〜5** (失敗 → 実装 → 通る → Commit)

### Task 3: Unity — 取得・保存・シャドーイングの流れ・画面

**Files:** Create `Shadow/ShadowEpisodeStore.cs` (MonoBehaviour なし、async static でもよい)、`Shadow/ShadowSession.cs`、`Shadow/ShadowUi.cs`。Modify `Repeat/RepeatPracticeManager.cs`、`Repeat/RepeatAudioPlayer.cs` (切り出した AudioClip を流す)、`SupabaseService.cs` (変更なし、mode は RepeatResult に入る)。

**Interfaces:**
- `ShadowEpisodeStore.NextChunkAsync(GeminiApiClient gemini) : Task<(BbcFeed.Episode ep, AudioClip full, List<ShadowChunker.Line> lines)>` (フィード → 進み具合 → MP3 を保存/読み込み → JSON が無ければ切り出し → 次の 5 文。PlayerPrefs `shadow_progress_v1`。最大 3 回分を残して古いものを消す。だめな回は飛ばす、全部だめなら例外)、`MarkDone(ep, chunkIndex)`
- `RepeatAudioPlayer.PlaySegmentAsync(AudioClip full, double start, double end, bool slow, CancellationToken ct)` (区間を切り出した AudioClip を作って流す)
- `ShadowUi.Attach(RepeatPracticeManager owner)`: Repeat 画面の Root の先頭に「Shadowing: On/Off」「Slow: On/Off」のボタン (mainButton を複製して作る) と、全画面のポップアップ (半透明の背景 + 文 + 2 ボタン) を実行時に作る。`Task<bool> ConfirmEarphonesAsync()`
- `EarphoneCheck.HasEarphones()` (Android: `AudioManager.getDevices(2)` を `IsEarphone` で調べる、例外・エディタは true)
- `ShadowSession`: 状態 `Preparing, ListenFirst, Countdown, Shadowing, Checking, Summary`、まとめで「My voice」(録音の WAV を AudioClip に) と「Original」。保存は `SupabaseService.SaveRepeatResultsAsync` (mode=shadow, source=bbc, voice=human) / オフラインは `PendingRepeatStore`、Discord は `SendShadowResultsAsync`、`DiaryManager.CountOtherPracticeAsStudyDay` / `RefreshStreakFromCloud`
- `RepeatPracticeManager`: Shadowing がオンなら Start で `ShadowSession` を動かす。オフなら今まで通り。`OnDisable` で両方止める。

- [ ] **Step 1: 実装** → **Step 2: Unity コンパイル (UnityEditor 込み) 0 エラー・ロジックテスト PASS** → **Step 3: Commit** + Ruling (MonoBehaviour・ネットワーク・Android API、実機で確認)

### Task 4: Web — シャドーの印

**Files:** Modify `WEB/index.html`、`WEB/src/streak-client.js` (`mode` を返す)、`WEB/tests/web_test.py`。

- [ ] **Step 1: テスト**: shadow の行に「シャドー」の印 (`.rep-row .mode`)、苦手な文に shadow の文が入らない、統計は両方を数える
- [ ] **Step 2〜5**

### Task 5: 仕上げ

- [ ] README (Unity: シャドーイングの使い方、BBC は個人の学習用・APK を配らない、イヤホン)、全部のテスト、最後のレビュー、利用者への手順 (Supabase で `repeat.sql` を実行し直す、APK、実機で: フィードと MP3 が取れる・ポップアップ・1 回目 → 2 回目・判定・My voice)。

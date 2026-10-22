# リピーティング練習 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unity アプリに「Repeat」画面を足し、Tatoeba の人の録音 (と自分の添削文を Gemini の声で) 1 文ずつ聞いて言い返す練習をできるようにし、結果を Supabase・Web・Discord に残す。

**Architecture:** サーバー (Supabase SQL `repeat.sql`) に結果の表と「出す文を選ぶ」関数を足す。アプリは純粋なロジック (5 文の組み立て・音声の変換・引き伸ばし・保存の上限) を MonoBehaviour から分けてテストし、画面はお題スピーキングと同じ作り (Manager + エディタの Builder) で作る。Tatoeba の文はスクリプトで 1,000 文選んで `Resources` に入れ、録音は必要なときにダウンロードして端末に保存する。

**Tech Stack:** Unity 2022.3.62f3 / C# 9 / TextMeshPro 3.0.7 / uGUI / `UnityWebRequestMultimedia` (MP3)、Gemini API (REST, 音声合成 `responseModalities: ["AUDIO"]`)、Supabase (PostgreSQL, PostgREST RPC, RLS)、素の HTML + ES modules、Python 3 (変換スクリプト)。

**Spec:** `docs/superpowers/specs/2026-10-22-repeat-practice-design.md` (My project リポジトリ)

**リポジトリ:**
- Unity: `C:\Users\haru0\club\class\es\My project` (以下 `UNITY/`)。未コミットの変更が多数あり、利用者が `git add` 済みのものもある。**コミットは必ず `git commit -- <自分が変えたファイル>` の形で、パスを指定する** (ほかの変更を巻き込まないため)。
- Web + SQL: `C:\Users\haru0\club\class\es\database_learnApp` (以下 `WEB/`)。同じくパス指定でコミット。
- push は利用者がする。

## Global Constraints

- UI の文字: アプリ = 英語のみ、Web = 日本語 (既存どおり)。
- 1 日の区切りは夜中 3:00 (`public.app_today()` / `_practice_date`、`DiaryStreakView.DayBoundaryHour = 3`)。
- 1 回の練習 = 5 文。内訳: 言えなかった文 最大 2 → 自分の添削文 最大 2 → 残りを Tatoeba。「言えなかった」= 一番新しい結果が 80% 未満。
- 自分の添削文の候補: 直近 30 日の `diary_entries.corrected_text`・`summary_results.improved_summary`・`topic_results.corrected` を `.` `!` `?` の後の空白で区切ったもの。4〜25 語、300 文字以内。直近 7 日に練習した文は出さない。
- Tatoeba の文: 6〜15 語、120 文字以内、`"` `(` を含まない、ライセンス欄が空でない。1,000 文。録音 URL `https://tatoeba.org/audio/download/<audioId>`。
- 声: Tatoeba = 人の録音、自分の文 = Gemini `gemini-3.8-flash-tts` の声 `Leda`、失敗時 = 端末の読み上げ `AndroidTts("en","US")`。ゆっくり = アプリで 0.75 倍に引き伸ばす (声の高さは変えない)。
- Gemini のキー: `geminiTtsApiKey` (空なら `geminiApiKey`)。429 なら 日記 → 要約 → お題 のキーを借りる。上限に達した日は (夜 3 時区切りで) Gemini の読み上げを呼ばない。ほかの画面の借りる順の最後に `geminiTtsApiKey` を足す。
- 音声の保存: `Application.persistentDataPath/repeat_audio/`、最大 300 個・60 MB、古いもの (最終使用が古い順) から消す。
- `repeat_results`: 1 人 500 件まで。`save_repeat_results` は 1 回 10 件まで。`sentence` 1〜300 文字、`heard` 0〜500 文字、`accuracy` 0〜100、`replays` 0〜99。`source` ∈ `diary, summary, topic, tatoeba` (知らない値 → `tatoeba`)、`voice` ∈ `human, gemini, device` (知らない値 → `device`)。
- `activity_days.kind` は `diary, summary, topic, repeat` の 4 種類。`schema.sql`・`topic.sql`・`repeat.sql` のどれを後から実行しても、この 4 種類の制約になる。実行順 `schema.sql` → `summary.sql` → `topic.sql` → `repeat.sql`。
- カレンダー: diary=オレンジ `#ff922b`、summary=青 `#4dabf7`、topic=緑 `#40c057`、repeat=ピンク `#f06595`、2 種類=銅 `#c27c3e`、3 種類=銀 `#a3acb9`、4 種類=金 `#fcc419`。紫 (`both`) はなくす。
- 書き込みは SECURITY DEFINER の関数経由だけ。表は本人の行の select だけ。SQL は何度実行しても壊れない。
- Web で文章を入れるときは `textContent`。
- Tatoeba の表示: Repeat 画面の下に `Sentences & audio: Tatoeba (tatoeba.org) — voice: <author> (<license>)`。個人の学習用 (営利目的でない) でだけ使う。
- Discord: `repeatDiscordWebhookUrl` / `repeatDiscordChannelId`。5 文で 1 メッセージ、80% 以上 ✅・未満 ⚠️、2000 文字を超えたら分ける。

## Review Focus

1. `RepeatSentences.json` がまだ無い・空 (利用者が Tatoeba のファイルを置く前) → 自分の添削文と言えなかった文だけで練習でき、足りない分は出さずに「Add Tatoeba sentences (see README)」と出す。何も無ければ Start で同じ案内を出して Idle (Task 3・Task 6)。
2. 録音のダウンロードに失敗 (通信なし・404・MP3 が読めない) → その文だけ端末の読み上げで流し、voice=`device` で記録して練習を続ける (Task 6)。
3. 音声認識が何も返さない・Speak 中にタブ移動 → 0% にしない / 練習を中断して Idle、途中の文は保存しない (Task 6)。
4. 添削文の中に `Mr.` `e.g.` `3.5` や改行・箇条書き (`- `) がある → 文の途中で切れた短すぎる断片は語数の条件で落ち、`- ` は取り除かれる (Task 1)。
5. ログインしていない間の結果を後から送る → 元の日付で保存され、その日が repeat として数えられる (Task 1・Task 4)。

---

### Task 1: SQL — リピーティングの表と関数、カレンダーの 4 種類

**Files:**
- Modify: `WEB/supabase/schema.sql` (`activity_days` の check 制約を 4 種類に、`get_my_activity_days` に `repeat` 列)
- Modify: `WEB/supabase/topic.sql` (先頭の制約の付け直しを 4 種類に)
- Create: `WEB/supabase/repeat.sql`
- Test: `WEB/supabase/tests/repeat_test.sql` (既存の `tests/stub.sql` を使う)

**Interfaces:**
- Produces (RPC、`authenticated` のみ):
  - `save_repeat_results(p_timezone text, p_results jsonb, p_taken_at timestamptz default null) returns int`
    (`p_results` = `[{sentence, heard, accuracy, replays, source, voice, tatoeba_id, audio_id}]`)
  - `get_my_repeat_sentences(p_missed int default 2, p_own int default 2, p_timezone text default 'Asia/Tokyo')`
    → `sentence text, source text, missed boolean, tatoeba_id bigint, audio_id bigint`
  - `get_my_repeat_results(p_limit int default 200)` → `id, entry_date, created_at, sentence, heard, accuracy, replays, source, voice`
  - `get_my_activity_days(p_from, p_to)` → `day, diary, summary, topic, repeat`
- 内部: `_split_sentences(p_text text) returns setof text` (区切り・`- ` 除去・語数と長さの条件)、`_sentence_key(p_text text) returns text` (小文字・英数字と空白だけ・空白 1 つに)。

- [ ] **Step 1: テストを書く** — `tests/repeat_test.sql`: `\i stub.sql` → `schema.sql` → `summary.sql` → `topic.sql` → `repeat.sql`。ユーザー A で:
  - `_split_sentences('I went home. - She has two cats! Mr. Smith is here? ok.')` → `I went home.` は 3 語で除外、`She has two cats!` (4 語) を含む、`ok.` を含まない、先頭の `- ` が取れている
  - `save_repeat_results` に 12 件 → 戻り値 10、`accuracy` 150 → 100、`replays` -3 → 0、`source` `'web'` → `tatoeba`、`voice` `'robot'` → `device`、空の `sentence` は数えない
  - 520 件入れる (何回かに分けて) → `get_my_repeat_results(1000)` が 500 件
  - 言えなかった文: 同じ文 (`_sentence_key` が同じ、大文字違い) を 60% → 90% の順で保存 → `get_my_repeat_sentences` の `missed=true` に出ない。別の文を 70% で保存 → 出る (source・audio_id がもとのまま)
  - 自分の文: 今日の日記 `corrected_text` に 5 語の文を 3 つ → `p_own=2` で `missed=false` が 2 件、`source='diary'`。そのうち 1 文を `postgres` で 3 日前の `repeat_results` に入れる → その文は出ない。40 日前の `topic_results.corrected` の文は出ない
  - `save_repeat_results` (今日) → `get_my_streak` の `studied_today = true`、`get_my_activity_days` の今日が `repeat = true`
  - `p_taken_at` = 2 日前 → その日の `repeat = true`、連続記録の最後の日は変わらない
  - ユーザー B では `get_my_repeat_results` と `get_my_repeat_sentences` が 0 行
  - `repeat_results` への直接 insert は `insufficient_privilege`
  - 4 ファイルをもう一度 `\i`、さらに `topic.sql` だけもう一度 `\i` → エラーなし、`repeat` の行が残る

- [ ] **Step 2: 失敗を確認** — Run: `runsql.sh` を `repeat_test.sql` 用に (手元の PostgreSQL 16、新しい DB)。Expected: `repeat.sql` が無いので FAIL

- [ ] **Step 3: 実装** — `repeat.sql`: 表 (RLS・本人 select・直接書き込み不可)、`_practice_date` (topic.sql のもの) を使う、`activity_days` に `('repeat')` を入れて連続記録を更新する仕組みは `topic_counts_as_study` と同じ形、500 件を超えたら古い順に消す。`get_my_repeat_sentences`: 言えなかった文は `distinct on (_sentence_key(sentence))` で一番新しい結果を取り、`accuracy < 80` を新しい順に `p_missed` (0〜5) 件。自分の文は 3 つの表の直近 30 日を `_split_sentences` して、直近 7 日の `repeat_results` と言えなかった文と同じキーを除き、`order by random()` で `p_own` (0〜5) 件。

- [ ] **Step 4: 通ることを確認** — Expected: `ALL REPEAT TESTS PASSED`、さらに既存の `topic_test.sql` も PASS (4 種類の制約で壊れていない)

- [ ] **Step 5: Commit** — `git commit -- supabase/schema.sql supabase/topic.sql supabase/repeat.sql supabase/tests/repeat_test.sql` ("Add repeat practice: results table, sentence picker, repeat activity day")

---

### Task 2: Tatoeba の文を選ぶ変換スクリプト

**Files:**
- Create: `UNITY/Tools~/tatoeba/build_repeat_sentences.py`
- Create: `UNITY/Tools~/tatoeba/test_build_repeat_sentences.py` (+ 見本データ `UNITY/Tools~/tatoeba/fixtures/`)
- Create: `UNITY/Assets/Resources/RepeatSentences.json` (最初は `{"sentences":[]}`)
- Modify: `UNITY/.gitignore` (`Tools~/tatoeba/*.bz2`、`Tools~/tatoeba/*.tsv`、`Tools~/tatoeba/*.csv` を除外)

**Interfaces:**
- Produces: `RepeatSentences.json` = `{"sentences":[{"id":int,"audioId":int,"text":str,"author":str,"license":str}]}`
- CLI: `python3 build_repeat_sentences.py --sentences eng_sentences.tsv.bz2 --audio sentences_with_audio.tar.bz2 --out ../../Assets/Resources/RepeatSentences.json --count 1000 --seed 42`
- 関数: `select_sentences(sentence_rows, audio_rows, count, seed) -> list[dict]`

- [ ] **Step 1: テストを書く** (`unittest`、見本データは 30 行程度): ライセンスが空の録音は選ばない / 5 語・16 語・121 文字・`"`・`(` を含む文は選ばない / 同じ文字列の文は 1 回だけ / 同じ seed なら同じ結果、違う seed なら順番が変わる / count より候補が少なければ全部 / 録音が複数ある文は audio_id が一番小さいもの / 出力 JSON が上の形
- [ ] **Step 2: 失敗を確認** — Run: `python3 -m unittest Tools~/tatoeba/test_build_repeat_sentences.py`。Expected: ImportError
- [ ] **Step 3: 実装** — `bz2` と `tarfile` (標準ライブラリだけ)。語数は空白区切り、数字は 2 個以下。
- [ ] **Step 4: 通ることを確認** — Expected: OK
- [ ] **Step 5: Commit** — パス指定

---

### Task 3: Unity — 純粋なロジック (5 文の組み立て・音声・保存の上限)

**Files:**
- Create: `UNITY/Assets/Script/CorrectionDiary/Repeat/RepeatSentence.cs` (データ型)
- Create: `.../Repeat/RepeatSetBuilder.cs`
- Create: `.../Repeat/TatoebaCatalog.cs` (JSON の読み込み・選び方。`Resources.Load` は呼び出し側)
- Create: `.../Repeat/PcmAudio.cs` (PCM16 ⇔ float、WAV の作成)
- Create: `.../Repeat/TimeStretch.cs`
- Create: `.../Repeat/AudioCachePolicy.cs` (名前の作り方・消す順番)
- Test: `UNITY/Tests~/LogicTests/Program.cs` に追加

**Interfaces:**
- `public class RepeatSentence { string Text; string Source; bool Missed; long? TatoebaId; long? AudioId; string Author; string License; }`
- `public static class RepeatSetBuilder { List<RepeatSentence> Build(IList<RepeatSentence> missed, IList<RepeatSentence> own, IList<RepeatSentence> tatoeba, ISet<string> recentKeys, Random rng, int total = 5); static string Key(string text); }`
  - 並び: missed (最大 2) → 残りの own (最大 2) と tatoeba を混ぜる。`Key` が同じものは 1 回。`recentKeys` にある tatoeba は使わない (足りなければ使う)。
- `public static class TatoebaCatalog { List<RepeatSentence> Parse(string json); }` (壊れた JSON・空 → 空のリスト)
- `public static class PcmAudio { float[] Pcm16ToFloats(byte[] pcm); byte[] ToWav(float[] samples, int channels, int sampleRate); }`
- `public static class TimeStretch { float[] Stretch(float[] samples, int sampleRate, double speed); }` (WSOLA、`speed` 0.5〜1.0)
- `public static class AudioCachePolicy { string TatoebaName(long audioId); string GeminiName(string text, string voice); List<string> ToDelete(IList<(string name, long bytes, DateTime lastUsed)> files, int maxCount = 300, long maxBytes = 60L*1024*1024); }`

- [ ] **Step 1: テストを書く**:
  - Build: missed 3・own 3・tatoeba 10 → 5 件、先頭 2 件が missed、own は 2 件まで / tatoeba が空 → missed+own だけ (5 未満でもよい) / own の 1 文と tatoeba の 1 文が大文字・句読点違いで同じ → 1 回だけ / recentKeys の tatoeba は避ける / 同じ rng の種なら同じ並び
  - Parse: 正しい JSON → 件数・中身、`{}` や `not json` → 空
  - Pcm16ToFloats: `[0x00,0x80, 0xFF,0x7F]` → `[-1, ~0.99997]`; ToWav: 先頭が `RIFF`、サンプルレート・長さが合う
  - TimeStretch: 1 秒の 440Hz サイン波 (16 kHz) を 0.75 → 長さが 1/0.75 秒 ±2%、ゼロ交差の数から求めた周波数が 440Hz ±3%
  - GeminiName: 同じ文・声 → 同じ名前、文か声が違えば違う名前、ファイル名に使えない文字を含まない; ToDelete: 301 個 → 一番古い 1 個、60 MB 超え → 古い順に下回るまで
- [ ] **Step 2: 失敗を確認** — Run: `runlogic.sh` に新しいファイルを足して。Expected: 型が無いのでコンパイルエラー
- [ ] **Step 3: 実装** (どれも UnityEngine を使わない純粋な C#)
- [ ] **Step 4: 通ることを確認** — Expected: `ALL LOGIC TESTS PASSED`、Unity コンパイル 0 エラー
- [ ] **Step 5: Commit** — パス指定

---

### Task 4: Unity — Gemini の読み上げ、キー、クラウド保存、Discord

**Files:**
- Modify: `UNITY/Assets/Script/CorrectionDiary/GeminiApiClient.cs` (読み上げ)
- Modify: `.../ApiSecrets.cs`、`UNITY/ApiSecrets.example.json` (`geminiTtsApiKey`、`repeatDiscordWebhookUrl`、`repeatDiscordChannelId`)
- Modify: `.../DiaryManager.cs`、`.../SummaryPracticeManager.cs`、`.../TopicPracticeManager.cs` (借りるキーの最後に `geminiTtsApiKey`)
- Modify: `.../SupabaseService.cs` (保存・取得・ログイン時の送信)
- Create: `.../Repeat/PendingRepeatStore.cs` (`PendingTopicStore` と同じ作り、キー `repeat_pending_v1`)
- Create: `.../Repeat/TtsQuotaGate.cs` (上限に達した日を覚える。日付の判定は純粋な関数)
- Modify: `UNITY/Assets/Script/Talk3D/DiscordClient.cs`
- Test: `Tests~/LogicTests/Program.cs`

**Interfaces:**
- `GeminiApiClient.TtsRequestBody(string text, string voice) : string` (static) — `contents` に文、`generationConfig.responseModalities = ["AUDIO"]`、`speechConfig.voiceConfig.prebuiltVoiceConfig.voiceName = voice`
- `GeminiApiClient.SynthesizeSpeechAsync(string text, string voice, string ttsModel) : Task<byte[]>` — PCM16 24kHz モノラル (`inlineData.data` の base64)。429 はキーを替えて、それでも 429 なら `GeminiQuotaException`
- `TtsQuotaGate.IsBlocked(DateTime blockedAtLocal, DateTime nowLocal, int boundaryHour = 3) : bool` (static)、`TtsQuotaGate.BlockToday()` / `IsBlockedNow` (PlayerPrefs)
- `SupabaseService.SaveRepeatResultsAsync(IList<RepeatResult> results, DateTime takenAtUtc) : Task<int>`
- `SupabaseService.GetRepeatSentencesAsync(int missed = 2, int own = 2) : Task<List<RepeatSentence>>`
- `SupabaseService.UploadPendingRepeatResultsAsync() : Task<int>` (`ApplySession` から呼ぶ)
- `public class RepeatResult { string sentence, heard; int accuracy, replays; string source, voice; long tatoebaId, audioId; }` (0 = なし)
- `DiscordClient.RepeatMessage(DateTime date, IList<RepeatResult> results) : string` (static)、`SendRepeatResultsAsync(DateTime, IList<RepeatResult>) : Task`

- [ ] **Step 1: テストを書く**: TtsRequestBody が正しい JSON で、声の名前と `AUDIO` を含み、文の `"` がエスケープされる / IsBlocked: 上限が 10/22 23:00 → 10/23 02:59 は true、10/23 03:00 は false / RepeatMessage: 見出し `🔁 **Repeat practice - 2026-10-22 17:40**`、`Average: 84%`、80 以上 ✅・未満 ⚠️、⚠️ の行に `(heard: …)`、長いときは 2000 文字以内 (分割は既存の `Split`)
- [ ] **Step 2: 失敗を確認** — Expected: コンパイルエラー
- [ ] **Step 3: 実装** — 借りる順は `KeyList(ttsKey, [geminiApiKey, summaryKey, topicKey])`。`SaveRepeatResultsAsync` は `RpcAsync("save_repeat_results", …)` (RpcAsync が `p_timezone` を足す)。`GetRepeatSentencesAsync` は `p_missed`・`p_own` を送る。
- [ ] **Step 4: 通ることを確認** — `ALL LOGIC TESTS PASSED`、Unity コンパイル 0 エラー
- [ ] **Step 5: Commit** — パス指定

---

### Task 5: Unity — 音声を流す部品 (RepeatAudioPlayer)

**Files:**
- Create: `UNITY/Assets/Script/CorrectionDiary/Repeat/RepeatAudioPlayer.cs` (MonoBehaviour、`AudioSource` を持つ)

**Interfaces:**
- `Task<string> PrepareAsync(RepeatSentence s, CancellationToken ct)` — 音声を用意して保存 (キャッシュ) し、使う声 (`human`/`gemini`/`device`) を返す。先読みにも使う。
- `Task PlayAsync(RepeatSentence s, bool slow, CancellationToken ct)` — 流し終わるまで待つ。`slow` は `TimeStretch.Stretch(…, 0.75)` した `AudioClip` (作ったものは文ごとに覚えておく)。
- `void Stop()`
- 中身: Tatoeba は `UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG)` (保存済みならそのファイルを `file://` で)、Gemini は `SynthesizeSpeechAsync` → `PcmAudio.ToWav` で保存、`AudioClip.Create` で再生。失敗・`TtsQuotaGate.IsBlockedNow` なら `AndroidTts("en","US")` (ゆっくりは `SetVoiceStyle(1, 0.75)`)、エディタでは `EditorDummyTts`。保存のたびに `AudioCachePolicy.ToDelete` で古いものを消す。

- [ ] **Step 1: 実装** (MonoBehaviour なので自動テストはなし。Task 3・4 の純粋な部品だけを組み合わせる)
- [ ] **Step 2: Unity コンパイル** — Expected: 0 エラー
- [ ] **Step 3: Commit** — パス指定 + ledger に `Ruling: RepeatAudioPlayer has no automated test — MonoBehaviour + network; checked on device`

---

### Task 6: Unity — Repeat 画面のロジック (RepeatPracticeManager)

**Files:**
- Create: `UNITY/Assets/Script/CorrectionDiary/Repeat/RepeatPracticeManager.cs`
- Create: `UNITY/Assets/Script/CorrectionDiary/Repeat/RecentRepeatLog.cs` (直近 7 日に出した Tatoeba の文のキー、PlayerPrefs)

**Interfaces:**
- 状態: `Idle, Preparing, Listening, Ready, Speaking, Result, Summary`
- Inspector: ボタン (main, again, slow, speak, next, tryAgain)、テキスト (header, sentence, result, summary, credit, status)、`ttsModel = "gemini-3.8-flash-tts"`、`ttsVoice = "Leda"`、`speechLanguage = "en-US"`、`sendToDiscord = true`、`discordWebhookUrl`、`discordChannelId`
- 使う: `RepeatSetBuilder.Build`、`TatoebaCatalog.Parse(Resources.Load<TextAsset>("RepeatSentences").text)`、`SupabaseService.GetRepeatSentencesAsync`、`RepeatAudioPlayer`、`NativeSpeechRecognizer` + `AudioRecorder` (お題の言い直しと同じ聞き取り方。使えなければ録音して `TranscribeAsync`、キーは `geminiTtsApiKey`)、`ReadAloudMatcher.Match`、`SaveRepeatResultsAsync` / `PendingRepeatStore` / `SendRepeatResultsAsync`、日記画面の `RefreshStreakFromCloud()`
- 流れと表示はスペックの「画面 Repeat」のとおり。英文は Result まで出さない。Result の英文は枠で囲む (お題の `SetSentenceBox` と同じ作り)。credit は Tatoeba の文のときだけ出す。
- 次の文の `PrepareAsync` を、今の文の Ready に入ったところで始める。
- `OnDisable`: Summary 以外なら止めて Idle (途中の結果は捨てる)。
- 空の聞き取り → 0% にせず Ready に戻る。
- Tatoeba も自分の文も無いとき → `Add Tatoeba sentences (see README) or practice Diary / Topic first.` を出して Idle。

- [ ] **Step 1: 実装**
- [ ] **Step 2: Unity コンパイル** — 0 エラー、`runlogic.sh` も PASS のまま
- [ ] **Step 3: Commit** — パス指定 + Ruling (MonoBehaviour、実機で確認)

---

### Task 7: Unity — 画面を組み立てるエディタツールとタブ

**Files:**
- Create: `UNITY/Assets/Editor/RepeatScreenBuilder.cs` (メニュー `Tools/Repeat Practice/Create Repeat Screen`)

**Interfaces:**
- `TopicScreenBuilder` と同じ作り: 「repeat screen」(子に `RepeatPracticeManager`、`RepeatAudioPlayer`、`AudioSource`)、ボタン・テキストを Inspector の欄に入れる。タブ「Repeat」は Topic の右。ほかの画面へ切り替えるボタンに「repeat screen を隠す」を足し、**新しいタブには自分の画面を隠す呼び出しを写さない** (お題で直した不具合と同じ)。
- 同じシーンにもうある場合は作り直さずに案内を出す。

- [ ] **Step 1: 実装** - [ ] **Step 2: Unity コンパイル (UnityEditor 込み)** 0 エラー - [ ] **Step 3: Commit** パス指定 + Ruling (エディタツール、実機で確認)

---

### Task 8: Web — リピーティングのページとカレンダーの色

**Files:**
- Modify: `WEB/index.html`、`WEB/src/streak-client.js`、`WEB/tests/web_test.py`、`WEB/README.md` (実行順に `repeat.sql`)

**Interfaces:**
- `getRepeatResults(limit = 200)` → `[{id, entryDate, createdAt, sentence, heard, accuracy, replays, source, voice}]`
- `getActivityDays` の各日に `repeat` を足す
- ページ `#repeat` (メニューの「お題スピーキング」と「よくあるミス」の間、ラベル「リピーティング」)、id は `rep-*`:
  - `#rep-howto` (たたんだ使い方)、`#rep-stats` (練習した日数・平均・最新・最高)、`#rep-svg` (日ごとの平均の折れ線)、`#rep-weak` (苦手な文: 一番新しい結果が 80% 未満、文・最新の一致率・回数)、`#rep-list` (日ごとの結果)
- カレンダー: クラス `diary` `summary` `topic` `repeat` (1 種類)、`bronze` (2)、`silver` (3)、`gold` (4)。凡例 `日記 / 要約 / お題 / リピーティング / 2種類 / 3種類 / 4つ全部`。

- [ ] **Step 1: テストを書く** (`web_test.py`、今ある「紫 (`both`) が 1 日」「金 = 3 種類」の確認はこの新しい確認に置き換える): ダミーの活動日を 1・2・3・4 種類の日と repeat だけの日 → `.cal-day.repeat`=1、`.bronze`=1、`.silver`=1、`.gold`=1、`.both`=0 / 凡例に「リピーティング」「4つ全部」/ メニューに「リピーティング」/ `#repeat` ページだけが見える、統計・グラフ・苦手な文 (80% 未満だけ) が出る / 文に `<img onerror>` を入れても実行されない / `#rep-howto` が閉じた状態で、開くと `Start`・`Again`・`Slow`・`Speak`・`Next` を含む / ライトとダークでコンソールエラーなし
- [ ] **Step 2: 失敗を確認** — `python3 tests/web_test.py`。Expected: FAIL
- [ ] **Step 3: 実装**
- [ ] **Step 4: 通ることを確認** — `ALL WEB TESTS PASSED`
- [ ] **Step 5: Commit** — パス指定

---

### Task 9: 仕上げ — README と利用者の手順

**Files:**
- Modify: `UNITY/README.md` (Repeat の使い方、Tatoeba のファイルの取り方と変換コマンド、ライセンスの注意、`ApiSecrets.json` の新しい項目)

- [ ] **Step 1: 全部のテストをもう一度** — SQL (`topic_test.sql`・`repeat_test.sql`)、`runlogic.sh`、`python3 -m unittest`、`web_test.py`、Unity コンパイル。すべて PASS
- [ ] **Step 2: Commit** — パス指定
- [ ] **Step 3: 利用者への手順** (最後のメッセージ):
  1. Supabase で `schema.sql` → `summary.sql` → `topic.sql` → `repeat.sql`
  2. Tatoeba の 2 ファイルを `Tools~/tatoeba/` に置き、変換コマンド (私が実行してもよい)
  3. `ApiSecrets.json` に `geminiTtsApiKey` (別プロジェクト)、`repeatDiscordWebhookUrl`
  4. Unity で `Tools > Repeat Practice > Create Repeat Screen` → Ctrl+S → APK
  5. 実機で 1 回: 人の声が流れる (`https://tatoeba.org/audio/download/<id>` が使えるか)、自分の文が Leda の声、🐢 で声の高さが変わらない、Web にページが出る
  6. `database_learnApp` を push

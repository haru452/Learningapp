# お題スピーキング・言い直し・よくあるミス集計 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unity アプリに「お題スピーキング」画面 (言い直しチェック付き) を追加し、日記・要約・お題の添削で出たミスを Supabase に貯めて Web と採点で使う。

**Architecture:** サーバー (Supabase SQL) に結果・ミスの表と関数を足し、アプリは既存の要約練習 (`SummaryPracticeManager` + `SummaryScreenBuilder`) と同じ作りで新しい画面を作る。Gemini の添削・採点の返り値に「ミスの一覧」を足して保存する。Web はハンバーガーメニューに2ページを足し、カレンダーの色を増やす。

**Tech Stack:** Unity 2022.3.62f3 / C# 9 / TextMeshPro 3.0.7 / uGUI、Gemini API (REST, JSON schema)、Supabase (PostgreSQL 15+, PostgREST RPC, RLS)、素の HTML + ES modules (supabase-js v2)。

**Spec:** `docs/superpowers/specs/2026-10-21-topic-speaking-design.md` (My project リポジトリ)

**リポジトリ:**
- Unity: `C:\Users\haru0\club\class\es\My project` (以下 `UNITY/`)。未コミットの変更が多数あるので、**コミットは各タスクで作ったファイルだけ** `git add <path>` する。
- Web + SQL: `C:\Users\haru0\club\class\es\database_learnApp` (以下 `WEB/`)。

## Global Constraints

- UI の文字はアプリ = 英語のみ、Web = 日本語 (既存どおり)。
- 1日の区切りは夜中 3:00 (`public.app_today()`、`DiaryStreakView.DayBoundaryHour = 3`)。
- 点数: お題 = 内容 40 / 構成 30 / 言葉 30 (合計 100)。要約は変えない (50/25/25)。
- 話す時間: `daily` 45 秒、`exam`・`work` 60 秒。考える時間 15 秒 (スキップ可)。
- ミスの種類コードは固定 10 個: `article, tense, preposition, agreement, plural, verb_form, word_order, word_choice, missing_word, other`。句読点・大文字小文字はミスに数えない。
- 保存数: `topic_results` は1人 20 件、`mistakes` は直近 90 日かつ1人 500 件まで。`save_mistakes` は1回 20 件まで、`wrong` / `fixed` は 300 文字まで。
- カレンダー: diary=オレンジ `#ff922b`、summary=青 `#4dabf7`、topic=緑 `#40c057`、2種類=紫 `#9775fa`、3種類=金 `#fcc419`。
- 書き込みは SECURITY DEFINER の関数経由だけ。表は本人の行の select だけ許可。SQL は何度実行しても壊れないこと。実行順 `schema.sql` → `summary.sql` → `topic.sql`。
- 「今日の注意」は直近 30 日で 2 回以上出た種類だけ出す。
- DeepL Write で添削した日記のミスは集めない。
- Web で文章を入れるときは `textContent` (HTML として解釈させない)。

## Review Focus

1. Gemini が10種類にないコード (例 `"spelling"`) や大文字 (`"Article"`) を返す → アプリで小文字化し、知らないコードは `other` にして保存する (Task 2・Task 1 のサーバー側も `other` に寄せる)。
2. ほとんど何も話さなかった (文字起こしが空・2語未満) → 採点に送らず「Please speak a little more.」を出し、保存も連続記録の加算もしない (Task 5)。
3. 言い直しで音声認識が縮約形・句読点・大文字を変えて返す (`don't` ⇔ `do not`、`Hello,` ⇔ `hello`) → 一致として数える (Task 2)。
4. ログインしていない間のお題の結果を後から送る (日付が古い) → 結果は元の日付で保存され、連続記録は変わらない (Task 1・Task 4)。
5. ミスの配列が大きすぎる・長すぎる文 → サーバーで 20 件・300 文字に切り詰め、エラーにしない (Task 1)。

---

### Task 1: SQL — お題の結果・ミスの表と関数、カレンダーの種類

**Files:**
- Modify: `WEB/supabase/schema.sql` (セクション 2b `activity_days` の check 制約、セクション 4b `get_my_activity_days`)
- Create: `WEB/supabase/topic.sql`
- Test: `WEB/supabase/tests/topic_test.sql` (+ 手元実行用スタブ `WEB/supabase/tests/stub.sql`)

**Interfaces:**
- Produces (RPC, すべて `authenticated` のみ実行可):
  - `save_topic_result(p_timezone text, p_kind text, p_topic text, p_time_limit int, p_score int, p_content_score int, p_organization_score int, p_language_score int, p_transcript text, p_corrected text, p_natural text, p_good_points text, p_fix_points text, p_phrases text, p_read_aloud_accuracy int, p_mistakes jsonb, p_taken_at timestamptz default null) returns bigint`
  - `set_topic_read_aloud(p_id bigint, p_accuracy int) returns void` — 言い直しの一致率をあとから入れる (本人の行だけ)
  - `save_mistakes(p_timezone text, p_source text, p_mistakes jsonb, p_taken_at timestamptz default null) returns int` (保存した件数)
  - `get_my_topic_results(p_limit int default 20)` → `id, entry_date, created_at, kind, topic, time_limit_seconds, score, content_score, organization_score, language_score, transcript, corrected, natural_version, good_points, fix_points, phrases, read_aloud_accuracy`
  - `get_my_weak_points()` → `type text, count int, previous_count int` (上位 3、count 降順)
  - `get_my_mistake_stats(p_days int)` → `type text, count int, previous_count int, examples jsonb` (`examples` = `[{wrong, fixed, source, date}]` 新しい順 最大 5)
  - `get_my_activity_days(p_from date, p_to date)` → `day date, diary bool, summary bool, topic bool`
- `p_mistakes` の形: `[{"type":"tense","wrong":"I go yesterday","fixed":"I went yesterday"}, ...]`

- [ ] **Step 1: テストを書く** — `tests/stub.sql` は Supabase 相当の最小スタブ (ロール `anon`/`authenticated`、`auth.users`、`auth.uid()` = `request.jwt.claim.sub`、public の既定権限を anon/authenticated に全付与、ユーザー2人)。`tests/topic_test.sql` は `\i stub.sql`、`\i ../schema.sql`、`\i ../summary.sql`、`\i ../topic.sql` のあと、ユーザー A として次を確かめる (`\set ON_ERROR_STOP 1`、不一致なら `raise exception`):
  - `save_topic_result` で 22 件保存 → `get_my_topic_results(50)` が 20 件
  - 内訳と合計が合わない (40+30+30 ≠ p_score)、範囲外 (`p_content_score = 41`)、`p_kind = 'chat'` → それぞれ `22023` エラー
  - `p_mistakes` に 25 件・`type` が `"Spelling"`・`wrong` が 400 文字 → 保存されたのは 20 件、`type` は `other`、`wrong` は 300 文字
  - `save_mistakes('Asia/Tokyo','diary', ...)` で tense×3 / article×2 / plural×1 → `get_my_weak_points()` の1行目が `tense, 3`
  - 35 日前の日付のミスを `postgres` 権限で直接入れる → `previous_count` に数えられる。95 日前のものは次の保存で消える
  - 今日のお題を保存 → `get_my_streak` の `studied_today = true`、`get_my_activity_days` の今日が `topic = true`
  - 今日 diary・summary・topic を全部やる → その日の3列がすべて true
  - `p_taken_at` = 3 日前のお題 (最後の学習日が昨日) → 連続記録は変わらない
  - ユーザー B では `get_my_topic_results` / `get_my_mistake_stats(30)` が 0 行
  - 表への直接 insert は `insufficient_privilege`
  - 最後に3ファイルをもう一度 `\i` してもエラーなし、件数が変わらない

- [ ] **Step 2: 失敗を確認**

Run: `psql -X -q -v ON_ERROR_STOP=1 -f supabase/tests/topic_test.sql` (手元の PostgreSQL 16、`initdb` したばかりの DB)
Expected: `topic.sql` が無いので FAIL

- [ ] **Step 3: `schema.sql` を直す** — `activity_days.kind` の check を `('diary','summary','topic')` にする (`alter table ... drop constraint if exists activity_days_kind_check; add constraint ...` の形で再実行に耐える)。`get_my_activity_days` を `drop function if exists` してから `topic boolean` 列つきで作り直す (`bool_or(a.kind = 'topic')`)。

- [ ] **Step 4: `topic.sql` を書く** — `summary.sql` と同じ構成 (表 → RLS → 関数 → grant → 既存データの掃除)。決めておくこと:
  - 日付: `((v_taken at time zone p_timezone) - interval '3 hours')::date`。`p_taken_at` が未来 5 分超 / 30 日より前なら `now()`。
  - ミスの正規化は共通の内部関数 `public._insert_mistakes(p_uid uuid, p_date date, p_source text, p_mistakes jsonb) returns int` に集める: `jsonb_array_elements` を最大 20 件、`lower(trim(type))` が10種類に無ければ `other`、`wrong`/`fixed` を `left(..., 300)`、どちらかが空なら飛ばす。挿入後に `entry_date < current_date - 90` と、500 件を超えた古いものを消す。`revoke all ... from public, anon, authenticated`。
  - `save_topic_result` は `topic_results` に挿入 → `_insert_mistakes(..., 'topic', ...)` → 20 件を超えた古い結果を消す。1日 100 件まで (`54000`)。
  - `topic_results` の insert トリガー `topic_counts_as_study()` は `summary.sql` の `summary_counts_as_study()` と同じ規則 (`activity_days` に `topic` を記録、`set_config('app.streak_source','topic',true)` を付けて streaks を更新)。
  - `get_my_weak_points`: `count` = `entry_date > app_today('Asia/Tokyo') - 30`、`previous_count` = その前の 30 日。`count > 0` の上位 3。
  - `get_my_mistake_stats(p_days)`: `p_days` は 1〜90 に丸める。`count > 0` か `previous_count > 0` の種類を `count` 降順。

- [ ] **Step 5: テストが通ることを確認**

Run: Step 2 と同じ
Expected: 最後まで実行され `ALL TOPIC TESTS PASSED` (テストの最後に `\echo` する)

- [ ] **Step 6: Commit**

```bash
cd WEB
git add supabase/schema.sql supabase/topic.sql supabase/tests/stub.sql supabase/tests/topic_test.sql
git commit -m "Add topic speaking results, mistake stats and topic days to the calendar (SQL)"
```

---

### Task 2: Unity — ミスの種類と言い直しの照合 (純粋なロジック)

**Files:**
- Create: `UNITY/Assets/Script/CorrectionDiary/MistakeTypes.cs`
- Create: `UNITY/Assets/Script/CorrectionDiary/ReadAloudMatcher.cs`
- Test: `UNITY/Tests~/LogicTests/Program.cs` (Unity が読まない `Tests~` フォルダ。`System` だけを使うコンソールテスト)

**Interfaces:**
- Produces:
  - `[Serializable] public class Mistake { public string type; public string wrong; public string @fixed; }` — JSON 名は `fixed` (`JsonUtility` はフィールド名をそのまま使うので `@fixed`)
  - `public static class MistakeTypes { public static readonly string[] All; public static string Normalize(string type); public static string Label(string type); public static Dictionary<string,int> Count(IEnumerable<Mistake> mistakes); }` — `Label` は英語の表示名 (spec の表の英語側)、`Normalize` は小文字化して未知を `"other"`
  - `public static class ReadAloudMatcher { public static Result Match(string expected, string heard); }`、`public class Result { public List<Word> Words; public int AccuracyPercent; }`、`public struct Word { public string Text; public bool Matched; }` — `Words` は **expected の単語** を元の綴り (句読点込み) で並べたもの

- [ ] **Step 1: テストを書く** (`Tests~/LogicTests/Program.cs`、失敗したら `Environment.Exit(1)`)

```csharp
Check(MistakeTypes.Normalize(" Tense ") == "tense");
Check(MistakeTypes.Normalize("spelling") == "other");
Check(MistakeTypes.Normalize(null) == "other");
var c = MistakeTypes.Count(new[] { M("tense"), M("TENSE"), M("xyz") });
Check(c["tense"] == 2 && c["other"] == 1);

var r = ReadAloudMatcher.Match("I don't like it.", "i do not like it");
Check(r.AccuracyPercent == 100 && r.Words.Count == 4 && r.Words[1].Text == "don't" && r.Words.TrueForAll(w => w.Matched));
r = ReadAloudMatcher.Match("I went to the park yesterday.", "I went the park yesterday");
Check(r.AccuracyPercent == 83 && !r.Words[2].Matched);          // "to" が抜け: 5/6
r = ReadAloudMatcher.Match("She has two cats.", "she has two cat");
Check(r.AccuracyPercent == 75 && !r.Words[3].Matched);
r = ReadAloudMatcher.Match("Hello, world!", "hello hello world");
Check(r.AccuracyPercent == 100);                                 // 余分な語は減点しない
Check(ReadAloudMatcher.Match("Hi there.", "").AccuracyPercent == 0);
Check(ReadAloudMatcher.Match("", "anything").Words.Count == 0);
```

- [ ] **Step 2: 失敗を確認**

Run: `csc` (Roslyn) で `Tests~/LogicTests/Program.cs` と2つの本体ファイルをまとめてコンパイル → 実行
Expected: 本体ファイルが無いのでコンパイルエラー

- [ ] **Step 3: 実装** — `MistakeTypes`: spec の表どおり。`ReadAloudMatcher`: 両方を「小文字化 → `’` を `'` に → 英字・数字・`'` 以外を空白に → 空白で分割」。縮約形は比較用に展開する (`n't→ not`、`'re→ are`、`'m→ am`、`'ll→ will`、`'ve→ have`、`'d→ would`、`can't→can not`、`won't→will not`)。expected 側は「元の単語 → 展開後の部分語」の対応を持ち、展開後どうしで LCS を取る。元の単語は、部分語がすべて一致したとき `Matched`。`AccuracyPercent = round(一致した元の単語数 / 元の単語数 * 100)`、expected が空なら 0。

- [ ] **Step 4: テストが通ることを確認**

Run: Step 2 と同じ
Expected: `ALL LOGIC TESTS PASSED`

- [ ] **Step 5: Unity の部品でコンパイル** — Unity 2022.3.62f3 の `UnityEngine.*Module.dll`、`netstandard.dll` を参照して2ファイルを `csc -langversion:9` でコンパイル。Expected: エラー 0

- [ ] **Step 6: Commit**

```bash
cd UNITY
git add Assets/Script/CorrectionDiary/MistakeTypes.cs Assets/Script/CorrectionDiary/ReadAloudMatcher.cs Tests~/LogicTests/Program.cs
git commit -m "Add mistake types and read-aloud matcher"
```

---

### Task 3: Unity — Gemini: お題の作成・採点・発音コメント、日記と要約のミス

**Files:**
- Modify: `UNITY/Assets/Script/CorrectionDiary/GeminiApiClient.cs` (`CorrectionResult`、`DiarySchema`、`SummaryGrade`、`SummaryGradeSchema`、各プロンプト、新しいメソッド)
- Test: `UNITY/Tests~/LogicTests/Program.cs` に追記 (スキーマ文字列が正しい JSON であること)

**Interfaces:**
- Consumes: `Mistake`, `MistakeTypes.All` (Task 2)
- Produces:
  - `CorrectionResult` と `SummaryGrade` に `public Mistake[] mistakes;` を追加
  - `public const int TopicContentMax = 40, TopicOrganizationMax = 30, TopicLanguageMax = 30;`
  - `[Serializable] public class TopicPrompt { public string kind; public string topic; public string hint; }`
  - `[Serializable] public class TopicGrade { public string goodPoints; public string fixPoints; public int contentScore; public int organizationScore; public int languageScore; public int score; public string corrected; public string natural; public string[] phrases; public Mistake[] mistakes; public string[] readAloud; }`
  - `public Task<TopicPrompt> GenerateTopicAsync(string kind, IList<string> weakTypes, IList<string> recentTopics)`
  - `public Task<TopicGrade> GradeTopicAsync(string kind, string topic, int seconds, string spokenText, IList<string> weakTypes)` — 点数は範囲に収めてから `score` を合計で上書き。`readAloud` は最大 3 件に切る。`mistakes` の `type` は `MistakeTypes.Normalize`
  - `public Task<string> CheckPronunciationAsync(string expectedText, byte[] wav)` — 2〜3 文の英語コメント
  - `internal const string TopicGradeSchema, TopicPromptSchema` (テストで JSON として読むため internal)

- [ ] **Step 1: テストを追記** — `DiarySchema`、`SummaryGradeSchema`、`TopicGradeSchema`、`TopicPromptSchema` を `System.Text.Json.JsonDocument.Parse` で読めること。`TopicGradeSchema` の `properties` の並び順が `goodPoints, fixPoints, contentScore, ...` で始まり、`propertyOrdering` も同じ順であること (理由を先に書かせるため)。`mistakes.items.properties.type.enum` が `MistakeTypes.All` と一致すること。

- [ ] **Step 2: 失敗を確認** — Run: Task 2 と同じコンパイル + 実行。Expected: `TopicGradeSchema` が無いのでコンパイルエラー

- [ ] **Step 3: 実装** — 決めておく文言:
  - ミスの指示 (日記・要約・お題の3つのプロンプトに共通で足す): `"\"mistakes\": every grammar or word mistake as {type, wrong, fixed}. type is one of: article, tense, preposition, agreement, plural, verb_form, word_order, word_choice, missing_word, other. wrong/fixed are the short phrase before and after the fix. Ignore punctuation and capitalization. Empty list if none."`
  - お題の作成: 種類ごとの説明 (`exam` = Versant/IELTS 風の意見・説明問題、`daily` = 身近な体験・好み、`work` = 仕事・面接)。`weakTypes` があれば「その文法を自然に使うお題にする」、`recentTopics` (最大 10) とは別の話題にする。`hint` は話の組み立てのヒント1文。`temperature` 1.0。
  - お題の採点: 先に `goodPoints` / `fixPoints` (各 `- ` で始まる箇条書き) を書かせ、その後に点数。各点数に帯の基準を書く (例: content 35-40 = お題に完全に答え、理由と例がある / 20-34 = 答えているが理由か例が弱い / 1-19 = 一部しか答えていない)。`weakTypes` があれば「これらの種類のミスは見落とさないこと」。`corrected` = 本人の文を正しく直したもの、`natural` = 同じ内容をネイティブらしく、`phrases` = 次に使える表現 3 つ、`readAloud` = `corrected` の中で直しが入った文を最大 3 つ。何も話していなければ全部 0 点。`temperature` 0.2。
  - 発音コメント: `AudioPart(wav)` と期待文を渡し、聞き取りにくかった語と直し方を 2〜3 文。スキーマは `{comment: STRING}`。

- [ ] **Step 4: テストが通ることを確認** — Expected: `ALL LOGIC TESTS PASSED`、Unity の部品でのコンパイル エラー 0

- [ ] **Step 5: Commit**

```bash
git add Assets/Script/CorrectionDiary/GeminiApiClient.cs Tests~/LogicTests/Program.cs
git commit -m "Gemini: topic prompt/grading, pronunciation check, mistakes in diary and summary"
```

---

### Task 4: Unity — クラウド保存 (お題の結果・ミス・苦手) と、日記・要約からのミス保存

**Files:**
- Modify: `UNITY/Assets/Script/CorrectionDiary/SupabaseService.cs`
- Create: `UNITY/Assets/Script/CorrectionDiary/PendingTopicStore.cs` (`PendingSummaryStore` と同じ形。キー `topic_pending_v1`、最大 50 件)
- Modify: `UNITY/Assets/Script/CorrectionDiary/DiaryManager.cs` (Gemini で日記を添削したあと)
- Modify: `UNITY/Assets/Script/CorrectionDiary/SummaryPracticeManager.cs` (採点結果を保存したあと)

**Interfaces:**
- Consumes: `Mistake`, `TopicGrade` (Task 2・3)、RPC (Task 1)
- Produces (`SupabaseService`):
  - `public struct WeakPoint { public string Type; public int Count; public int PreviousCount; }`
  - `public Task<long> SaveTopicResultAsync(string kind, string topic, int seconds, GeminiApiClient.TopicGrade g, string transcript, int? readAloudAccuracy, DateTime? takenAtUtc = null)` — 結果の id を返す
  - `public Task SetTopicReadAloudAsync(long id, int accuracy)`
  - `public Task SaveMistakesAsync(string source, Mistake[] mistakes, DateTime? takenAtUtc = null)` — 0 件なら何もしない
  - `public Task<WeakPoint[]> GetWeakPointsAsync()` — 未ログイン・失敗時は空配列 (例外を出さない)
  - `public Task<int> UploadPendingTopicResultsAsync()` — `ApplySession` で `UploadPendingSummaryResultsAsync` と並べて呼ぶ
  - `PendingTopicStore.Add(string kind, string topic, int seconds, GeminiApiClient.TopicGrade g, string transcript, DateTime takenAtUtc)` / `Load()` / `Save(list)` / `Count`

- [ ] **Step 1: 実装** — `p_mistakes` は `JsonUtility.ToJson` で `{"items":[...]}` を作り、中の配列部分だけを JSON 値として RPC の本文に入れる (文字列ではなく jsonb として渡す)。保存できなかった要約の結果をあとで送るとき (`UploadPendingSummaryResultsAsync`) は、成功後に `SaveMistakesAsync("summary", item.grade.mistakes, item.TakenAtUtc)` も呼ぶ。`DiaryManager` は `_engine == Engine.Gemini` のときだけ `SaveMistakesAsync("diary", result.mistakes)` (待たない、失敗はログだけ)。`SummaryPracticeManager.SaveResultToCloudAsync` は保存成功後に `SaveMistakesAsync("summary", g.mistakes, takenAt)`。

- [ ] **Step 2: 本文の組み立てをテスト** — `Tests~/LogicTests` に `SupabaseService` の JSON 組み立て部分を `internal static string BuildMistakesJson(Mistake[] m)` として切り出してテスト: `[{type:"tense",wrong:"I go",fixed:"I went"}]` → `[{"type":"tense","wrong":"I go","fixed":"I went"}]`。`"` や改行を含む文が正しくエスケープされること。`null` → `[]`。(このテストは `JsonUtility` を使わない実装にするため、`HttpUtil.JsonEscape` を使って手で組み立てる。)

Run: Task 2 と同じ。Expected: `ALL LOGIC TESTS PASSED`

- [ ] **Step 3: Unity の部品でコンパイル** — 変更した4ファイル + Task 2・3 のファイルを、プロジェクトの `Assembly-CSharp.dll` も参照してコンパイル (`-nowarn:0436`)。Expected: エラー 0

- [ ] **Step 4: Commit**

```bash
git add Assets/Script/CorrectionDiary/SupabaseService.cs Assets/Script/CorrectionDiary/PendingTopicStore.cs Assets/Script/CorrectionDiary/DiaryManager.cs Assets/Script/CorrectionDiary/SummaryPracticeManager.cs Tests~/LogicTests/Program.cs
git commit -m "Save topic results and mistakes to Supabase; fetch weak points"
```

---

### Task 5: Unity — お題スピーキングの画面ロジック

**Files:**
- Create: `UNITY/Assets/Script/CorrectionDiary/TopicCatalog.cs`
- Create: `UNITY/Assets/Script/CorrectionDiary/TopicPracticeManager.cs`
- Modify: `UNITY/Assets/Script/Talk3D/DiscordClient.cs` (お題の結果の送信)
- Test: `UNITY/Tests~/LogicTests/Program.cs` (`TopicCatalog`)

**Interfaces:**
- Consumes: Task 2〜4 のすべて、`DiaryManager.CountOtherPracticeAsStudyDay()` / `RefreshStreakFromCloud()`、`NativeSpeechRecognizer`、`AudioRecorder`、`ApiSecrets` (要約と同じキー: `geminiApiKey`、要約用 Discord の `summaryDiscordWebhookUrl` / `summaryDiscordChannelId`)
- Produces:
  - `public static class TopicCatalog { public static readonly string[] Kinds = { "exam", "daily", "work" }; public static int SecondsFor(string kind); public static string RandomKind(System.Random rng); public static GeminiApiClient.TopicPrompt Offline(string kind, System.Random rng, IList<string> recent); }` — `SecondsFor`: daily 45、それ以外 60。`Offline` は各種類 20 件のリストから `recent` に無いものを選ぶ (全部使っていたら何でも)
  - `TopicPracticeManager : MonoBehaviour` — Inspector: `supabase`, `mainButton`, `mainButtonLabel`, `skipThinkButton`, `kindText`, `topicText`, `hintText`, `weakPointText`, `timerText`, `statusText`, `resultText` (スクロール内、リッチテキスト)、`readAloudGroup`, `readAloudText`, `readAloudButton`, `readAloudButtonLabel`, `pronunciationButton`, `retryButton`
  - `DiscordClient.SendTopicResultAsync(DateTime date, string kind, string topic, GeminiApiClient.TopicGrade g)`

- [ ] **Step 1: `TopicCatalog` のテストを書く** — `SecondsFor("daily") == 45`、`SecondsFor("exam") == 60`、`SecondsFor("work") == 60`、`SecondsFor("??") == 60`。`Offline("work", rng, recent)` を 200 回呼んで、`recent` に入れた 5 件が一度も出ない・`kind == "work"`・`topic` が空でない。

- [ ] **Step 2: 失敗 → 実装 → 成功** — Run: Task 2 と同じ。Expected: `ALL LOGIC TESTS PASSED`

- [ ] **Step 3: `TopicPracticeManager` を実装** — 状態 `Idle → Generating → Thinking → Speaking → Grading → Result → Reading → Result/Idle`。決めておくこと:
  - メインボタンの文字: Idle「New Topic」/ Thinking「Start now」/ Speaking「Finish」/ Result「New Topic」。
  - お題を作る前に `GetWeakPointsAsync()`。`Count >= 2` の先頭を「Today's focus: <Label>」に出し、Gemini には `Count >= 2` の種類を最大 3 つ渡す。最近のお題は PlayerPrefs `topic_recent_v1` に最大 10 件。
  - Gemini が失敗したら `TopicCatalog.Offline` を使い、`statusText` に「Offline topic」。
  - 話す時間が 0 になるか Finish で止め、文字起こしが空か 2 語未満なら「Please speak a little more.」を出して Idle (保存しない)。
  - 採点失敗時は `retryButton` を出し、同じ文字起こしで送り直す。
  - 結果表示: 合計と3項目、`goodPoints`、`fixPoints`、`corrected` (要約・日記と同じ `TextDiff` で直した語に下線)、`natural`、`phrases`、苦手のチェック (「<Label>: this time N / last time M」、M は PlayerPrefs `topic_last_counts_v1` に前回の種類別回数を保存しておき、それを使う)。
  - 採点後すぐ: クラウド保存 (未ログイン・失敗は `PendingTopicStore`)、`CountOtherPracticeAsStudyDay()`、Discord 送信 (待たない)、`topic_recent_v1` と `topic_last_counts_v1` を更新。
  - 言い直し: `readAloud` が空なら `readAloudGroup` を隠す。1 文ずつ「Read aloud」→ 音声認識 → `ReadAloudMatcher.Match` の結果を `<color=#40c057>` / `<color=#fa5252>` で表示、全文の平均一致率を出す。全部読んだら `SetTopicReadAloudAsync(id, 平均)` (保存済みの id があるとき)。「Pronunciation check」は `AudioRecorder` で同じ文を録音して `CheckPronunciationAsync` の結果を表示。

- [ ] **Step 4: Unity の部品でコンパイル** — Expected: エラー 0

- [ ] **Step 5: Commit**

```bash
git add Assets/Script/CorrectionDiary/TopicCatalog.cs Assets/Script/CorrectionDiary/TopicPracticeManager.cs Assets/Script/Talk3D/DiscordClient.cs Tests~/LogicTests/Program.cs
git commit -m "Add topic speaking practice (prompt, timer, grading, read-aloud check)"
```

---

### Task 6: Unity — 画面を組み立てるエディタツールとタブ

**Files:**
- Create: `UNITY/Assets/Editor/TopicScreenBuilder.cs`

**Interfaces:**
- Consumes: `TopicPracticeManager` の Inspector 項目 (Task 5)
- Produces: メニュー `Tools/Topic Speaking/Create Topic Screen`

- [ ] **Step 1: 実装** — `SummaryScreenBuilder` を手本にする (同じ色定数、`UI/Skin` の標準スプライト、要約画面の隣に「topic screen」を作る、Diary のタブボタンを複製して「Topic」タブを作り、タブを並べ直す、「Diary 画面を隠す」ボタンに「topic screen も隠す」を足す、Undo 対応、すでに `TopicPracticeManager` があればダイアログで止める)。並び: 種類と今日の注意 → お題 → ヒント → 残り時間 → メインボタンと Start now → 状態 → 結果 (ScrollRect) → 言い直し (文・Read aloud・Pronunciation check) → Retry。`TopicPracticeManager` の参照をすべて `SerializedObject` で入れ、`supabase` はシーンから探す。

- [ ] **Step 2: Unity の部品でコンパイル** (`UnityEditor.dll` も参照) — Expected: エラー 0

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/TopicScreenBuilder.cs
git commit -m "Add editor tool that builds the Topic screen and tab"
```

---

### Task 7: Web — お題スピーキングとよくあるミスのページ、カレンダーの色

**Files:**
- Modify: `WEB/src/streak-client.js`
- Modify: `WEB/index.html` (メニュー、2 ページ、カレンダーの色と凡例)
- Test: `WEB/tests/web_test.py` (Playwright、Supabase をダミーに差し替える。既存の手元テストと同じ方式)

**Interfaces:**
- Consumes: RPC (Task 1)
- Produces (`createStreakApi` に追加):
  - `getTopicResults(limit = 20)` → `[{ id, date, createdAt, kind, topic, timeLimit, score, contentScore, organizationScore, languageScore, transcript, corrected, natural, goodPoints, fixPoints, phrases, readAloudAccuracy }]` (新しい順)
  - `getMistakeStats(days)` → `[{ type, count, previousCount, examples: [{ wrong, fixed, source, date }] }]`
  - `getActivityDays` の各行に `topic: boolean` を追加

- [ ] **Step 1: テストを書く** — ダミーの RPC 応答で次を確かめる (明るい表示と暗い表示の両方、幅 390px):
  - メニューに「お題スピーキング」「よくあるミス」があり、押すとそのページだけ表示、URL が `#topic` / `#mistakes`
  - お題ページ: 結果 3 件 → 数字のカード (回数 3・最新・平均・最高)、推移グラフの点が 3 つ、一覧 3 件、`corrected` に `<img onerror>` を入れても実行されない
  - ミスページ: 種類 4 つ → 横棒が 4 本で回数の多い順、上位 5 つまで強調クラス、`count > previousCount` に ↑・少なければ ↓。種類を押すと例が開き、どの練習かが出る。期間ボタン 7/30/90 を押すと `get_my_mistake_stats` が `p_days` 7/30/90 で呼ばれる
  - カレンダー: diary+summary+topic の日が `.gold`、2 種類が `.both`、topic だけが `.topic`。凡例に「お題」「3つ全部」
  - コンソールエラー 0

- [ ] **Step 2: 失敗を確認** — Run: `python3 tests/web_test.py` Expected: FAIL (メニュー項目が無い)

- [ ] **Step 3: 実装** — お題ページは要約ページのグラフ関数 (`renderSummaryChart` / `renderSummaryParts`) を、満点と色を引数で受け取れるように直して使い回す (要約: 50/25/25、お題: 40/30/30)。ページを表示したときにグラフを描き直す処理 (`showPage`) にお題も足す。ミスの表示名は spec の表の日本語側を `MISTAKE_LABELS` に持つ。カレンダーは `kinds = [diary, summary, topic].filter(Boolean).length` で 1 → 各色、2 → `both`、3 → `gold`。

- [ ] **Step 4: テストが通ることを確認** — Expected: すべて OK、エラー 0。スクリーンショットを目で確認 (お題ページ・ミスページ・金の炎)

- [ ] **Step 5: Commit**

```bash
cd WEB
git add src/streak-client.js index.html tests/web_test.py
git commit -m "Web: topic speaking and mistake stats pages; topic and gold days in the calendar"
```

---

### Task 8: 仕上げ — 利用者に渡す手順

**Files:**
- Modify: `WEB/README.md` (SQL の実行順に `topic.sql` を追加、2 ページの説明)

- [ ] **Step 1: README を更新し、利用者向けの手順を確認** — 手順: ① Supabase で `schema.sql` → `summary.sql` → `topic.sql` を実行 ② Web を push ③ Unity で `Tools > Topic Speaking > Create Topic Screen` → `TopicPracticeManager` の Inspector を確認 → Ctrl+S → APK ビルド ④ 実機で「お題 → 話す → 採点 → 言い直し → Web に出る → カレンダーが緑」を確認。

- [ ] **Step 2: Commit**

```bash
cd WEB
git add README.md
git commit -m "README: topic.sql and the new pages"
```

# Talk: Lufka を英会話の練習相手にする 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Talk で、Lufka が返事のあとに間違いを声で言い直し、今日の単語を会話に混ぜ、終わったらまとめを出して連続記録に数える。待ち時間は今より増やさない。

**Architecture:** 返事は今のまま端末の Gemma 1B が作る。発話ごとに Gemini Flash-Lite の添削を並行して走らせ、返事を言い終えたら (間に合わなければつなぎの一言で最大 5 秒待って) 言い直す。判断はすべて Unity に依存しない小さなクラス (`TalkCoach` / `TalkPlan` / `TalkSummary`) に置いてテストし、`ConversationController` にはつなぐ所だけを足す。

**Tech Stack:** Unity 2022.3 / C#、LLMUnity (Gemma 3 1B)、Gemini API (`gemini-flash-lite-latest`)、Supabase (PostgreSQL)、Web (素の JS)

**Spec:** `docs/superpowers/specs/2026-11-10-talk-coach-design.md`

## Global Constraints

- 返事のモデルは変えない (`gemma-3-1b-it-Q4_K_M.gguf`)。声も端末の読み上げのまま
- 添削のモデル: `gemini-flash-lite-latest`、予備 `gemini-3.8-flash`。キーは `geminiApiKey`、上限なら `backupApiKeys` (今のほかの画面と同じ)
- 言い直しの文: `"By the way, you can say: " + corrected`
- つなぎ: 最大 2 回、待つのは合わせて最大 5 秒。1B に作らせない
- 単語: 最大 5 語。レベル未設定は `B1`
- 連続記録: 1 回の会話で発話 3 回以上
- 句読点・大文字・言いよどみ (uh, um) は直さない
- コードのファイルは 1 つ 600 行以内 (`ConversationController.cs` は今 471 行。足す分は `ConversationController.Coach.cs` へ)
- **commit しない**: Unity 側はカラオケの作業が終わってから、利用者の指示でまとめて commit する (2026-11-10 の約束)。各タスクの最後は「テストとコンパイルが通る」までとする
- **タスク 4 (SQL・Web) は、カラオケの会話が `check (kind in ...)` の変更を終えてから始める**

## Review Focus

1. 添削が返事より先にできた場合 (ネットが速く、返事が長い): 返事を言い終えた直後に言い直す。待たずに済むので、つなぎは出さない → タスク 5 の手順で確認
2. 返事の途中で End / 画面を離れた: 進行中の添削・つなぎ・言い直しが、次の会話で話し出さない (セッション番号で止める) → タスク 5
3. 発話が数字だけ・1 語だけ (「Yes.」): 添削を頼まない (Gemini の回数を使わない) → タスク 1 `ShouldCheck` のテスト
4. Gemini が JSON 以外・`corrected` が空で `ok=false` を返す: 添削なしとして扱い、言い直さない → タスク 1 `Parse` のテスト
5. 単語帳を一度も使っていない (評価もデッキも空): 単語の指示を付けず、上の単語の並びも出さない → タスク 2 のテスト

テストの実行 (このパソコン): `python C:\Users\haru0\AppData\Local\Temp\claude\7df45528f914df3b\04d30114-a2fc-4f06-9ca9-4a43a2248346\scratchpad\logictest.py "C:\Users\haru0\club\class\es\My project"` → 最後に `ALL LOGIC TESTS PASSED`。Android のコンパイル: 同じ場所の `unitycheck_android.py` → `errors: 0`

---

### Task 1: 添削 (TalkCoach と Gemini の呼び出し)

**Files:**
- Create: `Assets/Script/Talk3D/TalkCoach.cs`
- Create: `Assets/Script/CorrectionDiary/GeminiApiClient.Talk.cs`
- Create: `Tests~/LogicTests/TalkTests.cs` (`public static partial class LogicTests` に `TalkCoachTests()`)
- Modify: `Tests~/LogicTests/Program.cs` (`Main` で `TalkCoachTests();` を呼ぶ)

**Interfaces:**
- Produces:
  - `public class TalkCorrection { public bool Ok; public string Corrected = ""; public string NoteJa = ""; }`
  - `TalkCoach.Parse(string json) -> TalkCorrection` (形が違う・`ok=false` なのに `corrected` が空 → `null`)
  - `TalkCoach.ShouldCheck(string utterance) -> bool` (英字を含む語が 2 語以上)
  - `TalkCoach.RecastLine(string said, TalkCorrection c) -> string` (言わないときは `null`)
  - `TalkCoach.Normalize(string s) -> string` (小文字・英数字とアポストロフィ以外を除く・空白を 1 つに)
  - `TalkCoach.MaxFillers = 2`、`TalkCoach.MaxWaitSeconds = 5f`、`TalkCoach.Fillers` (`"Hmm, let me think."`, `"Oh, you know what?"`, `"Wait, one thing."`, `"Hmm, okay."` の 4 つ)
  - `class TalkCoach.FillerPicker { FillerPicker(System.Random rng); string Next(); }` (直前と同じものを返さない)
  - `GeminiApiClient.TalkCorrectionSchema` (const string)、`GeminiApiClient.CorrectTalkAsync(string utterance, string lufkaLine) -> Task<TalkCorrection>` (失敗は例外。呼ぶ側が「添削なし」にする)

- [ ] **Step 1: テストを書く** (`TalkCoachTests`)

```csharp
var ok = TalkCoach.Parse("{\"ok\":true,\"corrected\":\"\",\"note_ja\":\"\"}");
Check(ok != null && ok.Ok, "talk parse: ok");
var fix = TalkCoach.Parse("{\"ok\":false,\"corrected\":\"I went to Osaka yesterday.\",\"note_ja\":\"過去のことは went\"}");
Check(fix != null && !fix.Ok && fix.Corrected == "I went to Osaka yesterday." && fix.NoteJa == "過去のことは went", "talk parse: fix");
Check(TalkCoach.Parse("not json") == null && TalkCoach.Parse("{\"ok\":false,\"corrected\":\"\"}") == null, "talk parse: bad -> null");
Check(TalkCoach.RecastLine("I go to Osaka yesterday", fix) == "By the way, you can say: I went to Osaka yesterday.", "talk recast");
Check(TalkCoach.RecastLine("i went to osaka yesterday", fix) == null, "talk recast: only case/punctuation differs -> none");
Check(TalkCoach.RecastLine("x y", ok) == null && TalkCoach.RecastLine("x y", null) == null, "talk recast: ok/null -> none");
Check(!TalkCoach.ShouldCheck("Yes.") && !TalkCoach.ShouldCheck("123 456") && TalkCoach.ShouldCheck("I go"), "talk should check");
var picker = new TalkCoach.FillerPicker(new System.Random(1));
string prev = null; bool repeated = false;
for (int i = 0; i < 50; i++) { string f = picker.Next(); repeated |= f == prev; prev = f; }
Check(!repeated && TalkCoach.Fillers.Contains(prev), "talk filler: never the same twice in a row");
Check(System.Text.Json.JsonDocument.Parse(GeminiApiClient.TalkCorrectionSchema) != null, "talk schema is JSON");
```

- [ ] **Step 2: テストを実行して失敗を確かめる** — 実行: 上の `logictest.py`。期待: `TalkCoach` が無いのでコンパイルエラー

- [ ] **Step 3: `TalkCoach.cs` を作る** — `Parse` は `JsonUtility` ではなく、ほかの Parse と同じ `MiniJson` (`Repeat/MiniJson.cs`) か `JsonGet` で `ok` / `corrected` / `note_ja` を読む。`RecastLine` は `Normalize(said) == Normalize(c.Corrected)` なら `null`。`FillerPicker` は乱数で選び、直前と同じなら次の要素にずらす

- [ ] **Step 4: `GeminiApiClient.Talk.cs` を作る** — スキーマ: `{"type":"OBJECT","properties":{"ok":{"type":"BOOLEAN"},"corrected":{"type":"STRING"},"note_ja":{"type":"STRING"}},"required":["ok","corrected","note_ja"]}`。`CorrectTalkAsync` は `GeneratePartsAsync(TextPart(prompt), TalkCorrectionSchema, "\"temperature\":0.2")` を呼び、`TalkCoach.Parse` で読む (`null` なら例外)。プロンプトに入れること (英語): 音声認識の結果である / 句読点・大文字・uh, um は直さない / 意味を変えず最小限 / 間違いが無ければ ok=true / note_ja は 30 字以内の日本語 / 直前の Lufka の発言 (文脈) と発話

- [ ] **Step 5: テストを実行して通るのを確かめる** — 期待: `ALL LOGIC TESTS PASSED`

### Task 2: 今日の単語とレベル (TalkPlan)

**Files:**
- Create: `Assets/Script/Talk3D/TalkPlan.cs`
- Modify: `Tests~/LogicTests/TalkTests.cs` (`TalkPlanTests()`)、`Program.cs` (呼ぶ)

**Interfaces:**
- Consumes: `VocabReview` (`Word`, `At`)、`VocabCard` (`Word`, `Status`, `DueAt`, `LastReviewAt`)、`VocabSettings.Level`
- Produces:
  - `TalkPlan.MaxWords = 5`、`TalkPlan.DefaultLevel = "B1"`
  - `TalkPlan.PickWords(IList<VocabReview> reviews, IList<VocabCard> cards, DateTime nowLocal, int max = MaxWords) -> List<string>`: (1) `At` が `nowLocal` と同じ地元の日付の評価、新しい順・重なりなし (2) 足りなければ `Status == learning` を `LastReviewAt` の新しい順、次に `known` を `DueAt` の早い順
  - `TalkPlan.Instruction(string level, IList<string> words) -> string`
  - `TalkPlan.WithInstruction(string basePrompt, string instruction) -> string` (前回足した分 `TalkPlan.Marker` 以降を消してから足す)
  - `TalkPlan.Marker = "\n\n[Practice]\n"`
  - `TalkPlan.UsedWords(string utterance, IList<string> words) -> List<string>`

- [ ] **Step 1: テストを書く** (`TalkPlanTests`)

```csharp
var now = new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Local);
VocabReview R(string w, int daysAgo, int h = 10) => new VocabReview { Word = w, At = now.Date.AddDays(-daysAgo).AddHours(h) };
var cards = new List<VocabCard> {
    new VocabCard { Word = "calm", Status = VocabCard.Learning, LastReviewAt = now.AddDays(-1) },
    new VocabCard { Word = "brave", Status = VocabCard.Known, DueAt = now.AddDays(2) },
    new VocabCard { Word = "quiet", Status = VocabCard.Learning, LastReviewAt = now.AddDays(-3) } };
var w = TalkPlan.PickWords(new[] { R("study", 0, 9), R("run", 0, 11), R("study", 0, 12), R("old", 1) }, cards, now);
Check(string.Join(",", w) == "study,run,calm,quiet,brave", "talk words: today first, then learning, then due (" + string.Join(",", w) + ")");
Check(TalkPlan.PickWords(new VocabReview[0], new VocabCard[0], now).Count == 0, "talk words: no vocab -> none");
string ins = TalkPlan.Instruction("A2", new[] { "study", "run" });
Check(ins.Contains("A2") && ins.Contains("study, run"), "talk instruction: level and words");
Check(TalkPlan.Instruction("", new string[0]).Contains("B1") && !TalkPlan.Instruction("", new string[0]).Contains("learning these words"), "talk instruction: default level, no words");
string p1 = TalkPlan.WithInstruction("You are Lufka.", "X");
Check(TalkPlan.WithInstruction(p1, "Y") == "You are Lufka." + TalkPlan.Marker + "Y", "talk prompt: old practice part replaced");
var used = TalkPlan.UsedWords("Yesterday I Went running and she studies a lot", new[] { "go", "run", "study", "calm" });
Check(string.Join(",", used) == "go,run,study", "talk used words: went/running/studies (" + string.Join(",", used) + ")");
```

- [ ] **Step 2: 実行して失敗を確かめる** — 期待: コンパイルエラー

- [ ] **Step 3: `TalkPlan.cs` を作る** — `Instruction` の文 (英語・仕様どおり): `"The user is an {level} English learner. Use short, simple words and sentences."` と、単語があれば `"Today the user is learning these words: {a, b, c}. Naturally use one of them in some replies, and ask questions that make the user want to use them."`。`UsedWords` は発話の各語を小文字にし、その語・`-s/-es/-ies→y/-ed/-ied→y/-ing` (語尾の子音の重なり running→run、e の脱落 making→make も) を外した形・不規則動詞の表 (go/went/gone、be/was/were/been、have/had、do/did/done、make/made、take/took/taken、see/saw/seen、come/came、get/got、know/knew/known、think/thought、say/said、tell/told、find/found、give/gave/given、buy/bought、eat/ate/eaten、write/wrote/written、run/ran、begin/began/begun、feel/felt、leave/left、meet/met、bring/brought、teach/taught の範囲) と比べる。返す順は `words` の順

- [ ] **Step 4: 実行して通るのを確かめる** — 期待: `ALL LOGIC TESTS PASSED`

### Task 3: 1 回の会話の記録とまとめ (TalkSummary)

**Files:**
- Create: `Assets/Script/Talk3D/TalkSummary.cs`
- Modify: `Tests~/LogicTests/TalkTests.cs` (`TalkSummaryTests()`)、`Program.cs`

**Interfaces:**
- Consumes: `TalkCorrection` (タスク 1)、`TalkPlan.UsedWords` (タスク 2)
- Produces:
  - `TalkSummary(IList<string> words)`、`void AddUserTurn(string text, float atSeconds)`、`void AddCorrection(string said, TalkCorrection c)` (ok や言わない添削は足さない)、`void End(float atSeconds)`
  - `int Turns`、`float Seconds` (最初の発話から End まで)、`IReadOnlyList<string> Words`、`IReadOnlyCollection<string> Used`、`IReadOnlyList<(string said, string corrected, string note)> Corrections`
  - `bool CountsForStreak` (`Turns >= TalkSummary.MinTurnsForStreak`、`MinTurnsForStreak = 3`)
  - `string Text()`: まとめの文 (英語の見出しと日本語の説明。例は下のテスト)

- [ ] **Step 1: テストを書く** (`TalkSummaryTests`)

```csharp
var s = new TalkSummary(new[] { "go", "calm" });
s.AddUserTurn("Hello there", 10f);
s.AddUserTurn("I go to Osaka yesterday", 20f);
s.AddCorrection("I go to Osaka yesterday", new TalkCorrection { Ok = false, Corrected = "I went to Osaka yesterday.", NoteJa = "過去のことは went" });
s.AddCorrection("Hello there", new TalkCorrection { Ok = true });
Check(!s.CountsForStreak && s.Turns == 2, "talk summary: 2 turns -> not counted");
s.AddUserTurn("It was fun", 40f);
s.End(70f);
Check(s.CountsForStreak && s.Turns == 3 && Math.Abs(s.Seconds - 60f) < 0.01f, "talk summary: 3 turns, 60 s");
Check(s.Used.Contains("go") && !s.Used.Contains("calm") && s.Corrections.Count == 1, "talk summary: used words and corrections");
string t = s.Text();
Check(t.Contains("3") && t.Contains("1:00") && t.Contains("I go to Osaka yesterday") && t.Contains("I went to Osaka yesterday.") && t.Contains("calm"), "talk summary text: " + t);
```

- [ ] **Step 2: 実行して失敗を確かめる**
- [ ] **Step 3: `TalkSummary.cs` を作る** — `Text()` の形: `"Turns: 3   Time: 1:00\nWords: go ✓  calm\n\nBetter ways to say it:\n• I go to Osaka yesterday\n  → I went to Osaka yesterday. (過去のことは went)"`。言い直しが無ければ `"No corrections. Nice!"`。単語が 0 語なら Words の行を出さない
- [ ] **Step 4: 実行して通るのを確かめる**

### Task 4: クラウドの連続記録 (talk) — カラオケの作業が終わってから

**Files:**
- Create: `C:\Users\haru0\club\class\es\database_learnApp\supabase\talk.sql`、`supabase\tests\talk_test.sql`
- Modify: database_learnApp の `check (kind in (...))` を持つすべての SQL (今は `schema.sql`, `repeat.sql`, `answer.sql`, `karaoke.sql`) に `'talk'` を足す
- Modify: database_learnApp `src/page/streak.js` の `KIND_INFO` (Talk の行と色)、`tests/web_test.py`
- Modify: `Assets/Script/CorrectionDiary/SupabaseService.cs` (`CompleteTalkSessionAsync`)

**Interfaces:**
- Produces: SQL `public.complete_talk_session(p_timezone text default 'Asia/Tokyo', p_taken_at timestamptz default null) returns table (streak integer, already_done boolean)` (`complete_vocab_session` と同じ作り、`kind = 'talk'`、`app.streak_source = 'talk'`)。C# `SupabaseService.CompleteTalkSessionAsync(DateTime takenAtUtc) -> Task`

- [ ] **Step 1: `talk_test.sql` を書く** — `vocab_test.sql` の連続記録の部分を写し、関数名と種類を変える: 初回 → 連続 1・`already_done=false`、同じ日の 2 回目 → `already_done=true`、翌日 → 連続 2、`activity_days` に `kind='talk'` が 1 行
- [ ] **Step 2: 実行して失敗を確かめる** — `python C:\Users\haru0\AppData\Local\Temp\claude\7df45528f914df3b\04d30114-a2fc-4f06-9ca9-4a43a2248346\scratchpad\sqltest.py <WEB>/supabase/tests/talk_test.sql` (実行順の SQL を先に読む作りは `vocab_test.sql` に合わせる)
- [ ] **Step 3: `talk.sql` と各 `check` を直す** → Step 2 のコマンドで `ALL ... PASSED`
- [ ] **Step 4: Web の `KIND_INFO` に Talk を足す** — 色は、その時点の `KIND_INFO` と `FLAME_COLORS` で使っていない色 (候補 `#20c997` 水色がかった緑)。`web_test.py` のダミーの `activity_days` に `talk` の日を 1 つ足し、カレンダーの凡例に Talk が出ることを確かめる。実行: `run_web_test.py` (前からある `supabase.from` の 2 件以外が通る)
- [ ] **Step 5: `SupabaseService.CompleteTalkSessionAsync` を足す** (`CompleteVocabSessionAsync` と同じ形) → `logictest.py` が通る

### Task 5: 会話につなぐ (ConversationController.Coach.cs と TalkCoachView)

**Files:**
- Create: `Assets/Script/Talk3D/ConversationController.Coach.cs` (partial)
- Create: `Assets/Script/Talk3D/TalkCoachView.cs` (OnGUI)
- Modify: `Assets/Script/Talk3D/ConversationController.cs` (呼び出しを足すだけ)、`ConversationController.Support.cs` (ボタンの名前 End、まとめを出す)

**Interfaces:**
- Consumes: タスク 1〜4 のすべて。`DiaryManager.CountOtherPracticeAsStudyDay()`、`SupabaseService.EnsureSignedInAsync()`、`VocabStore.LoadReviews()`、`VocabStore.LoadDeck().Cards`
- Produces (partial の中):
  - `void CoachBeginSession()` — `StartConversation` の最初で呼ぶ: 単語を決め (`TalkPlan.PickWords(..., DateTime.Now)`)、`llmAgent.systemPrompt = TalkPlan.WithInstruction(...)`、`summary = new TalkSummary(words)`。単語帳の読み込みが例外を投げたら単語 0 語で続ける (Console に警告)
  - `Task<TalkCorrection> coachTask` — `HandleUserSpeech` で発話が決まったら、`TalkCoach.ShouldCheck` なら `CorrectTalkAsync(userText, lastLufkaLine)` を開始 (例外は捕まえて `null`)。`summary.AddUserTurn`
  - `async Task CoachAfterReply(int id, string said)` — `await speaker` のあと、`BeginListening` の前に呼ぶ。完了済みなら言い直す。未完了なら `FillerPicker.Next()` を読み上げ、合わせて `MaxWaitSeconds` まで (つなぎは `MaxFillers` 回まで) 待つ。各 await のあとで `id != sessionId` なら何もせず戻る。間に合わなければ、完了したときに `TalkCoachView` と `summary` にだけ入れる
  - `void CoachEnd()` — 会話を止めたとき (End): `summary.End(...)`、まとめを出す、`CountsForStreak` なら端末とクラウド (ログイン中・失敗は Console だけ) に数える。`OnApplicationPause` での止め方 (再開する止め方) ではまとめを出さない
- `TalkCoachView`: 上に単語の並び (使えた語に ✓)、`MessageWindow` の下に最新の添削 (✎ 言った文 → 直した文、`note_ja`)、まとめのパネル (`TalkSummary.Text()` と「Talk again」)。文字はほかの OnGUI と同じ大きさの決め方

- [ ] **Step 1: 実装する** (上の Interfaces どおり。読み上げは今の `tts.SpeakAsync(line, destroyCancellationToken)` と `messageWindow.ShowCharacterLine(line)`、口パクは今の `Update` のまま動く)
- [ ] **Step 2: コンパイルを確かめる** — `unitycheck_android.py` → `errors: 0`、`logictest.py` → `ALL LOGIC TESTS PASSED`、`wc -l Assets/Script/Talk3D/ConversationController*.cs` がどれも 600 行以内
- [ ] **Step 3: スマホで確かめる手順を `docs/vocabulary-next.md` の末尾に足す** (利用者が実機で見る):
  - 「I go to Osaka yesterday」と言う → 返事のあとに「By the way, you can say: I went to Osaka yesterday.」と言い、画面に ✎
  - 正しい文 → 言い直さず、すぐ聞く番になる
  - 機内モード → 添削なしで今まで通り話せる
  - 単語帳で何語かやってから Talk → 上に単語が並び、使うと ✓
  - 3 回話して End → まとめ、日記の画面の連続記録が今日になる。2 回で End → 数えない
  - 返事の途中で End → 言い直し・つなぎが後から話し出さない

# 単語帳 (Vocabulary, M1〜M3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** アプリに「Vocab」タブ (Today / Words / Add / Settings) を足し、CEFR-J の単語を FSRS で復習し、端末の音声認識と Gemini で発音を見て、1 回終えた日を連続記録とカレンダー (紫) に数える。

**Architecture:** 純粋なロジック (FSRS・出題順・辞書の解析・発音の判定・CEFR-J・JSON・レベルの提案・Gemini の返事の読み取り) を `Vocab/` の小さなクラスに分けてテストし、MonoBehaviour (`VocabManager`) と 4 つの画面クラスがそれを使う。単語帳・履歴・辞書の結果は端末の JSON。クラウドは連続記録だけ (`complete_vocab_session`)。画面は `VocabScreenBuilder` (Editor) が Repeat と同じやり方で組み立てる。

**Tech Stack:** Unity 2022.3.62f3 / C# 9、Merriam-Webster Learner's Dictionary API、Gemini API (JSON スキーマ)、Supabase (PostgreSQL)、素の HTML + ES modules、Python 3 (変換スクリプト)。

**Spec:** `docs/superpowers/specs/2026-11-02-vocabulary-design.md` (`docs/vocabulary-handoff.md` の付録 A と同じ)

**リポジトリ:** Unity `C:\Users\haru0\club\class\es\My project` (`UNITY/`)、Web + SQL `C:\Users\haru0\club\class\es\database_learnApp` (`WEB/`)。**コミットは必ず `git commit -- <自分が変えたファイル>`**。push は利用者。

**テストの動かし方 (このパソコン):** `csc`・`psql` がパスに無いので、作業用フォルダ (scratchpad) のスクリプトで動かす。
- Unity の参照コンパイル: `python unitycheck.py` (Unity 同梱の dotnet + Roslyn で Assembly-CSharp / -Editor をコンパイルだけ。`Assets/Script` と `Assets/Editor` の新しい .cs も拾う) → 両方 `errors: 0`
- ロジック: `python logictest.py` (上のあと `Tests~/LogicTests/Program.cs` を `acs.dll` 参照でコンパイルし .NET 6 で実行) → `ALL LOGIC TESTS PASSED`
- SQL: `pgvenv/Scripts/python minipsql.py WEB/supabase/tests/<x>_test.sql` (同梱 Postgres + psql の代わり) → `ALL ... TESTS PASSED`
- Web: `pgvenv/Scripts/python WEB/tests/web_test.py` → `ALL WEB TESTS PASSED`
- Python: `cd UNITY/Tools~/cefrj && python -m unittest test_build_cefrj_words`

## Global Constraints

- 返事・コミットの説明は日本語。**UI に絵文字を使わない。** UI の文言は既存の画面に合わせて英語 (Gemini のアドバイスだけ日本語)。設計書の日本語の文言は次の英語にする:
  - タブ `Today` `Words` `Add` `Settings`。評価 `Didn't know` / `Hard` / `Knew it` / `Easy` (下に目安 `Next time` / `in 4 days` …)。説明 `If you couldn't remember it, tap "Didn't know".`
  - 1 段階目 `Clear` / `Close` / `Try again` (内部コード `clear` / `close` / `again`)。Gemini `Good` / `Close` / `Practice` (`good` / `close` / `practice`)
  - 学習中 `Learning`、わかった `Known`、`Back to learning`、`Delete`、`Add to my words`、`Not found`、`Score again`、`Practice weak words`、`Level up to B1?`、`My voice`、`Model`、`Sound`、`Say it`
  - 辞書につながらない: `Can't reach the dictionary, so today is review only.`。キー無し: `No dictionary key. Add merriamWebsterApiKey to ApiSecrets.json.`
- 著作権: Tatoeba のデータ・BBC などは git に入れない。CEFR-J の単語リスト (`CefrjWords.json`) は入れる。出典 (README と Settings): `CEFR-J Wordlist Version 1.5, Tono Laboratory, TUFS` / `Merriam-Webster's Learner's Dictionary`。
- キーは `ApiSecrets.json` だけ。**利用者の `ApiSecrets.json` を上書きしない。** `ApiSecrets.cs` に `merriamWebsterApiKey`・`geminiVocabApiKey` を足し、`ApiSecrets.example.json` と README に書く。
- `index.html` は CRLF のまま。Web/SQL を書き換える直前に `git status` と `git log -1` で利用者の変更が無いか確かめる。
- FSRS-4.5 の標準パラメータ `W = {0.4872, 1.4003, 3.7145, 13.8206, 5.1618, 1.2298, 0.8975, 0.031, 1.6474, 0.1367, 1.0461, 2.1072, 0.0793, 0.3246, 1.587, 0.2272, 2.8755}`。`F = 19/81`、`R = (1 + F·t/S)^-0.5`、`I = round(S/F·(r^-2 − 1))` を 1〜36500 に。
- 語数 5/10/15/20 (初期 10)、目標の想起率 80/85/90/95% (初期 90)、期限切れ復習 > 50 なら新しい単語なし、辞書の失敗 10 回連続で止める、Gemini 1 リクエスト最大 20 語、音声のキャッシュ最大 300 個。
- PlayerPrefs: `vocab_level` (`A1`..`B2`、空 = 未選択)、`vocab_session_size`、`vocab_retention` (int 80..95)、`vocab_level_set_at` (ISO UTC)、`vocab_stt_only` (1 = 録音と認識を同時にできない端末)。
- 端末のファイル: `persistentDataPath/vocab/deck.json`、`reviews.json`、`dict/<word>.json`、`audio/<file>.mp3`、`rec/<word>.wav`。時刻は UTC の ISO 8601 (`"o"`)。
- 連続記録: カレンダーの色 `#845ef7`、表示名 `単語帳` / `Vocabulary`。**金 = 5 種類全部、銀 = 3〜4 種類、銅 = 2 種類。**

## Review Focus

1. 単語帳がまだ空・CEFR-J の候補を全部入れ終えた・辞書のキーが無い → 回は「あるだけ」で始まり、0 語なら Start で理由を出して止まる (落ちない)。Task 4 `VocabQueue` のテスト「候補が無い → 新しい単語 0」と Task 9 の分岐。
2. 辞書の返事が変な形 (`hwi` が無い・`prs` が無い・`fl` が無い・`shortdef` が空・HTML で返る) → 例外にせず、取れたものだけの結果 (または Not found)。Task 3 のテスト「ipa なし」「fl なし」「空配列」「壊れた JSON は FormatException」。
3. `deck.json` が壊れている・古い (項目が足りない) → 空の単語帳として読み、壊れたファイルは `deck.broken.json` に退避して上書きしない。Task 5 のテスト「壊れた JSON → 空」「項目なし → 既定値」。
4. 同じ単語を大文字・前後の空白違いで追加 (`Apple ` と `apple`) → 1 つだけ。Task 5 `VocabDeck` のテスト。
5. 日付をまたいだ・端末の時計が戻った (`lastReviewAt` が未来) → 経過日数は 0 として計算し、NaN や負の間隔にならない。Task 2 のテスト「elapsed < 0 → 0 扱い」。

---

### Task 1: SQL — `complete_vocab_session` と `vocab` の種類

**Files:** Create `WEB/supabase/vocab.sql`、`WEB/supabase/tests/vocab_test.sql`。Modify `WEB/supabase/schema.sql` (kind 制約に `vocab`、`get_my_activity_days` に `vocab boolean` 列、コメント)、`WEB/supabase/topic.sql`・`WEB/supabase/repeat.sql` (kind 制約の付け直しの行に `vocab`)、`WEB/supabase/weeks.sql` (コメントに単語帳)、`WEB/README.md` (実行順に `vocab.sql`)。

**Interfaces (Produces, `authenticated` のみ実行可):**
- `complete_vocab_session(p_timezone text default 'Asia/Tokyo', p_taken_at timestamptz default null) returns table (streak integer, already_done boolean)` — `security definer`。日は `_practice_date(p_taken_at, p_timezone)`。`activity_days` に `(uid, day, 'vocab')`、`app.streak_source = 'vocab'` を付けて `streaks` を repeat と同じ規則で更新 (前の日 +1 / 空けば 1 / 同じ日・過去は変えない、best も)。未ログインは `28000`。
- `get_my_activity_days(p_from date, p_to date)` → `day, diary, summary, topic, repeat, vocab` (schema.sql と vocab.sql の両方で同じ定義。drop → create)。

- [ ] **Step 1: テストを書く** (`\i stub.sql` → schema → summary → topic → repeat → vocab): A で呼ぶ → `activity_days` に A の今日の `vocab` が 1 行、`streaks.current_streak = 1` / 同じ日にもう一度 → 行は 1 つ、streak 1、`already_done = true` / `p_taken_at` を翌日 (postgres で前日の行を作ってから) → streak +1 / 2 日空けて → 1 / B には行が入らない / 未ログイン (sub 空) は `28000` / `get_my_activity_days` の `vocab` 列が true、ほかの種類は false / anon は実行できない / schema・topic・repeat・vocab をもう一度 `\i` しても壊れず、`vocab` の行が残る → `\echo ALL VOCAB TESTS PASSED`
- [ ] **Step 2: 失敗を確認** (`minipsql.py vocab_test.sql` → `vocab.sql` が無い)
- [ ] **Step 3: 実装** (repeat.sql の `repeat_counts_as_study` と同じ作りの関数。kind 制約は 5 種類 `('diary','summary','topic','repeat','vocab')` を 4 ファイル全部で同じに)
- [ ] **Step 4: 通る** + `repeat_test` `topic_test` `sounds_test` `weeks_test` も PASS
- [ ] **Step 5: Commit** (WEB、パス指定) `単語帳: complete_vocab_session と vocab の日`

### Task 2: FSRS (`Fsrs`)

**Files:** Create `UNITY/Assets/Script/CorrectionDiary/Vocab/Fsrs.cs`。Test: `Tests~/LogicTests/Program.cs` (`VocabFsrsTests()` を Main に足す)。

**Interfaces (Produces):**
- `public struct FsrsMemory { public double Stability; public double Difficulty; }`
- `public static class Fsrs` : `double[] W`、`double Retrievability(double elapsedDays, double stability)`、`int IntervalDays(double stability, double retention)`、`FsrsMemory First(int rating)`、`FsrsMemory Next(FsrsMemory m, int rating, double elapsedDays)` (elapsed < 0 は 0)、`int PreviewDays(FsrsMemory? m, int rating, double elapsedDays, double retention)` (rating 1 → 0 = 次回)、`string IntervalLabel(int days)`

- [ ] **Step 1: テスト**: `First(1..4).Stability == W[0..3]`、`First(3).Difficulty == W[4]`、`First(1).Difficulty == min(10, W[4]+2·W[5])`、`First(4).Difficulty == W[4]−W[5]` / `IntervalDays(S, 0.9)` が S の ±1 (S = 3.7145, 13.8206, 100) / `IntervalDays(0.01, 0.9) == 1`、`IntervalDays(1e9, 0.9) == 36500` / `Retrievability(0, 5) == 1` / 同じ記憶 (S=10, D=5, 10 日後) で `Next(.., 2) < Next(.., 3) < Next(.., 4)` の安定度、`Next(.., 1).Stability < 10` / D は全評価の組合せ (D=1,5,10 × G=1..4) で 1〜10 / 経過 −3 日 = 0 日と同じ結果・NaN なし / 初回の目安 `PreviewDays(null, g, 0, 0.9)` が g=2<3<4 で増え、g=1 は 0 / `IntervalLabel`: 0→`Next time`、1→`in 1 day`、4→`in 4 days`、45→`in 2 months`、60→`in 2 months`、30→`in 1 month`、400→`in 1 year`、800→`in 2 years` (月 = 日/30、年 = 日/365、四捨五入。30 日以上で月、365 日以上で年)
- [ ] **Step 2: 失敗を確認** (`logictest.py` → `Fsrs` が無い)
- [ ] **Step 3: 実装** (式は Global Constraints と設計書 §4。D0(G) = W4 − (G−3)·W5、平均回帰 `D' = W7·D0(3) + (1−W7)·(D − W6·(G−3))`)
- [ ] **Step 4: 通る** (`ALL LOGIC TESTS PASSED`)
- [ ] **Step 5: Commit** `単語帳: FSRS-4.5 の計算`

### Task 3: 辞書の解析 (`MerriamWebsterParser`)

**Files:** Create `Vocab/DictionaryTypes.cs`、`Vocab/MerriamWebsterParser.cs`。Test: `Program.cs` (`VocabDictionaryTests()`)。

**Interfaces (Produces):**
- `public class DictEntry { string Headword; string Pos; string Ipa; string AudioUrl; List<string> Definitions; List<string> Examples; }`
- `public class DictionaryResult { bool Found; List<DictEntry> Entries; List<string> Suggestions; }`
- `public interface IDictionaryProvider { bool IsConfigured { get; } Task<DictionaryResult> LookupAsync(string word); }`
- `MerriamWebsterParser.Parse(string json, string word) : DictionaryResult` (壊れた JSON は `FormatException`)、`AudioUrl(string audio) : string` (空なら "")、`Clean(string text) : string`

- [ ] **Step 1: テスト** (架空の JSON を Program.cs に書く): 見つかった (`meta.id "apple:1"` と `"apple pie"` → `apple` の 1 件だけ、Headword は `hwi.hw` の `*` を除く、Pos `noun`、Ipa `ˈæpəl`、Definitions = shortdef、Examples は `def` の中の `vis` から最大 2) / 一致する見出しが無い → 最初のエントリー / 文字列の配列 `["apply","apple"]` → Found false、Suggestions 2 / `[]` → Found false、候補 0 / `Clean("{bc}a {it}red{/it} fruit {ldquo}x{rdquo} {sx|pome||}")` == `a red fruit “x” pome` / AudioUrl: `bixapple01`→`.../mp3/bix/bixapple01.mp3`、`ggask01`→`gg`、`3d00001`→`number`、`_aaa`→`number`、`apple001`→`a` (前は `https://media.merriam-webster.com/audio/prons/en/us/mp3/`) / ipa・prs・fl なし → Ipa "" AudioUrl "" Pos "" で落ちない / `"<html>"` → FormatException
- [ ] **Step 2: 失敗を確認** → **Step 3: 実装** (MiniJson で読む。`vis` は `def` の中を再帰で探す。`{a_link|w}` `{d_link|w|id}` `{sx|w||}` は w を残し、ほかの `{...}` は消し、空白を 1 つに) → **Step 4: 通る** → **Step 5: Commit** `単語帳: 辞書の返事の解析`

### Task 4: 出題・発音の判定・CEFR-J・レベルの提案

**Files:** Create `Vocab/VocabCard.cs` (データ)、`Vocab/VocabQueue.cs`、`Vocab/VocabSession.cs`、`Vocab/VocabPronJudge.cs`、`Vocab/CefrWordList.cs`、`Vocab/VocabLevel.cs`。Test: `Program.cs` (`VocabQueueTests()` `VocabPronTests()` `CefrTests()` `VocabLevelTests()`)。

**Interfaces (Produces):**
- `public class VocabCard` : `string Word` (小文字・前後の空白なし), `Cefr` (`A1`..`B2` or `—`), `Source` (`auto`/`search`/`manual`), `MyMeaning`, `Status` (`learning`/`known`), `DateTime? DueAt`, `double Stability`, `double Difficulty`, `DateTime? LastReviewAt`, `int Reps`, `int MissCount`, `DateTime? KnownAt`, `DateTime CreatedAt`, `string PronBest` (`""`/`again`/`close`/`clear`), `int PronAttempts`, `string GeminiPron` (`""`/`good`/`close`/`practice`)
- `public class VocabReview { string Word; int Rating; DateTime At; double ElapsedDays; double Stability; double Difficulty; bool IsNew; }`
- `VocabQueue.Plan(IList<VocabCard> deck, DateTime nowUtc, int size) : (List<VocabCard> cards, int newCount)` — 学習中 (`LastReviewAt ?? CreatedAt` の古い順) → 期限切れの known (`DueAt` の古い順) → size まで。期限切れが 50 を超えたら newCount 0。`VocabQueue.NewCandidates(CefrWordList list, string level, IEnumerable<string> deckWords, Random rng) : List<string>` (そのレベルで、単語帳にある語 (状態を問わない) を除いてシャッフル)
- `VocabSession` : `VocabSession(List<VocabCard> cards, double retention)`, `VocabCard Current`, `int Index`, `int Count`, `bool Done`, `VocabReview Rate(int rating, DateTime nowUtc)` (カードを更新して次へ。G=1: learning、MissCount+1、DueAt null。G≥2: known、DueAt = now + IntervalDays、KnownAt が無ければ now。Reps+1、LastReviewAt = now。同じ回では出し直さない), `int[] PreviewDays(DateTime nowUtc)` (4 つ)
- `VocabPronJudge.Judge(string target, string heard) : string` (`clear`/`close`/`again`)、`VocabPronJudge.Better(string a, string b) : string`
- `CefrWordList.Parse(string json)`, `string LevelFor(string word, IEnumerable<string> dictPos)`, `string LevelForPos(string word, string dictPos)` (一致なしは null), `IEnumerable<string> WordsAt(string level)` (一番低いレベルがそれの語)。品詞の対応: MW `noun verb adjective adverb preposition pronoun conjunction interjection` はそのまま、`definite article`/`indefinite article`/`determiner` → `determiner`、`auxiliary verb`/`modal verb` → `modal auxiliary` と `verb`、CEFR-J の `be-verb do-verb have-verb` → `verb`、`number` → MW `noun`/`adjective` どちらとも一致
- `VocabLevel.Next(string level) : string` (B2 → null)、`VocabLevel.ShouldSuggestUp(IEnumerable<VocabReview> reviews, DateTime levelSetAtUtc) : bool` (IsNew かつ At ≥ levelSetAt の新しい順 20 件がそろい、うち 16 件以上が rating ≥ 2)

- [ ] **Step 1: テスト**: Queue — 学習中 2・期限切れ 3・期限前 1・size 10 → 学習中 2 が先、期限の古い順に 3、newCount 5 / 期限切れ 51 → newCount 0、cards は 10 / 学習中 12 で size 10 → 10 枚・newCount 0 / NewCandidates は known・learning の語を含まない、レベル違いを含まない、同じ seed で同じ順、候補が無ければ空 / Session — G=1 で Status learning・MissCount 1・DueAt null・次のカードへ進み、最後まで同じ語が 2 度出ない / G=3 で known・DueAt > now・KnownAt セット / 2 回目の known で KnownAt は変わらない / Judge — `apple`×`I said apple.` → clear、`Apple`×`APPLE!` → clear、`apple`×`apples` → close (複数形は同じにしない)、`banana`×`bandana` → close、`banana`×`orange` → again、`""` → again、`ice cream`×`I like ice cream` → clear / Better(`close`,`clear`) = `clear` / CEFR — `{"w":"light","pos":"noun","cefr":"A2"}{"w":"light","pos":"adjective","cefr":"A1"}` で `LevelFor("light",["noun"]) == "A2"`、`["verb"]` (一致なし) → `A1` (一番低い)、無い語 → `—`、`LevelFor("the",["definite article"])` → determiner のレベル、`WordsAt("A1")` に light が入り A2 には入らない / Level — 20 件中 16 件が ≥2 → true、15 件 → false、19 件しかない → false、levelSetAt より前のものは数えない、`Next("B2") == null`
- [ ] **Step 2: 失敗を確認** → **Step 3: 実装** (編集距離は Levenshtein。正規化 = 小文字、a-z0-9 と空白だけ。2 語以上の target は heard の同じ語数の並びと比べる。close のしきい値 = max(1, target の長さ / 3) (整数)) → **Step 4: 通る** → **Step 5: Commit** `単語帳: 出題の順・発音の判定・CEFR-J・レベルの提案`

### Task 5: 端末の保存の形 (`VocabJson` と `VocabDeck`)

**Files:** Create `Vocab/VocabJson.cs`、`Vocab/VocabDeck.cs`。Test: `Program.cs` (`VocabJsonTests()`)。

**Interfaces (Produces):**
- `VocabJson.DeckToJson(IList<VocabCard>) : string` / `DeckFromJson(string) : List<VocabCard>` (壊れた・null → 空、項目なし → 既定値)、`ReviewsToJson` / `ReviewsFromJson`、`DictToJson(DictionaryResult)` / `DictFromJson(string)` (壊れたら null)。キーは設計書 §2 の名前 (`word, cefr, source, myMeaning, status, dueAt, stability, difficulty, lastReviewAt, reps, missCount, knownAt, createdAt, pronBest, pronAttempts, geminiPron`)。数は InvariantCulture。
- `VocabDeck` : `VocabDeck(List<VocabCard> cards)`, `List<VocabCard> Cards`, `static string Key(string word)` (小文字・trim・空白 1 つ), `VocabCard Find(string word)`, `VocabCard Add(string word, string source, string myMeaning, string cefr, DateTime nowUtc)` (同じ語があれば null), `bool Remove(string word)`, `void MoveToLearning(string word)` (DueAt null), `List<VocabCard> List(string status, string levelFilter)` (`all` / `A1`..`B2` / `—`。learning は新しく足した順、known は DueAt の近い順)

- [ ] **Step 1: テスト**: 全項目の往復 (null の日時・日本語の myMeaning・`"` と改行を含む文字) / 壊れた JSON・`null`・`{}` → 空 / `[{"word":"x"}]` → Status `learning`、Reps 0、DueAt null / Reviews 往復 / Dict 往復 (Entries 2・Suggestions)、壊れたら null / Deck — `Add("Apple ")` のあと `Add("apple")` は null で 1 件 / Remove / MoveToLearning で DueAt null・Status learning / List の絞り込み (`—` の語だけ、A2 だけ)
- [ ] **Step 2: 失敗を確認** → **Step 3: 実装** (読みは MiniJson、書きは StringBuilder + `HttpUtil.JsonEscape`) → **Step 4: 通る** → **Step 5: Commit** `単語帳: 単語帳と履歴の保存形式`

### Task 6: CEFR-J の変換スクリプトと同梱データ

**Files:** Create `UNITY/Tools~/cefrj/build_cefrj_words.py`、`UNITY/Tools~/cefrj/test_build_cefrj_words.py`、`UNITY/Assets/Resources/CefrjWords.json`。(元の CSV は git に入れない: `Tools~/cefrj/*.csv` を `.gitignore` に足す)

**Interfaces (Produces):** `CefrjWords.json` = `[{"w":"a","pos":"determiner","cefr":"A1"}, ...]` (1 行 1 語、w の小文字・前後空白なし、`/` の別形は最初の形、同じ (w,pos) は一番低いレベル、A1〜B2 だけ、w→pos の順に並べる)。`build(rows) -> list[dict]` と CLI `--csv <in> --out <out>`。

- [ ] **Step 1: テスト** (架空の CSV): ヘッダーを飛ばす / `a.m./A.M./am/AM` → `a.m.` / `Light` の B1 と A2 (同じ pos) → A2 だけ / C1 の行は入らない / 空の headword は飛ばす / 出力の並び
- [ ] **Step 2: 失敗を確認** → **Step 3: 実装** → **Step 4: 通る**
- [ ] **Step 5: 本物の CSV (https://raw.githubusercontent.com/openlanguageprofiles/olp-en-cefrj/master/cefrj-vocabulary-profile-1.5.csv) を `Tools~/cefrj/` に落として変換** → 約 7,700 行。`CefrWordList.Parse` で読めることを Program.cs のテストで確かめる (ファイルを読んで A1 の語が 1,000 以上、`apple` がある)
- [ ] **Step 6: Commit** `単語帳: CEFR-J の単語リスト (出典つき) と変換スクリプト`

### Task 7: Gemini のまとめ採点

**Files:** Modify `GeminiApiClient.cs`。Test: `Program.cs` (`SchemaTests` に足す、`VocabGeminiTests()`)。

**Interfaces (Produces):**
- `public const string VocabPronSchema` — ARRAY of OBJECT `{word STRING, rating STRING enum [good, close, practice], advice STRING, sounds ARRAY of STRING enum [th, r_l, v_b, f_h, s_sh, vowel, long_short, final_consonant, extra_vowel, stress, other]}`、required `word rating advice`
- `public class WordPronResult { string Word; string Rating; string Advice; List<string> Sounds; }`
- `public static List<WordPronResult> ParseWordPronunciations(string json, IList<string> words)` — 送った語に無い語・知らない rating は捨てる、知らない sound は捨てる、同じ語は最初だけ
- `public async Task<List<WordPronResult>> CheckWordPronunciationsAsync(IList<string> words, IList<byte[]> wavs)` — 最大 20。parts = 指示文 + 語ごとに `TextPart("Word N: <word>") + AudioPart(wav)`。待つ時間 `AudioGradeTimeoutSeconds`。指示: 日本語の学習者、各語について rating と日本語 1〜2 文のアドバイスと苦手な音コード (既存の 10 種類 + other)。

- [ ] **Step 1: テスト**: スキーマが JSON として読める (既存 SchemaTests に追加) / `[{"word":"apple","rating":"good","advice":"よい","sounds":[]},{"word":"zzz",...},{"word":"Banana","rating":"bad",...},{"word":"cat","rating":"practice","advice":"a","sounds":["th","xx"]}]` を `["apple","banana","cat"]` で読む → apple と cat の 2 件、cat の Sounds = `["th"]`
- [ ] **Step 2〜4** (失敗 → 実装 → 通る) → **Step 5: Commit** `単語帳: Gemini のまとめ採点`

### Task 8: Unity の部品 — 辞書の通信・保存・音声・発音の取り込み・連続記録

**Files:** Create `Vocab/MerriamWebsterClient.cs`、`Vocab/VocabStore.cs`、`Vocab/VocabAudio.cs`、`Vocab/VocabPronCapture.cs`。Modify `ApiSecrets.cs` (`merriamWebsterApiKey`、`geminiVocabApiKey`)、`UNITY/ApiSecrets.example.json` (2 項目)、`SupabaseService.cs` (`CompleteVocabSessionAsync`)。確認: `unitycheck.py` (ロジックではないので単体テストなし)。

**Interfaces (Produces):**
- `MerriamWebsterClient : IDictionaryProvider` (`new MerriamWebsterClient(string apiKey)`)。URL `https://www.dictionaryapi.com/api/v3/references/learners/json/<Uri.EscapeDataString(word)>?key=<key>`。`HttpUtil.GetAsync`、429・5xx は 1 秒・2 秒で 2 回まで再試行。
- `VocabStore` (static): `VocabDeck LoadDeck()`、`void SaveDeck(VocabDeck)` (一時ファイルに書いて置き換え。読めない deck.json は `deck.broken.json` へ)、`void AppendReview(VocabReview)`、`List<VocabReview> LoadReviews()`、`DictionaryResult LoadDict(string word)` (無ければ null)、`void SaveDict(string word, DictionaryResult)`、`string RecPath(string word)`、`void DeleteAllRecordings()`、`string AudioDir`
- `VocabAudio : MonoBehaviour` : `Task PlayWordAsync(string word, string audioUrl, CancellationToken ct)` (mp3 を `audio/` に保存して使い回し、`AudioCachePolicy.ToDelete` で 300 個まで。取れないときは `AndroidTts` / Editor は `EditorDummyTts`)、`void PlayClip(AudioClip)`、`void Stop()`
- `VocabPronCapture` (plain class): `VocabPronCapture(AudioRecorder recorder, NativeSpeechRecognizer stt, GeminiApiClient gemini)`、`Task<bool> StartAsync()` (権限を取り、`vocab_stt_only` でなければ録音 → 認識を同時に始める。認識が始まらない・1.5 秒以内にエラーなら録音を止め、`vocab_stt_only = 1` にして認識だけで始め直す。認識が使えない端末 (Editor) は録音だけ)、`Task<(string heard, byte[] wav)> StopAsync()` (認識だけのときは wav null。Editor は wav を `gemini.TranscribeAsync` で文字にする)、`void Cancel()`
- `SupabaseService.CompleteVocabSessionAsync(DateTime takenAtUtc) : Task` — RPC `complete_vocab_session`、`,"p_taken_at":"<o>"`

- [ ] **Step 1: 実装** → **Step 2: `unitycheck.py` → errors 0、`logictest.py` PASS** → **Step 3: Commit** `単語帳: 辞書の通信・端末の保存・音声・発音の取り込み`

### Task 9: 画面の動き (`VocabManager` と 4 つの画面)

**Files:** Create `Vocab/VocabManager.cs` (MonoBehaviour: 部品の用意、タブの切り替え、共有の deck・CEFR-J・辞書)、`Vocab/VocabTodayView.cs`、`Vocab/VocabWordsView.cs`、`Vocab/VocabAddView.cs`、`Vocab/VocabSettingsView.cs` (どれも plain class、`ShadowSession` と同じく View 構造体で UI を受け取る)。確認: `unitycheck.py`。

**Interfaces:**
- Consumes: Task 2〜8 の全部。`DiaryManager.CountOtherPracticeAsStudyDay()` / `RefreshStreakFromCloud()`。
- Produces (Task 10 が組み立てで使う `VocabManager` の [SerializeField] 名): タブ `todayTab wordsTab addTab settingsTab`、パネル `todayPanel wordsPanel addPanel settingsPanel`、共通 `statusText loadingIndicator`。Today: `levelPickRow`(GameObject) `levelPickButtons`(Button[4] A1..B2) `startButton startLabel progressText cardButton cardText soundButton sayButton sayLabel ratingRow ratingButtons`(Button[4]) `ratingLabels`(TMP_Text[4]) `ratingHint summaryScroll summaryContent summaryRowTemplate`(GameObject: 子に `Text` `MyVoice` `Model`) `rescoreButton weakButton levelUpButton`。Words: `learningToggle knownToggle filterButtons`(Button[6] All A1 A2 B1 B2 —) `wordsContent wordRowTemplate`(Button + 子 TMP) `dialogPanel dialogText dialogButtons`(Button[3]) `dialogLabels`(TMP_Text[3])。Add: `searchInput searchButton resultText addResultButton suggestionContent suggestionTemplate manualWordInput manualMeaningInput manualAddButton`。Settings: `settingsLevelButtons`(Button[4]) `sizeButtons`(Button[4] 5/10/15/20) `retentionButtons`(Button[4] 80/85/90/95) `creditText`。`supabase`。
- 動き (設計書 §1・§5・§7 のとおり。ここに無い決めごと):
  - Start: Plan → 既存カードの辞書は `LoadDict` (無ければ取りに行く、失敗しても続ける) → 新しい単語は `NewCandidates` を順に引き、`Found` のものだけ、10 回続けて失敗 (Not found も通信失敗も) で止める。通信失敗が 1 回でもあれば下に「Can't reach the dictionary…」。0 枚なら Start に戻して理由 (`No words yet. Add words or check the dictionary key.`)。新しい単語は引けた時点で `deck.Add(word, "auto", "", LevelFor(...))` (ここではまだ保存しない。評価したときに保存)。
  - 評価したら毎回 `SaveDeck` と `AppendReview` (タブを移っても評価済みは残る。録音は捨てる)。ほかのタブへ移ったら回は終わり (次の Start は最初から)。
  - 発音の判定の表示: `Clear` 緑 `#40c057`、`Close` 黄 `#fab005`、`Try again` 赤 `#fa5252` と `Heard: "..."`。`PronBest = Better(...)`、`PronAttempts+1`。録音は単語ごとに最後だけメモリと `rec/<word>.wav` に。
  - 最後の評価のあと まとめ → `CountOtherPracticeAsStudyDay()`、ログイン中 (`EnsureSignedInAsync`) なら `CompleteVocabSessionAsync` → `RefreshStreakFromCloud()`、失敗は `Debug.LogWarning` だけ → Gemini (録音のある語だけ、`GeminiPron` を保存、届いたら行を書き直す、終わったら `rec/` を消す。失敗なら `Score again`)。
  - `Practice weak words`: 1 段階目が close/again か Gemini が practice の語。評価ボタンを隠した発音だけのカード。結果は `PronBest`・`PronAttempts` に保存。Gemini は呼ばない。
  - レベル: 初回 (`vocab_level` が空) は Today に `levelPickRow` だけ。提案は `ShouldSuggestUp` が true で B2 でないとき `Level up to <Next>?`、押したら `vocab_level` と `vocab_level_set_at` を更新。
  - Awake で `VocabStore.DeleteAllRecordings()`。
- [ ] **Step 1: 実装** → **Step 2: `unitycheck.py` errors 0、`logictest.py` PASS** → **Step 3: Commit** `単語帳: Today / Words / Add / Settings の動き`

### Task 10: 画面の組み立て (`VocabScreenBuilder`)

**Files:** Create `UNITY/Assets/Editor/VocabScreenBuilder.cs`。確認: `unitycheck.py`。

- メニュー `Tools/Vocabulary/Create Vocabulary Screen`。`RepeatScreenBuilder` と同じ作り (同じ部品関数を写す、`vocab screen` / `vocab manager` / タブ `Vocab` を Repeat の右に、ほかの画面へ切り替えるボタンに「vocab screen を隠す」)。Task 9 の [SerializeField] を全部つなぐ。入力欄は `TMP_DefaultControls.CreateInputField`。テンプレートは非表示で置く。manager に `AudioSource`・`VocabAudio`・`AudioRecorder`・`NativeSpeechRecognizer` を付ける。絵文字を使わない (既存の builder の ⚠ はダイアログの文なので、新しいファイルでは使わない)。
- [ ] **Step 1: 実装** → **Step 2: `unitycheck.py` errors 0** → **Step 3: Commit** `単語帳: 画面を組み立てるメニュー`

### Task 11: Web — カレンダーの紫と 5 種類の金

**Files:** Modify `WEB/index.html` (CSS `.cal-day.vocab { --flame: #845ef7; }`、凡例 `単語帳`/`Vocabulary` と `2種類` `3〜4種類` (`3-4 kinds`) `5つ全部` (`All 5`)、`kinds` に `a.vocab && tr("単語帳", "vocabulary")`、金 = 5・銀 = 3〜4・銅 = 2、`KIND_INFO` に `["vocab", "#845ef7", "単語帳", "Vocabulary"]`、`FLAME_COLORS` を 5/4/3/2 に、コメント)、`WEB/src/streak-client.js` (`vocab: !!r.vocab`)、`WEB/tests/web_test.py`。

- [ ] **Step 1: テストを変える**: `ACTIVITY` に `vocab` を足す (5 種類全部の日 = 金 1、4 種類の日 = 銀、3 種類 = 銀、2 種類 = 銅、単語帳だけ = 紫 1)。「月初めだと前の月に入る」問題を直す: テストの日を「今月の 15 日」に固定し、ブラウザの時計も `page.clock.set_system_time` でそこにする (今の失敗はこれが原因)。紫の色 `#845ef7`、凡例に `単語帳`、「この月にやったこと」に `単語帳` / `Vocabulary`
- [ ] **Step 2: 失敗を確認** → **Step 3: 実装** (CRLF を保つ) → **Step 4: `web_test.py` → ALL WEB TESTS PASSED** → **Step 5: Commit** (WEB) `単語帳: カレンダーの紫と 5 種類の金`

### Task 12: 仕上げ

**Files:** Modify `UNITY/README.md` (Vocab の節、キー 2 つの表、出典、スクリプト構成)、`UNITY/.gitignore` (`Tools~/cefrj/*.csv`)、`WEB/README.md` (単語帳の色)。

- [ ] **Step 1: 全テスト** (ロジック・Python・SQL 5 本・Web・参照コンパイル)
- [ ] **Step 2: `git status` と `git diff --cached` で `ApiSecrets.json`・`RepeatSentences.json`・Tatoeba・キーが入っていないことを確かめる**
- [ ] **Step 3: 別の目で全体レビュー** (superpowers:requesting-code-review)。重大なものは直す
- [ ] **Step 4: Commit** と、実機で確かめることの一覧 (画面の見た目、辞書・Gemini の実通信、録音と認識の同時使用、`.meta` の commit、`vocab.sql` の実行、APK)

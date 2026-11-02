# 単語帳 (Vocabulary) — 設計 (M1〜M3)

日付: 2026-11-02
もとにした PRD: 「PRD: 単語帳機能 (英語日記アプリ 追加機能)」(利用者作成、2026-11-02)
対象: Unity アプリ (`My project`)、Supabase / Web (`database_learnApp`、連続記録とカレンダーだけ)

## 範囲

今回作るのは PRD の **M1〜M3** と、連続記録への組み込み。

| 入れる | 入れない (あとで) |
|---|---|
| 今日の単語 (CEFR-J から自動、A1〜B2)、めくるカード、4 つの評価ボタン、FSRS の復習日程、わかったフォルダ・学習中 (M1) | 例文の AI 生成・動画の使用例 (M4) |
| 発音チェック: 1 段階目 = 端末の音声認識、2 段階目 = Gemini のまとめ採点とアドバイス (M2) | 単語帳のクラウド同期・復習履歴のクラウド保存 (M5) |
| 単語の追加 (検索して追加・手入力)、単語帳画面、設定 (レベル・語数・目標の想起率・出典) (M3) | リマインダーの復習数、C1/C2 (M6・M7)、苦手な発音への集計 (P2) |
| 1 回分を終えた日を連続記録・カレンダーに数える | Web の単語帳ページ |

## 決めたこと (利用者との合意)

| 項目 | 決定 |
|---|---|
| 範囲 | M1〜M3 (端末内) |
| CEFR-J | アプリに同梱して git にも入れる。出典をアプリと README に書く (配布元の条件: 研究・商用とも無料、出典の明記が必要) |
| 連続記録 | 数える (リピーティングと同じ扱い)。カレンダーに新しい色 |
| 発音の録音 | PRD 通り: 端末の音声認識と同時に録音。同時にできない端末では、認識だけ (まとめの Gemini のアドバイスは無し) |

PRD の未決事項のうち、ここで決めたもの (理由つき):
- 1 回の語数: 設定で 5 / 10 / 15 / 20、初期値 10 (未決 5)。「次回」= 次に単語帳で Start したとき。
- 「わからなかった」は同じ回では出し直さない (未決 17)。次の回の最初に出す。
- 1 段階目が「もう一度」でも評価ボタンは押せる (未決 3)。発音の結果は単語に別に記録する。
- FSRS の標準パラメータ (FSRS-4.5) を端末で計算 (未決 6)。目標の想起率 90% (設定で 80〜95%)。期限が来た復習が 50 語を超えたら新しい単語は出さない。
- レベル: 初回に A1 / A2 / B1 / B2 から選ぶ (中間のレベルは作らない、未決 4)。直近 20 個の新しい単語の評価のうち 8 割以上が「わからなかった」以外なら、まとめ画面で「レベルを上げる」を提案する。
- 辞書に音声が無い単語は端末の読み上げ (未決 7。Gemini の音声合成は使わない)。
- 発音記号: Learner's Dictionary の `ipa` を表示 (未決 25)。無ければ出さない。
- 例文: 辞書にあるものだけ (AI の例文は M4)。
- Gemini への送り方: 1 回分の録音 (最大 20 語) をまとめて 1 リクエスト (未決 15)。キー `geminiVocabApiKey` (空なら日記のキー、上限ならほかを借りる)。
- 辞書の先読みはしない (未決 24): Start したときに、その回の単語だけを 1 語ずつ取る。取った結果は端末に保存して二度と取らない。
- カードはタップでめくる (未決 16。スワイプは無し)。
- 辞書のキー `merriamWebsterApiKey` は `ApiSecrets.json` (git に入らない)。APK は配らない前提 (未決 1・12)。

## 1. 画面 (新しいタブ「Vocab」)

Editor のメニュー **Tools → Vocabulary → Create Vocabulary Screen** (`VocabScreenBuilder.cs`) で、Repeat と同じようにシーンに組み立てる。上に 4 つの切り替え: **Today / Words / Add / Settings**。

### Today (今日の単語)
- 初めて開いたとき: レベルを選ぶ (A1 / A2 / B1 / B2) → 設定に保存。
- **Start** → その回の単語を決める: ①学習中 ②期限が来た復習 (期限の古い順) ③新しい単語 (語数に足りない分。復習の期限切れが 50 語を超えたら出さない)。新しい単語は、CEFR-J の自分のレベルの単語から、まだ単語帳に無いものをランダムに選び、辞書で引けたものだけ使う (引けなければ次の候補。10 回続けて失敗したら、そこで止めて揃った分で始める)。
- **カードの表**: 単語、CEFR レベル、発音記号、[音声] ボタン、[発音してみる] ボタン、1 段階目の判定 (通じた / 惜しい / もう一度) と聞き取れた文字列。
- **カードの裏** (カードをタップでめくる。もう一度タップで表): 品詞ごとの英語の意味 (各品詞の CEFR レベルつき)、自分の意味、例文 (辞書にあれば)、4 つのボタン:
  - わからなかった (次回) / 難しかった (n 日後) / わかった (n 日後) / 簡単だった (n 日後)。n は FSRS で計算した目安。
  - 説明の 1 行: 「思い出せなかったときは『わからなかった』を押してください」
- 語数が終わったら **まとめ**:
  - 単語ごとに: 1 段階目の判定、Gemini の判定 (良い / 惜しい / 要練習) と日本語のアドバイス・苦手な音 (届いた単語から順に足す)、[My voice] [お手本]。
  - Gemini が失敗したら [もう一度採点] ボタン。録音が無い (同時にできない端末・話さなかった) 単語は Gemini 欄なし。
  - [苦手な単語を練習]: 「惜しい / もう一度 / 要練習」の単語だけ、発音だけのカード (評価ボタンなし) でもう一度。
  - レベルを上げる提案 (条件を満たしたとき)。
  - 保存: 連続記録に数える (ログイン中はクラウド、していなければ端末の連続記録だけ)。
- 途中でほかのタブへ移っても、評価した単語の結果はその時点で保存済み。録音は捨てる。

### Words (単語帳)
- 「学習中」「わかった (次の復習日つき)」の切り替えと、レベルでの絞り込み (すべて / A1〜B2 / —)。
- 各行: 単語、レベル、次の復習日、「わからなかった」の回数、発音の最高判定。行をタップ → 操作: [学習中に戻す] [削除] (確認つき)。

### Add (単語を追加)
- 検索: 単語を入れて Search → 辞書の結果 (見出し・品詞・発音記号・意味・音声) → [単語帳に追加]。見つからないときは「見つかりません」と、辞書が返した似た単語の候補 (タップで検索し直し) と、手入力への案内。
- 手入力: 単語と自分の意味 (日本語可) → [追加]。辞書で引けたら情報も付ける。
- 追加した単語は学習中に入り、次の回の最初に出る。同じ単語 (見出し語が同じ) は重複して入らない。

### Settings
- 自分のレベル、1 回の語数、目標の想起率、出典の表示 (CEFR-J Wordlist Version 1.5, Tono Laboratory, TUFS / Merriam-Webster's Learner's Dictionary)。

## 2. データ

### 同梱データ
- `Assets/Resources/CefrjWords.json`: CEFR-J Vocabulary Profile 1.5 (olp-en-cefrj の CSV) を変換したもの。`[{w, pos, cefr}]`。見出しに `/` で別形 (a.m./A.M.) があるものは最初の形を使う。git に入れる。変換スクリプトは `Tools~/cefrj/build_cefrj_words.py`。
- 単語のレベル: 辞書の品詞と照合し、一致しなければ一番低いレベル。CEFR-J に無い単語は「—」。

### 端末の保存 (persistentDataPath)
- `vocab/deck.json`: 単語帳。1 単語 = `{word, cefr, source (auto/search/manual), myMeaning, status (learning/known), dueAt, stability, difficulty, lastReviewAt, reps, missCount, knownAt, createdAt, pronBest, pronAttempts, geminiPron}`。見出し語 (小文字) で重複させない。
- `vocab/reviews.json`: 評価の履歴 `{word, rating 1-4, at, elapsedDays, stability, difficulty}` (消さない。M5 でクラウドへ)。
- `vocab/dict/<word>.json`: 辞書の結果 (意味・品詞・発音記号・音声 URL・例文)。一度取ったら取り直さない。
- `vocab/audio/<file>.mp3`: 再生した音声 (最大 300 個、古いものから消す)。
- `vocab/rec/<word>.wav`: 発音の録音。Gemini の採点が終わったら消す。回が終わってアプリを閉じたら次の起動時に消す。
- 設定は PlayerPrefs (`vocab_level`, `vocab_session_size`, `vocab_retention`)。

### クラウド (連続記録だけ)
- `supabase/vocab.sql`: `complete_vocab_session(p_timezone, p_taken_at)` → `activity_days` に `vocab` を入れ、リピーティングと同じ規則で連続記録を進める (次の日なら +1、空けば 1、同じ日は変えない)。
- `activity_days.kind` の制約に `vocab` を足す (schema.sql / topic.sql / repeat.sql の付け直しの行も)。
- Web: カレンダーに単語帳の色 (紫 `#845ef7`) と凡例、「やったこと」一覧に「単語帳」/ "Vocabulary"。**金色は「5 種類全部」**、銀 = 3 種類以上、銅 = 2 種類 (要確認: 4 種類で金のままにするか)。

## 3. 辞書 (Merriam-Webster Learner's Dictionary)

- `IDictionaryProvider.LookupAsync(word) : Task<DictionaryResult>` (`Found`, `Entries[{headword, pos, ipa, audioUrl, definitions[], examples[]}]`, `Suggestions[]`)。実装 `MerriamWebsterClient` (`https://www.dictionaryapi.com/api/v3/references/learners/json/<word>?key=...`)。
- 解析は純粋な関数 `MerriamWebsterParser.Parse(json, word)`:
  - 文字列の配列が返ったら「見つからない」、その文字列を候補にする。
  - 見出し (`meta.id` の `:` より前) が単語と一致するエントリーだけ使う (無ければ最初のエントリー)。
  - 意味は `shortdef`、例文は `def` の `vis` (最大 2)。`{it}` `{bc}` `{wi}` などの記号を取り除く。
  - 音声: `hwi.prs[].sound.audio` → `https://media.merriam-webster.com/audio/prons/en/us/mp3/<フォルダ>/<audio>.mp3` (フォルダ: `bix` で始まれば bix、`gg` で始まれば gg、数字・記号で始まれば number、それ以外は先頭の 1 文字)。
- 通信は `HttpUtil` (429・5xx は再試行)。キーが無いときは「Settings に辞書のキーが無い」と表示。

## 4. 復習の日程 (FSRS-4.5、純粋なロジック `Fsrs`)

- 評価 G: 1 = わからなかった、2 = 難しかった、3 = わかった、4 = 簡単だった。標準パラメータ w0〜w16 (FSRS-4.5)。
- 想起率 `R(t, S) = (1 + 19/81 · t / S)^(−0.5)`、間隔 `I = S / (19/81) · (r^(1/−0.5) − 1)` (r = 目標の想起率、日に丸めて最低 1 日、最長 36500 日)。
- 初めて: `S = w[G−1]`、`D = w4 − (G−3)·w5` (1〜10 に収める)。
- 2 回目以降: `D' = w7·D0(3) + (1−w7)·(D − w6·(G−3))`。思い出せた (G ≥ 2): `S' = S · (e^w8 · (11−D) · S^(−w9) · (e^(w10·(1−R)) − 1) · (G=2 なら w15) · (G=4 なら w16) + 1)`。わからなかった: `S' = w11 · D^(−w12) · ((S+1)^w13 − 1) · e^(w14·(1−R))`。
- G = 1: 学習中に戻し、`missCount + 1`、次の回の最初に出す (dueAt は無し)。G ≥ 2: わかったに入れ、`dueAt = 評価した時刻 + I 日`、初めてなら `knownAt`。
- ボタンの目安: 押したときの `I` を「1日後」「4日後」「2か月後」のように表示 (1 → 次回)。

## 5. 発音チェック

### 1 段階目 (その場)
- [発音してみる] → 録音を始めてから `NativeSpeechRecognizer` で聞き取る。認識が始まらない・すぐエラーになる端末では録音を止めて、認識だけでもう一度始める (この端末では次から認識だけ。PlayerPrefs に覚える)。
- 判定 `VocabPronJudge.Judge(target, heard)`: 聞き取れた語のどれかが単語と同じ (大文字・記号を無視。複数形・-ing などは同じにしない) → 通じた。単語との編集距離が max(1, 長さの 1/3) 以下の語がある → 惜しい。それ以外・何も聞き取れない → もう一度。
- 単語ごとに最後の録音だけ残す。結果は `pronBest` (一番良い判定)・`pronAttempts` に保存。

### 2 段階目 (まとめ)
- 回が終わったら、録音のある単語 (最大 20) を 1 リクエストで Gemini に送る (`GeminiApiClient.CheckWordPronunciationsAsync(words, wavs)`)。返事は JSON スキーマで固定: `[{word, rating: good|close|practice, advice (日本語 1〜2 文), sounds: [code (既存の 10 種類 + other)]}]`。
- 届いたらまとめ画面に足す。失敗したら [もう一度採点]。採点が終わったら録音を消す (My voice の再生用に、まとめ画面を閉じるまではメモリに残す)。
- 判定は目安として表示し、アドバイスを中心にする。

## 6. 既存部品の再利用

`NativeSpeechRecognizer`、`AudioRecorder`、`HttpUtil`、`GeminiApiClient` (キーの借り方)、`AndroidTts` / `EditorDummyTts` (`ITextToSpeech`)、`DiaryManager.CountOtherPracticeAsStudyDay` / `RefreshStreakFromCloud`、`SupabaseService.RpcAsync`、タブの組み立て方 (RepeatScreenBuilder と同じ)。

## 7. エラーとオフライン

- 辞書が引けない (通信なし): 保存済みの単語 (学習中・復習) だけで回を始める。新しい単語は「辞書につながらないので、今日は復習だけです」。
- 音声が取れない → 端末の読み上げ。
- Gemini の上限・失敗 → [もう一度採点]、回の結果 (評価) はもう保存済み。
- 連続記録の保存に失敗 → Console だけ (練習は止めない)。ログインしていなければ端末の連続記録だけ。

## 8. テスト

- ロジック (`Tests~/LogicTests`): `Fsrs` (初回 4 つの S・D、間隔の順 1 < 2 < 3 < 4、r = 0.9 で I ≈ S、わからなかったで S が下がる、D が 1〜10、目安の文字)、`VocabQueue` (学習中 → 期限切れ → 新しい単語、50 語超で新しい単語なし、わかったは再び新しい単語にならない、同じ回で出し直さない)、`MerriamWebsterParser` (架空の JSON: 見つかった・候補だけ・記号の除去・音声のフォルダ 4 種・ipa なし)、`VocabPronJudge` (通じた / 惜しい / もう一度)、`CefrWordList` (品詞の照合・一番低いレベル・無い単語)、レベルの提案 (20 個中 16 個)、Gemini のスキーマが JSON、変換スクリプトの Python テスト (架空の CSV)。
- SQL (`vocab_test.sql`): `complete_vocab_session` で `vocab` の日が入る・連続記録が進む・同じ日に 2 回呼んでも 1 回分・ほかの人に入らない・何度実行しても壊れない、ほかの .sql を再実行しても `vocab` の行で壊れない。
- Web: カレンダーの紫・凡例・金銀銅 (5 種類)・「やったこと」の「単語帳」。
- 画面・音声・録音と認識の同時使用・辞書と Gemini の実際の通信は実機で確かめる。

## やらないこと (今回)

例文の AI 生成と動画の使用例 (M4)、クラウド同期 (M5)、リマインダー (M6)、C1/C2 (M7)、苦手な発音への集計 (P2)、日本語訳の自動表示、スワイプ、iOS の確認。

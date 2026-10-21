# お題スピーキング・言い直し・よくあるミス集計 — 設計

日付: 2026-10-21
対象: Unity アプリ (`My project`)、Web (`database_learnApp`)、Supabase

## 目的

英語のスピーキング力を伸ばすために、次の3つを追加する。

1. **お題スピーキング**: お題に対して準備なしで話し、添削・採点を受ける。
2. **言い直し**: 添削された文を声に出して読み、合っているかを確かめる (お題スピーキングの中だけ)。
3. **よくあるミスの集計**: 日記・要約・お題の添削で出たミスを種類ごとに貯め、Web で苦手を見せ、お題と採点にも使う。

成功の基準:
- お題 → 考える → 話す → 採点 → 言い直し、が1画面で途切れずにできる。
- 苦手 (ミスの多い種類) が Web で分かり、アプリの「今日の注意」と採点の重点チェックに反映される。
- お題をやった日も連続記録・カレンダーに数えられる。

## 決めたこと (利用者との合意)

| 項目 | 決定 |
|---|---|
| お題の種類 | 試験 (Exam) / 身近 (Daily) / 仕事・面接 (Work) を混ぜて出す |
| 話す時間 | 種類で自動: Daily 45秒、Exam・Work 60秒。考える時間は 15秒 (スキップ可) |
| 言い直しの判定 | 文字で照合 (音声認識 → 単語ごとに比較) が基本。ボタンを押したときだけ Gemini に発音コメントを頼む |
| 言い直しを出す練習 | お題スピーキングだけ |
| ミスの使い道 | Web 表示 + 話す前の「今日の注意」+ 採点で苦手を重点チェック (前回との比較つき) |
| ミスの集め方 | 添削のついでに Gemini に分類させる。DeepL Write で添削した日記は集計に入らない |
| お題の出し方 | Gemini に毎回作らせる (苦手と最近のお題を渡す)。通信できなければアプリ内のリストから |
| カレンダーの色 | 日記=オレンジ、要約=青、お題=緑、2種類=紫、3種類全部=金 |

## ミスの種類 (固定 10 種類)

Gemini にはこのコードから選ばせる。表示名はアプリ・Web で持つ。

| コード | 表示名 (Web / アプリ) |
|---|---|
| `article` | 冠詞 (a / the) / Articles |
| `tense` | 時制 / Tense |
| `preposition` | 前置詞 / Prepositions |
| `agreement` | 主語と動詞の一致 / Subject-verb agreement |
| `plural` | 単数・複数 / Singular & plural |
| `verb_form` | 動詞の形 (to / -ing など) / Verb forms |
| `word_order` | 語順 / Word order |
| `word_choice` | 単語の選び方 / Word choice |
| `missing_word` | 抜けた語 / Missing words |
| `other` | その他 / Other |

句読点・大文字小文字は数えない。

## 1. アプリ (Unity)

### 画面「Topic」

要約練習 (summary screen) と同じ作りの新しい画面。エディタ用ツール `Tools > Topic Speaking > Create Topic Screen` で組み立て、タブボタンを1つ増やす。

状態の流れ:

```
Idle ──New Topic──▶ Generating ──▶ Thinking (15秒, Start now で飛ばせる)
     ──▶ Speaking (45/60秒, Finish で早く終われる) ──▶ Grading
     ──▶ Result (添削・点数) ──▶ Reading (言い直し、任意) ──▶ Idle
```

表示するもの:
- 種類 (Exam / Daily / Work)、お題、今日の注意 (苦手1つ。データが無ければ出さない)
- 残り時間
- 結果: 合計点と 3 項目 (内容 40 / 構成 30 / 言葉 30)、良かった点、直す点、添削 (直した語に下線)、言い換え (ネイティブらしい言い方)、次に使える表現 3 つ、苦手のチェック (「冠詞: 今回 1 回 / 前回 3 回」)
- 言い直し: 直された文 (最大 3 文)。読んだ結果を単語ごとに緑 (合っている) / 赤 (違う・抜け) で表示し、一致率 % を出す。「Pronunciation check」で Gemini の発音コメント

聞き取りは日記・要約と同じ内蔵音声認識 (`NativeSpeechRecognizer`、無音で切れても自動で再開)。

### 新しいクラス

| ファイル | 役割 |
|---|---|
| `TopicPracticeManager.cs` | 画面の状態と流れ。録音・音声認識・Gemini・保存・Discord の呼び出し |
| `TopicCatalog.cs` | 通信できないとき用のお題リスト (各種類 20 件程度) と、種類ごとの話す時間 |
| `ReadAloudMatcher.cs` | 言い直しの照合 (純粋なロジック。単語に分けて比較し、一致・不一致の印と一致率を返す) |
| `MistakeTypes.cs` | ミスの種類コード・表示名、Gemini から受け取るミスの形 (`Mistake { type, wrong, fixed }`) |
| `Editor/TopicScreenBuilder.cs` | 画面の組み立てツール |

### GeminiApiClient に足すもの

- `GenerateTopicAsync(kind, weakPoints, recentTopics)` → `{ kind, topic, hint }`
- `GradeTopicAsync(topic, kind, seconds, spokenText, weakPoints)` → 点数 3 項目 (先に `goodPoints` / `fixPoints` を書かせてから点数)、`corrected`、`natural`、`phrases` (3 つ)、`mistakes[]`、`weakPointCounts[]`、`readAloud[]` (直した文 最大 3)
- `CheckPronunciationAsync(text, wav)` → 短いコメント
- 既存の `CorrectDiaryAsync` と `GradeVideoSummaryAsync` の返り値に `mistakes[]` を追加 (スキーマに項目を足す)

### 保存・連携

- 結果はクラウドへ (`SupabaseService.SaveTopicResultAsync`)。ログインしていない・送れないときは端末に一時保存し、ログイン時にまとめて送る (要約練習の `PendingSummaryStore` と同じ仕組み)。
- 日記 (Gemini のとき) と要約の採点のあと、`SupabaseService.SaveMistakesAsync(source, mistakes)` で保存。失敗しても練習には影響しない。
- 苦手は `SupabaseService.GetWeakPointsAsync()` で取得 (未ログイン・失敗時は苦手なしとして続ける)。
- Discord: 要約と同じチャンネルに結果を送る。
- 連続記録: 端末内の記録と夜のリマインダーには `DiaryManager.CountOtherPracticeAsStudyDay()` で数える (要約と同じ)。クラウドはサーバーが数える。

### 通信できないとき

- お題: `TopicCatalog` から出す。
- 採点: エラーを出し、「Retry」で同じ文字起こしを送り直せる。

## 2. サーバー (Supabase) — `supabase/topic.sql` (新規)

`schema.sql` → `summary.sql` → `topic.sql` の順に実行する。何度実行してもよい。

### 表

`topic_results`
- `id`, `user_id`, `entry_date` (夜中 3:00 区切り), `created_at`
- `kind` (`exam` / `daily` / `work`), `topic`, `time_limit_seconds`
- `score` (0-100), `content_score` (0-40), `organization_score` (0-30), `language_score` (0-30)
- `transcript`, `corrected`, `natural_version`, `good_points`, `fix_points`, `phrases`, `read_aloud_accuracy` (null または 0-100)
- 1人あたり新しい 20 件だけ残す

`mistakes`
- `id`, `user_id`, `entry_date`, `created_at`
- `source` (`diary` / `summary` / `topic`), `type` (上の 10 種類のコード)
- `wrong`, `fixed` (各 300 文字まで)
- 直近 90 日・1人あたり最大 500 件だけ残す

どちらも RLS で本人の行だけ読める。書き込みは関数経由だけ。

### 関数

| 関数 | 呼ぶ側 | 内容 |
|---|---|---|
| `save_topic_result(p_timezone, ...結果..., p_mistakes jsonb, p_taken_at)` | アプリ | 結果とミスを保存。範囲・合計のチェック。1日 100 件まで |
| `save_mistakes(p_timezone, p_source, p_mistakes jsonb, p_taken_at)` | アプリ | 日記・要約のミスを保存。1回 20 件まで、種類コードを検証 |
| `get_my_topic_results(p_limit)` | Web | 新しい順に最大 20 件 |
| `get_my_weak_points()` | アプリ | 直近 30 日で多い種類の上位 3 つ (`type`, `count`, `previous_count` = その前の 30 日) |
| `get_my_mistake_stats(p_days)` | Web | 期間内の種類ごとの回数、前の同じ長さの期間の回数、例 (新しい順 最大 5 件) |

### 連続記録・カレンダー

- `activity_days.kind` に `topic` を追加 (check 制約を作り直す)。
- `topic_results` に結果が入ったら、トリガーで `activity_days` に記録し、連続記録に数える (要約と同じ規則: 次の日なら +1、空いていれば 1、同じ日・過去の日なら変えない)。
- `get_my_activity_days` は `topic` 列も返すように作り直す。

## 3. Web (`database_learnApp`)

- ハンバーガーメニューに「お題スピーキング」「よくあるミス」を追加。
- **お題スピーキング**: 要約練習のページと同じ部品 (数字のカード、推移グラフ、項目ごとの点数、一覧)。一覧にはお題・種類・点数・添削・言い換え・表現・一致率。
- **よくあるミス**: 期間 (7 / 30 / 90 日) を選ぶ。種類ごとの回数を多い順の横棒で表示し、上位 5 つを強調。前の期間より増えた・減ったを ↑↓。種類を押すと例 (あなたの文 → 直した文、どの練習か) を開く。
- **カレンダー**: お題=緑、2 種類=紫、3 種類=金。凡例に追加。
- 文章は今と同じく textContent で入れる (HTML として解釈させない)。

## 確認のしかた

- **SQL**: 手元の PostgreSQL で、保存・検証エラー・20 件 / 90 日 / 500 件の削除・苦手上位と前回比較・連続記録の加算・カレンダーの種類 (金を含む)・他人のデータが読めないこと・再実行しても壊れないこと。
- **Unity**: 利用者の Unity 2022.3 / TextMeshPro 3.0.7 の部品でコンパイル。`ReadAloudMatcher` は単体テスト (大文字小文字・句読点の無視、抜け・余分な語、一致率)。
- **Web**: ダミーデータでブラウザを動かし、2 ページとカレンダーを明るい表示・暗い表示で確認。
- **実機**: 録音・音声認識・Gemini の返答は、利用者の実機で最後に確認。

## 範囲外

- DeepL Write で添削した日記のミス集計。
- 言い直しを日記・要約に広げること。
- 発音の自動採点 (Gemini の発音コメントはボタンを押したときの一言だけ)。

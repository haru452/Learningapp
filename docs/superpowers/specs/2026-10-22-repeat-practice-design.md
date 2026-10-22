# リピーティング練習 — 設計

日付: 2026-10-22
対象: Unity アプリ (`My project`)、Web (`database_learnApp`)、Supabase

## 目的

聞こえてきた英語の文を、そのまま言い返す練習 (リピーティング) を追加する。
耳 (聞き取り) と口 (言う) を同時に鍛え、特に **自分が添削で直された文** を何度も聞いて言うことで、
正しい形を体で覚える。

成功の基準:
- 1 回 5 文の練習が 1 画面で途切れずにでき、文ごとに「言えた単語 / 抜けた単語」と一致率が分かる。
- 言えなかった文 (80% 未満) が次回に優先して出る。
- 自分の添削文 (日記・要約・お題) から文が出る。足りないときだけ Gemini を使い、通信できなくても練習できる。
- やった日が連続記録・カレンダーに数えられ、Web で一致率の推移と苦手な文が見られる。

## 決めたこと (利用者との合意)

| 項目 | 決定 |
|---|---|
| 練習の形 | リピーティング (1 文を聞き終わってから言い返す)。シャドーイングはしない |
| 文の出どころ | ① 前回言えなかった文 → ② 自分の添削文 (日記・要約・お題、直近 30 日) → ③ 足りなければ Gemini → ④ 通信できなければアプリ内のリスト |
| 英文の表示 | 聞く間は隠す。言い終わったら出し、言えた単語は緑・抜けた単語は赤 |
| 聞き直し | 「もう一度」「ゆっくり (0.75 倍)」を何度でも。回数を記録する |
| 1 回の練習 | 5 文 |
| 「言えなかった」の基準 | 一致率 80% 未満 |
| 記録 | Supabase に保存。連続記録・カレンダーに数える。Web に専用ページ |
| Gemini のキー | リピーティング専用の 4 つ目のキー `geminiRepeatApiKey` (空なら日記のキー)。上限 (429) ならほかの 3 つを借りる |
| Discord | 専用のチャンネル (`repeatDiscordWebhookUrl` / `repeatDiscordChannelId`) |
| カレンダーの色 | 1 種類はその色 (日記=オレンジ、要約=青、お題=緑、リピーティング=ピンク)、2 種類=銅、3 種類=銀、4 つ全部=金。今の紫はなくなる |

## 1. アプリ (Unity)

### 画面「Repeat」

お題スピーキング (topic screen) と同じ作りの新しい画面。エディタ用ツール
`Tools > Repeat Practice > Create Repeat Screen` で組み立て、タブボタン「Repeat」を Topic の右に足す。
(お題のツールと同じく、ほかの画面へ切り替えるボタンには「repeat screen を隠す」を足し、
新しいタブは自分の画面を隠さないようにする。)

状態の流れ:

```
Idle ──Start──▶ Preparing (文を集める) ──▶ Listening (読み上げ中)
   ▲                                         │ 読み終わり
   │                                         ▼
   │                                    Ready (聞き直し / Speak を待つ)
   │                                         │ Speak
   │                                         ▼
   │                                    Speaking (音声認識中) ──Stop──▶ Result (英文と色分け・一致率)
   │                                                                      │ Next
   │                                                                      ▼
   └──────────────── Summary (5 文の平均・言えなかった文) ◀── 5 文目のあと / それ以外は次の文の Listening
```

- **Start**: 5 文を準備する (下の「文の選び方」)。準備中は「Preparing...」。
- **Listening**: 端末の読み上げ (`AndroidTts("en", "US")`) で 1 文を読む。英文は出さず「🎧 Listen (1 / 5)」だけ出す。
- **Ready**: ボタンは「🔁 Again」「🐢 Slow」「🎤 Speak」。Again は普通の速さ、Slow は 0.75 倍で読み直す。どちらも聞き直し回数に数える。
- **Speaking**: 端末の音声認識 (お題の言い直しと同じ) で聞き取る。「Stop」で終わる。何も聞き取れなかったら 0% にせず「聞き取れなかったので、もう一度 Speak を押してください」と出して Ready に戻る。
- **Result**: 英文を大きく、枠で囲んで出す (お題の言い直しと同じ枠)。言えた単語は緑、抜けた単語は赤、その下に一致率と「(heard: ...)」。ボタンは「🔁 Again」(もう一度聞く)「🎤 Try again」(もう一度言う。新しい結果で上書き)「Next」。
- **Summary**: 平均の一致率、文ごとの一致率、80% 未満の文の一覧。ボタンは「Start」(次の 5 文)。結果の保存と Discord 送信はここで行う (5 文まとめて)。
- 照合は `ReadAloudMatcher` をそのまま使う (大文字小文字・句読点を無視、縮約形を展開、ハイフンで区切る)。
- 読み上げや音声認識の途中でほかのタブへ移ったら中断して Idle に戻る (それまでに終わった文は捨てる)。Summary まで行った結果の保存は、画面を移っても続ける。
- TTS が使えない端末 (エディタ / iOS) では、Start を押すと「この端末では読み上げが使えません」と出す (エディタではダミーの TTS で流れだけ確かめられる)。
- 音声認識が使えない端末では、お題と同じく録音して Gemini で書き起こす (1 文ごとに 1 回使う)。

### 文の選び方 (アプリ側)

1. ログインしていれば、Supabase の `get_my_repeat_sentences(5)` を呼ぶ (下の SQL)。
2. 5 文に足りなければ、Gemini に足りない数だけ作ってもらう (`GenerateRepeatSentencesAsync(count)`)。
3. それでも足りない (通信できない・ログインしていない・Gemini が失敗) ときは、アプリ内のリスト `RepeatCatalog` から、直近に出した文を避けて選ぶ。
4. 同じ文が 2 回入らないようにする (小文字にし、句読点を除いて比べる)。

アプリ内のリスト `RepeatCatalog`: 日常・仕事・試験の文を合わせて 30 文以上。1 文 6〜15 語。

### Gemini

- 新しい関数 `GenerateRepeatSentencesAsync(int count)`: 中級の学習者向けに、日常・仕事でよく使う 6〜15 語の自然な英文を `count` 個作らせる。返り値は `{ "sentences": [string] }` (JSON スキーマで固定)。
- 使うキー: `ApiSecrets.geminiRepeatApiKey` (空なら `geminiApiKey`)。上限 (429) ならほかのキーを `geminiApiKey` → `geminiSummaryApiKey` → `geminiTopicApiKey` の順に借りる。
  日記・要約・お題の画面も、上限のときに借りるキーの最後に `geminiRepeatApiKey` を足す。
- `ApiSecrets.example.json` に `geminiRepeatApiKey`、`repeatDiscordWebhookUrl`、`repeatDiscordChannelId` を足す。

### 保存

- ログインしていれば Summary のときに `save_repeat_results` を 1 回呼んで 5 文分を送る。
- ログインしていない・送れなかったときは、端末に一時保存 (`PendingRepeatStore`、お題の `PendingTopicStore` と同じ作り) し、ログインしたときにまとめて送る。
- 送れたら、日記画面の連続記録カードを更新する (お題と同じ)。

### Discord

5 文まとめて 1 メッセージ:

```
🔁 Repeat practice - 2026-10-22 17:40
Average: 84%
✅ 100%  I went to the station yesterday.
✅  90%  She has lived in Tokyo for three years.
⚠️  60%  Could you send me the file by Friday?   (heard: could you send me file friday)
...
```

80% 以上は ✅、未満は ⚠️。2000 文字を超えるときは分けて送る。

## 2. Supabase (`repeat.sql`、新規)

### テーブル `repeat_results`

| 列 | 型 | 説明 |
|---|---|---|
| id | bigint (identity) | |
| user_id | uuid | `auth.users` |
| entry_date | date | 練習した日 (夜 3 時区切り、`_practice_date`) |
| created_at | timestamptz | |
| sentence | text | 1〜300 文字 |
| heard | text | 聞き取れた文 (0〜500 文字) |
| accuracy | int | 0〜100 |
| replays | int | 聞き直した回数 (0〜99) |
| source | text | `diary` / `summary` / `topic` / `missed` / `gemini` / `builtin` |

- RLS: 自分の行だけ読める。直接の書き込みはできない (関数経由だけ)。
- 1 人あたり最新 500 件まで (保存のたびに古いものを消す)。

### 関数

- `save_repeat_results(p_timezone, p_results jsonb, p_taken_at)` → 保存した件数
  - `p_results` は `[{sentence, heard, accuracy, replays, source}]`、最大 10 件。空の文は飛ばす。accuracy は 0〜100、replays は 0〜99 に収める。source が知らない値なら `builtin`。
  - 1 件以上保存したら、その日の `activity_days` に `repeat` を入れ、連続記録を更新する (お題の `topic_counts_as_study` と同じ仕組み)。
- `get_my_repeat_sentences(p_limit int default 5, p_timezone text default 'Asia/Tokyo')` → `(sentence text, source text)`
  - 候補 A (`missed`): 文ごとに一番新しい結果が 80% 未満のもの。新しく練習したものから。
  - 候補 B: 直近 30 日の `diary_entries.corrected_text` (`diary`)、`summary_results.improved_summary` (`summary`)、`topic_results.corrected` (`topic`) を 1 文ずつに分けたもの。
    - 分け方: `.` `!` `?` の後の空白で区切る。前後の空白と先頭の `- ` を取る。
    - 4 語未満、25 語より多い、300 文字より長い文は除く。
    - 直近 7 日に練習した文 (`repeat_results` にある文) は除く。
    - 順番はランダム。
  - A を先に、残りを B で埋めて `p_limit` (1〜10) 件返す。同じ文 (小文字にして比べる) は 1 回だけ。
- `get_my_repeat_results(p_limit int default 200)` → 結果の新しい順 (Web 用)。

### カレンダー

- `activity_days.kind` に `repeat` を足す。制約の付け直しは `schema.sql`・`topic.sql`・`repeat.sql` のすべてで 4 種類 (`diary`, `summary`, `topic`, `repeat`) にする
  (`topic.sql` を後から実行し直しても `repeat` の行が制約違反にならないように)。
- `get_my_activity_days` が `repeat` 列も返すようにする (`schema.sql`)。
- 実行の順番: `schema.sql` → `summary.sql` → `topic.sql` → `repeat.sql` (何度実行しても壊れない)。

## 3. Web

- メニューに「リピーティング」ページ (`#repeat`) を足す (お題スピーキングとよくあるミスの間)。
  - 開閉できる「使い方」(最初は閉じている)。
  - 回数 (練習した日数)、平均、最新、最高の一致率。
  - 一致率の推移のグラフ (日ごとの平均)。
  - 「苦手な文」: 文ごとに一番新しい結果が 80% 未満のもの。文、最新の一致率、練習した回数。
  - 「最近の結果」: 日ごとにまとめて、文と一致率。
- カレンダー:
  - 1 種類の日はその色 (リピーティング = ピンク)、2 種類 = 銅、3 種類 = 銀、4 種類 = 金。
  - 凡例: 日記 / 要約 / お題 / リピーティング / 2種類 (銅) / 3種類 (銀) / 4つ全部 (金)。
- 文や聞き取った文は、必ず文字として入れる (HTML として入れない)。

## 4. テスト

- SQL (`supabase/tests/repeat_test.sql`): 保存と範囲の丸め、ほかの人の結果が見えない、直接書き込めない、500 件の上限、文の分け方と語数の条件、80% 未満が先に出る・80% 以上になったら出ない・直近 7 日の文が出ない、`repeat` の日が連続記録とカレンダーに入る、4 種類の制約、`topic.sql` を後から実行し直しても壊れない。
- アプリのロジック (`Tests~/LogicTests`): `RepeatCatalog` (30 文以上、6〜15 語、重複なし)、文の重複除き、Discord の文の作り方、Gemini のスキーマが正しい JSON。
- Web (`tests/web_test.py`): リピーティングのページ (統計・グラフ・苦手な文・使い方)、カレンダーの銅・銀・金・ピンク、文が HTML として入らない。
- 画面の動き (読み上げ・音声認識・タブ移動) は自動テストができないので、Unity のコンパイルを通したうえで実機で確かめる。

## やらないこと

- シャドーイング (音を流しながら同時に話す)。
- 録音の保存。
- リピーティングでの Gemini の発音チェック (お題の言い直しにはある)。
- iOS の読み上げ対応 (今のアプリは Android 用の TTS しかない)。

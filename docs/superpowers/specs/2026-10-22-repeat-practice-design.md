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
- 基本は **本物の人の声** (Tatoeba の録音) で聞ける。5 文のうち最大 2 文は自分の添削文 (日記・要約・お題) で、Gemini の自然な声で読む。
- やった日が連続記録・カレンダーに数えられ、Web で一致率の推移と苦手な文が見られる。

## 決めたこと (利用者との合意)

| 項目 | 決定 |
|---|---|
| 練習の形 | リピーティング (1 文を聞き終わってから言い返す)。シャドーイングはしない |
| 文の出どころ | 5 文 = ① 前回言えなかった文 (最大 2) → ② 自分の添削文 (日記・要約・お題、直近 30 日、最大 2) → ③ 残りは Tatoeba の人の録音つきの文 |
| 英文の表示 | 聞く間は隠す。言い終わったら出し、言えた単語は緑・抜けた単語は赤 |
| 読み上げの声 | Tatoeba の文 = 本物の人の録音。自分の添削文 = Gemini の音声合成 (女性の若い声 `Leda`、Inspector で変えられる)。前回言えなかった文は前と同じ声。失敗・上限のときは端末の読み上げ |
| 聞き直し | 「もう一度」「ゆっくり」を何度でも。ゆっくりはアプリで音を引き伸ばす (声の高さは変えない)。回数を記録する |
| 1 回の練習 | 5 文 |
| 「言えなかった」の基準 | 一致率 80% 未満 |
| 記録 | Supabase に保存。連続記録・カレンダーに数える。Web に専用ページ |
| Gemini のキー | 読み上げの音声用に専用のキー `geminiTtsApiKey` (空なら日記のキー)。上限 (429) ならほかのキーを借りる。リピーティングで Gemini を使うのは読み上げだけ (文は作らせない) |
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
- **Listening**: 1 文を読み上げる (下の「読み上げの声」)。英文は出さず「🎧 Listen (1 / 5)」だけ出す。
- **Ready**: ボタンは「🔁 Again」「🐢 Slow」「🎤 Speak」。Again は普通の速さ、Slow はゆっくり読み直す。どちらも聞き直し回数に数える。
- **Speaking**: 端末の音声認識 (お題の言い直しと同じ) で聞き取る。「Stop」で終わる。何も聞き取れなかったら 0% にせず「聞き取れなかったので、もう一度 Speak を押してください」と出して Ready に戻る。
- **Result**: 英文を大きく、枠で囲んで出す (お題の言い直しと同じ枠)。言えた単語は緑、抜けた単語は赤、その下に一致率と「(heard: ...)」。ボタンは「🔁 Again」(もう一度聞く)「🎤 Try again」(もう一度言う。新しい結果で上書き)「Next」。
- **Summary**: 平均の一致率、文ごとの一致率、80% 未満の文の一覧。ボタンは「Start」(次の 5 文)。結果の保存と Discord 送信はここで行う (5 文まとめて)。
- 照合は `ReadAloudMatcher` をそのまま使う (大文字小文字・句読点を無視、縮約形を展開、ハイフンで区切る)。
- 読み上げや音声認識の途中でほかのタブへ移ったら中断して Idle に戻る (それまでに終わった文は捨てる)。Summary まで行った結果の保存は、画面を移っても続ける。
- Gemini の音声も端末の読み上げも使えないとき (エディタ / iOS で通信できない など) は「読み上げができません」と出して Idle に戻る。
- 音声認識が使えない端末では、お題と同じく録音して Gemini で書き起こす (1 文ごとに 1 回使う。キーは読み上げと同じ `geminiTtsApiKey`)。

### 読み上げの声

**Tatoeba の文 (人の声)**
- アプリに Tatoeba の英文リスト `Resources/RepeatSentences.json` を入れておく (下の「Tatoeba の文のリスト」)。
  1 件 = `{ id: 文の番号, audioId: 録音の番号, text: 英文, author: 録音した人, license: ライセンス }`。
- 録音は、流すときに Tatoeba からダウンロードして (MP3)、端末に保存する。次からは保存したものを使う。
- アプリの Repeat 画面の下に「Sentences & audio: Tatoeba (tatoeba.org) — 録音: <author> (<license>)」を小さく出す。

**自分の添削文 (Gemini の声)**
- Gemini の音声合成モデル (Inspector `ttsModel`、初期値 `gemini-3.8-flash-tts`) に、声 `Leda` (Inspector `ttsVoice`) で文を読ませる。
  返ってくる音声 (PCM 16bit・24kHz・モノラル) を `AudioClip` にして流す。
- 使うキー: `ApiSecrets.geminiTtsApiKey` (空なら `geminiApiKey`)。上限 (429) ならほかのキー (日記 → 要約 → お題) を借りる
  (読み上げの上限もプロジェクトごとなので、別プロジェクトのキーなら枠が増える)。
- Gemini の上限に達したら、その日のうちは Gemini の読み上げを呼ばず、端末の読み上げを使う (日付は夜 3 時区切り)。

**共通**
- **ゆっくり**: 同じ音声をアプリの中で 0.75 倍の速さに引き伸ばして流す (`TimeStretch`、声の高さは変えない)。Gemini はもう一度呼ばない。
- **保存して使い回す**: 録音・Gemini の音声はどちらも端末に保存する (`Application.persistentDataPath/repeat_audio/`)。
  名前は「人の声 = `tatoeba_<audioId>.mp3`」「Gemini = 文・声から作った名前の `.wav`」。保存は最大 300 個・60 MB まで (古いものから消す)。
- **先に作っておく**: 次の文の音声は、今の文を練習している間にダウンロード・作成しておく。
- **失敗したとき** (通信できない・上限など): 端末の読み上げ (`AndroidTts("en", "US")`) でその文を読む。画面に小さく「(device voice)」と出す。練習は止めない。
- どの声で流したかを結果に記録する (`human` / `gemini` / `device`)。

### Tatoeba の文のリスト

- 利用者が Tatoeba のダウンロードページから 2 つのファイルを一度だけ取ってきて、`My project/Tools~/tatoeba/` に置く
  (この作業環境からは Tatoeba に直接つなげないため):
  - 英語の文: `eng_sentences.tsv.bz2` (per_language → eng)
  - 録音のある文: `sentences_with_audio.tar.bz2` (列: sentence_id, audio_id, username, license, attribution_url)
- 変換スクリプト `Tools~/tatoeba/build_repeat_sentences.py` が、次の条件で選んで `Assets/Resources/RepeatSentences.json` を作る:
  - 英語で、録音があり、ライセンス欄が空でないもの (空 = 再利用の許可がない)。
  - 6〜15 語、120 文字以内、`"` や `(` を含まない、数字が多すぎない。
  - 同じ文は 1 回だけ。決まった乱数で 1,000 文選ぶ (毎回同じ結果)。
- 録音の URL は `https://tatoeba.org/audio/download/<audioId>` (実装の最初に、実機で 1 つ流せることを確かめる)。
- ライセンス: 文は CC BY 2.0 FR、録音は録音した人ごと (主に CC BY-NC-ND)。**個人の学習用 (営利目的でない) でだけ使う**。音声ファイルそのものは加工して配らない (アプリの中でゆっくり再生するだけ)。作者名を画面に出す。

### 文の選び方 (アプリ側)

1. ログインしていれば、Supabase の `get_my_repeat_sentences()` を呼ぶ (下の SQL)。前回言えなかった文 (最大 2) と自分の添削文 (最大 2) が返る。
2. 残り (5 文に足りない分) を `RepeatSentences.json` から選ぶ。直近 7 日に出した文 (端末に覚えておく) は避ける。
3. ログインしていない・通信できないときは、全部 `RepeatSentences.json` から選ぶ。
4. 同じ文が 2 回入らないようにする (小文字にし、句読点を除いて比べる)。
5. 並びは「言えなかった文 → Tatoeba と自分の文を混ぜる」。

### 設定

- `ApiSecrets.example.json` に `geminiTtsApiKey`、`repeatDiscordWebhookUrl`、`repeatDiscordChannelId` を足す。
- 日記・要約・お題の画面は、上限のときに借りるキーの最後に `geminiTtsApiKey` を足す。

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
| source | text | `diary` / `summary` / `topic` / `tatoeba` (前回言えなかった文でも、もとの出どころのまま) |
| voice | text | 流した声: `human` / `gemini` / `device` |
| tatoeba_id | bigint (null 可) | Tatoeba の文の番号 |
| audio_id | bigint (null 可) | Tatoeba の録音の番号 (言えなかった文を同じ人の声で流すため) |

- RLS: 自分の行だけ読める。直接の書き込みはできない (関数経由だけ)。
- 1 人あたり最新 500 件まで (保存のたびに古いものを消す)。

### 関数

- `save_repeat_results(p_timezone, p_results jsonb, p_taken_at)` → 保存した件数
  - `p_results` は `[{sentence, heard, accuracy, replays, source, voice, tatoeba_id, audio_id}]`、最大 10 件。空の文は飛ばす。accuracy は 0〜100、replays は 0〜99 に収める。source が知らない値なら `tatoeba`、voice が知らない値なら `device`。
  - 1 件以上保存したら、その日の `activity_days` に `repeat` を入れ、連続記録を更新する (お題の `topic_counts_as_study` と同じ仕組み)。
- `get_my_repeat_sentences(p_missed int default 2, p_own int default 2, p_timezone text default 'Asia/Tokyo')` → `(sentence text, source text, missed boolean, tatoeba_id bigint, audio_id bigint)`
  - 候補 A (言えなかった文): 文ごとに一番新しい結果が 80% 未満のもの。新しく練習したものから最大 `p_missed` (0〜5) 件。もとの source・tatoeba_id・audio_id を返す。
  - 候補 B: 直近 30 日の `diary_entries.corrected_text` (`diary`)、`summary_results.improved_summary` (`summary`)、`topic_results.corrected` (`topic`) を 1 文ずつに分けたもの。
    - 分け方: `.` `!` `?` の後の空白で区切る。前後の空白と先頭の `- ` を取る。
    - 4 語未満、25 語より多い、300 文字より長い文は除く。
    - 直近 7 日に練習した文 (`repeat_results` にある文) は除く。
    - 順番はランダム。
  - A のあとに B を最大 `p_own` (0〜5) 件。同じ文 (小文字にして比べる) は 1 回だけ。
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
- アプリのロジック (`Tests~/LogicTests`): 5 文の組み立て (言えなかった文 → 自分の文 → Tatoeba、重複なし、直近 7 日の文を避ける)、
  `RepeatSentences.json` の読み込み、Discord の文の作り方、Gemini 読み上げのリクエストの形 (声の名前・音声で返す指定)、PCM から WAV への変換、
  `TimeStretch` (長さが約 1/0.75 倍になる・音の高さが変わらない)、保存ファイルの名前と上限で古いものから消す順番。
- 変換スクリプト (`build_repeat_sentences.py`): 小さな見本データで、条件どおりに選ぶ・ライセンスが空のものを除く・毎回同じ結果になる。
- Web (`tests/web_test.py`): リピーティングのページ (統計・グラフ・苦手な文・使い方)、カレンダーの銅・銀・金・ピンク、文が HTML として入らない。
- 画面の動き (読み上げ・音声認識・タブ移動) は自動テストができないので、Unity のコンパイルを通したうえで実機で確かめる。

## やらないこと

- シャドーイング (音を流しながら同時に話す)。
- 自分の声 (言い返した声) の録音の保存。
- リピーティングでの Gemini の発音チェック (お題の言い直しにはある)。
- 自分の添削文を人の声で流すこと (録音がないため、Gemini の声で読む)。
- アプリを有料で配ること (Tatoeba の録音のライセンスが営利目的を認めていないため)。
- iOS の端末の読み上げ (Gemini の音声は iOS でも流れるが、失敗したときの代わりは Android だけ)。

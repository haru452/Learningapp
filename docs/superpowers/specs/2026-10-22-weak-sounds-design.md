# 苦手な発音と「今日の発音練習」動画 — 設計

日付: 2026-10-22
対象: Unity アプリ (`My project`)、Web (`database_learnApp`)、Supabase

## 目的

どの発音が苦手かを練習の記録から見つけて教え、その発音の練習動画を 1 日 1 本 Discord に送る。
利用者は「どう作ればいいか思いつかない」状態から始めたので、仕組みはこの設計で決める。

成功の基準:
- お題の発音チェックで、アドバイスと一緒に「どの音の問題か」が出る。
- 言い直し・リピーティングで言えなかった単語から、苦手な音が自動で数えられる。
- Web の「よくあるミス」ページで、苦手な発音 (30 日) が分かる。
- その日最初の練習のあと、苦手 1 位の発音の英語ネイティブの練習動画が 1 本、発音用の Discord チャンネルに届く (同じ動画は 30 日送らない)。

## 決めたこと (利用者との合意)

| 項目 | 決定 |
|---|---|
| 見つけ方 | 両方: Gemini の発音チェックの判定 (重み 3) + 言えなかった単語のつづりからの推測 (重み 1) |
| 送るタイミング | 1 日 1 回まで (夜 3 時区切り)。その日最初に言い直し・発音チェック・リピーティングが終わったとき、アプリから送る |
| 動画 | 英語ネイティブの動画を YouTube で毎回探す |
| 作り | 案 1: アプリ + Supabase (動画探しと送信はアプリ) |
| 送り先 | 今ある発音チェック用の Discord チャンネル (`pronunciationDiscordWebhookUrl` / `pronunciationDiscordChannelId`) |
| 音の種類 | 固定 10 種類 + 「その他」(固定に当てはまらないときだけ、Gemini が短い英語の名前を付ける。名前ごとに数える) |

## 音の種類 (固定 10 種類 + その他)

| コード | アプリの表示 | Web の表示 | 例 | YouTube の検索語 |
|---|---|---|---|---|
| `th` | TH | TH の音 | think / this | `how to pronounce TH sound English pronunciation` |
| `r_l` | R / L | R と L | right / light | `R and L sounds English pronunciation lesson` |
| `v_b` | V / B | V と B | very / berry | `V and B sounds English pronunciation lesson` |
| `f_h` | F / H | F と H | food / who'd | `F sound English pronunciation lesson` |
| `s_sh` | S / SH | S と SH | see / she | `S and SH sounds English pronunciation lesson` |
| `vowel` | Vowels (æ ʌ ɑ ə) | あいまいな母音 | cat / cut / cot | `short vowels cat cut cot English pronunciation` |
| `long_short` | Long / short vowels | 長い母音と短い母音 | ship / sheep | `ship sheep long short vowels English pronunciation` |
| `final_consonant` | Final consonants | 語尾の子音 | walked / hand | `final consonant sounds English pronunciation` |
| `extra_vowel` | Extra vowels | 余計な母音 (カタカナ発音) | stop → ストップ | `stop adding vowels English pronunciation consonant clusters` |
| `stress` | Word stress | 強勢・リズム | PHO-to-graph | `word stress English pronunciation lesson` |

**その他 (`other`)**: 固定の 10 種類に当てはまらない苦手だけ、Gemini が短い英語の名前 (`otherName`、例 `Z sound (zoo)`) を付けて返す。
- 名前はそろえてから保存する (`SoundTypes.NormalizeOtherName`、SQL でも同じ処理): 小文字にする → カッコとその中身を取る → `sound` `sounds` という語を取る → 英字・数字・空白・`/` `-` だけ残す → 空白を 1 つに → 前後の空白を取る → 30 文字まで。
  例: `Z sound (zoo)` → `z`、`The "W" Sound` → `the w`、`NG sounds` → `ng`。そろえた名前が空なら保存しない。
- 数えるのは名前ごと (`other` の中で名前が同じものをまとめる)。表示は「Other: z」(アプリ)、「その他: z」(Web)。
- YouTube の検索語: `how to pronounce <名前> sound English pronunciation`。
- つづりからの推測では `other` を作らない (Gemini の判定だけ)。

知らないコード (10 種類と `other` 以外) は保存しない。

## 1. 苦手な発音の見つけ方 (アプリ)

### Gemini の発音チェック (重み 3)
- お題の「Pronunciation check」の返事 (JSON スキーマ) に `sounds` を足す: 0〜2 個の `{ code, otherName }`。
  `code` は上の 10 種類か `other` (enum で固定)、`otherName` は `other` のときだけ使う。
  アドバイスと同じ 1 回の呼び出しなので、Gemini の使用回数は増えない。
- 結果の画面に、アドバイスの下に「Sound: TH, Other: z」のように出す。
- 返ってきた種類を、そのとき読んだ文の単語 (文の中でその音を含む最初の語、無ければ空) と一緒に保存する。

### 言えなかった単語からの推測 (重み 1)
- 言い直し (お題) とリピーティングで赤くなった単語 (照合で一致しなかったお手本の語) を、つづりから種類に分ける (`SoundGuesser`)。1 語が 2 種類に当てはまることもある。
  - `th`: `th` を含む
  - `v_b`: `v` を含む
  - `f_h`: `f` または `ph` を含む
  - `r_l`: 語頭の `r` / `l`、または子音のすぐ後の `r` / `l` (`pr pl br bl cr cl dr fr fl gr gl tr`)
  - `s_sh`: `sh` を含む、または `si` / `se` で始まる
  - `final_consonant`: `ed` `st` `nd` `nt` `ld` `ct` `pt` `ks` `ts` で終わる
  - ほかの種類 (`vowel` `long_short` `extra_vowel` `stress`) はつづりから決められないので、推測では数えない
  - 3 文字未満の語と、`the` `this` `that` `these` `those` `there` `they` `then` `than` `with` `of` `for` `from` などのとても短い機能語は数えない (言い忘れが多く、苦手が `th` ばかりになるため)
- リピーティングのまとめに「Watch out: TH, R / L」(今回の推測で多かった順に最大 3) を出す。
- 1 回の練習 (言い直し 1 文・リピーティング 1 回分) でまとめて保存する。

## 2. 記録 (Supabase、`sounds.sql` 新規)

### 表 `pronunciation_issues`
| 列 | 型 | 説明 |
|---|---|---|
| id | bigint (identity) | |
| user_id | uuid | |
| entry_date | date | 練習した日 (夜 3 時区切り、`_practice_date`) |
| created_at | timestamptz | |
| sound | text | 10 種類か `other` |
| label | text | `other` のときのそろえた名前 (1〜30 文字)。ほかは空 |
| weight | int | 3 (Gemini の判定) または 1 (推測) |
| word | text | 例の単語 (0〜40 文字) |
| source | text | `check` (発音チェック) / `read_aloud` (言い直し) / `repeat` (リピーティング) |

- RLS: 本人の行の select だけ。書き込みは関数経由だけ。
- 90 日より古いものと、1 人 500 件を超えた古いものは、保存のたびに消す。
- 連続記録・カレンダーには数えない (練習そのものはほかの記録で数えている)。

### 関数
- `save_pronunciation_issues(p_timezone text, p_issues jsonb, p_taken_at timestamptz default null) returns int`
  - `p_issues` = `[{sound, label, weight, word, source}]`、1 回 20 件まで。知らない sound は飛ばす。`other` は label をそろえ直し、空なら飛ばす (ほかの sound の label は空にする)。weight は 3 以外なら 1。word は 40 文字まで。source が知らない値なら `repeat`。
- `get_my_weak_sounds(p_days int default 30, p_timezone text default 'Asia/Tokyo')`
  → `sound text, label text, points int, previous_points int, words jsonb` (`sound` と `label` の組ごとに 1 行)
  - `points` = 期間内の weight の合計、`previous_points` = その前の同じ長さ (期間が 45 日を超えるときは null)、`words` = 新しい順に重ならない単語 最大 5 個。points の多い順。

## 3. 今日の発音練習 (アプリ → Discord)

- 言い直しの 1 文・発音チェック・リピーティングのまとめが終わったあと (保存のあと) に「今日の分を送るか」を判断する (`DailySoundPractice`)。
  - 今日 (夜 3 時区切り) もう送っていたら送らない。今日 2 回失敗していたら、その日はもう試さない。
  - `get_my_weak_sounds(30)` の 1 位の points が 3 以上のときだけ送る。
  - その音の検索語で `YouTubeSearchClient.SearchShortVideosAsync(query, 240, 1200)` (4〜20 分) を呼び、直近 30 日に送った動画 (端末に覚えておく) を除いた一番上を選ぶ。全部送っていたら一番上。
  - 発音用の Discord チャンネルに送る:
    ```
    🎯 Today's pronunciation practice - 2026-10-23
    Your weakest sound: TH (think / this) — 9 points in 30 days      (その他なら: Other: z — 6 points in 30 days)
    Words you missed: think, three, weather
    ▶ <動画のタイトル> (<チャンネル名>)
    https://www.youtube.com/watch?v=<id>
    ```
  - 送れたら、今日の日付と動画 ID を端末に覚える。ログインしていない・Discord の送り先が無い・YouTube のキーが無いときは何もしない (失敗として数えない)。
- 画面は止めない (待たない)。失敗は Console に出すだけ。

## 4. Web

- 「よくあるミス」ページの下に「苦手な発音」の欄を足す (同じページの期間ボタン 7 / 30 / 90 日に合わせる)。
  - 種類ごとの points (棒)、前の期間との増減の矢印 (`previous_points` が null なら出さない)、例の単語。
  - 何も無いときは「発音チェックや言い直し・リピーティングをすると、ここに苦手な発音が出ます。」
- 文字は必ず textContent で入れる。

## 5. テスト

- SQL (`supabase/tests/sounds_test.sql`): 保存の丸め (知らない sound を飛ばす・weight・word の長さ・source・other の名前のそろえ方と空の名前を飛ばす・ほかの sound の label を空に)、other は名前ごとに別の行、20 件まで、500 件・90 日、ほかの人に見えない、直接書けない、points の合計と順番、前の期間 (30 日は数値、90 日は null)、words の重なりなし、何度実行しても壊れない。
- アプリのロジック (`Tests~/LogicTests`): `SoundTypes` (10 種類・表示・検索語、`NormalizeOtherName` の例、other の検索語)、`SoundGuesser` (上の規則と除外語)、発音チェックのスキーマに `sounds` (code の enum が SoundTypes の 10 種類 + `other` と同じ、`otherName` がある)、`DailySoundPractice` の判断 (今日送った / 2 回失敗 / points 3 未満 / 夜 3 時の区切り)、動画の選び方 (30 日以内に送ったものを避ける)、Discord の文。
- Web (`tests/web_test.py`): 「苦手な発音」の欄、矢印、期間ボタン、HTML として入らない。
- 画面の動き・YouTube・Discord への実際の送信は、実機で確かめる。

## やらないこと

- 固定の種類を画面から増やすこと (増やすときはコードを変える。想定外の音は「その他」で数える)。
- 発音の点数付け (正確さを数値で採点すること)。
- 日本語の解説動画 (利用者が英語ネイティブの動画を選んだ)。
- 週ごとのまとめや、アプリを開かない日の送信 (Worker は変えない)。

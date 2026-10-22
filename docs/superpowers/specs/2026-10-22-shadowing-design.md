# シャドーイング (Repeat 画面のモード) — 設計

日付: 2026-10-22
対象: Unity アプリ (`My project`)、Web (`database_learnApp`)、Supabase

## 目的

Repeat 画面にスイッチを付けて、シャドーイング (流れている英語に少し遅れてついていき、同時に声に出す) をできるようにする。
素材は**人の声で、話がつながっていて、普通の速さ**の英語 = BBC Learning English「6 Minute English」の会話。

成功の基準:
- スイッチを入れると、続いた 5 文を「1 回目: 聞くだけ → 2 回目: シャドーイング」で練習できる。
- イヤホンが付いていないときだけ、イヤホンを使うよう促すポップアップが出る。
- 終わると、文ごとに言えた単語 (緑)・抜けた単語 (赤) と一致率が出て、自分の声とお手本を聞き比べられる。
- 結果が保存され、連続記録・カレンダー・Web・Discord に反映される。
- BBC の音声・原稿・書き起こしは git にも Web にも一切入らない (スマホの中だけ)。APK は公開しない (利用者が確認済み)。

## 決めたこと (利用者との合意)

| 項目 | 決定 |
|---|---|
| 形 | 5 文を続けて流す。1 回目は聞くだけ、2 回目でシャドーイング |
| 判定 | 2 回目を録音し、終わってから Gemini で 1 回書き起こして照合。まとめで「自分の声を聞く」「お手本を聞く」 |
| イヤホン | Android でイヤホンが付いていないときだけポップアップ (「このまま始める」「やめる」)。iOS・エディタでは出さない |
| 素材 | BBC Learning English「6 Minute English」(人の声・会話・普通の速さ)。個人の学習用のみ |
| ゆっくり | 「Slow」スイッチで 1 回目・2 回目とも 0.75 倍 (声の高さは変えない) |

## 1. 画面 (Repeat 画面に足す)

- 上に「**Shadowing**」と「**Slow**」のスイッチ。切り替えられるのは練習していないとき (Idle・まとめ) だけ。選んだ状態は端末に覚える。
- Shadowing がオフなら今までのリピーティング (何も変えない)。
- Shadowing がオンのときの流れ:
  1. **Start** → 今の回の次の 5 文を用意する (下の「素材」)。用意中は「Preparing...」。
  2. **イヤホンの確認** (Android だけ): 出力先にイヤホンが無ければポップアップ
     「Please use earphones for shadowing. If the phone speaker is used, the microphone hears it and the result will be wrong.」
     ボタン「Start anyway」「Cancel」。Cancel なら Idle に戻る。
  3. **1 回目 (聞くだけ)**: 見出し「Listen first (n / 5)」。5 文を 0.5 秒ずつあけて続けて流す。英文は出さない。「Skip」で 2 回目へ。
  4. **カウント**: 「3」「2」「1」(各 0.8 秒)。
  5. **2 回目 (シャドーイング)**: 録音を始め、同じ 5 文を 0.5 秒ずつあけて流す。見出し「Shadow (n / 5)」。「Stop」でやめると保存せず Idle。
  6. 最後の文が終わって 1.5 秒後に録音を止め、「Checking...」→ Gemini で書き起こし → 照合。
  7. **まとめ**: 5 文それぞれ (英文・色分け・一致率)、平均、「My voice」(録音を再生)、「Original」(5 文をもう一度)、回のタイトル。ボタン「Start」で次の 5 文。
- 下の表示: 「Audio: BBC Learning English — 6 Minute English (<回のタイトル>). Personal study only.」
- ほかのタブへ移ったら止める (保存しない)。まとめ以降の保存は続ける。
- 書き起こしが空 (何も言っていない) → 0% にせず「I couldn't hear you. Tap Start to try again.」で Idle。

## 2. 素材 (BBC 6 Minute English)

- 公開されているポッドキャストのフィード (RSS) から回の一覧を読む (`BbcFeed.Parse`: 各回のタイトル・日付・MP3 の URL・回の ID)。フィードの URL は作り始めに確かめて定数にする (候補: `https://podcasts.files.bbci.co.uk/p02pc9tn.rss`)。
- 回の選び方: 最初は一番新しい回。その回を最後まで練習したら、まだ終わっていない次に新しい回へ。進み具合 (回の ID・次の文の番号) は端末に覚える。
- 音声: MP3 をスマホに保存 (`Application.persistentDataPath/shadow/<回の ID>.mp3`)。最大 3 回分、古いものから消す。
- **文と時間の切り出し** (回ごとに 1 回だけ): Gemini に音声を渡し、JSON で `{"sentences":[{"text","speaker","start","end"}]}` (秒) を返してもらう。
  - 指示: 番組の最初と最後の音楽・番組紹介の決まり文句は除く、文は話された通りに書く、時間は秒 (小数 2 桁)。
  - 受け取ったあとアプリで確かめる (`ShadowChunker`): 時間が増えていない・長さ 0 以下・音声の長さを超える文は捨てる。4〜20 語の文だけ使う。続いた 5 文ずつのかたまりにする (間に捨てた文があっても、元の順番で続いていれば同じかたまりに入れてよい)。
  - 結果をスマホに保存 (`<回の ID>.json`)。同じ回では Gemini を呼ばない。
  - 切り出すときは前後に 0.2 秒ずつ余裕を持たせる (隣の文と重なってもよい)。
- 音声の取得・切り出しに失敗したら「Could not prepare the BBC audio. Please try again later.」で Idle (保存しない)。

## 3. 判定

- 2 回目の録音 (WAV) を `GeminiApiClient.TranscribeAsync` で書き起こす (キーは `geminiTtsApiKey`、上限ならほかを借りる)。
- `ShadowScorer.Score(IList<string> sentences, string heard)`: 5 文をつなげた文章と書き起こしを `ReadAloudMatcher` で照合し、文ごとに分けて「単語ごとの一致」と一致率を返す。全体の平均も出す。
- Gemini の使用回数: 1 回の練習で 1 回 (書き起こし) + 新しい回を始めるときだけ 1 回 (切り出し)。

## 4. 保存・表示

- Supabase: `repeat_results` に列 `mode` (`repeat` / `shadow`、既定 `repeat`) を足し、source に `bbc` を足す。シャドーイングは 1 文 = 1 行で `mode='shadow'`、`source='bbc'`、`voice='human'`、`replays=0`。
  - `save_repeat_results` は各行の `mode` を受け取る (知らない値は `repeat`)。
  - `get_my_repeat_sentences` の「言えなかった文」は `mode='repeat'` だけから選ぶ (BBC の会話は前後がないと意味が取りにくいので、リピーティングに混ぜない)。
  - `get_my_repeat_results` は `mode` も返す。
- 連続記録・カレンダー: リピーティングと同じ (`repeat`、ピンク)。
- Web のリピーティングのページ: 日ごとの結果の各行に「シャドー」の印。統計・グラフは両方まとめて。苦手な文は `mode='repeat'` だけ。
- Discord (リピーティングと同じチャンネル): 見出し「🎧 Shadowing - <日時>」、回のタイトル、平均、文ごとの ✅ / ⚠️ (リピーティングの文と同じ形)。
- オフラインのときは今の一時保存 (`PendingRepeatStore`) に入れる (`mode` も持つ)。

## 5. イヤホンの確認 (Android)

- `AudioManager.getDevices(GET_DEVICES_OUTPUTS)` の種類にイヤホンがあるか (`EarphoneCheck.IsEarphone(int type)`):
  有線ヘッドセット (3)、有線ヘッドホン (4)、Bluetooth SCO (7)、Bluetooth A2DP (8)、補聴器 (23)、USB ヘッドセット (22)、BLE ヘッドセット (26)。BLE スピーカー (27) などは含めない。
- 調べられない (エディタ・iOS・エラー) ときは「付いている」とみなし、ポップアップは出さない。

## 6. git に入れないもの

- BBC の MP3、切り出しの JSON、自分の録音はスマホの `persistentDataPath` にだけ保存する (プロジェクトのフォルダには作らない)。
- テストは自分で作った架空の文・架空のフィード (XML)・作った音だけを使う。
- README に「6 Minute English の音声は個人の学習用。アプリを配らない」と書く。

## 7. テスト

- アプリのロジック (`Tests~/LogicTests`):
  - `BbcFeed.Parse` (架空の RSS から タイトル・日付・URL・ID、壊れた XML → 空)
  - `ShadowChunker` (時間のおかしい文を捨てる、4〜20 語、5 文ずつ、前後 0.2 秒、最後の半端な文の扱い: 3 文以上なら 1 かたまり、2 文以下は捨てる)
  - `ShadowScorer` (文ごとの一致率、言い忘れた文が 0%、書き起こしが空 → 全部 0 だが「空」と分かる)
  - `EarphoneCheck.IsEarphone` (上の番号)
  - 切り出しの Gemini スキーマが正しい JSON
  - Discord の「Shadowing」の文
- SQL (`repeat_test.sql` に追加): `mode` の保存と既定値、`bbc`、言えなかった文に shadow が混ざらない、`get_my_repeat_results` が mode を返す、何度実行しても壊れない。
- Web (`web_test.py`): シャドーの印、苦手な文に shadow が入らない。
- 画面の動き・録音しながらの再生・イヤホンの確認・BBC からの取得は実機で確かめる。

## やらないこと

- ほかの番組・自分で回を選ぶ画面 (順番に進むだけ)。
- 話している人ごとに分けて練習すること。
- BBC の原稿 (公式の Transcript) の取得 (Gemini の切り出しだけ使う)。
- リピーティングの流れの変更。

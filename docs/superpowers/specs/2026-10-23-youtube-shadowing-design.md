# YouTube シャドーイング (Repeat 画面のモード) — 設計

日付: 2026-10-23
対象: Unity アプリ (`My project`)、Web (`database_learnApp`)、Supabase
置き換えるもの: `2026-10-22-shadowing-design.md` (BBC の 5 文ずつ)。BBC 用の部品は消す。

## 目的

ふだん YouTube でシャドーイングしている動画で、**最後の確認**をする。
動画を最初から最後まで流しながら同時に録音し、どれだけ言えたかと、声を聞いたうえでのアドバイスを受け取る。

成功の基準:
- タイトルで検索して選んだ YouTube 動画を、アプリの中で流しながら最後まで録音できる (最長 15 分)。
- 終わると、文ごとに言えた単語 (緑)・抜けた単語 (赤)・一致率、全体の平均、Gemini のアドバイス (日本語で 3 つほど) が出る。
- Web には点数だけ、Discord には文とアドバイスも届く。連続記録・カレンダーに数えられる。
- BBC の音声のダウンロード・保存をやめる (スマホに残っている分も消す)。

## 決めたこと (利用者との合意)

| 項目 | 決定 |
|---|---|
| 素材 | YouTube の動画 (BBC 以外でもよい)。再生はアプリの中の YouTube 公式の埋め込みプレーヤー (`YouTubePlayerView`) |
| 選び方 | タイトルを入力して検索 → 候補から選ぶ (b) |
| 形 | 動画を最初から最後まで流しながら、同時に録音 (1 回だけ。「聞くだけ」の 1 回目は無し) |
| お手本 | Gemini に YouTube の URL を渡して文字起こし (動画ごとに 1 回、スマホに保存) |
| アドバイス | 声を聞いたうえで (a): 書き起こしと一緒に発音・リズム・ついていけているかを聞き、最後にまとめて日本語で 3 つほど |
| 保存 | Web (Supabase) は点数だけ。Discord には文とアドバイスも |
| Slow | 無し |

## 1. 画面 (Repeat 画面、Shadowing: On のとき)

- 上の行: 「Shadowing: On/Off」(今のまま)。「Slow」ボタンは消す。
- 検索 (Idle・まとめのときだけ): 入力欄 (TMP_InputField、実行時に作る) と「Search」ボタン。
  - YouTube で検索し、**1〜15 分**の動画を最大 5 件、候補のボタンとして出す (「タイトル — チャンネル (m:ss)」)。
  - タップでその動画に決める。見出しに選んだ動画のタイトル。最後に選んだ動画は端末に覚える (次は検索しなくてよい)。
  - 見つからない: 「No videos found (1-15 min). Try other words.」。YouTube のキーが無い: 「YouTube API key is not set.」
- **Start** (動画が決まっているとき):
  1. お手本が無ければ準備: 「Preparing the script (first time only)...」。
  2. イヤホンの確認 (今と同じポップアップ、Android でイヤホンが無いときだけ)。
  3. 動画をアプリの中で再生し、同時に録音を始める。表示「Shadowing... m:ss / m:ss」。画面を消さない。
  4. 動画が最後まで流れたら 2 秒後に録音を止める。**Stop** でも止められる (そこまでで判定)。録音の上限は「動画の長さ + 30 秒」(上限に達したらそこで止めて判定)。
  5. 「Checking...」→ 判定 → まとめ。
- **まとめ**: 平均、アドバイス、文ごと (色分け・%)、「My voice」(自分の録音)、動画のタイトル。Start でもう一度、検索で別の動画。
- 中断 (保存しない): タブの移動、電話・アプリの切り替え (`OnApplicationPause`)。再生が始まらない (WebView が使えない・エディタ) ときは録音せず「Could not play the video in the app.」で Idle。
- 書き起こしが空 → 0% にせず「I couldn't hear you. Tap Start to try again.」で Idle。
- 失敗: お手本の準備・判定の Gemini が失敗 → 「Could not check (理由). Tap Start to try again.」で Idle (保存しない)。

## 2. お手本 (Gemini、動画ごとに 1 回)

- `GeminiApiClient.TranscribeVideoAsync(string youtubeUrl)`: YouTube の URL を渡し、`{"sentences":[{"text"}]}` を返してもらう。
  指示: 話されている英語を文ごとに、話された通りに。最初と最後の音楽・番組の決まった挨拶と締めは除く。英語でない部分は除く。
- アプリで確かめる (`ShadowScript`): 空の文を除く、1 文 40 語まで (長すぎる文は切らずに捨てる)、全体 1500 語まで。使える文が 0 → 失敗。
- `persistentDataPath/shadow_scripts/<動画ID>.json` に保存、最大 20 本 (古いものから消す)。同じ動画では Gemini を呼ばない。

## 3. 判定とアドバイス

- 録音 (16 kHz モノラル) を **60 秒ずつ** WAV に分け (180 秒だとスマホの回線で時間切れになりやすかったため。返事が来ないときは 2 回まで送り直す。それでも失敗したら録音を残し、「Check again」で失敗した区切りから判定し直す)、それぞれ `GeminiApiClient.TranscribeShadowingAsync(byte[] wav, string referenceExcerpt)` に送る。
  返事: `{"transcript": "...", "notes": ["..."]}` (notes = 発音・リズム・遅れについての短い気付き、0〜3 個、英語)。
  `referenceExcerpt` はお手本の全文 (長ければ 6000 文字まで) で、聞き取りの手がかりにだけ使い、書き起こしを直さないよう指示する。
- 書き起こしをつなげ、`ShadowScorer.Score(sentences, heard)` (今のもの) で照合。
  **途中で止めたとき**: 録音した秒数 ÷ 動画の秒数 の割合までの文 (+1 文の余裕) だけを数える (`ShadowScript.SentencesUpTo(fraction)`)。
- アドバイス: `GeminiApiClient.ShadowAdviceAsync(notes, missedWords)` (文字だけ、1 回)。日本語で 3 つほどの短い箇条書き。
  失敗してもまとめは出す (アドバイス無しで「Advice: not available」)。
- Gemini の回数: 新しい動画で 1 回 (お手本)、1 回の練習で 録音 3 分ごとに 1 回 + アドバイス 1 回。キーは `geminiTtsApiKey` (上限ならほかを借りる)。

## 4. 保存・表示

- Supabase: 今の `repeat_results` (mode `shadow`)。**英文・言った内容は保存しない** (今のまま)。source に `youtube` を足す (`bbc` は前の行のために残す)。
  1 回の練習の文が 10 を超えるので、シャドーイングは **1 回 = 1 行** (accuracy = 全体の平均、replays 0)。`save_repeat_results` の 10 件の上限はそのまま。
- Web: 今と同じ (「シャドーイング」の行、点数、印)。
- Discord (リピーティングのチャンネル): 見出し「🎧 Shadowing - <日時>」、動画のタイトルと URL、平均、アドバイス、文ごとの ✅ / ⚠️ (長いときは分けて送る、今の仕組み)。
- オフライン: 今の一時保存 (`PendingRepeatStore`)。連続記録・カレンダー: 今と同じ (`repeat`)。

## 5. 消すもの

- `BbcFeed`、`ShadowEpisodeStore`、`ShadowChunker`、`ShadowFailure`、`RepeatAudioPlayer.MakeSegment`、`GeminiApiClient.SegmentEpisodeAsync` と切り出しのスキーマ、Slow ボタン、BBC の表示・README の BBC の節、Web の使い方の BBC の注記。
- アプリの起動時に `persistentDataPath/shadow/` (BBC の MP3・JSON・フィード) と PlayerPrefs `shadow_progress_v1` を消す。
- 公開メモ (`docs/publishing-notes.md`) を書き直す: BBC の音声を使わなくなったので、シャドーイングは**記事にも公開のコードにも含めてよい**。
  気をつけること: 動画の文字起こし・自分の録音・Discord の内容を記事やリポジトリに載せない、キーを載せない、動画は YouTube 公式の埋め込みで再生する (音声を保存しない) こと、個人の学習用であることを書く。
  BBC 版のコードは git の履歴に残るので、公開するときは今まで通り「公開用の新しいリポジトリ」に最新のファイルだけを入れる。

## 6. テスト

- ロジック (`Tests~/LogicTests`):
  - `ShadowScript.Parse` (空の文・41 語の文を捨てる、1500 語まで、壊れた JSON → 空)、`SentencesUpTo` (0.5 → 前半 + 1 文、1.0 → 全部)
  - `WavSplitter.Split(wav, 180)` (7 分 → 3 つ、ヘッダーが正しい、短い録音は 1 つ)
  - お手本・書き起こし・アドバイスの Gemini スキーマが JSON、書き起こしのスキーマに `transcript` と `notes`
  - `YouTubeSearch` の候補の絞り込み (1〜15 分、最大 5、順番を保つ) と表示 (`m:ss`)
  - Discord の「Shadowing」のメッセージ (タイトル・URL・アドバイス・文)
  - RPC: shadow は 1 行、英文なし
- SQL: source `youtube` が保存できる。
- Web: 今のテストのまま。
- 画面・再生しながらの録音・YouTube の検索・Gemini は実機で確かめる。

## やらないこと

- URL を貼って選ぶ (検索だけ)。Slow。動画の一部だけの練習。お手本の文字起こしを YouTube の字幕から取ること。

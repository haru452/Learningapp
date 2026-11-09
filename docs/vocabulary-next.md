# 単語帳: 次にやること (2026-11-03 時点)

ブランチ: `develop-words-book` (Learningapp に push 済み。最新は `e0f59920`)。
GitHub の Learningapp では、merge 先は `main` ではなく `develop` (main は古い状態)。

## 1. 11/3 に直したことを確かめる (まだ commit していない)

直したこと:
- 画面: ボタンや行を小さくして、めくるカードを大きくした。学習中 (Start〜まとめ) は上の Today / Words / Settings を隠す。単語の文字を大きくした (表 2.2 倍、裏 1.5 倍)
- Add タブをやめた (`VocabAddView.cs` を削除)
- Say it: この端末は録音と認識を同時にできないので、録音に声があるのに認識が空なら、次から認識だけにする。Gemini の文字起こしは最大 10 秒で諦める。Editor では 3.5 秒で自動で止まる
- Sound: calmness のような派生語で、元の語 (calm) の音声を流さない (辞書の派生語の欄を読む。音声が無ければ端末の読み上げ)。古い保存データは自動で引き直す

手順:
- [ ] Unity で `SampleScene` を開き、**Tools → Vocabulary → Update Vocabulary Screen** を実行して **Ctrl+S**
- [ ] 再生して、カードが大きいこと・Add タブが無いこと・Start でタブが消えてまとめで戻ること
- [ ] APK を作り直してスマホで確かめる (スマホはパソコンにつないだままにすると、Claude がログを見られる)
  - [ ] Say it: 1 回目は少し待つことがある。2 回目からすぐ聞き取れる
  - [ ] calmness の Sound で「calmness」と読まれる
- [ ] よければ Claude に「commit して push して」と頼む (フォントの `LiberationSans SDF - Fallback.asset` と `.claude/` は入れない)
- [ ] 残りの確認は `docs/vocabulary-checklist.md` の 3〜5 (Words・Settings・まとめ・連続記録・機内モード・merge)

## 2. YouGlish のような「動画の場面」 (実装済み・確認待ち)

確認の手順は `docs/vocab-video-clips-check.md`。
設計書: `docs/superpowers/specs/2026-11-09-vocab-video-clips-design.md` (2026-11-09 に設計 1〜4 を決めた。下は当時のメモ)。

### 決まったこと
- 目的: 本物の発音を聞くことと、使われ方を知ることの両方。字幕の文つきで、何場面か切り替えられる
- 場面の探し方:
  - **A (基本)**: パソコンの Python スクリプト (`Tools~/clips/`) で、設定ファイルに書いたチャンネルの動画を YouTube の公式 API で一覧にし、`youtube-transcript-api` で英語の字幕 (人が付けたもの優先、無ければ自動字幕) を取る。単語帳の単語 (A1〜B2) が出てくる所を探して「単語・動画・始まりと終わりの秒数・その文」を作り、Supabase の新しい表 `vocab_clips` に入れる。1 語につき最大 10 場面、同じ動画からは 2 場面まで
  - **B (A に無いとき)**: アプリでその場で YouTube を検索し (字幕のある動画だけ)、スマホから字幕を取って探す。見つけた場面は `vocab_clips` に足す
- 動画の選び方: チャンネルを指定する (TED、BBC Learning English など。設定ファイルで変えられる)
- 出し方: カードの Sound / Say it の行に「Videos」ボタン。押したときだけ通信する
- 再生は今の埋め込みプレーヤー (`YouTubePlayerView`)。音声の抜き出し・保存はしない
- 字幕の文は動画の作者の著作物なので git に入れない (Supabase にだけ置く)
- パソコンのスクリプトが Supabase に書き込むキーは git に入れない

### 見せた案 (まだ「よい」と言っていない)
- Videos を押すと、カードと評価ボタンの代わりに動画の画面になる (動画は手前に描かれるので重ねられない)
- 動画 (16:9) の下に字幕の文 (単語を太字)、チャンネル名、「3 / 7」。ボタンは Replay / Next / Close
- 場面の 1 秒前から流して、文が終わったら止める。Close でカードに戻る (めくった状態はそのまま)
- YouTube の字幕は出さない。日本語訳は出さない
- 待ち: 一覧からは「Finding videos...」、その場で探すときは「Searching YouTube...」 (数秒〜十数秒)。無ければ「No videos for this word yet.」
- Editor ではブラウザでその秒数から開く

### 残っていること
- [ ] 上の画面の案を決める
- [ ] エラーと制限を決める (YouTube の検索は 1 日約 100 回まで。シャドーイングの検索とキーを分けるか、B を 1 日何回までにするか、字幕が取れない・仕様が変わったとき)
  - 予定: YouTube の API キーを何個か用意して、1 つが上限になったら次のキーに切り替える (Gemini の予備キーと同じ形)
  - 注意: YouTube API の規約では、回数制限を超えるために複数のプロジェクトを作ることが禁止されている。キーを分けるなら用途ごと (検索用・シャドーイング用など) にとどめ、足りなければ Google に上限の引き上げを申請する (無料)。B で見つけた場面は `vocab_clips` に保存して、同じ単語で二度検索しない
- [ ] テストの方法を決める
- [ ] 設計書を `docs/superpowers/specs/` に書く → 確認 → 実装計画 → 実装
- [ ] 気をつけること: 字幕の取得は非公式な方法で、YouTube の仕様が変わると止まることがある。クラウドのサーバーからは止められやすいので、A はパソコン、B はスマホで取る

参考: [YouGlish の埋め込みの規約](https://youglish.com/api/doc/widget) (スマホアプリで使うには YouGlish の許可が要るので、YouGlish 自体は使わない)

# 単語帳: 動画の場面 (YouGlish のような機能) 設計

日付: 2026-11-09 / ブランチ: `develop-words-book`

## 目的

単語帳のカードから、その単語を本物の英語の動画の中で聞けるようにする。

- 本物の発音を聞くことと、使われ方 (どんな文で使われるか) を知ることの両方が目的
- 字幕の文つきで、何場面か切り替えられる
- 押したときだけ通信する (ふだんの学習は今までどおり)

YouGlish 自体は使わない (スマホアプリで埋め込むには YouGlish の許可が要るため)。

## 全体の流れ

場面は 2 つの方法で集め、どちらも Supabase の表 `vocab_clips` に入れる。

- **A. パソコンで先に作る (基本)**: Python スクリプト (`Tools~/clips/`) が、設定ファイルに書いたチャンネル (TED、BBC Learning English など) の動画を集め、字幕から単語の場面を探して `vocab_clips` に入れる
- **B. アプリでその場で探す (A に無いとき)**: アプリが YouTube を検索し、スマホから字幕を取って場面を探す。見つけた場面は `vocab_clips` に足す (次からは A と同じくすぐ出る)

アプリは Videos を押したとき、まず `vocab_clips` を引き、無ければ B を行う。

## A. パソコンのスクリプト (`Tools~/clips/`)

- `channels.json`: 対象のチャンネル ID の一覧 (git に入れる。字幕は入れない)
- `build_clips.py`:
  1. YouTube Data API の `playlistItems.list` (チャンネルのアップロード一覧、1 回 1 ユニット・50 本) で動画を集める
  2. `youtube-transcript-api` で英語の字幕を取る。人が付けた字幕を優先し、無ければ自動字幕。取れない動画は飛ばす
  3. 単語帳の単語 (`Assets/Resources/CefrjWords.json` の A1〜B2) が出てくる字幕の区切りを探し、場面を作る (規則は「場面の探し方」)
  4. `vocab_clips` に書き込む (同じ `word` + `video_id` + `start_ms` は上書き)
- 一度調べた動画 ID は手元のファイル (`Tools~/clips/.done.json`、git に入れない) に記録し、次からは新しい動画だけ調べる
- キー: YouTube の API キーと Supabase の service role キーは環境変数か `Tools~/clips/.env` (git に入れない)

## B. アプリでその場で探す

1. `search.list` で `q=<単語>`、字幕付き・埋め込み可・英語・セーフサーチ・最大 10 本 (100 ユニット)
2. `videos.list` でチャンネル名を取る (1 ユニット)
3. 上から順に字幕を取り、場面を探す。場面が 3 つ見つかるか、5 本調べたら止める
4. 見つけた場面を `vocab_clips` に入れる (ログインしているとき。していなければこの回だけ使う)
5. 1 つも見つからなければ、その単語を端末に記録して 30 日間は B をしない

スマホでの字幕の取り方は、`youtube-transcript-api` と同じ方法を C# で書く (`YouTubeTranscriptClient`)。非公式な方法なので、YouTube の仕様が変わると止まることがある。止まっても A の場面は今までどおり見られる。字幕はクラウドのサーバーからは取らない (止められやすいため。A はパソコン、B はスマホ)。

## 先に探しておく (待ち時間を減らす)

これから出る 20 単語の場面を、いつも準備済みにしておく。

- **準備済み**: その単語の場面が端末に保存されている (`vocab/clips/<word>.json`)。探して無かった単語も「無い」と保存し、準備済みに数える (同じ単語を何度も探さない)。Videos を押したときはまずここを見る
- **準備する単語** (これから出る順に 20 個):
  1. 今の回の、まだ評価していない単語
  2. 学習中の単語
  3. 復習の期限が近い単語 (期限の早い順)
  4. 次に出る新しい単語。Start のときに決めるのをやめ、先に決めて `vocab/next_new.json` に保存しておく。Start ではそこから使う (足りなければ今までどおりランダムに足す)
- **いつ動くか**: Vocab を開いたとき・評価するたびに数え、20 個のうち 1 個でも準備できていなければ、裏で 20 個そろうまで探す
- **探す順番**:
  1. まだの単語をまとめて `vocab_clips` に 1 回で問い合わせる (`word=in.(...)`。YouTube の回数を使わない)
  2. それでも無い単語だけ B。すぐ出る単語から順に
  3. 1 日 30 回の上限に届いたら止めて、次の日に続ける
- **裏での動き方**:
  - 非同期処理 (async/await) で 1 語ずつ。Unity の通信はメインのスレッドで動くので、スレッドは使わない。待っている間もカードの操作は止まらない
  - ほかのタブにいる間も続ける。アプリを閉じたら止まり、次に開いたときに続きから
  - 準備中の単語で Videos を押したら、その単語を先に探して出す (同じ単語を二重に探さない)
  - 通信に失敗したら今の準備は止め、次に数えたときにやり直す
- 新しいファイル: `VocabClipPrefetcher.cs` (準備する単語を選ぶ部分は Unity に依存させず、テストできるようにする)

## 場面の探し方 (A と B で同じ規則)

- 字幕の区切りの文を小文字にし、記号を除いて単語に分け、単語として一致したときだけ場面にする (`calm` で `calmness` は拾わない)
- 1 語につき最大 10 場面、同じ動画からは 2 場面まで
- 場面の時間: 始まりは区切りの開始の 1 秒前 (0 未満なら 0)、終わりは区切りの終わり
- 文は字幕のまま保存する (画面では単語を太字にする)
- C# (`VocabClipFinder`) と Python で同じ見本のテストを通し、結果をそろえる

## データ: Supabase の表 `vocab_clips`

SQL は database_learnApp リポジトリに `vocab_clips.sql` として置く。

| 列 | 型 | 内容 |
|---|---|---|
| id | bigint (自動) | |
| word | text | 見出し語 (小文字) |
| video_id | text | YouTube の動画 ID |
| start_ms | int | 流し始め (1 秒前を含む) |
| end_ms | int | 止める位置 |
| sentence | text | 字幕の文 |
| channel | text | チャンネル名 |
| source | text | `channel` (A) か `search` (B) |
| created_at | timestamptz | |

- 一意: `(word, video_id, start_ms)`
- 読み取り: ログインした人は全員読める
- 書き込み: ログインした人は `source = 'search'` の行を足せる (消す・変えるはできない)。A は service role キーで書く
- 字幕の文は動画の作者の著作物なので git には入れず、Supabase にだけ置く

## 画面と操作

- カードの Sound / Say it の行に **Videos** ボタン。表でも裏でも押せる
- 押すと、カードと評価ボタンの代わりに動画の画面になる (動画は手前に描かれるので重ねられない)
- 動画 (16:9) の下に字幕の文 (単語を太字)、チャンネル名、「3 / 7」。ボタンは **Replay / Next / Close**
- 場面の開始から流して、終わりで止める。Replay は同じ場面をもう一度、Next は次の場面 (最後なら最初へ)、Close でカードに戻る (めくった状態はそのまま)
- YouTube の字幕は出さない。日本語訳は出さない
- 待ち: 一覧からは「Finding videos...」、その場で探すときは「Searching YouTube...」。無ければ「No videos for this word yet.」
- Editor では埋め込みが使えないので、ブラウザでその秒数から開く (`&t=<秒>s`)
- ほかのタブへ移ったら動画を止める。戻ったら同じ場面を表示する (流し直しはしない)

`YouTubePlayerView` に、始まりと終わりの秒数を渡せる `Show(videoId, startSeconds, endSeconds)` を足す (IFrame Player API の `start` / `end`)。今の `Show(videoId)` はそのまま残す。

## 制限

- キー: `ApiSecrets.json` に `youtubeVocabApiKey` を足す (空なら `youtubeApiKey`)。キーを何個も用意して上限を越える使い方はしない (YouTube API の規約)
- B は端末で 1 日 30 回まで (YouTube の 1 日の上限 10,000 ユニット ≒ 検索 100 回のうち、要約の練習とシャドーイングの分を残す)。足りなければ Google に上限の引き上げを申請する
- B で見つけた場面は `vocab_clips` に保存し、同じ単語で二度検索しない
- 見つからなかった単語は 30 日間 B をしない
- 回数と「見つからなかった単語」は端末に保存する (`vocab/clip_search.json`)

## エラー

| 起きたこと | 画面 | 動き |
|---|---|---|
| ネットにつながらない | Can't reach the internet. | Close だけ押せる |
| Supabase にログインしていない | (出さない) | 一覧は使わず B。見つけた場面は保存しない |
| YouTube の上限 (`quotaExceeded`)・端末の 30 回を使い切った | Video search limit reached. Try again tomorrow. | その日は B をしない |
| 字幕が取れない動画 | (出さない) | その動画を飛ばす |
| 全部の動画で字幕が取れない | No videos for this word yet. | Console にログ |
| 再生できない動画 (削除・埋め込み禁止) | (出さない) | 自動で Next。全部だめなら No videos for this word yet. |
| YouTube のキーが無い | No YouTube key. Add youtubeApiKey to ApiSecrets.json. | 一覧だけ使う |

## 新しいファイル

- `Assets/Script/CorrectionDiary/Vocab/VocabClipFinder.cs`: 字幕から場面を作る (Unity に依存しない)
- `Assets/Script/CorrectionDiary/Vocab/VocabClipSearchLog.cs`: 1 日の回数と見つからなかった単語の記録 (Unity に依存しない部分を分ける)
- `Assets/Script/CorrectionDiary/Vocab/VocabClipSource.cs`: `vocab_clips` を引く → 無ければ B、の流れ
- `Assets/Script/CorrectionDiary/Vocab/VocabVideosView.cs`: 動画の画面 (Replay / Next / Close)
- `Assets/Script/CorrectionDiary/YouTubeTranscriptClient.cs`: スマホで字幕を取る
- `SupabaseService` に `vocab_clips` の読み書きを足す。`YouTubeSearchClient` に単語の検索を足す
- `VocabScreenBuilder` に Videos ボタンと動画の画面を足す
- `Tools~/clips/`: `build_clips.py`、`channels.json`、`test_build_clips.py`、`README.md`
- database_learnApp: `vocab_clips.sql`、`vocab_clips_test.sql`

どのファイルも 600 行以内にする。

## テスト

Unity なしで動くテスト (`Tests~/LogicTests/`):
- 場面の探し方: 単語として一致したときだけ (`calm` と `calmness`)、大文字・記号、1 語 10 場面・1 動画 2 場面、始まりの 1 秒前と 0 未満
- 字幕の読み取り: 保存した見本で読める、人の字幕を自動字幕より優先
- 回数の記録: 30 回で止まる・日付が変わると戻る、見つからなかった単語は 30 日間検索しない
- エラーの見分け: YouTube の返事から `quotaExceeded` とほかのエラーを見分ける
- 先に探しておく: 準備する単語の順番 (今の回 → 学習中 → 期限の近い順 → 次の新しい単語)、1 個でも足りなければ動き 20 個で止まる、30 回の上限で止まる、先に決めた新しい単語が Start で使われる、同じ単語を二重に探さない

Python (`Tools~/clips/test_build_clips.py`):
- C# と同じ見本で、場面の探し方の結果がそろう
- Supabase に送るデータの形 (実際には送らない)

SQL (`vocab_clips_test.sql`): ログインした人が読める、`search` の行は足せる・`channel` の行は足せない・消せない、同じ場面は重ならない。

手で確かめること (スマホ):
- 一覧にある単語で Videos を押すとすぐ出る
- 一覧に無い単語でその場で探して出る。もう一度押すとすぐ出る
- Replay / Next / Close。Close でカードがめくった状態のまま
- 機内モードで「Can't reach the internet.」

YouTube との実際の通信は自動のテストに入れない。

## やらないこと

- 音声の抜き出し・保存 (再生は埋め込みプレーヤーだけ)
- 日本語訳、YouTube の字幕の表示
- 複数のキーで上限を越える仕組み
- 場面の評価 (よい・悪い) や通報

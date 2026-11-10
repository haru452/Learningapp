# 洋楽カラオケ (Karaoke) の設計

日付: 2026-11-10 / ブランチ: app-improvements

## 目的

洋楽を歌って、英語の発音の弱いところを見つける。曲名を入れると YouTube の動画が流れ、イヤホンで聞きながら画面の歌詞を見て歌う。
歌い終わったら Gemini が「どの単語がうまく発音できていなかったか」を返す。

- ユーザーが決めたこと: 歌詞は歌詞サイト (LRCLIB) + YouTube。採点は歌い終わってからまとめて。
- 前提 (こちらで決めたこと): Modes に「Karaoke」として足す。結果は Web と Discord に残し、連続記録にも数える。
  Gemini が混んでいたら「あとで送る」(GeminiLater) に残す。軽いモデルには切り替えない。

## シャドーイングとは別の機能

- Karaoke はシャドーイングとは別のモード。Modes に自分のカード、自分の画面・流れ・保存・表・Web の欄を持つ。
- シャドーイングのファイル (Shadow/*, RepeatPracticeManager) は変えない。
- 使い回すのは、どの機能でも使える小さな部品だけ (そのまま呼ぶ・中身は変えない):
  EarphoneCheck、WavSplitter、YouTubeSearchClient、YouTubePlayerView。

## 成功の基準

- 有名な洋楽の曲名を入れると、歌詞と動画が見つかり、歌詞が曲に合わせて進む (ずれは ± ボタンで直せる)。
- 歌い終わると、歌詞の行ごとに発音できなかった単語が赤で出て、点数 (うまく歌えた単語の割合) とアドバイスが出る。
- 結果が Web の「ミス」のページと Discord に出て、その日が連続記録に数えられる。

## 画面の流れ

1. **探す**: 曲名を入力 → LRCLIB で時刻つきの歌詞を探す → YouTube で「<曲名> <歌手> lyrics」を探す
   (歌詞動画は元の曲と同じ長さのことが多い)。候補から選ぶ。
2. **歌う前**: イヤホンの確認 (共通の部品 EarphoneCheck を使う)。Start で動画を流し、録音を始める。
3. **歌っている間**: 動画の下に、今の行を大きく明るく、次の行を小さく、前の行を薄く出す。
   - ずれ: 動画と歌詞の長さの差が ±3 秒以内ならずらさない。それ以上なら、差をイントロと見てずらす。
   - 「−0.5s / +0.5s」で直せる。直した値は動画ごとに端末に覚える。
   - 「Finish & check」でいつでも終われる (1 番だけ歌って押してもよい)。採点は止めた時刻までの行だけで、残りの行は点数に入れない。
4. **採点**: 録音を歌詞の時刻で最大 3 分ずつに切る (WavSplitter) → 1 つずつ Gemini に「録音 + その部分の歌詞 (行番号つき)」を送る。
   Gemini は行ごとに「発音できなかった・歌えていなかった単語」「原因の音 (th・R と L など)」と、全体の日本語のアドバイスを返す。
5. **結果**: 歌詞の一覧 (苦手な単語は赤)、点数、アドバイス。行を押すとその行の自分の声 (My voice)。
   苦手な音は今の「今日の発音」(SoundGuesser / 音の集計) に足す。

やらないこと: 歌詞のふりがな・訳、音程やリズムの採点。

## 保存・送信

- **Supabase** (`database_learnApp/supabase/karaoke.sql`):
  - 表 `karaoke_results`: id, user_id, taken_at, title, artist, video_id, score, items (jsonb: 行ごとの `{line, words:[{word, sound}]}`)。RLS は本人だけ。
  - RPC `complete_karaoke_session(p_title, p_artist, p_video_id, p_score, p_items, p_timezone, p_taken_at)`:
    結果を保存し、activity_days に kind = 'karaoke' を足して連続記録を返す (answer.sql と同じ形)。
  - activity_days の kind の制約を 7 種類にし、schema / repeat / topic / vocab / answer.sql も同じ一覧にそろえる。
  - テスト: `tests/karaoke_test.sql`。
- **Web**: 「ミス」のページの `practice-logs.js` に「カラオケ」の欄。曲ごとに新しい順、まとめない。苦手な単語と音のチップ。
- **Discord**: カラオケ用の送り先 (ApiSecrets の karaokeDiscordWebhookUrl / karaokeDiscordChannelId) に、曲名・点数・苦手な単語・アドバイス。
  設定が無ければ送らない (ほかの機能のチャンネルには流さない)。
- **連続記録**: 端末の記録 (DiaryManager.CountOtherPracticeAsStudyDay) + クラウド (上の RPC)。
- **Gemini が混んでいるとき**: GeminiLater に kind = "karaoke" を足し、録音 (切った後の wav) と歌詞の行を残す。
  あとで採点できたら保存・Discord・お知らせ (AppToast)。

## エラー

- 歌詞が無い: 「No lyrics for this song. Try another title.」
- 動画が無い・その日の検索の上限: 今のシャドーイングと同じ文。
- 時刻つきの歌詞が無い (歌詞だけある): 使わない (同じく「No lyrics」)。
- 録音が短すぎる・聞き取れない: 採点しない (「Please sing a little more.」)。

## ファイル (どれも 600 行以内)

| ファイル | 役割 |
|---|---|
| `Karaoke/LrcLyrics.cs` | LRC の歌詞を読む・ずらし量の計算・今の行を探す (純粋な部分。テストあり) |
| `Karaoke/LrclibClient.cs` | LRCLIB の検索 (`/api/search`)。キー不要。User-Agent はアプリ名だけ |
| `Karaoke/KaraokeScorer.cs` | Gemini の返事 → 行ごとの赤い単語と点数 (テストあり) |
| `GeminiApiClient.Karaoke.cs` | 採点のプロンプトと返事の形 (responseSchema) |
| `Karaoke/KaraokeSession.cs` | 探す → 歌う → 採点 → 結果 の流れ |
| `Karaoke/KaraokeScreen.cs` / `.Ui.cs` | 画面。Answer と同じく Modes から開く (シーンは変えない) |
| `Karaoke/KaraokeOffsetStore.cs` | 動画ごとのずらし量を端末に覚える |
| `SupabaseService.Karaoke.cs` | 保存の RPC |
| `Talk3D/DiscordClient.Karaoke.cs` | カラオケの Discord の文 (DiscordClient を partial にする) |

使い回すもの: YouTubeSearchClient (上限 30 回/日もそのまま)、YouTubePlayerView、WavSplitter、EarphoneCheck、RepeatAudioPlayer の My voice の再生、GeminiLater、AppToast、UiStyle / UiTheme。

## テスト

- ロジックテスト: LRC の読み込み (複数の時刻・空行・壊れた行)、ずらし量 (±3 秒以内は 0)、今の行の探し方、
  録音の切り方 (3 分ごと・行の途中で切らない)、Gemini の返事の読み方と点数、GeminiLater の karaoke。
- SQL テスト: karaoke_test.sql (保存・本人だけ・連続記録・同じ日は 1 回)。
- コンパイル確認: unitycheck (エディター) と unitycheck_android。
- 実機: 歌詞の進み方とずれの直し方、イヤホンでの録音、採点の結果。

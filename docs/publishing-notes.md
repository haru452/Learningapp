# 公開するときのメモ（記事・GitHub のリンク）

## BBC 関連の機能は、記事に載せる GitHub のリンクに含めない

シャドーイング（BBC Learning English「6 Minute English」の音声を使う機能）は**個人の学習用**です。
BBC の規約・AI での利用への方針がはっきりしないため、**記事に載せる GitHub のリポジトリ（リンク先）には BBC 関連の機能を入れない**。

- BBC の音声・切り出し結果・録音は、もともとスマホの中（`persistentDataPath/shadow/`）だけで、git には入っていない
- ただし**コード**（BBC のフィードの URL、音声を Gemini に送る処理など）は git に入っている
- APK は配らない

### 記事での書き方

記事には、シャドーイングの機能があることだけを一言書く（素材・仕組み・BBC の名前などの詳しいことは書かない）。例：

> ※ このアプリには、この記事では紹介していないシャドーイングの練習機能もあります（個人用のため、公開しているコードには含めていません）。

### 公開用に外すもの

Unity（`My project`）
- `Assets/Script/CorrectionDiary/Shadow/` のフォルダ全部（BbcFeed, ShadowChunker, ShadowScorer, EarphoneCheck, EarphoneCheckDevice, ShadowEpisodeStore, ShadowUi, ShadowSession, ShadowFailure と .meta）
- `Repeat/RepeatPracticeManager.cs` のシャドーイングの部分（`ShadowUi` / `ShadowSession` / `OnApplicationPause` / `SendShadowToDiscordAsync`）
- `Repeat/RepeatAudioPlayer.cs` の `MakeSegment`
- `GeminiApiClient.cs` の `ShadowSegmentPrompt` / `ShadowSegmentSchema` / `SegmentEpisodeAsync`
- `Talk3D/DiscordClient.cs` の `SendShadowResultsAsync` / `ShadowMessage`
- `Tests~/LogicTests/Program.cs` の `ShadowTests`
- `README.md` の「シャドーイング」の節
- `docs/superpowers/specs/2026-10-22-shadowing-design.md`、`docs/superpowers/plans/2026-10-22-shadowing.md`

Web（`database_learnApp`）
- `index.html` の使い方の「シャドーイング」の行と、BBC の注記
- （`supabase/repeat.sql` の `mode` 列・`bbc` は残しても BBC の内容は含まないが、気になるなら外す）

### 注意：ファイルを消すだけでは足りない

git の**履歴**に BBC 関連のコードが残っている。今のリポジトリをそのまま公開すると、過去のコミットから見えてしまう。
公開するときは次のどちらかにする：
1. **公開用の新しいリポジトリ**を作り、上のものを外した最新のファイルだけを 1 回のコミットで入れる（おすすめ）
2. 履歴から消す（`git filter-repo` など。手間がかかり、間違えやすい）

Tatoeba のデータ（`Assets/Resources/RepeatSentences.json` など）も、これまで通り公開しない。

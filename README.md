# Voice English Corrector（英語音声添削アプリ）

スマホに向かって英語で話すと、文字起こしして **Gemini API** または **DeepL Write API** で自然な英語に添削してくれる、Unity 製の英語学習アプリです。
話した内容を英語日記として残して連続記録（ストリーク）をつけられるほか、Versant 対策の練習（動画の要約・お題スピーキング・短い答え・リピーティング・シャドーイング）や、FSRS で復習日を決める単語帳もあります。

> このリポジトリは、[Learningapp](https://github.com/haru452/Learningapp)（記事「その１」の時点のコード）の続きです。
> 「その１」からの主な変更（記事「その２」で解説）：
> - **Summary**：YouTube の短い英語動画を見て、制限時間内に要約を話す。Gemini が動画そのものを見て採点する
> - **Topic**：お題について話す → 採点 → 直された文を読み直す。よくあるミスを 10 種類に分けて数える
> - **Repeat**：聞いた英文を言い返す（リピーティング）。YouTube の動画でシャドーイング
> - **Answer**：短い質問に一言で答える（Versant の Short Answer Questions）。問題は裏で作り置きする
> - **苦手な発音**：言えなかった音を数えて、毎日その音の練習動画を Discord に送る
> - **Vocab**：単語帳。FSRS-4.5 で復習日を決める。カードの画像（Pixabay）と、単語が使われている動画の場面も見られる
> - **Gemini の API キーを画面ごとに分ける**：上限（429）になったら、ほかの画面のキーを借りる

📝 **解説記事（Qiita）**
- [その１：Unity で「話した英語を AI が添削してくれる」英語日記アプリを作った](https://qiita.com/haru452/items/8656d7d15db5d20c7b41)
- [その２：Versant 対策の練習と単語帳を足した（備忘録）](https://qiita.com/haru452/items/7b5560b8db5a6ac0253f)

| 添削（Practice） | 3D キャラとの会話（Talk・実験中） |
|---|---|
| <img src="docs/images/demo_correction.gif" width="240"> | <img src="docs/images/demo_talk.gif" width="240"> |

## 主な機能

下のタブは **Diary / Topic / Vocab / Modes** の 4 つです。ほかの画面（Practice・Summary・Repeat・Answer・Talk など）は **Modes** から開きます。

### Practice：英語の添削
- 録音ボタンで話し始め、もう一度押すと停止して添削
- **文字起こし**：端末内蔵の音声認識（Android: SpeechRecognizer / iOS: SFSpeechRecognizer）。使えない環境（Unity Editor など）では録音した WAV を Gemini で文字起こし
- **添削エンジンの切り替え**
  - Gemini：文法の修正と、なぜ直したかの解説
  - DeepL Write：より自然な言い回しへの言い換え

### Diary：英語日記
- 停止ボタンを押すまで話し続けられる（音声認識が途中で切れても自動で再開）
- 話し終わったら、日記全体をまとめて添削
- **連続記録（ストリーク）**：日記を書いた日数と、今週の達成状況を表示
- **ログイン・クラウド保存**（任意）：Supabase でログインすると、記録と日記をクラウドに保存（Web 版と共通）
- **Discord 連携**（任意）：添削した日記を Discord に送信。元の文から直した単語には下線を付ける
- **リマインダー**（任意）：夜までに日記を書いていないと Discord に通知

### Summary：動画の要約練習（Versant 対策）
- YouTube Data API で、字幕付きの短い英語動画（40秒〜2分半）を毎回ランダムに出題
- 動画はアプリ内の埋め込みプレーヤーで再生（Editor ではブラウザで開く）
- 動画が終わると制限時間（標準60秒）のカウントダウンが始まり、話して要約する
- Gemini が動画そのものを見て、項目ごとに採点：内容（50点）・構成（25点）・文法・語彙（25点）。合計はアプリで計算
- あわせて、押さえた点・抜けた点・文法の指摘・お手本の要約・改善例も表示
- 採点結果は Discord に送信（任意。項目ごとの点数を棒グラフ風に表示）。ログイン中はクラウドに保存され、Web 版で点数の推移と項目ごとの内訳をグラフで見られる
- 音声認識が途中で切れても、日記モードと同じく自動で再開する
- YouTube Data API の無料枠は 1 日 10,000 ユニットで、動画の検索 1 回に 100 ユニット使う（1 日に約 100 回まで出題できる）
- Summary 画面は、Unity のメニュー **Tools → Summary Practice → Create Summary Screen** で、今のシーンに自動で組み立てられる（`Assets/Editor/SummaryScreenBuilder.cs`）

### Topic：お題スピーキング
- お題 → 考える時間 → 45〜60 秒話す → 採点 → 言い直し、の流れ
- 録音をそのまま Gemini に渡し、**文字起こしと採点を 1 回のリクエストで**行う
- **Read aloud**：直された文を声に出して読み直す。言えた単語は緑、言えなかった単語は赤（日記の下線と同じ、単語単位の差分）
- **よくあるミス**：日記・要約・お題で直された所を、Gemini が「冠詞・時制・前置詞・単数複数…」の 10 種類に分けてクラウドに貯める（`MistakeTypes.cs`）。多かったミスは、次のお題の前に「今日の注意」として出し、採点でも重点的に見る
- Topic 画面は、Unity のメニュー **Tools → Topic Speaking → Create Topic Screen** で組み立てる（`Assets/Editor/TopicScreenBuilder.cs`）

### Repeat：リピーティング（聞いた英文を言い返す）
- 1 回 5 文。英文は見せずに読み上げ、聞き終わったら同じ文を言い返す。言えた単語は緑・抜けた単語は赤で、一致率（%）を出す
- 文の出どころ：前回 80% 未満だった文（最大 2）→ 自分の添削文（日記・要約・お題、直近 30 日、最大 2）→ 残りは [Tatoeba](https://tatoeba.org/) の文
- 声：Tatoeba の文は**本物の人の録音**、自分の添削文は Gemini の音声合成（声 `Leda`）。失敗したときは端末の読み上げ
- **Again** でもう一度、**Slow** で 0.75 倍（アプリの中で引き伸ばすので声の高さは変わらない）
- 録音・作った音声は端末に保存して使い回す（最大 300 個・60 MB）
- 結果はクラウドに保存され、Web 版の「リピーティング」で一致率の推移と苦手な文を見られる。Discord にも送れる（専用のチャンネル）
- Repeat 画面は、Unity のメニュー **Tools → Repeat Practice → Create Repeat Screen** で組み立てる（`Assets/Editor/RepeatScreenBuilder.cs`）

#### Tatoeba の文を入れる（最初に 1 回だけ）
Tatoeba のデータは**著作権のため git に入れていません**（`.gitignore` 済み）。各自のパソコンで作ります。

1. [Tatoeba のダウンロードページ](https://tatoeba.org/en/downloads) から次の 2 つを取ってきて、`Tools~/tatoeba/` に置く
   - `eng_sentences.tsv.bz2`（Sentences → English）
   - `sentences_with_audio.tar.bz2`
2. 変換スクリプトで 1,000 文を選ぶ（`Assets/Resources/RepeatSentences.json` ができる）
   ```
   cd Tools~/tatoeba
   python3 build_repeat_sentences.py --sentences eng_sentences.tsv.bz2 --audio sentences_with_audio.tar.bz2 --out ../../Assets/Resources/RepeatSentences.json --count 1000 --seed 42
   ```
   条件：録音とライセンスがある / 5〜10 語 / Tom・Mary が出てくる文は 15% まで（テスト：`python3 -m unittest test_build_repeat_sentences`）
3. 録音は、練習で流すときにアプリが Tatoeba からダウンロードする

> ⚠️ 文は CC BY 2.0 FR、録音は録音した人ごとのライセンス（主に CC BY-NC-ND）です。**個人の学習用（営利目的でない）でだけ**使ってください。アプリを有料で配ったり、録音を加工して配ったりはできません。画面に作者名を表示しています。
> `RepeatSentences.json` が無いときは、自分の添削文と前回言えなかった文だけで練習します。

#### シャドーイング（Repeat 画面の `Shadowing: On`）
ふだん YouTube でシャドーイングしている動画で、最後の確認をする機能。
- 検索欄に動画のタイトルを入れて `Search` → 候補（1〜15 分、最大 5 件）から選ぶ。選んだ動画は覚えておく
- `Start` → 初めての動画だけ、Gemini が YouTube の URL からお手本の文字起こしを作る（スマホに保存、最大 20 本）
- 動画がアプリの中に出るので **▶ を押す**。再生が始まると同時に録音が始まる。最後まで流れると止まる（`Stop` で途中まででも判定）
- 録音を 1 分ずつ Gemini で書き起こし、お手本と照合 → 平均・文ごとの色分けと %・**日本語のアドバイス**（発音・リズム・遅れ）・`My voice`
- 送信に失敗しても録音は残す。`Check again` で失敗した所からやり直せる
- **イヤホンを使う**。Android でイヤホンが付いていないときは確認が出る
- クラウド・Web には**点数だけ**（1 回 = 1 行、`mode = 'shadow'`、`source = 'youtube'`）。動画のタイトル・アドバイス・文ごとの結果は Discord にだけ送る
- 動画は YouTube 公式の埋め込みプレーヤーで流すだけで、音声は保存しない。お手本の文字起こしと録音はスマホの中だけ（`persistentDataPath/shadow_scripts/`）。個人の学習用
- キー：シャドーイング専用の `geminiShadowApiKey`（空なら `geminiTtsApiKey`、上限ならほかを借りる）と `youtubeShadowApiKey`（空なら `youtubeApiKey`）。古い Supabase の場合は `supabase/repeat.sql` を実行し直す

### Answer：短い答えの練習（Versant 対策）
- Gemini が 10 問作り、1 問ずつ**音声だけ**で出題。すぐに一言で答える。音声認識が使えないとき（Unity Editor など）は文字で答える
- **問題の作り置き**：アプリを開いたら裏で 10 問作って端末に保存（`answer_stock.json`）。`Start` で作り置きから取り出すので待たずに始まり、使った分は裏で作り足す（`Answer/ShortAnswerStock.cs`）
  - 途中でやめたら、答えていない問題は作り置きに戻す
  - Gemini が混んでいて作れなかったら、1 分おきに最大 5 回作り直す
  - 同じ問題が続かないように、作り置きの問題と最近出した 60 問を Gemini に渡して、被らない問題を頼む
- 1 回終えると、クラウドに保存して連続記録に数える
- 画面は Modes から開く（起動時に自動で作る。シーンの組み立ては不要）
- キー：`geminiAnswerApiKey`（任意。空なら `geminiApiKey`）。読み上げは `geminiTtsApiKey`

### 苦手な発音と「今日の発音練習」
- お題の **Pronunciation check** で、アドバイスと一緒に「どの音の問題か」が出る（`Sound: TH` など。固定 10 種類＋その他）
- 言い直し（お題）とリピーティングで言えなかった単語のつづりからも、苦手な音を数える。リピーティングのまとめに `Watch out: TH, R / L` が出る
- 記録はクラウド（`supabase/sounds.sql`）に保存し、Web の「よくあるミス」ページの「苦手な発音」で見られる（期間ボタン 7 / 30 / 90 日）
- その日最初の練習のあと、30 日で 3 点以上の苦手 1 位の音について、英語ネイティブの練習動画を YouTube で探し、**発音用の Discord チャンネル**（`pronunciationDiscordWebhookUrl`）に 1 日 1 回送る（同じ動画は 30 日送らない）
- 必要なもの：ログイン、`youtubeApiKey`、発音用の Discord。どれか無ければ送らない（練習はそのまま使える）

### Vocab：単語帳
- 画面の上で **Today / Words / Add / Settings** を切り替える
- **Today**：初めてのときにレベル（A1 / A2 / B1 / B2）を選ぶ。`Start` で 1 回分（5 / 10 / 15 / 20 語、初期 10）。出る順は 学習中 → 復習の期限が来た単語 → 新しい単語（[CEFR-J](https://github.com/openlanguageprofiles/olp-en-cefrj) の自分のレベルから自動。辞書で引けたものだけ）。期限切れの復習が 50 語を超えた日は新しい単語を出さない
  - カードはタップでめくる。表 = 単語・レベル・発音記号、`Sound`（辞書の音声。無ければ端末の読み上げ）、`Say it`（発音チェック）。裏 = 品詞ごとの意味（品詞ごとの CEFR レベル）・自分の意味・例文
  - 評価は `Didn't know` / `Hard` / `Knew it` / `Easy`。次の復習日は **FSRS-4.5**（目標の想起率 90%、設定で 80〜95%）で計算する。`Didn't know` は学習中に戻り、次の回の最初に出る
  - 発音チェック：その場で端末の音声認識が聞き取り `Clear` / `Close` / `Try again`。同時に録音して、回が終わったら **Gemini** がまとめて採点（`Good` / `Close` / `Practice` と日本語のアドバイス・苦手な音）。録音と認識を同時にできない端末では認識だけ（Gemini のアドバイスなし）
  - まとめ：`My voice` / `Model`、`Score again`（Gemini が失敗したとき）、`Practice weak words`（発音だけもう一度）、条件を満たすと `Level up to ...?`（直近の新しい単語 20 個のうち 16 個以上が `Didn't know` 以外）
  - 1 回終えた日は**連続記録に数える**（ログイン中はクラウド、Web のカレンダーは紫）
  - **カードの画像**：裏に単語の画像を出す（[Pixabay](https://pixabay.com/) の API。セーフサーチ・人気順の一番上）。取った画像は端末に保存して同じ単語では聞き直さない。見つからなかった単語は 30 日探し直さない。表を見ている間に先に取っておく。合わない画像は `Hide` で隠せる（`Show image` で戻す）。`pixabayApiKey` が空なら画像は出さない
  - **動画の場面**（`Videos`）：その単語が英語の動画で使われている場面を、字幕つきで見られる。場面はパソコンのスクリプト（下の「単語帳の動画の場面」）で先に作るか、無い単語はアプリが YouTube を検索してその場で探し、Supabase に足す。これから出る 20 単語の場面は裏で先に探しておく。YouTube の検索はアプリの中で 1 日 30 回まで
- **Words**：学習中 / わかった（次の復習日）とレベルで絞り込み。行をタップ → `Back to learning` / `Delete`
- **Add**：辞書で検索して追加（見つからなければ候補）、または手入力（単語と自分の意味。日本語可）
- 単語帳・評価の履歴・辞書の結果はスマホの中だけ（`persistentDataPath/vocab/`）。クラウドには「やった日」だけ
- Vocab 画面は、Unity のメニュー **Tools → Vocabulary → Create Vocabulary Screen** で組み立てる（`Assets/Editor/VocabScreenBuilder.cs`）。Supabase では `supabase/vocab.sql` を実行する
- キー：`merriamWebsterApiKey`（必須。[dictionaryapi.com](https://dictionaryapi.com/) で **Learner's Dictionary** を選んで登録した Key (Dictionary)）、`geminiVocabApiKey`（任意。空なら日記のキー）
- 出典：単語のレベルは **CEFR-J Wordlist Version 1.5**（Tono Laboratory, TUFS。研究・商用とも無料、出典の明記が必要）、辞書は **Merriam-Webster's Learner's Dictionary**。アプリの Settings にも表示している
  - `Assets/Resources/CefrjWords.json` は CSV から変換したもの（`Tools~/cefrj/build_cefrj_words.py`、テスト：`python -m unittest test_build_cefrj_words`）。元の CSV は git に入れていない

### Talk：3D キャラクターとの英会話（実験中）
- VRM キャラクターと音声で会話（LLM は LLMUnity でスマホの中で動かす）
- 返事は1文できるたびに読み上げ、口パク・うなずきも付く
- 返事の感情（うれしい・悲しい・驚きなど）に合わせて表情が変わる。ユーザーが悲しい話をすると、考えている間も悲しそうな顔をする
- 現在は Android のみ対応

## 動作環境

| 項目 | 内容 |
|---|---|
| Unity | 2022.3.62f3 |
| 対象 OS | Android（iOS 向けのコードもあるが未検証） |
| 言語 | C# |
| 主なパッケージ | TextMeshPro、[UniVRM](https://github.com/vrm-c/UniVRM)、[LLMUnity](https://github.com/undreamai/LLMUnity)、[unity-webview](https://github.com/gree/unity-webview) |

## セットアップ

1. このリポジトリをクローンします。
   ```
   git clone https://github.com/haru452/Learningapp2.git
   ```
2. Unity Hub で **Unity 2022.3.62f3** を使ってフォルダを開きます。
   初回は `Library/` の生成やパッケージのダウンロードに時間がかかります。
3. LLMUnity のライブラリ（`Assets/StreamingAssets/LlamaLib-*`）はサイズが大きいため、リポジトリには含めていません。Unity でプロジェクトを開くと LLMUnity が自動でダウンロードします。
4. Talk 画面を使う場合は、LLMUnity の `LLM` コンポーネントで使う AI モデル（`.gguf`）をダウンロードして設定してください。モデルもサイズが大きいため、リポジトリには含めていません。
5. `Assets/Scenes/SampleScene.unity` を開きます。
6. 下の「API キーの設定」に従ってキーを入力し、再生（Play）します。

## API キーの設定

API キーは **`Assets/Resources/ApiSecrets.json`** に書きます。このファイルは `.gitignore` で除外しているので、GitHub には上がりません。

1. リポジトリ直下の `ApiSecrets.example.json` を `Assets/Resources/ApiSecrets.json` にコピーします。
2. 必要なキーを書き込みます。

```json
{
  "geminiApiKey": "ここに Gemini のキー",
  "geminiSummaryApiKey": "",
  "geminiTopicApiKey": "",
  "geminiTtsApiKey": "",
  "geminiShadowApiKey": "",
  "geminiVocabApiKey": "",
  "geminiAnswerApiKey": "",
  "deeplApiKey": "ここに DeepL のキー",
  "discordWebhookUrl": "",
  "discordBotToken": "",
  "discordChannelId": "",
  "reminderWorkerUrl": "",
  "reminderSecret": "",
  "supabaseUrl": "",
  "supabaseAnonKey": "",
  "youtubeApiKey": "",
  "youtubeShadowApiKey": "",
  "youtubeVocabApiKey": "",
  "summaryDiscordWebhookUrl": "",
  "summaryDiscordChannelId": "",
  "pronunciationDiscordWebhookUrl": "",
  "pronunciationDiscordChannelId": "",
  "repeatDiscordWebhookUrl": "",
  "repeatDiscordChannelId": "",
  "merriamWebsterApiKey": "",
  "pixabayApiKey": ""
}
```

| 項目 | 取得先 | 必須 |
|---|---|---|
| geminiApiKey | [Google AI Studio](https://aistudio.google.com/apikey) | ✅ |
| geminiSummaryApiKey / geminiTopicApiKey / geminiTtsApiKey / geminiShadowApiKey / geminiVocabApiKey / geminiAnswerApiKey | Google AI Studio（要約練習・お題・リピーティングの読み上げ・シャドーイング・単語帳の発音の採点・短い答えの問題作り用。**それぞれ別のプロジェクトで作る**と無料枠が増える） | 任意。空なら geminiApiKey。上限になったらほかのキーを借りる |
| deeplApiKey | [DeepL API](https://www.deepl.com/pro-api) | DeepL Write を使う場合 |
| merriamWebsterApiKey | [dictionaryapi.com](https://dictionaryapi.com/)（Merriam-Webster's **Learner's Dictionary** の Key (Dictionary)） | 単語帳を使う場合 |
| pixabayApiKey | [Pixabay API](https://pixabay.com/api/docs/)（単語カードの画像） | 任意。空なら画像は出さない |
| youtubeApiKey | [Google Cloud Console](https://console.cloud.google.com/apis/library/youtube.googleapis.com)（YouTube Data API v3 を有効化） | 要約練習を使う場合 |
| youtubeShadowApiKey | 同上（シャドーイングの動画検索用。別のプロジェクトで作ると上限が分かれる） | 任意。空なら youtubeApiKey |
| youtubeVocabApiKey | 同上（単語帳の動画の場面の検索用） | 任意。空なら youtubeApiKey |
| supabaseUrl / supabaseAnonKey | [Supabase](https://supabase.com/) | ログイン・日記保存を使う場合 |
| discordWebhookUrl | Discord のサーバー設定 | 任意 |
| summaryDiscordWebhookUrl | Discord のサーバー設定（要約練習の結果を送るチャンネルの Webhook。日記とは別） | 任意。空なら要約の結果は送らない |
| pronunciationDiscordWebhookUrl | Discord（お題の発音チェックのアドバイスを送るチャンネル） | 任意 |
| repeatDiscordWebhookUrl | Discord（リピーティングの結果を送るチャンネル） | 任意 |
| reminderWorkerUrl / reminderSecret | 自前のリマインダー用 Worker | 任意 |

> 💡 **Gemini のキーの切り替え**：上限（429）になったら、そのキーでメインのモデル → 予備のモデル → ほかの画面のキー、の順に試します。Gemini の上限は**キーごとではなく Google Cloud のプロジェクトごと**です。同じプロジェクトでキーを何個作っても、上限は増えません。

> ⚠️ **注意**
> - キーは Inspector に入力しないでください。Inspector の値はシーンファイル（`.unity`）に保存され、コミットすると GitHub に公開されてしまいます。
> - `ApiSecrets.json` はビルドしたアプリ（APK など）の中には含まれます。キー入りのビルドは公開しないでください。

## 単語帳の動画の場面（Tools~/clips）

単語帳の Videos ボタンで出す「場面の一覧」を、パソコンで作るスクリプトです（任意。無くても、その場で探して出せます）。

- `Tools~/clips/.env` に `YOUTUBE_API_KEY` / `SUPABASE_URL` / `SUPABASE_SERVICE_KEY` を入れる（`.env.example` をコピー）
- `python build_clips.py --dry-run` で確認 → `python build_clips.py` で Supabase に入れる
- くわしくは `Tools~/clips/README.md`、確認の手順は `docs/vocab-video-clips-check.md`

## 関連リポジトリ

このアプリが使うサーバー側のコードは、別のリポジトリで管理しています。

| リポジトリ | 内容 |
|---|---|
| [my-project-connected-to-discord](https://github.com/haru452/my-project-connected-to-discord) | Cloudflare Workers のコード（日記のリマインダー・Discord 通知）。毎日の実行は [cron-job.org](https://cron-job.org/en/) から呼び出し |
| [database_learnApp](https://github.com/haru452/database_learnApp) | データベース（Supabase）のコードと Web 版 |

## スクリプト構成

```
Assets/Script/
├── CorrectionDiary/
│   ├── AppManager.cs            … 録音 → 文字起こし → 添削 → 表示 の全体制御
│   ├── DiaryManager.cs          … 日記モードの制御
│   ├── SummaryPracticeManager.cs… 動画の要約練習（Versant 対策）の制御
│   ├── TopicPracticeManager.cs  … お題スピーキングの制御
│   ├── MistakeTypes.cs          … よくあるミスの 10 種類
│   ├── Repeat/                  … リピーティング
│   ├── Shadow/                  … シャドーイング
│   ├── Answer/                  … 短い答えの練習（問題の作り置き）
│   ├── Sounds/                  … 苦手な発音の集計
│   ├── YouTubeSearchClient.cs   … YouTube Data API で短い動画を検索
│   ├── YouTubePlayerView.cs     … YouTube をアプリ内（WebView）で再生
│   ├── AudioRecorder.cs         … マイク録音と WAV 変換
│   ├── NativeSpeechRecognizer.cs… 端末内蔵の音声認識ラッパー
│   ├── GeminiApiClient*.cs      … Gemini API（文字起こし・添削・採点・読み上げ・問題作り。キーの切り替え）
│   ├── DeepLApiClient.cs        … DeepL Write API（言い換え）
│   ├── TextDiff.cs              … 原文と添削後の差分計算
│   ├── DiaryStreakTracker.cs / DiaryStreakView.cs … 連続記録
│   ├── SupabaseService.cs / AuthPanel.cs / LoginGate.cs … ログイン・データ保存
│   ├── ReminderClient.cs        … リマインダー Worker への通知
│   ├── Vocab/                   … 単語帳（FSRS・辞書・CEFR-J・発音チェック・画像・動画の場面・画面）
│   ├── ApiSecrets.cs            … API キーの読み込み
│   └── HttpUtil.cs              … 通信の共通処理
├── Talk3D/                      … 3D キャラクターとの会話（実験中）
├── SpeechRecognizerBridge.mm    … iOS 用音声認識ブリッジ
└── AndroidManifest.xml          … マイク権限など
```

内部の仕組みは [`docs/TECHNICAL.md`](docs/TECHNICAL.md)、もともとの要件は [`Assets/prd.md`](Assets/prd.md) を参照してください。

## ビルド

- **Android**：File → Build Settings → Android を選んで Build。マイクの権限が必要です。
- **iOS**：iOS を選んで Build します。`Info.plist` へのマイク・音声認識の利用目的の追加と `Speech.framework` のリンクは、`Assets/Editor/IosPostBuild.cs` がビルド後に自動で行います（iOS 実機では未検証）。

## 注意

- 次のものは `.gitignore` で除外しています：`Library/`、`Temp/`、`Build/`、LLMUnity のライブラリ、`*.gguf`（AI モデル）
- GitHub は 100MB を超えるファイルを受け付けません。大きいファイルを追加するときは注意してください。

## ライセンス

このプロジェクトのコードは [MIT License](LICENSE) で公開しています。

ただし、次のものは MIT License の対象外です。それぞれの利用条件に従ってください。

- `Assets/My assets/amanogawa.vrm`：VRM モデルの中に書き込まれた利用条件（再配布・改変は不可）
- `Packages/` 内のパッケージ（UniVRM など）、TextMesh Pro、LLMUnity：各パッケージのライセンス


メモ
i use　claude opus and it can create ui for me, so i dont have to aligne it...

## はじめに

[その１](https://qiita.com/haru452/items/8656d7d15db5d20c7b41) では、話した英語を AI が添削してくれる英語日記アプリの、添削・日記・連続記録・リマインダーの仕組みを書きました。

その１を書いたあと、練習の種類を増やしました。理由としては、単純によりversantの対策になるアプリになったらいいなと思っていたからです。(そして就活で自作アプリでこんだけ英語上手になったと言いたい！！)

今回は、増やした機能のうち、作っていて面白かったところ・苦労したところを書きます。

- 前回のリポジトリ：https://github.com/haru452/Learningapp
- 今回のリポジトリ：前回と同じリポジトリの develop ブランチです (develop-words-book は、AI にコードを書かせて壊れたときのためのバックアップです）


## 増えた機能



| 画面 | できること |
|---|---|
| 🎬 Summary | 短い英語の動画を見て、制限時間で要約を話す。AI が採点 |
| 🗣 Topic | お題について話す → 採点 → 直された文を読み直す |
| 🔁 Repeat | 聞いた英文を言い返す (リピーティング)。YouTube の動画でシャドーイング |
| ❓ Answer | 短い質問に一言で答える (Versant の Short Answer Questions) |
| 👄 苦手な発音 | 言えなかった音を数えて、毎日その音の練習動画を Discord に送る |
| 📚 Vocab | 単語帳。FSRS で復習日を決める。画像でイメージして覚えられる。単語が使われている動画の場面も見られる |

Summary・Topic・Answer は、Versant の問題の形に合わせています。

## 全体の構成 (その１から増えたもの)

| 項目 | 使ったもの |
|---|---|
| 動画 | YouTube Data API、YouTube の埋め込みプレーヤー |
| 採点・文字起こし・問題作り | Gemini API (動画をそのまま読ませる・音声をそのまま渡す) |
| 読み上げ | Gemini の音声合成 (TTS) |
| 単語の一覧 | CEFR-J の単語リスト (A1〜B2) |
| 辞書 | Merriam-Webster's Learner's Dictionary API |
| 復習の日程 | FSRS-4.5 (自分で C# に移植) |
| カードの画像 | Pixabay API |
| 例文・リピーティングの文 | Tatoeba |

## 1. Summary：Gemini に動画そのものを見てもらって採点する

Versant の要約問題の練習です。字幕付きの短い英語の動画 (40 秒〜2 分半) を出して、見終わったら 60 秒で要約を話します。

最初は「字幕を取って、字幕と自分の要約を比べて採点」と考えていましたが、**YouTube の URL をそのまま Gemini に渡して、動画を見てもらって採点**する形にしました。

- 採点：内容 50 + 構成 25 + 文法・語彙 25
- 押さえた点・抜けた点・お手本の要約も返してもらう
- 結果は前回と同じく `responseSchema` で JSON の形を決める

```csharp:GeminiApiClient.cs
// 公開されている YouTube 動画は URL をそのまま渡せる
// fps を指定すると、動画を読むコマ数を減らせる (指定しなければ Gemini の初期値)
private static string YouTubePart(string videoUrl, float fps = 0f) =>
    "{\"file_data\":{\"file_uri\":\"" + HttpUtil.JsonEscape(videoUrl) + "\"}" +
    (fps > 0f
        ? ",\"video_metadata\":{\"fps\":" + fps.ToString(CultureInfo.InvariantCulture) + "}"
        : "") +
    "}";
```

```csharp:GeminiApiClient.Practice.cs
// 動画はプロンプトより前に置くのが推奨されている
string tail = "," + TextPart(prompt) + (wav != null ? "," + AudioPart(wav) : "");

// 要約の採点に必要なのは主に音声なので、映像は低画質・少ないコマ数 (2 秒に 1 コマ) で読ませてトークンを節約する
string innerJson;
try
{
    innerJson = await GeneratePartsAsync(
        YouTubePart(youtubeUrl, VideoFps) + tail, SummaryGradeSchema,
        extraConfig: "\"mediaResolution\":\"MEDIA_RESOLUTION_LOW\",\"temperature\":0.2");
}
catch (HttpException e) when (e.StatusCode == 400)
{
    // この設定が受け付けられなかったときは、普通の送り方でやり直す
    innerJson = await GeneratePartsAsync(YouTubePart(youtubeUrl) + tail, SummaryGradeSchema);
}
```

動画は 1 秒 1 コマで読ませると、2 分の動画でもかなりのトークンになります。要約の採点では映像より音声が大事なので、`video_metadata.fps` を 0.5 にして、`mediaResolution` も低くしています。

## 2. Topic
<img src="https://qiita-image-store.s3.ap-northeast-1.amazonaws.com/0/4515452/5d4792b9-7958-49df-b597-6206eb8de046.png" width="49%"> <img src="https://qiita-image-store.s3.ap-northeast-1.amazonaws.com/0/4515452/8e746af3-ed3f-4961-b62c-cf3b8af88fe0.png" alt="9927.png" alt="9926.png" width="49%">

お題 → 考える時間 → 45〜60 秒話す → 採点 → 言い直し、の流れです。

その１では「端末の音声認識で文字にしてから Gemini に送る」でしたが、Topic では**録音をそのまま Gemini に渡して、文字起こしと採点を 1 回のリクエストで**やっています。待ち時間が半分くらいになりました。(体感で)

採点のあとは、直された文を声に出して読み直す「Read aloud」があります。読んだ文と直された文を単語ごとに比べて、言えた単語を緑・言えなかった単語を赤にします。比べ方は、その１の Discord の下線と同じ LCS (単語単位の差分) を使い回しています。

### よくあるミスを 10 種類に分けて数える

日記・要約・お題で直された所を、Gemini に「冠詞・時制・前置詞・…」の 10 種類に分けてもらい、Supabase に貯めています。
多かったミスは、次にお題を話す前に「今日の注意」として出し、採点でも重点的に見てもらいます。

自分はこれのおかげで、単数・複数がめっちゃ苦手なんだなと知れました。
![image.png](https://qiita-image-store.s3.ap-northeast-1.amazonaws.com/0/4515452/204240bb-2606-4216-b74f-3e1e4d2d1772.png)


## 3. Repeat：リピーティングとシャドーイング

### リピーティング

英文を見せずに読み上げて、言い返します。出す文の順番は次のとおりです。

1. 前回 80% 未満だった文
2. 自分の日記で直された文
3. Tatoeba の文

自分の文を出すのがポイントで、「自分が実際に間違えた文」を何度も言うことになります。
声は、Tatoeba の文は人の録音、自分の文は Gemini の音声合成です。

### シャドーイング

YouTube の動画を流しながら録音して、お手本の文字起こしと比べます。

- お手本の文字起こしは、初めての動画だけ Gemini が URL から作り、端末に保存
- 録音は 1 分ずつに分けて Gemini で書き起こし、お手本と照合
- 送信に失敗しても録音は残し、「Check again」で失敗した所からやり直せる


## 4. Answer：待ち時間をなくすために、問題を作り置きする

Versant の「短い質問に一言で答える」問題です。Gemini に 10 問作ってもらい、1 問ずつ音声だけで出して、すぐに答えます。

最初は Start を押してから問題を作っていたので、**始まるまで毎回待たされる**のが不満でした。そこで作り置きにしました。

- アプリを開いたら、裏で 10 問作って端末に保存しておく
- Start で作り置きから取り出すので、すぐ始まる
- 使った分は裏で作り足す
- 途中でやめたら、答えていない問題は作り置きに戻す
- Gemini が混んでいて作れなかったら、1 分おきに最大 5 回作り直す

```csharp:ShortAnswerStock.cs
/// <summary>作り置きから最大 n 問を取り出す (残りは保存したまま)</summary>
public static List<ShortAnswerQuestion> Take(int n)
{
    var all = Load();
    var (taken, rest) = Split(all, n);
    Save(rest);
    return taken;
}

/// <summary>作り置きが足りなければ、裏で作り足す (同時に動くのは 1 つだけ)</summary>
public static Task FillAsync(GeminiApiClient client, Func<IList<string>> recent)
{
    if (!_filling.IsCompleted) return _filling;
    if (client == null || Count >= Size) return Task.CompletedTask;
    _filling = DoFillAsync(client, recent);
    return _filling;
}

private static async Task DoFillAsync(GeminiApiClient client, Func<IList<string>> recent)
{
    try
    {
        var have = Load();
        // 最近出した問題と作り置きの問題は、もう作らないように伝える
        var avoid = have.Select(q => q.Question).Concat(recent?.Invoke() ?? new List<string>()).ToList();
        var made = await client.GenerateShortAnswerQuestionsAsync(avoid);
        Save(Merge(Load(), made));   // 作っている間に取り出された分も考えて、読み直してから足す
        _retries = 0;
    }
    catch (Exception)
    {
        // Gemini が混んでいるときなどは、1 分おきに作り直す (最大 5 回)
        if (_retries < MaxRetries)
        {
            _retries++;
            _ = RetryLaterAsync(client, recent);
        }
    }
}
```

Start を押したときは、作り置きから取り出して、すぐに裏で作り足します。

```csharp:ShortAnswerScreen.cs
// 作り置きがあれば待たずに始める。足りなければ、作っている途中のものを待つ
var set = ShortAnswerStock.Take(GeminiApiClient.ShortAnswerCount);
if (set.Count < GeminiApiClient.ShortAnswerCount)
{
    await ShortAnswerStock.FillAsync(_questions, LoadRecent);
    set = ShortAnswerStock.Merge(set, ShortAnswerStock.Take(GeminiApiClient.ShortAnswerCount - set.Count));
}
_set = set;
SaveRecent(_set.Select(q => q.Question));
_ = ShortAnswerStock.FillAsync(_questions, LoadRecent);   // 使った分を裏で作り足す
```

`FillAsync` は動いている途中ならその `Task` を返すので、「開いたときの作り足し」と「Start の作り足し」が重なっても、Gemini に 2 回頼むことはありません。

同じ問題が続かないように、新しく作るときは「作り置きにある問題」と「最近出した 60 問」を Gemini に渡して、それと被らない問題を頼んでいます。

## 5. Vocab：単語帳
![combined_9928_9929.png](https://qiita-image-store.s3.ap-northeast-1.amazonaws.com/0/4515452/c1f6509d-fabf-4f11-b6d8-f5839586fcd8.png)

### FSRS で復習の日を決める

単語帳は、Anki にも入っている **FSRS-4.5** で次の復習日を決めています。評価は `Didn't know` / `Hard` / `Knew it` / `Easy` の 4 段階で、「90% の確率で覚えている日」に次の復習を入れます。

式を C# に移して、公式の実装と同じ値になるかをテストで確かめました。

```csharp:Fsrs.cs
private const double Decay = -0.5;
private const double Factor = 19.0 / 81.0;

/// <summary>最後に復習してから elapsedDays 日後に思い出せる確率</summary>
public static double Retrievability(double elapsedDays, double stability) =>
    Math.Pow(1 + Factor * Math.Max(0, elapsedDays) / Math.Max(stability, 0.01), Decay);

/// <summary>想起率が retention (0.9) まで下がる日数 = 次の復習までの間隔</summary>
public static int IntervalDays(double stability, double retention)
{
    double days = stability / Factor * (Math.Pow(retention, 1 / Decay) - 1);
    if (double.IsNaN(days) || days < 1) return 1;
    if (days > MaxIntervalDays) return MaxIntervalDays;
    return Math.Max(1, (int)Math.Round(days, MidpointRounding.AwayFromZero));
}

/// <summary>2 回目以降の評価のあとの記憶 (1 = Didn't know, 2 = Hard, 3 = Knew it, 4 = Easy)</summary>
public static FsrsMemory Next(FsrsMemory m, int rating, double elapsedDays)
{
    int g = ClampRating(rating);
    double s = Math.Max(m.Stability, 0.01);
    double d = ClampD(m.Difficulty);
    double r = Retrievability(elapsedDays, s);

    double nd = ClampD(W[7] * InitialDifficulty(3) + (1 - W[7]) * (d - W[6] * (g - 3)));
    double ns;
    if (g == 1)
    {
        ns = W[11] * Math.Pow(d, -W[12]) * (Math.Pow(s + 1, W[13]) - 1) * Math.Exp(W[14] * (1 - r));
        ns = Math.Min(ns, s);   // 忘れたのに強くはならない
    }
    else
    {
        double hard = g == 2 ? W[15] : 1;
        double easy = g == 4 ? W[16] : 1;
        ns = s * (Math.Exp(W[8]) * (11 - d) * Math.Pow(s, -W[9]) * (Math.Exp(W[10] * (1 - r)) - 1) * hard * easy + 1);
    }
    if (double.IsNaN(ns) || ns < 0.01) ns = 0.01;
    return new FsrsMemory { Stability = ns, Difficulty = nd };
}
```

`W` は FSRS-4.5 の標準パラメータ (17 個) をそのまま使っています。`Stability` は「思い出せる確率が 90% に下がるまでの日数」の目安なので、`retention = 0.9` なら次の間隔はほぼ `Stability` 日になります。

出す単語は、CEFR-J の単語リストから自分のレベル (A1〜B2) の単語を選びます。期限が過ぎた復習が 50 語を超えたら、新しい単語は出しません (復習がたまりすぎないように)。

### 辞書と例文

意味・発音・例文は Merriam-Webster の学習者向け辞書から取っています。
名詞と動詞のように品詞が複数ある単語は、品詞ごとに例文を出すようにしました。

### 画像でイメージして覚える

カードの裏には、単語の画像も出しています。日本語の訳だけで覚えるより、**画像で単語のイメージをつかんだほうが記憶に残りやすい**と考えたためです。



画像は Pixabay の API で、単語をそのまま検索して取っています。

- 検索はセーフサーチをオンにし、人気順の上から 3 件を取って、いちばん上の画像を使う
- 取った画像は端末に保存し、同じ単語では Pixabay に聞き直さない (Pixabay の利用ルールでも、保存して使うことになっています)
- 画像が見つからなかった単語は「画像なし」と記録し、30 日間は探し直さない
- カードの表を見ている間に裏の画像を先に取っておくので、めくったときにすぐ出る
- 画面には、画像を Pixabay から取ったことを表示する

```csharp:PixabayClient.cs
private const string ApiUrl = "https://pixabay.com/api/";

/// <summary>単語の画像を探す URL (セーフサーチ・英語で検索・人気順で 3 件)</summary>
public static string SearchUrl(string apiKey, string word) =>
    ApiUrl + "?key=" + Uri.EscapeDataString(apiKey ?? "") + "&q=" + Uri.EscapeDataString(word ?? "") +
    "&lang=en&image_type=all&safesearch=true&order=popular&per_page=3";

/// <summary>検索の応答から、いちばん上の画像の URL (webformatURL、幅 640 まで) を選ぶ。無ければ null</summary>
public static string PickImageUrl(string json)
{
    if (string.IsNullOrEmpty(json)) return null;
    object root;
    try { root = MiniJson.Parse(json); }
    catch (Exception) { return null; }
    var hits = (root as Dictionary<string, object>)?.TryGetValue("hits", out var h) == true ? h as List<object> : null;
    if (hits == null) return null;
    foreach (var hit in hits)
    {
        if (!(hit is Dictionary<string, object> d)) continue;
        string url = JsonGet.Str(d, "webformatURL");
        if (url.Length > 0) return url;
    }
    return null;
}
```

ただ、困ったことに、**単語によっては合わない画像が出る**ことでした。
そこで、合わない画像は `Hide` を押すと、その単語だけ隠せるようにしました。隠したあとは細い「Show image」の行になり、押すとまた出せます。

### 発音チェック

`Say it` で単語を言うと、端末の音声認識でその場で `Clear` / `Close` / `Try again` を出します。
同時に録音もしておき、1 回分の最後に **最大 20 語を 1 回のリクエストで** Gemini に採点してもらいます。1 語ずつ送ると、すぐに 1 日の上限に届いてしまうためです。

### 動画の場面 (YouGlish のような機能)

カードから、その単語が実際の英語の動画で使われている場面を、字幕つきで見られます。


場面は 2 つの方法で集めて、Supabase の表に貯めています。

1. **パソコンで先に作る**：Python のスクリプトが、決めたチャンネルの動画の字幕から単語の場面を探して入れる
2. **アプリでその場で探す**：1 で見つからない単語だけ、アプリが YouTube を検索して字幕から場面を探す。見つけた場面は表に足すので、次からはすぐ出る

さらに、**これから出る 20 単語の場面を裏で先に探しておく**ので、ボタンを押したときに待たずに見られます。

YouTube Data API の検索は 1 回 100 ユニットと重く、1 日の上限 (10,000 ユニット) にすぐ届きます。そこでアプリの中で「1 日 30 回まで」と数えています。


## 6. Gemini の API キーを画面ごとに分ける

機能が増えて、Gemini の 1 日の上限 (429) に届くことが増えました。
そこで、日記・要約・お題・読み上げ・シャドーイング・単語帳で API キーを分け、**上限に届いたら、ほかの画面のキーを借りる**ようにしています。

単語帳の画面では、自分のキーのあとに、ほかの画面のキーを「借りるキー」として渡しています。

```csharp:VocabManager.cs
var secrets = ApiSecrets.Data;
string diaryKey = secrets.geminiApiKey;
var gemini = new GeminiApiClient(ApiSecrets.Pick(secrets.geminiVocabApiKey, diaryKey), geminiModel, geminiFallbackModel,
    backupApiKeys: new[] { diaryKey, secrets.geminiSummaryApiKey, secrets.geminiTopicApiKey,
                           secrets.geminiTtsApiKey, secrets.geminiShadowApiKey });
```

`GeminiApiClient` では、「自分のキーでメインのモデル → 予備のモデル、それでも上限ならその次のキーで…」の順に試します。

```csharp:GeminiApiClient.cs
/// <summary>自分のキー → ほかのキーの順に並べる。空・重複したキーは除く</summary>
public static string[] KeyList(string apiKey, string[] backupApiKeys)
{
    var all = new List<string> { apiKey };
    if (backupApiKeys != null) all.AddRange(backupApiKeys);
    return all.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().ToArray();
}

// 429: 利用上限 / 404: モデル名が見つからない → 同じモデルではやり直さず、すぐ次のモデル・キーへ
private static bool ShouldSwitchModel(HttpException e) => e.StatusCode == 429 || e.StatusCode == 404;
// 500 / 503: 一時的な混雑 → 少し待てば通ることが多いので、同じモデルでやり直す
private static bool IsTemporary(HttpException e) => e.StatusCode == 500 || e.StatusCode == 503;

private async Task<string> PostToModelsAsync(string body, string[] models, int timeoutSeconds, int attemptsPerModel = MaxAttemptsPerModel)
{
    var keys = UsableKeys;
    var order = AttemptOrder(keys.ToArray(), models);   // (キー1, メイン), (キー1, 予備), (キー2, メイン), …
    HttpException lastError = null;

    foreach (var (keyIndex, model) in order)
    {
        string url = $"{EndpointBase}{model}:generateContent";
        for (int attempt = 0; attempt < attemptsPerModel; attempt++)
        {
            try
            {
                return await HttpUtil.PostJsonAsync(
                    url, body, new[] { ("x-goog-api-key", keys[keyIndex]) }, timeoutSeconds: timeoutSeconds);
            }
            catch (HttpException e) when (ShouldSwitchModel(e))
            {
                lastError = e;
                break;   // 次のモデル・キーへ
            }
            catch (HttpException e) when (IsTemporary(e))
            {
                lastError = e;
                if (attempt < attemptsPerModel - 1)
                    await Task.Delay(1000 * (1 << attempt));   // 1 秒 → 2 秒待ってやり直す
            }
        }
    }
    throw lastError;
}
```

429 のときに同じモデルで送り直すと、さらに枠を使ってしまうだけなので、待たずに次へ進むようにしています。上限はモデルごとに別なので、まず予備のモデルを試し、それでもだめならキーを替えます。



## まとめ

その１の「話す → 直してもらう」に加えて、要約・お題・短い答え・リピーティング・シャドーイング・単語帳と、Versant に必要な練習をひと通りアプリの中でできるようになりました。
versantの結果が実際どうなったかは半年後に記事にすると思います。（結果が良ければ）



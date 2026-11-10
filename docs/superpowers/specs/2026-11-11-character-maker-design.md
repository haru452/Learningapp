# CharacterMaker（1 枚絵から話す 2D キャラを作るデスクトップアプリ）設計

日付: 2026-11-11

## 目的

- 1 枚の絵から、口パクとまばたきをする 2D キャラを作る。
- 作ったキャラは、デスクトップアプリの中で動きを確かめられる。
- 書き出したキャラを Talk（Unity）に入れると、確認用の画面で同じように動く。

できたと言える状態:
- 好きな 1 枚絵から 10 分ほどでキャラを作れて、デスクトップで口パクする。
- 書き出したフォルダを Talk の `StreamingAssets/Characters/` に置くと、Unity の確認用画面で同じように口パク・まばたきする。

## 決めたこと

| 項目 | 決定 |
|---|---|
| デスクトップアプリ | Electron（HTML/JavaScript）。`C:\Users\haru0\club\class\es\CharacterMaker` に独立した git リポジトリとして作る |
| 差分の作り方 | AI で作る（Gemini か、PC で動かす Waifu Diffusion を設定で選ぶ。だめなら絵をゆがめて自動で作る）＋ 自分で描いた絵の読み込み |
| Talk への渡し方 | 書き出したフォルダを Talk の Unity プロジェクトに入れてビルドに含める |
| Unity 側 | 確認用の画面だけ。Talk の会話画面・キャラ切り替えボタンは今回は作らない |
| 動き | 口パク・まばたき・ゆっくりした上下の揺れだけ。表情は作らない |

## 今回作らないもの

- Talk の会話画面への組み込み、キャラ切り替えボタン
- 表情（喜ぶ・驚くなど）、うなずき
- Supabase からのダウンロード
- スマホ上での編集

## 1. 書き出すファイルの形（2 つのアプリの約束）

```
Characters/
  index.json          … キャラのフォルダ名の一覧 {"characters": ["mika", ...]}（Unity の JsonUtility は配列だけの JSON を読めないため、オブジェクトで包む）
  mika/
    character.json
    base.png          … 元の 1 枚絵
    mouth_1.png       … 口・半開き（口の四角の大きさ、透明あり）
    mouth_2.png       … 口・全開
    eye_l_closed.png  … 左目・閉じ（左目の四角の大きさ）
    eye_r_closed.png  … 右目・閉じ
```

- `index.json` は、Android では StreamingAssets のフォルダの中身を一覧できないために置く。書き出しのたびにアプリが作り直す（すでにあるキャラ名は残し、新しい名前を足す）。
- 差分は印の場所だけの小さな絵。元の絵の上に重ねる。ふち（四角の短い辺の 15%）は透明に向かってぼかし、境目を目立たなくする。

`character.json`:

```json
{
  "version": 1,
  "name": "mika",
  "imageWidth": 1024,
  "imageHeight": 1536,
  "base": "base.png",
  "mouth": {
    "rect": { "x": 0.45, "y": 0.38, "w": 0.10, "h": 0.05 },
    "frames": ["mouth_1.png", "mouth_2.png"]
  },
  "eyes": [
    { "rect": { "x": 0.38, "y": 0.28, "w": 0.08, "h": 0.04 }, "closed": "eye_l_closed.png" },
    { "rect": { "x": 0.54, "y": 0.28, "w": 0.08, "h": 0.04 }, "closed": "eye_r_closed.png" }
  ]
}
```

- `rect` は絵の幅・高さに対する割合（0〜1）。**原点は左上**、y は下向き。Unity 側は読み込むときに左下原点へ直す。
- `mouth.frames` は [半開き, 全開]。閉じた口は元の絵そのもの。
- `version` が 1 以外なら、Unity 側は読み込まずにメッセージを出す。

## 2. 口パク・まばたきの決まり（両方のアプリで同じ数値を使う）

口の開き（0〜1）は、Talk の `PseudoLipSync` と同じ作り方で出す:
- 最大 0.8、最小 0.2、目標を変える間隔 0.06〜0.16 秒、一瞬閉じる確率 0.2（そのときの目標 0.05）、追従の速さ 20。

開き → 表示する口:
- 0.15 未満 → 閉じ（元の絵）
- 0.15 以上 0.5 未満 → 半開き（`mouth_1`）
- 0.5 以上 → 全開（`mouth_2`）

まばたき: 2〜6 秒ごと（ランダム）に、0.12 秒だけ両目の閉じた差分を出す。
揺れ: 3 秒周期で、表示の高さの 0.5% だけ上下にゆっくり動く。

## 3. デスクトップアプリ（Electron）

### 画面の流れ（1 つのウィンドウで 4 段階）

1. **絵を開く**: PNG・JPG をドラッグ＆ドロップかファイル選択で開く。作りかけのキャラも開ける。
2. **印を付ける**: 「口」「左目」「右目」を選んで四角で囲む。後からドラッグで動かす・大きさを変えることができる。3 つそろうまで次へ進めない。
3. **差分を作る**: 差分（口・半開き、口・全開、左目・閉じ、右目・閉じ）ごとに 3 つのボタン:
   - **AI で作る**: 設定で選んだ方（Gemini か Waifu Diffusion）で作る。「もう一度」で作り直せる。どちらも、返ってきた絵から印の四角の部分だけを取り出してふちをぼかす。
     - Gemini: 印の四角の周りに、四角の大きさの 50% の余白を付けて切り抜き、指示文（例: 「口だけを半分開けて。絵柄と大きさは変えない」）と一緒に送る。返ってきた絵を切り抜いた大きさに合わせて縮める。
     - Waifu Diffusion: PC で動いている Stable Diffusion WebUI（AUTOMATIC1111 / Forge。`--api` を付けて起動）の `/sdapi/v1/img2img` に、元の絵と、印の四角だけを白くしたマスクを送るインペイント。プロンプト（例: `open mouth`、`closed eyes`）と描き直しの強さ（初期値 0.6）、「印の周りだけを高い解像度で描く」設定を付ける。使うモデル（Waifu Diffusion など）は WebUI 側で選んでおく。
   - **自動で作る**（通信なし）:
     - 口: 四角の上下中央より下の画素を下へずらす（半開き 25%、全開 50%。四角のふちに向かってずれを小さくする）。開いたすき間に、四角の中で一番暗い色をさらに暗くした楕円を描く。
     - 目: 四角のすぐ上の画素の色（中央値）で目の楕円を塗り、四角の中で一番暗い色で、下から 40% の高さにまぶたの線を描く。
   - **自分の絵を読み込む**: 元の絵と同じ大きさなら印の四角を切り抜く。四角と同じ大きさならそのまま使う。どちらでもなければ四角の大きさに合わせて縮め、「大きさが違ったので合わせました」と出す。
4. **確かめる・書き出す**:
   - 「話す」ボタン（押すたびに話す・止める）と、入力した文を読み上げる間だけ話す機能（ブラウザの読み上げ機能）。§2 の決まりで口パク・まばたき・揺れをする。
   - 名前を付けて「書き出す」。書き出し先は初めは `C:\Users\haru0\club\class\es\My project\Assets\StreamingAssets\Characters`。設定で変えられる。同じ名前があれば上書きしてよいか聞く。

### そのほか

- **設定画面**: AI の選択（Gemini / Waifu Diffusion）。Waifu Diffusion を選んだときは WebUI の URL（初期値 `http://127.0.0.1:7860`）とプロンプトの頭に付ける言葉（初期値 `masterpiece, best quality`）。Gemini を選んだときは API キーと、画像生成に使うモデル名。キーは Electron の `safeStorage` で暗号化して PC のアプリ用フォルダに保存する（git には入らない）。モデル名の初期値は、実装のときに Gemini の公式ドキュメントで画像の編集に対応したモデルを確かめて決める。
- **自動保存**: 作業中のキャラ（元の絵、印、作った差分）をアプリ用フォルダに保存し、次に開いたときに続きから始められる。
- **失敗したとき**: 通信の失敗・上限・絵が返ってこないときは、理由を短く出し、「自動で作る」をすすめる。Gemini で API キーが無いとき、または Waifu Diffusion で WebUI につながらないとき（「WebUI を `--api` 付きで起動してください」と出す）は、AI のボタンを押せなくし、設定画面へ案内する。

### ファイルの分け方

API キーを画面側に渡さないため、Gemini への通信とファイルの読み書きは main プロセスで行う。画像の計算は画面に依存しない純粋な関数にして、Node のテストで確かめる。

```
CharacterMaker/
  package.json
  main/
    main.js          … ウィンドウ、ファイル選択、IPC のつなぎ
    preload.js       … 画面に渡す関数だけを公開
    settings.js      … API キー（safeStorage）とモデル名、書き出し先
    workspace.js     … 作りかけの自動保存と読み込み
    exporter.js      … キャラのフォルダと index.json を書き出す
    gemini.js        … Gemini への送信と返事から絵を取り出す（fetch は外から渡す）
    sd-webui.js      … WebUI へのインペイントの送信と返事から絵を取り出す（fetch は外から渡す）
  renderer/
    index.html, style.css
    app.js           … 4 段階の切り替え
    steps/open.js, steps/mark.js, steps/variants.js, steps/preview.js
  core/              … 画面に依存しない（テストする）
    pack.js          … character.json を作る・確かめる
    regions.js       … 割合⇔画素の変換、余白付きの切り抜き範囲
    lipsync.js       … PseudoLipSync と同じ口の開きの計算（乱数は外から渡す）
    mouth-frame.js   … 開き → 表示する口
    blink.js         … まばたきの時間
    feather.js       … ふちのぼかし
    warp.js          … 自動で作る口・目の差分（RGBA の配列を受け取る）
  test/              … node --test
```

各ファイルは 600 行以内。

## 4. Unity 側（Talk プロジェクト・確認用の画面だけ）

`Assets/Script/Talk2D/`:

| ファイル | 役目 |
|---|---|
| `CharacterPack.cs` | `index.json` と `character.json` と絵を `UnityWebRequest` で読む（エディタと Android の両方で動く）。左上原点 → 左下原点の変換 |
| `Sprite2DMouthTarget.cs` | `MouthTargetBase` を継ぐ。開き（0〜1）を §2 の決まりで口の差分に切り替える |
| `Character2DView.cs` | uGUI の RawImage で元の絵と差分を重ねて表示。まばたきと揺れ |
| `Character2DTestUI.cs` | キャラを選ぶドロップダウンと「話す」ボタン。`PseudoLipSync.IsSpeaking` を切り替える |

- 確認用のシーン `Character2DTest.unity` を作る。Talk の既存のシーンと `Talk3D` のコードは変えない。
- 読み込みに失敗したら（ファイルが無い、version が違う）、画面に理由を出す。

## 5. テスト

- デスクトップ: `core/` の関数を `node --test` で確かめる（character.json の形、割合と画素の変換、口の切り替えのしきい値、口パクの計算を決まった乱数で再現、ぼかし、自動の差分が四角の外を変えないこと）。`gemini.js` と `sd-webui.js` は偽の fetch で、送る中身（マスクの位置を含む）と返事の取り出しを確かめる。
- 手で確かめること（追加）: WebUI を `--api` 付きで起動し、Waifu Diffusion のモデルで口と目の差分を作れること。
- Unity: Unity を使わない判断（`character.json` の中身の確認、原点の変換、口の切り替えのしきい値、まばたき）を `Character2DLogic.cs` にまとめ、既存の `Tests~/LogicTests` に足して確かめる。
- 手で確かめること: 実際の絵で印を付け、Gemini と自動の両方で差分を作り、書き出して Unity の確認用画面で動くこと。

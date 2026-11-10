# CharacterMaker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 1 枚絵に口・目の印を付けて差分を作り、口パクする 2D キャラとして書き出す Electron アプリと、それを読み込んで確かめる Unity の確認用画面を作る。

**Architecture:** Electron アプリ（新しいリポジトリ `C:\Users\haru0\club\class\es\CharacterMaker`）は、画素の計算を `core/`（純粋な関数・Node でテスト）、ファイルと通信を `main/`（main プロセス・fetch と fs は外から渡してテスト）、画面を `renderer/` に分ける。2 つのアプリは `Characters/` フォルダの形（spec §1）と口パクの数値（spec §2）だけでつながる。Unity 側は Talk プロジェクトに `Assets/Script/Talk2D/` を足し、Unity を使わない判断は `Character2DLogic.cs` にまとめて既存の LogicTests で確かめる。

**Tech Stack:** Electron（最新の安定版）、素の HTML/JavaScript（ES modules、バンドラなし）、`node --test`（Node 24）、Unity 2022.3.62f3、uGUI、C#。

**Spec:** `docs/superpowers/specs/2026-11-11-character-maker-design.md`

## Global Constraints

- 1 ファイル 600 行以内（ユーザーの決まり）。
- 画面の文言・コメント・エラーメッセージは日本語。
- `character.json` の形・ファイル名・`version: 1`・左上原点の割合（0〜1）は spec §1 のとおり。`index.json` は `{"characters": [...]}`。
- 口パクの数値: 最大 0.8、最小 0.2、間隔 0.06〜0.16 秒、閉じる確率 0.2（目標 0.05）、追従の速さ 20。
- 口の切り替え: 0.15 未満 → 閉じ、0.5 未満 → 半開き、それ以上 → 全開。
- まばたき: 2〜6 秒ごと、0.12 秒。揺れ: 3 秒周期、表示の高さの 0.5%。
- 差分のふちのぼかし: 四角の短い辺の 15%。Gemini の切り抜きの余白: 四角の大きさの 50%。
- Waifu Diffusion: 初期 URL `http://127.0.0.1:7860`、描き直しの強さ 0.6、プロンプトの頭 `masterpiece, best quality`。
- 書き出し先の初期値: `C:\Users\haru0\club\class\es\My project\Assets\StreamingAssets\Characters`。
- API キーは main プロセスだけが持ち、`safeStorage` で暗号化して保存する。画面（renderer）には渡さない。
- Talk の既存のシーンと `Assets/Script/Talk3D/` は変えない。

## Review Focus

- 印の四角が絵の外にはみ出す・とても小さい（8px 未満）: 絵の中に収め、小さすぎる印は「もう少し大きく囲んでください」と出して次へ進ませない。→ Task 1 `clampRect` / `isUsableRect` のテスト。
- キャラ名が日本語・空白・空・既にある名前: フォルダ名に使える英数字・`_`・`-`（1〜32 文字）だけ受け付け、既にある名前は上書きしてよいか聞く。→ Task 1 `isValidName` と Task 5 `exportCharacter` の `exists` のテスト。
- `index.json` が壊れている・無い: 空として扱って作り直し、書き出しは止めない。→ Task 1 `mergeIndex` のテスト。
- 大きな絵（長い辺 4000px など）を WebUI に送る: 長い辺が 1536px を超えたら縮めて送り、戻った絵を元の大きさに戻してから切り出す。→ Task 3 `sdScale` のテスト。
- 書き出したフォルダの差分が 1 枚欠けている・version が違う: Unity の確認用画面は落ちずに理由を出す。→ Task 9 `Validate` のテスト。

---

## Part A: CharacterMaker（Electron）

作業場所はすべて `C:\Users\haru0\club\class\es\CharacterMaker`。

### Task 1: リポジトリの用意と、印・キャラ情報の計算（`core/regions.js`, `core/pack.js`）

**Files:**
- Create: `package.json`, `.gitignore`（`node_modules/`, `dist/`）, `README.md`（起動方法・WebUI の起動方法を数行）
- Create: `core/regions.js`, `core/pack.js`
- Test: `test/regions.test.js`, `test/pack.test.js`

**Interfaces:**
- Produces:
  - `toPixels(rect, imgW, imgH) -> {x, y, w, h}`（整数に丸める）
  - `toNormalized(px, imgW, imgH) -> {x, y, w, h}`
  - `clampRect(px, imgW, imgH) -> {x, y, w, h}`（絵の中に収める）
  - `isUsableRect(px) -> boolean`（w と h が 8 以上）
  - `cropWithMargin(px, imgW, imgH, margin = 0.5) -> {x, y, w, h}`（上下左右に w*margin・h*margin を足して絵の中に収める）
  - `PATCH_FILES = { mouth1: 'mouth_1.png', mouth2: 'mouth_2.png', eyeL: 'eye_l_closed.png', eyeR: 'eye_r_closed.png' }`
  - `buildCharacterJson({ name, imageWidth, imageHeight, rects: { mouth, eyeL, eyeR } }) -> object`（spec §1 の形そのまま）
  - `validateCharacterJson(obj) -> string[]`（日本語のエラー。空なら正しい）
  - `isValidName(name) -> boolean`（`/^[A-Za-z0-9_-]{1,32}$/`）
  - `mergeIndex(text | null, name) -> {characters: string[]}`（壊れた・無い → 空から。重複なし・名前順）

- [ ] **Step 1:** `git init`、`npm init -y`、`npm i -D electron`。`package.json` に `"type": "module"`, `"main": "main/main.js"`, `"scripts": {"start": "electron .", "test": "node --test test/"}`。
- [ ] **Step 2: 失敗するテストを書く**
  - `toPixels({x:0.5,y:0.25,w:0.1,h:0.1}, 1000, 2000)` → `{x:500,y:500,w:100,h:200}`、`toNormalized` で戻すと元と同じ。
  - `clampRect({x:-10,y:990,w:50,h:50}, 1000, 1000)` → `{x:0,y:990,w:40,h:10}`。
  - `isUsableRect({w:7,h:20}) === false`、`isUsableRect({w:8,h:8}) === true`。
  - `cropWithMargin({x:100,y:100,w:40,h:20}, 1000, 1000)` → `{x:80,y:90,w:80,h:40}`。端の近くでは絵の中に収まる（`{x:0,...}`）。
  - `buildCharacterJson(...)` の結果が spec §1 の例と同じキーを持ち、`mouth.frames` が `['mouth_1.png','mouth_2.png']`、`eyes[0].closed === 'eye_l_closed.png'`、`version === 1`、`base === 'base.png'`。
  - `validateCharacterJson` は正しいものに `[]`、`version: 2` や `frames` が 1 つのものにエラーを返す。
  - `isValidName`: `'mika'` ok、`'みか'`・`'a b'`・`''`・33 文字は ng。
  - `mergeIndex(null,'mika')` → `{characters:['mika']}`、`mergeIndex('{"characters":["zoe"]}','mika')` → `['mika','zoe']`、`mergeIndex('壊れた','mika')` → `['mika']`、同じ名前は 1 つ。
- [ ] **Step 3:** `npm test` → 失敗（モジュールが無い）。
- [ ] **Step 4:** 2 つのファイルを実装する。
- [ ] **Step 5:** `npm test` → すべて PASS。
- [ ] **Step 6:** commit `feat: 印の計算と character.json の形`

### Task 2: 口パク・まばたき・揺れ（`core/lipsync.js`, `core/mouth-frame.js`, `core/blink.js`）

**Files:** Create: 上の 3 つ。Test: `test/motion.test.js`

**Interfaces:**
- Produces:
  - `LIPSYNC = { maxOpen: 0.8, minOpen: 0.2, intervalMin: 0.06, intervalMax: 0.16, closeChance: 0.2, closeTarget: 0.05, followSpeed: 20 }`
  - `createLipSync(random = Math.random) -> { speaking: boolean, update(dt) -> number }`（Talk の `PseudoLipSync.Update` と同じ手順。`random()` は 0〜1。追従は `1 - Math.exp(-followSpeed * dt)`）
  - `mouthFrame(open) -> 0 | 1 | 2`
  - `createBlink(random = Math.random) -> { update(dt) -> boolean }`（true の間は目を閉じる。次のまばたきまで `2 + random()*4` 秒、閉じるのは 0.12 秒）
  - `bobOffset(t, height) -> number`（`Math.sin(2*Math.PI*t/3) * 0.005 * height`）

- [ ] **Step 1: 失敗するテストを書く**
  - `mouthFrame(0.149) === 0`, `mouthFrame(0.15) === 1`, `mouthFrame(0.499) === 1`, `mouthFrame(0.5) === 2`。
  - 決まった乱数（`() => 0.5`）で `speaking = true` にして 1 秒分（dt=1/60）動かすと、値が 0.2〜0.8 の間に入る。`speaking = false` にして 1 秒分動かすと 0.01 未満。
  - `random` が常に 0.1（< 0.2）なら目標が 0.05 になり、1 秒後の値が 0.04〜0.06。
  - `createBlink(() => 0)`: 1.99 秒までは false、2.0〜2.12 秒は true、その後 false に戻る。
  - `bobOffset(0.75, 1000) ≈ 5`、`bobOffset(0, 1000) === 0`。
- [ ] **Step 2:** `npm test` → 失敗。
- [ ] **Step 3:** 実装する。
- [ ] **Step 4:** `npm test` → PASS。
- [ ] **Step 5:** commit `feat: 口パク・まばたき・揺れの計算`

### Task 3: 画素の加工（`core/pixels.js`, `core/feather.js`, `core/warp.js`）

画像は `{ data: Uint8ClampedArray (RGBA), width, height }`（canvas の `ImageData` と同じ形）で受け渡す。

**Files:** Create: 上の 3 つ。Test: `test/pixels.test.js`, `test/warp.test.js`

**Interfaces:**
- Consumes: Task 1 の `{x,y,w,h}`（画素）。
- Produces:
  - `extractPatch(img, px) -> img`（四角を切り出す）
  - `makeMask(width, height, px) -> img`（四角の中が白・外が黒、不透明）
  - `ownImageMode(own: {width,height}, base: {width,height}, px) -> 'crop' | 'as-is' | 'resize'`（spec §3 の「自分の絵を読み込む」の 3 通り）
  - `sdScale(width, height, max = 1536) -> number`（長い辺が max 以下なら 1、超えたら `max / 長い辺`）
  - `featherAlpha(img, ratio = 0.15) -> img`（ふちからの距離 d、`k = min(1, d / (ratio * min(w,h)))` を alpha に掛ける。新しい配列を返す）
  - `makeMouthPatch(img, px, level: 1 | 2) -> img`（四角の大きさ。spec §3「自動で作る・口」。ずらす量は level 1 で h の 25%、2 で 50%）
  - `makeClosedEyePatch(img, px) -> img`（spec §3「自動で作る・目」）

- [ ] **Step 1: 失敗するテストを書く**
  - `extractPatch` で 10×10 の絵から `{x:2,y:3,w:4,h:5}` を切ると 4×5 で、左上の画素が元の (2,3)。
  - `makeMask(10,10,{x:2,y:2,w:3,h:3})` で (3,3) が白、(0,0) が黒。
  - `ownImageMode`: 元の絵と同じ大きさ → `'crop'`、四角と同じ大きさ → `'as-is'`、どちらでもない → `'resize'`。
  - `sdScale(1000, 1500) === 1`、`sdScale(4000, 2000) === 0.384`。
  - `featherAlpha` の結果: 角の alpha が 0、中央は 255 のまま。
  - `makeMouthPatch`: 大きさが四角と同じ。いちばん上の行は元の絵と同じ。level 2 の中央の画素は元より暗い（RGB の和が小さい）。
  - `makeClosedEyePatch`: 大きさが四角と同じ。中央の画素は、四角のすぐ上の行の色の中央値に近い（各色の差が 10 以内）。ただし 0 行目より上が無い（四角が y=0）ときも落ちない。
- [ ] **Step 2:** `npm test` → 失敗。
- [ ] **Step 3:** 実装する。
- [ ] **Step 4:** `npm test` → PASS。
- [ ] **Step 5:** commit `feat: 差分の切り出し・ぼかし・自動の差分`

### Task 4: AI への送信（`main/gemini.js`, `main/sd-webui.js`）

**Files:** Create: 上の 2 つ、`main/gen-error.js`。Test: `test/gemini.test.js`, `test/sd-webui.test.js`

**Interfaces:**
- Produces:
  - `class GenError extends Error { code: 'network' | 'limit' | 'http' | 'no-image' | 'no-key' | 'no-webui' }`
  - `KINDS = ['mouth_1', 'mouth_2', 'eye_l_closed', 'eye_r_closed']`
  - `generateWithGemini({ fetch, apiKey, model, pngBase64, kind }) -> Promise<string>`（返す絵の base64）
    - POST `https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent`、ヘッダー `x-goog-api-key`。本文は `contents[0].parts` に `inlineData {mimeType:'image/png', data}` と指示文、`generationConfig.responseModalities: ['TEXT','IMAGE']`。
    - 指示文（英語）: mouth_1 "Edit only the mouth so it is slightly open, as if speaking. Keep the art style, colors, framing and image size exactly the same."、mouth_2 は "wide open"、eye は "Edit only the eyes so they are gently closed. ..." 同じ後半。
    - 返事の `candidates[0].content.parts` で最初の `inlineData.data` を返す。無ければ `no-image`。429 → `limit`、その他の失敗 → `http`、fetch が投げたら `network`、apiKey が空なら送らずに `no-key`。
  - `DEFAULT_GEMINI_IMAGE_MODEL`: 実装のとき、context7 か Gemini の公式ドキュメントで、画像の入力と出力に対応した今のモデル名を確かめて決める。
  - `generateWithWebui({ fetch, url, imageBase64, maskBase64, kind, promptPrefix, denoise = 0.6 }) -> Promise<string>`
    - POST `${url}/sdapi/v1/img2img`。本文 `{ init_images:[imageBase64], mask: maskBase64, prompt, negative_prompt: 'lowres, bad anatomy, blurry', denoising_strength: denoise, inpainting_fill: 1, inpaint_full_res: true, inpaint_full_res_padding: 32, steps: 28 }`。
    - プロンプト: `${promptPrefix}, ` に続けて mouth_1 `slightly open mouth`、mouth_2 `open mouth`、目 `closed eyes`。
    - 返事の `images[0]` を返す（`data:` で始まれば外す）。fetch が投げたら `no-webui`。
  - `checkWebui({ fetch, url }) -> Promise<boolean>`（GET `${url}/sdapi/v1/sd-models` が ok なら true）

- [ ] **Step 1: 失敗するテストを書く**（偽の fetch で、受け取った URL・ヘッダー・本文を記録する）
  - Gemini: URL にモデル名が入り、ヘッダーにキーがあり、本文に絵と mouth_2 の指示文（"wide open"）がある。返事 `{candidates:[{content:{parts:[{text:'x'},{inlineData:{data:'QUJD'}}]}}]}` から `'QUJD'`。
  - Gemini: 429 → `code === 'limit'`、絵の無い返事 → `'no-image'`、apiKey `''` → `'no-key'` で fetch は呼ばれない。
  - WebUI: 本文の `mask` が渡したもの、`denoising_strength === 0.6`、`prompt === 'masterpiece, best quality, closed eyes'`（eye_l_closed）。返事 `{images:['data:image/png;base64,QUJD']}` → `'QUJD'`。fetch が投げる → `'no-webui'`。
  - `checkWebui`: ok → true、投げる → false。
- [ ] **Step 2:** `npm test` → 失敗。
- [ ] **Step 3:** 実装する（モデル名を確かめる作業もここで行う）。
- [ ] **Step 4:** `npm test` → PASS。
- [ ] **Step 5:** commit `feat: Gemini と WebUI への送信`

### Task 5: 設定・作りかけの保存・書き出し（`main/settings.js`, `main/workspace.js`, `main/exporter.js`）

**Files:** Create: 上の 3 つ。Test: `test/settings.test.js`, `test/workspace.test.js`, `test/exporter.test.js`（`fs.mkdtemp` の一時フォルダで）

**Interfaces:**
- Consumes: Task 1 の `mergeIndex`, `isValidName`, `validateCharacterJson`。
- Produces:
  - `createSettings({ file, safeStorage }) -> { get(), set(partial), setApiKey(key), getApiKey() -> string | null }`
    - `get()` は `{ provider: 'gemini' | 'waifu', geminiModel, webuiUrl, promptPrefix, exportDir, hasApiKey }`。初期値は Global Constraints の値、`provider: 'gemini'`、`geminiModel: DEFAULT_GEMINI_IMAGE_MODEL`。キーは `safeStorage.encryptString` した base64 で保存し、`get()` には入れない。
  - `saveWorkspace(dir, { meta, files }) -> Promise<void>` / `loadWorkspace(dir) -> Promise<{ meta, files } | null>`（`meta` は JSON にして `workspace.json`、`files` は `{ 'base.png': Buffer, ... }`。無い・壊れていれば null）
  - `exportCharacter({ exportDir, name, json, files, overwrite = false }) -> Promise<{ status: 'ok' | 'exists' | 'invalid', errors?: string[] }>`
    - 名前か json が正しくなければ `invalid`。フォルダが既にあり `overwrite` でなければ `exists`（何も書かない）。
    - 一時フォルダ `<name>.tmp` に書いてから入れ替える。最後に `index.json` を `mergeIndex` で書き直す。

- [ ] **Step 1: 失敗するテストを書く**
  - settings: 初期値が Global Constraints どおり。`set({provider:'waifu'})` の後に作り直しても残る。偽の safeStorage（文字列を反転）で `setApiKey('k')` → `getApiKey() === 'k'`、ファイルの中に `'k'` がそのまま無い、`get().hasApiKey === true`。
  - workspace: 保存して読むと meta と Buffer が同じ。フォルダが無ければ null、`workspace.json` が壊れていれば null。
  - exporter: 書き出すと `mika/character.json` と 5 枚の絵と `index.json`（`{"characters":["mika"]}`）ができる。2 回目は `exists` で中身は変わらない。`overwrite: true` なら書き換わり、`.tmp` は残らない。名前 `'みか'` → `invalid`。壊れた `index.json` があっても書き出せる。
- [ ] **Step 2:** `npm test` → 失敗。
- [ ] **Step 3:** 実装する。
- [ ] **Step 4:** `npm test` → PASS。
- [ ] **Step 5:** commit `feat: 設定・作りかけの保存・書き出し`

### Task 6: ウィンドウと「絵を開く」「印を付ける」の画面

**Files:**
- Create: `main/main.js`, `main/preload.js`, `renderer/index.html`, `renderer/style.css`, `renderer/app.js`, `renderer/steps/open.js`, `renderer/steps/mark.js`

**Interfaces:**
- Consumes: Task 1, Task 5。
- Produces（preload が `window.maker` に公開。後の Task で足す）:
  - `openImage() -> Promise<{ name, pngBase64 } | null>`（ファイル選択。JPG も受けて画面側で PNG にする）
  - `loadWorkspace() / saveWorkspace(state)`、`getSettings() / setSettings(p) / setApiKey(k)`
  - 画面の状態 `state = { image: ImageData, rects: { mouth, eyeL, eyeR } (割合), patches: { mouth_1, mouth_2, eye_l_closed, eye_r_closed } (ImageData | null) }` を `app.js` が持ち、各 step は `render(container, state, onChange)` の形。
- `contextIsolation: true`, `nodeIntegration: false`。
- 作りかけは、変更があってから 1 秒後に自動で保存する。起動時に作りかけがあれば読み込む。

- [ ] **Step 1:** 4 段階の見出しとボタンだけの画面を作り、`npm start` で開くことを確かめる。
- [ ] **Step 2:** 「絵を開く」: ドラッグ＆ドロップとファイル選択。開いたら「印を付ける」へ進む。
- [ ] **Step 3:** 「印を付ける」: 「口」「左目」「右目」のボタンで選び、canvas の上をドラッグして四角を描く。中をドラッグで移動、右下の角で大きさを変える。`clampRect` で絵の中に収め、`isUsableRect` でなければ「もう少し大きく囲んでください」。3 つそろうまで「次へ」を押せない。
- [ ] **Step 4:** 手で確かめる: 絵を開いて 3 つ囲み、アプリを閉じて開き直しても印が残っている。`npm test` も PASS のまま。
- [ ] **Step 5:** commit `feat: 絵を開く・印を付ける画面`

### Task 7: 「差分を作る」画面

**Files:**
- Create: `renderer/steps/variants.js`
- Modify: `main/main.js`, `main/preload.js`（`generate({ provider, kind, pngBase64, maskBase64 }) -> Promise<{ ok: true, pngBase64 } | { ok: false, code, message }>`、`checkWebui()` を足す）

**Interfaces:**
- Consumes: Task 3（`extractPatch`, `makeMask`, `ownImageMode`, `sdScale`, `featherAlpha`, `makeMouthPatch`, `makeClosedEyePatch`）、Task 4。

- [ ] **Step 1:** 差分 4 つの行を作る。各行に今の差分の小さな表示と「AI で作る」「自動で作る」「自分の絵を読み込む」。
- [ ] **Step 2:** 「自動で作る」: Task 3 の関数で作り、`featherAlpha` を掛けて表示する。
- [ ] **Step 3:** 「AI で作る」:
  - Gemini: `cropWithMargin` で切り抜いて送る → 戻った絵を canvas で切り抜いた大きさに縮める → 印の四角の位置を `extractPatch` → `featherAlpha`。
  - Waifu: 絵とマスクを `sdScale` で縮めて送る → 戻った絵を元の大きさに戻す → `extractPatch` → `featherAlpha`。
  - 失敗のときは `code` に合わせて日本語で出し、「自動で作る」をすすめる。キーが無い・WebUI につながらないときはボタンを押せなくし、設定画面へのリンクを出す（WebUI のときは「WebUI を `--api` 付きで起動してください」）。作っている間はボタンを押せなくし「作っています…」。
- [ ] **Step 4:** 「自分の絵を読み込む」: `ownImageMode` で切り抜く・そのまま・縮める。縮めたときは「大きさが違ったので合わせました」。
- [ ] **Step 5:** 手で確かめる: 自動と自分の絵で 4 つそろう。キーが無いと Gemini のボタンが押せない。
- [ ] **Step 6:** commit `feat: 差分を作る画面`

### Task 8: 「確かめる・書き出す」画面と設定画面

**Files:**
- Create: `renderer/steps/preview.js`, `renderer/settings.js`
- Modify: `main/main.js`, `main/preload.js`（`exportCharacter({ name, json, files, overwrite })`、書き出し先フォルダの選択）

**Interfaces:**
- Consumes: Task 2（`createLipSync`, `mouthFrame`, `createBlink`, `bobOffset`）、Task 1（`buildCharacterJson`, `isValidName`）、Task 5（`exportCharacter`）。

- [ ] **Step 1:** 確かめる: canvas に元の絵を描き、`requestAnimationFrame` ごとに `mouthFrame(lipSync.update(dt))` の口の差分と、`blink.update(dt)` が true なら両目の差分を重ね、全体を `bobOffset` だけ上下させる。
- [ ] **Step 2:** 「話す」ボタン（押すたびに話す・止める）と、文を入れて「読み上げる」（`speechSynthesis`、英語の声。`onstart` で話す、`onend`・`onerror` で止める）。
- [ ] **Step 3:** 書き出す: 名前の欄（`isValidName` でなければ「英数字と _ - だけ、32 文字まで」）。`exists` が返ったら上書きしてよいか聞き、はいなら `overwrite: true` で送り直す。成功したら書き出したフォルダの場所を出す。差分が 4 つそろうまで押せない。
- [ ] **Step 4:** 設定画面: AI の選択、Gemini のキー（入れた後は「設定済み」とだけ表示）とモデル名、WebUI の URL・プロンプトの頭・「つながるか確かめる」ボタン、書き出し先（フォルダ選択）。
- [ ] **Step 5:** 手で確かめる: 口パク・まばたき・揺れが見える。書き出すと `Characters/<名前>/` と `index.json` ができる。`npm test` PASS。
- [ ] **Step 6:** commit `feat: 確かめる・書き出す画面と設定画面`

---

## Part B: Talk（Unity）の確認用画面

作業場所は `C:\Users\haru0\club\class\es\My project`。commit するときは、このタスクで作ったファイルだけを `git add` する（ほかの会話の変更を混ぜない）。

### Task 9: Unity を使わない判断（`Character2DLogic.cs`）

**Files:**
- Create: `Assets/Script/Talk2D/Character2DLogic.cs`（`using UnityEngine` を書かない）
- Create: `Tests~/LogicTests/Character2DTests.cs`
- Modify: `Tests~/LogicTests/Program.cs`（`Main` に `Character2DTests();` を足す）

**Interfaces:**
- Produces:
  - `[Serializable] class CharacterJson { int version; string name; int imageWidth; int imageHeight; string @base; MouthJson mouth; EyeJson[] eyes; }`、`[Serializable] class RectJson { float x, y, w, h; }`、`MouthJson { RectJson rect; string[] frames; }`、`EyeJson { RectJson rect; string closed; }`、`CharacterIndexJson { string[] characters; }`（フィールドは public。JsonUtility で読む）
  - `static class Character2DLogic`:
    - `int MouthFrame(float open)`（spec §2 のしきい値）
    - `RectJson ToBottomLeft(RectJson r)`（`y' = 1 - r.y - r.h`）
    - `string Validate(CharacterJson c)`（正しければ null。version が 1 でない、mouth・frames 2 つ・eyes 2 つ・各ファイル名が無い → 日本語の理由）
    - `float BobOffset(float t, float height)`
  - `class BlinkTimer { BlinkTimer(Func<float> random); bool Update(float dt); }`（Task 2 の `createBlink` と同じ数値）

- [ ] **Step 1: 失敗するテストを書く**（Task 2 と同じ値で）
  - `MouthFrame(0.149f)==0`, `(0.15f)==1`, `(0.5f)==2`。
  - `ToBottomLeft({x:0.4,y:0.3,w:0.1,h:0.05})` の y が 0.65。
  - `Validate`: 正しいものに null、version 2・frames 1 つ・eyes 1 つ・null にそれぞれ null でない文字列。
  - `BlinkTimer(() => 0f)`: 1.99 秒まで false、2.05 秒で true、2.2 秒で false。
  - `BobOffset(0.75f, 1000f)` が 5 に近い。
- [ ] **Step 2:** LogicTests を、`Program.cs` の先頭コメントにある今までと同じやり方（Roslyn で `Program.cs` とテスト対象の `.cs` をまとめてコンパイルして実行）で動かし、対象に `Assets/Script/Talk2D/Character2DLogic.cs` を足す → コンパイルエラーか FAIL。
- [ ] **Step 3:** 実装する。
- [ ] **Step 4:** 同じコマンド → `ALL LOGIC TESTS PASSED`。
- [ ] **Step 5:** commit `Talk2D: 2D キャラの判断 (口の切り替え・原点の変換・まばたき)`

### Task 10: 読み込みと表示、確認用シーン

**Files:**
- Create: `Assets/Script/Talk2D/CharacterPack.cs`, `Sprite2DMouthTarget.cs`, `Character2DView.cs`, `Character2DTestUI.cs`
- Create: `Assets/Editor/Character2DTestSceneBuilder.cs`（メニュー `Tools/Talk2D/確認用シーンを作る` で `Assets/Scenes/Character2DTest.unity` を作る）

**Interfaces:**
- Consumes: Task 9、既存の `MouthTargetBase`, `PseudoLipSync`（変えない）。
- Produces:
  - `class LoadedCharacter { CharacterJson json; Texture2D baseTex; Texture2D[] mouth /*2*/; Texture2D[] eyesClosed /*2*/; }`
  - `static class CharacterPack`: `IEnumerator LoadIndex(Action<string[]> done, Action<string> fail)`、`IEnumerator Load(string name, Action<LoadedCharacter> done, Action<string> fail)`（`Application.streamingAssetsPath + "/Characters/..."` を `UnityWebRequest` で読む。`Validate` が null でなければ fail）
  - `Character2DView : MonoBehaviour`: `void Show(LoadedCharacter c)`、`void SetMouthFrame(int frame)`。自分の RectTransform の下に RawImage を作り、差分は `ToBottomLeft` の割合を anchorMin/anchorMax にして重ねる。縦横比は元の絵に合わせる。`Update` で `BlinkTimer` と `BobOffset`。
  - `Sprite2DMouthTarget : MouthTargetBase`: `SetMouthOpen(v)` → `view.SetMouthFrame(Character2DLogic.MouthFrame(v))`。
  - `Character2DTestUI : MonoBehaviour`: `LipSyncTestUI` と同じ OnGUI の作り。キャラ名のボタンを並べ、押すと読み込む。「話し始める／話すのをやめる」で `PseudoLipSync.IsSpeaking` を切り替える。失敗の理由を画面に出す。

- [ ] **Step 1:** 4 つのスクリプトとシーンを作るエディタスクリプトを書き、Unity で開いてコンパイルエラーが 0 件であることを確かめる。LogicTests も PASS のまま。
- [ ] **Step 2:** 手で確かめる（ユーザーと）: Part A で書き出したキャラがある状態でメニューからシーンを作って再生し、キャラを選ぶと表示され、「話し始める」で口パク、ときどきまばたき、ゆっくり揺れる。`mouth_2.png` を消すと、落ちずに理由が出る。
- [ ] **Step 3:** commit `Talk2D: 2D キャラの確認用画面`

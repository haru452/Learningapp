# プロダクト要件定義書 (PRD): Unity向け 英語音声添削・校正スマホアプリ

## 1. 概要 (Overview)
本アプリは、ユーザーがスマホのマイクに向かって発話した英語音声を録音・文字起こしし、**Gemini API** または **DeepL Write API** を利用して自然な英語表現や文法に修正して画面表示する音声学習・校正アプリです。

---

## 2. 目的・ゴール (Objectives)
* ユーザーが話した英語（音声）を直感的に文字起こし・校正できるUIを提供する。
* AIによる修正結果（Gemini）と専用アルゴリズムによる自然な言い換え（DeepL Write）を選択可能にする。
* Unity開発において、C#スクリプト生成AIに明確な指示を出せる技術仕様書として機能させる。

---

## 3. 主要機能 (Key Features)

| 機能名 | 詳細説明 |
|---|---|
| **マイク録音 (Mic Control)** | ボタンのトグル（ON/OFF）操作で音声の録音・停止を行う。録音データはWAV形式等で一時保存。 |
| **添削エンジン切替 (Engine Switch)** | 添削に使用するAPI（`Gemini API` / `DeepL Write API`）をスイッチ（Toggle / Dropdown）で切り替える。 |
| **音声認識・文字起こし (STT)** | 録音された音声をテキスト化する。<br>※ Gemini選択時はGemini APIで直接音声認識。DeepL選択時はWhisper APIまたはGeminiで文字起こし後にDeepLへ送信。 |
| **英文校正・添削 (Correction)** | 選択されたAPIにリクエストを送信し、文法修正・自然な表現への言い換えを取得する。 |
| **結果表示UI (Display UI)** | 原文（文字起こし結果）と修正後テキストをそれぞれ専用の表示領域（Scroll View / Text Mesh Pro）にリアルタイム描画する。 |

---

## 4. 画面構成・UIレイアウト要件 (UI Requirements)

UIレイアウトは画面上部から順に以下のように配置します。

```
+----------------------------------------+
|              [タイトル]                |
|       Voice English Corrector          |
+----------------------------------------+
|  添削エンジン選択:                     |
|  [ Gemini (音声ダイレクト) | DeepL Write ]|  <- Toggle/Switch
+----------------------------------------+
|                                        |
|  [ 🎤 録音開始 / 停止 ]                |  <- Recording Button
|  Status: 待機中 / 録音中...             |
+----------------------------------------+
| 【文字起こし (Transcript)】             |
|  "I has a meeting yesterday."          |  <- Original Text Box
|                                        |
+----------------------------------------+
| 【添削・自然な表現 (Corrected)】       |
|  "I had a meeting with my client       |  <- Corrected Text Box
|   yesterday."                          |
|                                        |
+----------------------------------------+
|  [ 🔄 処理中インジケータ (Loading) ]    |
+----------------------------------------+
```

---

## 5. システム・技術仕様 (Technical Specifications)

* **開発環境:** Unity (2022.3 LTS 以上推奨 / Universal Render Pipeline もしくは GUI)
* **対象OS:** iOS / Android
* **言語:** C#
* **通信処理:** `UnityEngine.Networking.UnityWebRequest` を使用した非同期通信 (Async/Await)
* **データ形式:** JSON (`JsonUtility` または `Newtonsoft.Json`)

### APIリクエスト仕様

#### A. Gemini API (音声直接送信パターン)
* **Endpoint:** `https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent`
* **入力:** 音声バイナリデータ (Base64エンコード) + プロンプト
* **出力:** JSONから「文字起こし」および「修正文」のテキストを抽出

#### B. DeepL Write API パターン
1. **ステップ1 (STT):** 音声データを文字起こし（Gemini等を利用）
2. **ステップ2 (Write):** 取得したテキストを DeepL Write API へ送信
   * **Endpoint:** `https://api-free.deepl.com/v2/write/rephrase` (Freeキーの場合)
   * **Headers:** `Authorization: DeepL-Auth-Key {YOUR_KEY}`
   * **Body:** `{"text": ["原文テキスト"], "target_lang": "EN-US"}`

---

## 6. C#スクリプト構成案 (AIコード生成指示用)

AIにプログラムを書かせる際、以下のスクリプト単位で分割して生成させます。

1. **`AudioRecorder.cs`**
   * Unityの `Microphone` クラスを使用し、録音開始・停止・WAVデータ変換を行う。
2. **`GeminiApiClient.cs`**
   * 音声データをGemini APIに送信し、文字起こし＋修正文をパースする。
3. **`DeepLApiClient.cs`**
   * テキストを DeepL Write API に送信し、修正結果を受け取る。
4. **`AppManager.cs`**
   * UIイベント（ボタン押下、スイッチ切替）を受け取り、全体の処理フローを制御するメインスクリプト。

---

## 7. 今後の拡張性・検討事項 (Future Considerations)

* **パーミッション取得:** iOS/Android 実機でのマイク利用権限（Microphone Permission）のダイアログ表示処理。
* **解説の追加:** Gemini使用時に「なぜその修正になったか」の解説テキストもUI下部に表示する機能拡張。
* **APIキーの保護:** 本番運用時のAPIキーの暗号化またはバックエンドサーバー経由での通信。
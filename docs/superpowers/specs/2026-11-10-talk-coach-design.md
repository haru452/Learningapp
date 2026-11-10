# Talk: Lufka を英会話の練習相手にする (言い直し・今日の単語・まとめ) 設計

日付: 2026-11-10 / ブランチ: `app-improvements`

## 目的

> **2026-11-10 変更:** 声での言い直し (「By the way, you can say: …」) とつなぎの一言はやめた。言い換えられると会話の内容が分かりにくくなるため。添削は**画面の上に出すだけ**にし、返事を言い終えたらすぐ聞く番に戻す。下の「1. 1 回のやりとり」のうち、声で言う・つなぎで待つ部分は使わない。


Talk で Lufka と話すことを、英会話の練習として役に立つものにする。**待ち時間は今より増やさない。**

- 自分の英語の間違いを、Lufka が声で言い直してくれる
- 単語帳で今日やった単語を、会話の中で使う機会がある
- 終わったら、何ができて何を直されたかがわかり、連続記録に数える

決めたこと (2026-11-10 の会話):
- 返事は今のまま**スマホの中の Gemma 3 1B** が作る (ネットが無くても話せる)。添削とまとめは **Gemini** が裏で作る
- 添削は、Lufka が**声で言い直す**
- 添削を待つ間も Lufka が話し続ける (あなたの番に戻さない)
- 最初の版に入れる: 今日の単語とレベル、まとめと連続記録。入れない: ミスの集計への保存、ヒントのボタン

## 今の作り (変えない所)

- `ConversationController`: 聞く → `LLMAgent.Chat` で返事をストリーミング → `SentenceSplitter` で 1 文ずつ → `AndroidTts` で読み上げ → 聞く
- 返事のモデル: `gemma-3-1b-it-Q4_K_M.gguf` (CPU、コンテキスト 2,234)
- 画面は OnGUI (`ConversationController.Support.cs` のボタン、`MessageWindow`)

## 1. 1 回のやりとり

```
あなたの発話が確定 (CommitUserText → HandleUserSpeech)
  ├─ (今まで通り) 1B が返事 → できた文から読み上げ
  └─ (新) TalkCoach.CorrectAsync(発話) を同時に開始 ── Gemini Flash-Lite
返事を言い終えた (await speaker のあと)
  ├─ 添削ができていて、直しあり → 「By the way, you can say: <直した文>」を読み上げ、画面に ✎
  ├─ 添削ができていて、直しなし → すぐ聞く番へ
  └─ まだ → つなぎの一言を読み上げて待つ (下記)
聞く番へ (BeginListening)
```

### 添削 (`TalkCoach`)

- Gemini `gemini-flash-lite-latest` (予備 `gemini-3.8-flash`) に、発話 1 つと直前の Lufka の一言 (文脈) を送る
- 返事は JSON (`responseSchema` で形を決める): `{ "ok": bool, "corrected": string, "note_ja": string }`
  - `ok = true`: 文法・語の選び方に間違いが無い (直さない)
  - 音声認識の結果なので、句読点・大文字・言いよどみ (uh, um) は直さない。意味を変えない。直すのは 1 文にまとめた最小限
  - `note_ja`: 何を直したかの日本語の短い説明 (30 字以内。例「過去のことは went」)
- キー: 日記のキー (`geminiApiKey`) を使い、上限 (429) ならほかの画面のキーを借りる (今の `backupApiKeys` と同じ)
- 失敗 (通信なし・上限・形が違う) は「添削なし」として扱い、会話は続ける。Console に警告だけ
- 言い直しの文 (`TalkCoach.RecastLine`): `"By the way, you can say: " + corrected`。corrected が発話と (大文字・句読点を除いて) 同じなら言わない

### 間に合わないとき: つなぎの一言

- 返事を言い終えた時点で添削がまだなら、つなぎの一言を読み上げて待つ
  - 例: "Hmm, let me think.", "Oh, you know what?", "Wait, one thing."
  - 決まった言葉の一覧から、直前と同じにならないように選ぶ (1B に作らせない。すぐ話せる)
- つなぎは最大 2 回、待つのは合わせて最大 5 秒
- その間に添削ができたら言い直してから、できなければ聞く番に戻す。あとから添削ができたら、画面とまとめにだけ出す (声では言わない)
- 間違いが無いとわかった時点で、すぐ聞く番に戻す

### 画面

- `MessageWindow` の下に、直した文を ✎ 付きで出す: 言った文 (取り消し線) → 直した文、`note_ja`
- 次の発話まで出しておく

## 2. 今日の単語とレベル (`TalkPlan`)

会話を始める (`StartConversation`) ときに決める。

- **単語** (最大 5 語):
  1. 単語帳の評価の記録 (`VocabStore.LoadReviews`) から、今日 (日の区切りは単語帳と同じ) 評価した単語。新しい順
  2. 5 語に足りなければ、学習中の単語 (デッキで期限が近い順)
  3. 単語帳を使っていなければ 0 語 (単語の指示は付けない)
- **レベル**: `VocabSettings.Level` (A1〜B2)。未設定なら B1 として扱う
- **Lufka への指示**: 元のシステムプロンプトの後ろに足す (会話を始めるたびに作り直す。前の回の分は消す)
  - レベル: 例 "The user is an A2 English learner. Use short, simple words and sentences."
  - 単語: "Today the user is learning these words: a, b, c. Naturally use one of them in some replies, and ask questions that make the user want to use them."
- **使えたかの判定** (`TalkPlan.UsedWords`): あなたの発話に単語が出てきたか。大文字小文字を無視し、よくある形の変化 (‑s, ‑es, ‑ed, ‑ing, ‑ied, 不規則動詞の一覧の過去形・過去分詞) も同じ語として数える
- **画面**: 上に 5 語を小さく並べ、使えた語に ✓

1B は指示を守らないこともある (単語を使わない回もある)。それでよい。

## 3. まとめと連続記録 (`TalkSummary`)

- 「会話を止める」ボタンを「End」にする。押すと会話を止めて、まとめを出す
- まとめ (OnGUI のパネル):
  - 話した回数・話した時間 (最初の発話から End まで)
  - 今日の単語: 使えた語 ✓ / 使えなかった語
  - 言い直された文の一覧: 言った文 → よりよい言い方 (`note_ja`)。裏で作っていて間に合わなかった分も、End の時点でできていれば入れる
  - 「Talk again」で次の会話へ (まとめを閉じる)
- **連続記録**: 1 回の会話であなたが 3 回以上話したら数える (End のとき)
  - 端末: `DiaryManager.CountOtherPracticeAsStudyDay()` (端末の連続記録とリマインダー)。ほかの練習と同じ
  - クラウド (ログイン中): 新しい関数 `complete_talk_session` (種類 `talk`)。単語帳の `complete_vocab_session` と同じ形
  - その日に数えた後の 2 回目以降は数えない (サーバーが同じ日を 1 回にする)

### クラウドの変更 (database_learnApp)

- `activity_days.kind` に `talk` を足す (`schema.sql` などの `check (kind in (...))`)
- `supabase/talk.sql`: `complete_talk_session(p_timezone, p_taken_at)`
- Web のカレンダー: 種類の一覧 (`KIND_INFO`) に Talk を足す (色は未使用の色)
- **カラオケの会話が同じ `check (kind in ...)` を今変えているので、この SQL はカラオケの作業が終わってから入れる**

## 4. ファイル

| ファイル | 役割 |
|---|---|
| `Talk3D/TalkCoach.cs` (新) | 添削の依頼・返事の読み取り・言い直しの文・つなぎの一言の選び方 |
| `Talk3D/TalkPlan.cs` (新) | 今日の単語とレベルの決め方・指示文・使えたかの判定 |
| `Talk3D/TalkSummary.cs` (新) | 1 回の会話の記録 (発話・添削・単語) とまとめの中身・連続記録に数えるか |
| `Talk3D/TalkCoachView.cs` (新) | 単語の並び・✎ の添削・まとめのパネル (OnGUI) |
| `Talk3D/ConversationController.cs` | つなぐ所だけ (添削の開始・返事のあとの言い直しとつなぎ・始める時の指示・End) |
| `CorrectionDiary/GeminiApiClient.Talk.cs` (新) | 添削の Gemini 呼び出しとスキーマ |
| `CorrectionDiary/SupabaseService.cs` | `CompleteTalkSessionAsync` |

`ConversationController.cs` は今 471 行。足す分で 600 行を超えそうなら、言い直しとつなぎの部分を `ConversationController.Coach.cs` に分ける。

## 5. エラー・端の場合

- Gemini が使えない: 添削なしで今まで通り話す。まとめの「言い直された文」は空
- 発話が短すぎる (2 語未満) ・英語でない: 添削を頼まない
- 会話の途中で End / 画面を離れた: 進行中の添削の結果は捨てる (セッション番号で古い処理を止める。今の仕組みと同じ)
- 単語帳のデータが無い・壊れている: 単語なしで始める

## 6. テスト (`Tests~/LogicTests/TalkTests.cs`)

- 添削の返事の読み取り (ok / 直しあり / 形が違う → 添削なし)
- 言い直しの文: 直しあり → 文を作る。句読点・大文字だけの違い → 作らない
- つなぎの一言: 続けて同じものを選ばない
- 単語の選び方: 今日の単語が先、足りなければ学習中、無ければ空。最大 5 語
- 指示文: レベルと単語が入る。単語 0 語なら単語の指示を付けない
- 使えたかの判定: went → go、studies → study、running → run、大文字小文字
- 連続記録: 3 回以上で数える。2 回では数えない
- Gemini のスキーマが正しい JSON
- SQL: `complete_talk_session` のテスト (`supabase/tests/talk_test.sql`)。Web: カレンダーに Talk の色

## 入れないもの

- 添削を「よくあるミス」に保存すること
- ヒントのボタン
- 返事を作るモデルを Gemini にすること、声を Gemini の音声合成にすること

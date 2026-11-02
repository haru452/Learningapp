# 単語帳: 明日やること

ブランチはどちらのリポジトリも `develop-words-book` (main には入っていない。push もしていない)。

## 1. 準備 (最初に 1 回)
- [ ] dictionaryapi.com で **Learner's Dictionary** を選んで登録し、Key (Dictionary) をもらう
- [ ] `Assets/Resources/ApiSecrets.json` に `"merriamWebsterApiKey": "<キー>"` を足す (任意: `"geminiVocabApiKey"`。空なら日記のキー)
- [ ] Supabase の SQL Editor で `database_learnApp/supabase/vocab.sql` を実行する

## 2. Unity で画面を作る
- [ ] `SampleScene` を開き、**Tools → Vocabulary → Create Vocabulary Screen** を実行する
  - 「すでに VocabManager があります」と出たら、もう作ってある (`SampleScene.unity` が変更済みになっている。保存したかを確かめるだけ)
- [ ] Repeat の右に「Vocab」タブがあるのを確かめて **Ctrl+S**
- [ ] `SampleScene.unity` を `develop-words-book` に commit する (フォントの `LiberationSans SDF - Fallback.asset` は Unity が勝手に書き換えたもの。入れなくてよい)

## 3. Editor で確かめる (再生)
- [ ] Vocab タブ → 初回はレベル (A1〜B2) を選ぶ画面が出る
- [ ] Start → カードが出る。タップでめくれる。裏に意味・品詞ごとのレベル・例文
- [ ] 4 つの評価ボタンの下に「in 4 days」などの目安。押すと次のカードへ
- [ ] Sound で音が出る (Editor では辞書の音声だけ。読み上げは出ない)
- [ ] 最後まで行くとまとめが出る
- [ ] Words: Learning / Known の切り替え、レベルで絞り込み、行をタップ → Back to learning / Delete
- [ ] Add: 単語を検索 → Add to my words。ありえないつづりで検索 → 候補が出る。手入力で追加
- [ ] Settings: レベル・語数・目標の想起率が選べる。出典が出ている
- [ ] 見た目 (ボタンの大きさ・文字のはみ出し・日本語が表示されるか)

## 4. スマホで確かめる (APK を作り直す)
- [ ] Say it → 話す → Clear / Close / Try again と聞き取れた文字が出る
- [ ] まとめで Gemini の判定と日本語のアドバイスが出る。My voice と Model が鳴る
  - 出ないときは、その端末は録音と認識を同時にできない (認識だけになる。設計どおり)
- [ ] 1 回終えたら連続記録が増える。Web のカレンダーで今日が紫 (ほかの練習もした日は銅・銀・金)
- [ ] 機内モードで Start → 「Can't reach the dictionary, so today is review only.」

## 5. 問題が無ければ
- [ ] それぞれのリポジトリで `develop-words-book` を main に merge して push する
  (Unity: `My project`、Web: `database_learnApp`)

困ったときは、出た表示 (ダイアログ・Console のエラー) をそのまま Claude に送る。

# 単語帳の「動画の場面」を確かめる手順

実装は終わっている。下の順に準備して、スマホで確かめる。
設計書: `docs/superpowers/specs/2026-11-09-vocab-video-clips-design.md`

## 準備

1. Supabase の SQL Editor で、database_learnApp の `supabase/vocab_clips.sql` を実行する。
2. `ApiSecrets.json` に `youtubeVocabApiKey` を足す。空なら `youtubeApiKey` が使われる。
3. Unity で `SampleScene` を開き、**Tools → Vocabulary → Update Vocabulary Screen** を押して Ctrl+S で保存する。
4. (任意) パソコンで場面の一覧を作る。
   - `cd Tools~/clips`
   - `pip install -r requirements.txt`
   - `.env.example` をコピーして `.env` を作り、値を入れる
   - `python build_clips.py --dry-run` で確認する
   - `python build_clips.py` で本番を流す

## スマホで確かめる (APK を作り直す)

- [ ] 一覧にある単語で Videos を押すと、すぐ出る
- [ ] 一覧に無い単語で、その場で探して出る。もう一度押すとすぐ出る
- [ ] Replay / Next / Close が動く。Close でカードがめくった状態のまま
- [ ] 機内モードで `Can't reach the internet.` が出る
- [ ] 評価を続けても止まらない。裏で準備が進む (Console / logcat の `[VocabClips]`)
- [ ] ほかのタブへ移って戻ると、同じ場面が出て、Replay で流れる
- [ ] 1 日 30 回探すと `Video search limit reached. Try again tomorrow.` が出る

## 気をつけること

- 字幕の取り方は非公式で、止まることがある。止まっても一覧の場面は見られる。
- 動画の画面の高さ (200 / 44 / 76) は、実際の画面を見て調整するかもしれない。
- 同じ YouTube キーを複数のプロジェクトで分けて、上限を越える使い方はしない。

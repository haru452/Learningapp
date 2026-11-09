# 単語帳の動画の場面を作るスクリプト

YouTube のチャンネル (`channels.json`) の動画から英語の字幕を取り、CEFR-J の A1〜B2 の単語が出る場面を探して、
Supabase の `vocab_clips` に入れる。1 語 10 場面まで、同じ動画から 2 場面まで。

## 準備
1. `pip install -r requirements.txt`
2. `.env.example` を `.env` にコピーし、`YOUTUBE_API_KEY`・`SUPABASE_URL`・`SUPABASE_SERVICE_KEY` (service role) を書く。

## 使い方
- 試し: `python build_clips.py --dry-run --max-videos 3` → `out/clips.json` に場面が出る (Supabase には書かない)
- 本番: `python build_clips.py` (`--max-videos` の既定は 200)
- 処理した動画は `.done.json` に記録され、次回は飛ばす。`.env`・`.done.json`・`out/` は git に入れない。
- 字幕の文は著作物なので git に入れない。

## テスト
`python -m unittest test_build_clips`

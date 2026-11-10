"""clip_finder.py のテスト (C# 版と同じ fixtures/finder_cases.json を使う)。
  python -m unittest test_build_clips   (このフォルダで)
"""
import json
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import clip_finder as f  # noqa: E402


def clip(video_id, start_ms):
    return {"word": "w", "video_id": video_id, "start_ms": start_ms, "end_ms": start_ms + 1000,
            "sentence": "s", "channel": "c", "source": "search"}


class FinderCasesTest(unittest.TestCase):
    def test_all_cases(self):
        with open(os.path.join(HERE, "fixtures", "finder_cases.json"), encoding="utf-8") as fp:
            cases = json.load(fp)
        self.assertTrue(cases)
        for case in cases:
            with self.subTest(case["name"]):
                got = f.find_in_video(case["word"], "vid", "ch", "search", case["lines"])
                self.assertEqual(
                    [{"startMs": c["start_ms"], "endMs": c["end_ms"], "sentence": c["sentence"]} for c in got],
                    case["expect"])


class TokensTest(unittest.TestCase):
    def test_tokens(self):
        self.assertEqual(f.tokens("Don't STOP, 2x 'quoted'"), ["don't", "stop", "x", "quoted"])
        self.assertEqual(f.tokens(""), [])


class MergeTest(unittest.TestCase):
    def test_cap_total(self):
        many = [clip("v%d" % i, 5) for i in range(12)]
        self.assertEqual(len(f.merge([], many)), 10)

    def test_dedupe(self):
        self.assertEqual(len(f.merge([], [clip("a", 5), clip("a", 5)])), 1)
        self.assertEqual(len(f.merge([clip("a", 5)], [clip("a", 5)])), 1)

    def test_per_video_cap_and_order(self):
        r = f.merge([clip("a", 1)], [clip("a", 2), clip("a", 3), clip("b", 1)])
        self.assertEqual([(c["video_id"], c["start_ms"]) for c in r], [("a", 1), ("a", 2), ("b", 1)])


if __name__ == "__main__":
    unittest.main()


# ---- build_clips.py ----
import build_clips as b  # noqa: E402


def lines_of(*texts):
    return [{"start": 10.0 * (i + 1), "duration": 2.0, "text": t} for i, t in enumerate(texts)]


class BuildRowsTest(unittest.TestCase):
    def setUp(self):
        self.videos = [
            {"video_id": "v1", "channel": "A", "lines": lines_of("I like apple pie", "no match here")},
            {"video_id": "v2", "channel": "B", "lines": lines_of("An apple a day", "the bread is good")},
        ]

    def test_rows_per_word(self):
        got = b.build_rows({"apple", "bread", "zebra"}, self.videos, {})
        self.assertEqual([(c["video_id"], c["start_ms"]) for c in got["apple"]],
                         [("v1", 9000), ("v2", 9000)])
        self.assertEqual([(c["video_id"], c["start_ms"]) for c in got["bread"]], [("v2", 19000)])
        self.assertNotIn("zebra", got)
        self.assertTrue(all(c["source"] == "channel" for c in got["apple"]))
        self.assertEqual(got["bread"][0]["channel"], "B")

    def test_full_word_not_extended(self):
        cur = [{"video_id": "x%d" % i, "start_ms": 0} for i in range(10)]
        got = b.build_rows({"apple"}, self.videos, {"apple": cur})
        self.assertEqual(got["apple"], cur)

    def test_existing_scene_not_duplicated(self):
        cur = [{"video_id": "v1", "start_ms": 9000}]
        got = b.build_rows({"apple"}, self.videos, {"apple": cur})
        self.assertEqual([(c["video_id"], c["start_ms"]) for c in got["apple"]],
                         [("v1", 9000), ("v2", 9000)])


class FakeTr:
    def __init__(self, lang, generated):
        self.language_code, self.is_generated = lang, generated


class PickTranscriptTest(unittest.TestCase):
    def test_manual_preferred(self):
        a, m = FakeTr("en", True), FakeTr("en-GB", False)
        self.assertIs(b.pick_transcript([a, m]), m)

    def test_generated_only(self):
        a = FakeTr("en", True)
        self.assertIs(b.pick_transcript([FakeTr("ja", False), a]), a)

    def test_no_english(self):
        self.assertIsNone(b.pick_transcript([FakeTr("ja", False)]))
        # 日本語の動画 (自動字幕が ja) に付いた英語の字幕は使わない
        self.assertIsNone(b.pick_transcript([FakeTr("en", False), FakeTr("ja", True)]))
        self.assertIsNone(b.pick_transcript([]))


class UploadPayloadTest(unittest.TestCase):
    def test_columns(self):
        row = {"word": "a", "video_id": "v", "start_ms": 1, "end_ms": 2, "sentence": "s",
               "channel": "c", "source": "search", "extra": 1}
        got = b.upload_payload([row])
        self.assertEqual(set(got[0]), {"word", "video_id", "start_ms", "end_ms", "sentence", "channel", "source"})
        self.assertEqual(got[0]["source"], "channel")


class LoadWordsTest(unittest.TestCase):
    def test_filter(self):
        import tempfile
        data = [{"w": "Apple", "cefr": "A1"}, {"w": "apple", "cefr": "A2"}, {"w": "'m", "cefr": "A1"},
                {"w": "a.m.", "cefr": "A1"}, {"w": "ice cream", "cefr": "B2"}, {"w": "hard", "cefr": "C1"}]
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "w.json")
            with open(p, "w", encoding="utf-8") as fp:
                json.dump(data, fp)
            self.assertEqual(b.load_words(p), {"apple", "ice cream"})


class NextOffsetTest(unittest.TestCase):
    def test_empty_page_stops(self):
        self.assertIsNone(b.next_offset(200, 0, 2000, None))

    def test_416_stops(self):
        self.assertIsNone(b.next_offset(416, 0, 3000, "*/3000"))

    def test_short_page_continues_when_more_remain(self):
        self.assertEqual(b.next_offset(206, 500, 0, "0-499/1200"), 500)

    def test_reaches_total_stops(self):
        self.assertIsNone(b.next_offset(200, 200, 1000, "1000-1199/1200"))

    def test_no_total_continues_until_empty(self):
        self.assertEqual(b.next_offset(200, 10, 0, None), 10)


class SafeErrTest(unittest.TestCase):
    def test_no_url(self):
        self.assertEqual(b.safe_err(ValueError("https://x/?key=SECRET")), "ValueError")
        self.assertEqual(b.safe_err(RuntimeError("YouTube API 403: quotaExceeded")), "YouTube API 403: quotaExceeded")

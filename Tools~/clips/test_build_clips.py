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

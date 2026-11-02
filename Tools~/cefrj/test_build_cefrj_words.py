"""build_cefrj_words.py のテスト (見本の CSV はテストの中で作る)。
  python -m unittest test_build_cefrj_words   (このフォルダで)
"""
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_cefrj_words as b  # noqa: E402

CSV = """headword,pos,CEFR,CoreInventory 1,CoreInventory 2,Threshold
a,determiner,A1,,,
a.m./A.M./am/AM,adverb,A1,,,
Light,noun,B1,,,
light,noun,A2,,,
light,adjective,A1,,,
 ,noun,A1,,,
zenith,noun,C1,,,
abandon,verb,B1,x,,
"""


class BuildTest(unittest.TestCase):
    def rows(self):
        return b.read_csv(CSV.splitlines())

    def test_first_form_and_lowest_level(self):
        words = b.build(self.rows())
        self.assertIn({"w": "a.m.", "pos": "adverb", "cefr": "A1"}, words)
        self.assertIn({"w": "light", "pos": "noun", "cefr": "A2"}, words)
        self.assertNotIn({"w": "light", "pos": "noun", "cefr": "B1"}, words)

    def test_skips_empty_and_c_levels(self):
        words = b.build(self.rows())
        self.assertFalse(any(x["w"] == "" for x in words))
        self.assertFalse(any(x["w"] == "zenith" for x in words))

    def test_sorted_by_word_then_pos(self):
        words = b.build(self.rows())
        self.assertEqual([(x["w"], x["pos"]) for x in words],
                         [("a", "determiner"), ("a.m.", "adverb"), ("abandon", "verb"),
                          ("light", "adjective"), ("light", "noun")])

    def test_cli_writes_one_word_per_line(self):
        with tempfile.TemporaryDirectory() as d:
            src, out = os.path.join(d, "in.csv"), os.path.join(d, "out.json")
            with open(src, "w", encoding="utf-8") as f:
                f.write(CSV)
            b.main(["--csv", src, "--out", out])
            text = open(out, encoding="utf-8").read()
            self.assertEqual(len(json.loads(text)), 5)
            lines = text.splitlines()
            self.assertEqual(len(lines), 7)   # "[" + 5 行 + "]"
            self.assertEqual((lines[0], lines[-1]), ("[", "]"))


if __name__ == "__main__":
    unittest.main()

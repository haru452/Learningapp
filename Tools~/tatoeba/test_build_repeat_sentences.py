"""build_repeat_sentences.py のテスト (見本データはテストの中で作る。Tatoeba の文は使わない)。
  python3 -m unittest test_build_repeat_sentences   (このフォルダで)
"""
import bz2
import io
import json
import os
import sys
import tarfile
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_repeat_sentences as b  # noqa: E402

GOOD = [
    (1, "I like to read books at night."),
    (2, "She went to the store this morning."),
    (3, "We are going to visit our friends tomorrow."),
    (4, "Could you open the window for me?"),
    (5, "He has been working here for two years."),
    (6, "They played soccer after school yesterday."),
]
BAD = [
    (10, "Too short here ok."),                                        # 4 語
    (11, "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen."),  # 16 語
    (12, 'He said "hello" to me today.'),                              # "
    (13, "I bought (two) new shirts today."),                          # (
    (14, "My number is 123 and my room is 45."),                       # 数字 5 個
    (15, "x" * 60 + " " + "y" * 60 + " a b c d."),                     # 120 文字超え
]


def audio_rows():
    rows = [(sid, 100 + sid, "speaker", "CC BY-NC-ND 3.0") for sid, _ in GOOD + BAD]
    rows.append((2, 50, "other", "CC BY 4.0"))        # 2 は録音が 2 つ → 小さい audio_id (50)
    rows.append((7, 107, "speaker", ""))              # ライセンスが空
    rows.append((8, 108, "speaker", "\\N"))           # ライセンスが \N
    return rows


SENTENCES = GOOD + BAD + [(7, "This one has no usable license."), (8, "This one also has no license."),
                          (9, "This sentence has no audio at all.")]


class SelectTests(unittest.TestCase):
    def select(self, count=100, seed=1):
        return b.select_sentences([(i, "eng", t) for i, t in SENTENCES], audio_rows(), count, seed)

    def test_only_good_sentences(self):
        ids = sorted(s["id"] for s in self.select())
        self.assertEqual(ids, [1, 2, 3, 4, 5, 6])

    def test_lowest_audio_id_and_fields(self):
        two = [s for s in self.select() if s["id"] == 2][0]
        self.assertEqual(two, {"id": 2, "audioId": 50, "text": "She went to the store this morning.",
                               "author": "other", "license": "CC BY 4.0"})

    def test_duplicate_text_once(self):
        rows = [(i, "eng", t) for i, t in SENTENCES] + [(99, "eng", "I like to read books at night.")]
        audio = audio_rows() + [(99, 199, "speaker", "CC BY 4.0")]
        texts = [s["text"] for s in b.select_sentences(rows, audio, 100, 1)]
        self.assertEqual(texts.count("I like to read books at night."), 1)

    def test_same_seed_same_result(self):
        self.assertEqual(self.select(3, 7), self.select(3, 7))
        self.assertEqual(len(self.select(3, 7)), 3)
        orders = {tuple(s["id"] for s in self.select(6, seed)) for seed in range(10)}
        self.assertGreater(len(orders), 1)

    def test_fewer_candidates_than_count(self):
        self.assertEqual(len(self.select(1000)), 6)


class NameCapTests(unittest.TestCase):
    def test_tom_and_mary_capped_at_15_percent(self):
        rows, audio = [], []
        for i in range(1, 100):                               # 60 文が Tom、39 文が名前なし
            who = "Tom wants" if i <= 60 else "I want"
            rows.append((i, "eng", f"{who} to visit the city number {i} soon."))
            audio.append((i, 1000 + i, "speaker", "CC BY 4.0"))
        chosen = b.select_sentences(rows, audio, 20, 5)
        named = [s for s in chosen if b.has_common_name(s["text"])]
        self.assertEqual(len(chosen), 20)
        self.assertLessEqual(len(named), 3)                  # 20 x 15% = 3

    def test_has_common_name(self):
        self.assertTrue(b.has_common_name("Tom is here today with us."))
        self.assertTrue(b.has_common_name("I saw Mary's dog in the park."))
        self.assertFalse(b.has_common_name("Tomorrow we will go to the park."))


class FileTests(unittest.TestCase):
    def test_main_writes_json(self):
        with tempfile.TemporaryDirectory() as d:
            sent = os.path.join(d, "eng_sentences.tsv.bz2")
            with bz2.open(sent, "wt", encoding="utf-8") as f:
                for i, t in SENTENCES:
                    f.write(f"{i}\teng\t{t}\n")
            audio = os.path.join(d, "sentences_with_audio.tar.bz2")
            data = "".join(f"{sid}\t{aid}\t{user}\t{lic}\t\n" for sid, aid, user, lic in audio_rows()).encode("utf-8")
            with tarfile.open(audio, "w:bz2") as tar:
                info = tarfile.TarInfo("sentences_with_audio.csv")
                info.size = len(data)
                tar.addfile(info, io.BytesIO(data))
            out = os.path.join(d, "out", "RepeatSentences.json")
            b.main(["--sentences", sent, "--audio", audio, "--out", out, "--count", "4", "--seed", "3"])
            with open(out, encoding="utf-8") as f:
                result = json.load(f)
            self.assertEqual(set(result), {"sentences"})
            self.assertEqual(len(result["sentences"]), 4)
            self.assertEqual(set(result["sentences"][0]), {"id", "audioId", "text", "author", "license"})


if __name__ == "__main__":
    unittest.main()

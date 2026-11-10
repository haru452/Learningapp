// シャドーイングの「Back 10s」: 動画を戻した分だけ、直前の録音を捨てる
using System.Linq;

public static partial class LogicTests
{
    private static string Show(System.Collections.Generic.List<(double start, double end)> segs) =>
        string.Join(" ", segs.Select(s => s.start + "-" + s.end));

    private static void ShadowRewindTests()
    {
        // 戻さなければ、録音はそのまま
        var none = new ShadowRewind();
        Check(Show(none.Segments(30)) == "0-30" && none.KeptSeconds(30) == 30 && none.Count == 0, "rewind: none -> " + Show(none.Segments(30)));
        var wav = FakeWav(30);
        Check(ShadowRewind.Cut(wav, none) == wav, "rewind: none -> same wav");

        // 25 秒で戻す: 15〜25 秒を捨てる
        var one = new ShadowRewind();
        Check(one.Rewind(25) == 10, "rewind: back 10 s");
        Check(Show(one.Segments(40)) == "0-15 25-40" && one.KeptSeconds(40) == 30 && one.Count == 1, "rewind: one -> " + Show(one.Segments(40)));

        // 始まってすぐ (5 秒) なら、5 秒だけ戻す
        var early = new ShadowRewind();
        Check(early.Rewind(5) == 5, "rewind: only 5 s at the start");
        Check(Show(early.Segments(12)) == "5-12" && early.KeptSeconds(12) == 7, "rewind: early -> " + Show(early.Segments(12)));

        // 続けて戻す: 前に残した所からさらに削る
        var two = new ShadowRewind();
        two.Rewind(25);   // 残り 0-15
        Check(two.Rewind(28) == 10, "rewind: second back 10 s");   // 25-28 (3 秒) と 8-15 (7 秒) を捨てる
        Check(Show(two.Segments(40)) == "0-8 28-40" && two.KeptSeconds(40) == 20 && two.Count == 2, "rewind: twice -> " + Show(two.Segments(40)));

        // 録音を切り詰める: 40 秒 → 20 秒
        var cut = ShadowRewind.Cut(FakeWav(40), two);
        Check(cut.Length == FakeWav(20).Length, "rewind: cut wav to 20 s (" + cut.Length + " vs " + FakeWav(20).Length + ")");
        Check(System.BitConverter.ToInt32(cut, 40) == cut.Length - 44 && System.BitConverter.ToInt32(cut, 4) == cut.Length - 8, "rewind: cut wav header sizes");

        // 録音が予定より短くても落ちない
        Check(ShadowRewind.Cut(FakeWav(30), two).Length == FakeWav(10).Length, "rewind: short recording clamps (0-8 + 28-30)");
        Check(ShadowRewind.Cut(null, two) == null, "rewind: null wav");
    }
}

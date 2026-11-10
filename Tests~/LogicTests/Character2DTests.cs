// 2D キャラ (CharacterMaker で作ったキャラ) の判断: 口の切り替え・原点の変換・中身の確認・まばたき・揺れ
using System;

public static partial class LogicTests
{
    private static CharacterJson SampleCharacter()
    {
        RectJson R(float x, float y, float w, float h) => new RectJson { x = x, y = y, w = w, h = h };
        return new CharacterJson
        {
            version = 1, name = "mika", imageWidth = 1024, imageHeight = 1536, @base = "base.png",
            mouth = new MouthJson { rect = R(0.45f, 0.38f, 0.1f, 0.05f), frames = new[] { "mouth_1.png", "mouth_2.png" } },
            eyes = new[]
            {
                new EyeJson { rect = R(0.38f, 0.28f, 0.08f, 0.04f), closed = "eye_l_closed.png" },
                new EyeJson { rect = R(0.54f, 0.28f, 0.08f, 0.04f), closed = "eye_r_closed.png" },
            },
        };
    }

    private static void Character2DTests()
    {
        // 口の切り替え (CharacterMaker の mouthFrame と同じしきい値)
        Check(Character2DLogic.MouthFrame(0.149f) == 0, "2d mouth: 0.149 -> closed");
        Check(Character2DLogic.MouthFrame(0.15f) == 1, "2d mouth: 0.15 -> half");
        Check(Character2DLogic.MouthFrame(0.499f) == 1, "2d mouth: 0.499 -> half");
        Check(Character2DLogic.MouthFrame(0.5f) == 2, "2d mouth: 0.5 -> wide");

        // 左上原点 -> 左下原点
        var bl = Character2DLogic.ToBottomLeft(new RectJson { x = 0.4f, y = 0.3f, w = 0.1f, h = 0.05f });
        Check(Math.Abs(bl.y - 0.65f) < 1e-5f && bl.x == 0.4f && bl.w == 0.1f && bl.h == 0.05f, "2d rect: bottom-left y = 1 - y - h");

        // 中身の確認
        Check(Character2DLogic.Validate(SampleCharacter()) == null, "2d validate: ok");
        var v2 = SampleCharacter(); v2.version = 2;
        Check(Character2DLogic.Validate(v2) != null, "2d validate: version 2");
        var oneFrame = SampleCharacter(); oneFrame.mouth.frames = new[] { "mouth_1.png" };
        Check(Character2DLogic.Validate(oneFrame) != null, "2d validate: one mouth frame");
        var oneEye = SampleCharacter(); oneEye.eyes = new[] { oneEye.eyes[0] };
        Check(Character2DLogic.Validate(oneEye) != null, "2d validate: one eye");
        var noFile = SampleCharacter(); noFile.eyes[1].closed = "";
        Check(Character2DLogic.Validate(noFile) != null, "2d validate: empty file name");
        Check(Character2DLogic.Validate(null) != null, "2d validate: null");

        // まばたき (乱数 0 -> 2 秒後に 0.12 秒だけ閉じる)
        var blink = new BlinkTimer(() => 0f);
        bool Run(float seconds)
        {
            bool closed = false;
            for (float t = 0; t < seconds - 1e-4f; t += 1f / 60f) closed = blink.Update(1f / 60f);
            return closed;
        }
        Check(!Run(1.98f), "2d blink: open until 2s");
        Check(Run(0.05f), "2d blink: closed at 2.03s");
        Check(!Run(0.15f), "2d blink: open again at 2.18s");

        // 揺れ (3 秒周期・高さの 0.5%)
        Check(Math.Abs(Character2DLogic.BobOffset(0.75f, 1000f) - 5f) < 1e-3f, "2d bob: peak 0.5%");
        Check(Character2DLogic.BobOffset(0f, 1000f) == 0f, "2d bob: 0 at t=0");
    }
}

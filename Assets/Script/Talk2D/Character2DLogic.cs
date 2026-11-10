// 2D キャラ (CharacterMaker で作ったキャラ) の、Unity を使わない判断。
// character.json の形と数値は CharacterMaker の spec (docs/superpowers/specs/2026-11-11-character-maker-design.md) と同じ。
// Tests~/LogicTests でテストするため、UnityEngine を使わない。
using System;

/// <summary>四角。割合 (0〜1)。character.json では左上原点。</summary>
[Serializable]
public class RectJson
{
    public float x, y, w, h;
}

[Serializable]
public class MouthJson
{
    public RectJson rect;
    public string[] frames; // [半開き, 全開]
}

[Serializable]
public class EyeJson
{
    public RectJson rect;
    public string closed;
}

/// <summary>character.json。JsonUtility で読む。</summary>
[Serializable]
public class CharacterJson
{
    public int version;
    public string name;
    public int imageWidth;
    public int imageHeight;
    public string @base;
    public MouthJson mouth;
    public EyeJson[] eyes; // [左目, 右目]
}

/// <summary>Characters/index.json</summary>
[Serializable]
public class CharacterIndexJson
{
    public string[] characters;
}

public static class Character2DLogic
{
    /// <summary>口の開き (0〜1) -> 0 = 閉じ (元の絵), 1 = 半開き, 2 = 全開</summary>
    public static int MouthFrame(float open)
    {
        if (open < 0.15f) return 0;
        if (open < 0.5f) return 1;
        return 2;
    }

    /// <summary>左上原点の四角を、Unity の左下原点に直す</summary>
    public static RectJson ToBottomLeft(RectJson r) =>
        new RectJson { x = r.x, y = 1f - r.y - r.h, w = r.w, h = r.h };

    /// <summary>正しければ null、そうでなければ日本語の理由</summary>
    public static string Validate(CharacterJson c)
    {
        if (c == null) return "character.json が読めません";
        if (c.version != 1) return $"version が 1 ではありません ({c.version})。CharacterMaker で書き出し直してください";
        if (c.imageWidth <= 0 || c.imageHeight <= 0) return "絵の大きさが正しくありません";
        if (string.IsNullOrEmpty(c.@base)) return "元の絵のファイル名がありません";
        if (c.mouth == null || c.mouth.rect == null) return "口の場所がありません";
        if (c.mouth.frames == null || c.mouth.frames.Length != 2 || Array.Exists(c.mouth.frames, string.IsNullOrEmpty))
            return "口の差分は 2 つ必要です";
        if (c.eyes == null || c.eyes.Length != 2) return "目は左右 2 つ必要です";
        foreach (var e in c.eyes)
            if (e == null || e.rect == null || string.IsNullOrEmpty(e.closed)) return "目の場所か、閉じた差分がありません";
        return null;
    }

    /// <summary>ゆっくりした上下の揺れ (3 秒周期・高さの 0.5%)</summary>
    public static float BobOffset(float t, float height) =>
        (float)Math.Sin(2 * Math.PI * t / 3) * 0.005f * height;
}

/// <summary>まばたき: 2〜6 秒ごとに 0.12 秒だけ目を閉じる (CharacterMaker の createBlink と同じ)</summary>
public class BlinkTimer
{
    const float MinWait = 2f, MaxWait = 6f, Closed = 0.12f;

    readonly Func<float> random; // 0〜1
    float wait;
    float closedLeft;

    public BlinkTimer(Func<float> random)
    {
        this.random = random;
        wait = NextWait();
    }

    float NextWait() => MinWait + random() * (MaxWait - MinWait);

    /// <summary>目を閉じている間 true</summary>
    public bool Update(float dt)
    {
        if (closedLeft > 0)
        {
            closedLeft -= dt;
            if (closedLeft > 0) return true;
            wait = NextWait();
            return false;
        }
        wait -= dt;
        if (wait <= 0)
        {
            closedLeft = Closed + wait; // 行き過ぎた分を引く
            return closedLeft > 0;
        }
        return false;
    }
}

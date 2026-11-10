// StreamingAssets/Characters/ から、CharacterMaker で書き出したキャラを読み込む。
// UnityWebRequest を使うので、エディタでも Android (jar の中) でも読める。
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class LoadedCharacter
{
    public CharacterJson json;
    public Texture2D baseTex;
    public Texture2D[] mouth;      // [半開き, 全開]
    public Texture2D[] eyesClosed; // [左目, 右目]
}

public static class CharacterPack
{
    static string Url(string relative)
    {
        string path = Application.streamingAssetsPath + "/Characters/" + relative;
        // Android は "jar:file://..."。PC はただのパスなので file:/// の URL にする (空白なども直す)
        return path.Contains("://") ? path : new Uri(path).AbsoluteUri;
    }

    static IEnumerator GetText(string relative, Action<string> done, Action<string> fail)
    {
        using var req = UnityWebRequest.Get(Url(relative));
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) fail($"{relative} が読めません ({req.error})");
        else done(req.downloadHandler.text);
    }

    static IEnumerator GetTexture(string relative, Action<Texture2D> done, Action<string> fail)
    {
        using var req = UnityWebRequestTexture.GetTexture(Url(relative));
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
        {
            fail($"{relative} が読めません ({req.error})");
            yield break;
        }
        var tex = DownloadHandlerTexture.GetContent(req);
        tex.wrapMode = TextureWrapMode.Clamp;
        done(tex);
    }

    /// <summary>index.json のキャラ名の一覧</summary>
    public static IEnumerator LoadIndex(Action<string[]> done, Action<string> fail)
    {
        string text = null;
        string error = null;
        yield return GetText("index.json", t => text = t, e => error = e);
        if (error != null) { fail(error + "。CharacterMaker で書き出してください"); yield break; }

        CharacterIndexJson index = null;
        try { index = JsonUtility.FromJson<CharacterIndexJson>(text); } catch (ArgumentException) { }
        if (index?.characters == null) { fail("index.json の形が正しくありません"); yield break; }
        done(index.characters);
    }

    /// <summary>1 キャラ分 (character.json と 5 枚の絵) を読む</summary>
    public static IEnumerator Load(string name, Action<LoadedCharacter> done, Action<string> fail)
    {
        string text = null;
        string error = null;
        yield return GetText(name + "/character.json", t => text = t, e => error = e);
        if (error != null) { fail(error); yield break; }

        CharacterJson json = null;
        try { json = JsonUtility.FromJson<CharacterJson>(text); } catch (ArgumentException) { }
        string invalid = Character2DLogic.Validate(json);
        if (invalid != null) { fail($"{name}: {invalid}"); yield break; }

        var c = new LoadedCharacter { json = json, mouth = new Texture2D[2], eyesClosed = new Texture2D[2] };
        Action<string> onFail = e => error ??= e;
        yield return GetTexture(name + "/" + json.@base, t => c.baseTex = t, onFail);
        for (int i = 0; i < 2; i++)
        {
            int k = i;
            yield return GetTexture(name + "/" + json.mouth.frames[k], t => c.mouth[k] = t, onFail);
            yield return GetTexture(name + "/" + json.eyes[k].closed, t => c.eyesClosed[k] = t, onFail);
        }
        if (error != null) { fail(error); yield break; }
        done(c);
    }
}

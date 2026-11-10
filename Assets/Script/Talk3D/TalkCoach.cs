using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>Gemini の添削の結果 (Talk で画面の上に出す)</summary>
public class TalkCorrection
{
    public bool Ok;              // 間違いが無い
    public string Corrected = "";
    public string NoteJa = "";   // 何を直したかの短い日本語
}

/// <summary>
/// Talk の添削まわりの決まり: 返事の読み取り・画面に出すか・添削を頼むか。
/// Unity に依存しない (テストで確かめる)。
/// </summary>
public static class TalkCoach
{
    /// <summary>Gemini の返事 (JSON) を読む。形が違う・直しなのに文が空なら null</summary>
    public static TalkCorrection Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        object root;
        try { root = MiniJson.Parse(json); }
        catch (Exception) { return null; }
        if (!(root is Dictionary<string, object> d) || !d.TryGetValue("ok", out object okValue) || !(okValue is bool ok)) return null;
        var c = new TalkCorrection { Ok = ok, Corrected = JsonGet.Str(d, "corrected").Trim(), NoteJa = JsonGet.Str(d, "note_ja").Trim() };
        if (!c.Ok && c.Corrected.Length == 0) return null;
        return c;
    }

    /// <summary>添削を頼むか: 英字を含む語が 2 語以上 (「Yes.」や数字だけは頼まない)</summary>
    public static bool ShouldCheck(string utterance) =>
        (utterance ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetter)) >= 2;

    /// <summary>画面に出すか: 直しがあり、大文字や句読点だけの違いではない</summary>
    public static bool ShouldShow(string said, TalkCorrection c) =>
        c != null && !c.Ok && !string.IsNullOrWhiteSpace(c.Corrected) && Normalize(said) != Normalize(c.Corrected);

    /// <summary>比べるための形: 小文字にし、英数字とアポストロフィ以外を除き、空白を 1 つにする</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (char ch in (s ?? "").ToLowerInvariant())
        {
            char c = ch == '’' ? '\'' : ch;
            if (char.IsLetterOrDigit(c) || c == '\'') sb.Append(c);
            else sb.Append(' ');
        }
        return string.Join(" ", sb.ToString().Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// 画面に出す最新の添削。今の発話 (turn) の添削だけを受け付け、次の発話で消す
    /// (遅れて届いた前の発話の添削で上書きしない)
    /// </summary>
    public class CorrectionSlot
    {
        public int Turn { get; private set; }
        public string Said { get; private set; } = "";
        public TalkCorrection Value { get; private set; }

        public void NewTurn(int turn, string said)
        {
            Turn = turn;
            Said = said ?? "";
            Value = null;
        }

        /// <summary>今の発話の添削で、言い直すものがあれば出す。出したら true</summary>
        public bool Offer(int turn, TalkCorrection c)
        {
            if (turn != Turn || !ShouldShow(Said, c)) return false;
            Value = c;
            return true;
        }
    }
}

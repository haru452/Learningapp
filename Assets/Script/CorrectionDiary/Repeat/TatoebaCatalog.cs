using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Resources/RepeatSentences.json (Tatoeba の文の一覧) を読む。
/// 形: {"sentences":[{"id":1,"audioId":2,"text":"...","author":"...","license":"..."}]}
/// 壊れていたり無かったりしたら空のリストを返す (JsonUtility を使わないので、Unity の外でもテストできる)。
/// </summary>
public static class TatoebaCatalog
{
    public static List<RepeatSentence> Parse(string json)
    {
        var list = new List<RepeatSentence>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            var root = new Reader(json).ReadValue() as Dictionary<string, object>;
            if (root == null || !(root.TryGetValue("sentences", out object arr) && arr is List<object> items)) return list;
            foreach (var o in items)
            {
                if (!(o is Dictionary<string, object> d)) continue;
                string text = Str(d, "text").Trim();
                if (text.Length == 0) continue;
                list.Add(new RepeatSentence
                {
                    Text = text,
                    Source = "tatoeba",
                    TatoebaId = Num(d, "id"),
                    AudioId = Num(d, "audioId"),
                    Author = Str(d, "author"),
                    License = Str(d, "license"),
                });
            }
        }
        catch (Exception)
        {
            list.Clear();
        }
        return list;
    }

    private static string Str(Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) && v is string s ? s : "";

    private static long? Num(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out object v) && v is double x ? (long)x : (long?)null;

    // 小さな JSON の読み取り (オブジェクト・配列・文字列・数・true/false/null)
    private class Reader
    {
        private readonly string _s;
        private int _i;
        public Reader(string s) { _s = s; }

        public object ReadValue()
        {
            Skip();
            if (_i >= _s.Length) throw new FormatException("end");
            char c = _s[_i];
            if (c == '{') return ReadObject();
            if (c == '[') return ReadArray();
            if (c == '"') return ReadString();
            if (c == 't') { Expect("true"); return true; }
            if (c == 'f') { Expect("false"); return false; }
            if (c == 'n') { Expect("null"); return null; }
            return ReadNumber();
        }

        private Dictionary<string, object> ReadObject()
        {
            var d = new Dictionary<string, object>();
            _i++; Skip();
            if (Peek() == '}') { _i++; return d; }
            while (true)
            {
                Skip();
                string key = ReadString();
                Skip(); Expect(":");
                d[key] = ReadValue();
                Skip();
                char c = Next();
                if (c == '}') return d;
                if (c != ',') throw new FormatException("object");
            }
        }

        private List<object> ReadArray()
        {
            var l = new List<object>();
            _i++; Skip();
            if (Peek() == ']') { _i++; return l; }
            while (true)
            {
                l.Add(ReadValue());
                Skip();
                char c = Next();
                if (c == ']') return l;
                if (c != ',') throw new FormatException("array");
            }
        }

        private string ReadString()
        {
            if (Next() != '"') throw new FormatException("string");
            var sb = new StringBuilder();
            while (true)
            {
                char c = Next();
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = Next();
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _i += 4;
                        break;
                    default: sb.Append(e); break;   // \" \\ \/
                }
            }
        }

        private double ReadNumber()
        {
            int start = _i;
            while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
            return double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private void Expect(string word)
        {
            if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw new FormatException(word);
            _i += word.Length;
        }

        private void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        private char Peek() => _i < _s.Length ? _s[_i] : '\0';
        private char Next() => _i < _s.Length ? _s[_i++] : throw new FormatException("end");
    }
}

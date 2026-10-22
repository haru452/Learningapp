using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// 小さな JSON の読み取り (オブジェクト = Dictionary、配列 = List、文字列、数 = double、true/false、null)。
/// JsonUtility は null の数や配列の一番外側を読めず、Unity の外でテストもできないので、リピーティングではこれを使う。
/// 読めないときは FormatException。
/// </summary>
public static class MiniJson
{
    public static object Parse(string json)
    {
        var r = new Reader(json ?? "");
        object v = r.ReadValue();
        r.EnsureEnd();
        return v;
    }

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

        public void EnsureEnd()
        {
            Skip();
            if (_i != _s.Length) throw new FormatException("trailing characters");
        }

        private void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        private char Peek() => _i < _s.Length ? _s[_i] : '\0';
        private char Next() => _i < _s.Length ? _s[_i++] : throw new FormatException("end");
    }
}

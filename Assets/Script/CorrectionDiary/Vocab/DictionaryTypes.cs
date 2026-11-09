using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>辞書の 1 つの見出し (品詞ごと)</summary>
public class DictEntry
{
    public string Headword = "";
    public string Pos = "";        // noun / verb / ... (辞書の書き方のまま)
    public string Ipa = "";        // 発音記号 (無ければ空)
    public string AudioUrl = "";   // 音声 (無ければ空)
    public List<string> Definitions = new List<string>();
    public List<string> Examples = new List<string>();
}

/// <summary>辞書で引いた結果。見つからないときは Found = false で、似た単語の候補が Suggestions に入る</summary>
public class DictionaryResult
{
    public bool Found;
    public List<DictEntry> Entries = new List<DictEntry>();
    public List<string> Suggestions = new List<string>();
    /// <summary>読み方の版。2 = 派生語 (calmness など) を正しく読む。古い保存データは引き直す</summary>
    public int Version = CurrentVersion;
    public const int CurrentVersion = 2;
}

public interface IDictionaryProvider
{
    /// <summary>キーなどがそろっていて引けるか</summary>
    bool IsConfigured { get; }

    /// <summary>単語を引く。通信できないときは例外</summary>
    Task<DictionaryResult> LookupAsync(string word);
}

/// <summary>辞書が JSON 以外を返した (多くはキーの間違い)。通信できないのとは別に扱う</summary>
public class DictionaryKeyException : System.Exception
{
    public DictionaryKeyException(string message) : base(message) { }
}

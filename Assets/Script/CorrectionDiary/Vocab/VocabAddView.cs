using System;
using System.Linq;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Add: 辞書で検索して単語帳に追加、または手入力 (単語と自分の意味) で追加。
/// 追加した単語は学習中に入り、次の回の最初に出る。同じ単語は重複して入らない
/// </summary>
public class VocabAddView
{
    public struct View
    {
        public TMP_InputField SearchInput;
        public Button SearchButton;
        public TMP_Text ResultText;
        public Button SoundButton;
        public Button AddButton;
        public Transform SuggestionContent;
        public GameObject SuggestionTemplate;
        public TMP_InputField ManualWordInput;
        public TMP_InputField ManualMeaningInput;
        public Button ManualAddButton;
    }

    private readonly VocabContext _ctx;
    private readonly View _v;
    private string _foundWord;
    private DictionaryResult _found;
    private bool _busy;

    public VocabAddView(VocabContext ctx, View v)
    {
        _ctx = ctx;
        _v = v;
        if (_v.SuggestionTemplate != null) _v.SuggestionTemplate.SetActive(false);
        if (_v.SearchButton != null) _v.SearchButton.onClick.AddListener(() => _ = SearchAsync(_v.SearchInput != null ? _v.SearchInput.text : ""));
        if (_v.SearchInput != null) _v.SearchInput.onSubmit.AddListener(t => _ = SearchAsync(t));
        if (_v.AddButton != null) _v.AddButton.onClick.AddListener(AddFound);
        if (_v.SoundButton != null) _v.SoundButton.onClick.AddListener(PlayFound);
        if (_v.ManualAddButton != null) _v.ManualAddButton.onClick.AddListener(() => _ = AddManualAsync());
        ShowResult("", null);
    }

    public void Show()
    {
        if (_ctx.Dictionary == null || !_ctx.Dictionary.IsConfigured) _ctx.SetStatus(VocabContext.NoDictionaryKey);
    }

    private async System.Threading.Tasks.Task SearchAsync(string text)
    {
        string word = VocabDeck.Key(text);
        if (word.Length == 0 || _busy) return;
        if (_ctx.Dictionary == null || !_ctx.Dictionary.IsConfigured)
        {
            _ctx.SetStatus(VocabContext.NoDictionaryKey);
            return;
        }
        if (_v.SearchInput != null) _v.SearchInput.text = word;
        _busy = true;
        _ctx.SetLoading(true);
        _ctx.SetStatus("Searching...");
        try
        {
            var r = await _ctx.LookupAsync(word);
            if (r != null && r.Found)
            {
                ShowResult(word, r);
                _ctx.SetStatus(_ctx.Deck.Find(word) != null ? "Already in your words." : "");
            }
            else
            {
                ShowResult("", null);
                ShowNotFound(word, r);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Vocab] search failed: " + e.Message);
            ShowResult("", null);
            _ctx.SetStatus("Could not reach the dictionary. Check the connection and try again.");
        }
        finally
        {
            _busy = false;
            _ctx.SetLoading(false);
        }
    }

    private void ShowResult(string word, DictionaryResult r)
    {
        _foundWord = word;
        _found = r;
        VocabContext.ClearChildren(_v.SuggestionContent, _v.SuggestionTemplate);
        VocabContext.Show(_v.AddButton, r != null);
        VocabContext.Show(_v.SoundButton, r != null);
        if (_v.ResultText == null) return;
        if (r == null) { _v.ResultText.text = ""; return; }
        _v.ResultText.text = DescribeEntries(r, _ctx, word);
    }

    /// <summary>辞書の結果を画面の文にする (見出し・品詞・CEFR・発音記号・意味)。Today のカードの裏でも使う</summary>
    public static string DescribeEntries(DictionaryResult r, VocabContext ctx, string word, int maxDefs = 3)
    {
        var sb = new StringBuilder();
        foreach (var e in r.Entries.Take(4))
        {
            string level = ctx.Cefr.LevelForPos(word, e.Pos);
            sb.Append("<b>").Append(VocabContext.Esc(e.Headword)).Append("</b>");
            if (e.Pos.Length > 0) sb.Append("  <i>").Append(VocabContext.Esc(e.Pos)).Append("</i>");
            if (level != null) sb.Append("  <color=#aaaaaa>").Append(level).Append("</color>");
            if (e.Ipa.Length > 0) sb.Append("  /").Append(VocabContext.Esc(e.Ipa)).Append('/');
            sb.Append('\n');
            foreach (var d in e.Definitions.Take(maxDefs)) sb.Append("<size=85%>- ").Append(VocabContext.Esc(d)).Append("</size>\n");
        }
        return sb.ToString().TrimEnd();
    }

    private void ShowNotFound(string word, DictionaryResult r)
    {
        if (_v.ResultText != null)
            _v.ResultText.text = "Not found: <b>" + VocabContext.Esc(word) + "</b>" +
                                 (r != null && r.Suggestions.Count > 0 ? "\nDid you mean:" : "") +
                                 "\n<size=80%>You can also add it by hand below.</size>";
        if (r == null || _v.SuggestionTemplate == null) return;
        foreach (string s in r.Suggestions.Take(8))
        {
            var row = VocabContext.CloneRow(_v.SuggestionTemplate, _v.SuggestionContent);
            var t = row.GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.text = VocabContext.Esc(s);
            var b = row.GetComponent<Button>();
            string again = s;
            if (b != null) b.onClick.AddListener(() => _ = SearchAsync(again));
        }
        _ctx.SetStatus("");
    }

    private void AddFound()
    {
        if (_found == null || string.IsNullOrEmpty(_foundWord)) return;
        var card = _ctx.Deck.Add(_foundWord, "search", "", _ctx.LevelFor(_foundWord, _found), DateTime.UtcNow);
        if (card == null) { _ctx.SetStatus("Already in your words."); return; }
        _ctx.SaveDeck();
        _ctx.SetStatus("Added \"" + card.Word + "\" to Learning. It comes first next time.");
    }

    private void PlayFound()
    {
        if (_found == null) return;
        string url = _found.Entries.Select(e => e.AudioUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u)) ?? "";
        _ = _ctx.Audio.PlayWordAsync(_foundWord, url, CancellationToken.None);
    }

    private async System.Threading.Tasks.Task AddManualAsync()
    {
        string word = VocabDeck.Key(_v.ManualWordInput != null ? _v.ManualWordInput.text : "");
        string meaning = _v.ManualMeaningInput != null ? _v.ManualMeaningInput.text.Trim() : "";
        if (word.Length == 0) { _ctx.SetStatus("Type a word first."); return; }
        if (_busy) return;
        if (_ctx.Deck.Find(word) != null) { _ctx.SetStatus("Already in your words."); return; }

        _busy = true;
        _ctx.SetLoading(true);
        DictionaryResult dict = null;
        try { dict = await _ctx.LookupAsync(word); }   // 引けたら情報も付ける (引けなくても追加する)
        catch (Exception e) { Debug.LogWarning("[Vocab] lookup for a manual word failed: " + e.Message); }
        finally { _busy = false; _ctx.SetLoading(false); }

        var card = _ctx.Deck.Add(word, "manual", meaning, _ctx.LevelFor(word, dict), DateTime.UtcNow);
        if (card == null) { _ctx.SetStatus("Already in your words."); return; }
        _ctx.SaveDeck();
        if (_v.ManualWordInput != null) _v.ManualWordInput.text = "";
        if (_v.ManualMeaningInput != null) _v.ManualMeaningInput.text = "";
        _ctx.SetStatus("Added \"" + card.Word + "\" to Learning." + (dict != null && dict.Found ? "" : " (not in the dictionary)"));
    }
}

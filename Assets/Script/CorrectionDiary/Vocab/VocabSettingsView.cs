using TMPro;
using UnityEngine.UI;

/// <summary>Settings: 自分のレベル・1 回の語数・目標の想起率・出典</summary>
public class VocabSettingsView
{
    public struct View
    {
        public Button[] LevelButtons, SizeButtons, RetentionButtons;
        public TMP_Text CreditText;
    }

    public const string Credits =
        "Word levels: CEFR-J Wordlist Version 1.5, Tono Laboratory, TUFS\n" +
        "Dictionary: Merriam-Webster's Learner's Dictionary (dictionaryapi.com)";

    private readonly VocabContext _ctx;
    private readonly View _v;

    public VocabSettingsView(VocabContext ctx, View v)
    {
        _ctx = ctx;
        _v = v;
        for (int i = 0; i < (_v.LevelButtons?.Length ?? 0); i++)
        {
            string level = CefrWordList.Levels[i];
            VocabContext.SetLabel(_v.LevelButtons[i], level);
            _v.LevelButtons[i].onClick.AddListener(() => { VocabSettings.Level = level; Refresh(); _ctx.SetStatus("Level: " + level); });
        }
        for (int i = 0; i < (_v.SizeButtons?.Length ?? 0); i++)
        {
            int size = VocabSettings.SessionSizes[i];
            VocabContext.SetLabel(_v.SizeButtons[i], size + " words");
            _v.SizeButtons[i].onClick.AddListener(() => { VocabSettings.SessionSize = size; Refresh(); _ctx.SetStatus(size + " words from the next session."); });
        }
        for (int i = 0; i < (_v.RetentionButtons?.Length ?? 0); i++)
        {
            int r = VocabSettings.Retentions[i];
            VocabContext.SetLabel(_v.RetentionButtons[i], r + "%");
            _v.RetentionButtons[i].onClick.AddListener(() => { VocabSettings.Retention = r; Refresh(); _ctx.SetStatus("Target recall: " + r + "%"); });
        }
        if (_v.CreditText != null) _v.CreditText.text = Credits;
    }

    public void Show() => Refresh();

    private void Refresh()
    {
        for (int i = 0; i < (_v.LevelButtons?.Length ?? 0); i++)
            _ctx.Highlight(_v.LevelButtons[i], CefrWordList.Levels[i] == VocabSettings.Level);
        for (int i = 0; i < (_v.SizeButtons?.Length ?? 0); i++)
            _ctx.Highlight(_v.SizeButtons[i], VocabSettings.SessionSizes[i] == VocabSettings.SessionSize);
        for (int i = 0; i < (_v.RetentionButtons?.Length ?? 0); i++)
            _ctx.Highlight(_v.RetentionButtons[i], VocabSettings.Retentions[i] == VocabSettings.Retention);
    }
}

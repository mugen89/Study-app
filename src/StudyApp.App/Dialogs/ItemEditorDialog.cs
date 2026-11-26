namespace StudyApp.App.Dialogs;

/// <summary>Create or edit any item type. LLMs make mistakes — this is where they get fixed.</summary>
public sealed class ItemEditorDialog : DialogBase
{
    private static readonly string[] TypeOrder = { ItemTypes.Flashcard, ItemTypes.Single, ItemTypes.Multiple, ItemTypes.TrueFalse, ItemTypes.Short, ItemTypes.Note };

    private readonly Item _item;
    private readonly DropDown _type;
    private readonly InputBox _prompt;
    private readonly Column _specific = new() { Gap = S(14) };
    private readonly InputBox _explanation;
    private readonly InputBox _page;
    private readonly InputBox _tags;
    private readonly Segmented _difficulty;
    private readonly Toggle _flagged;
    private readonly Toggle _wrong;
    private readonly InputBox _note;
    private readonly TextLabel _promptLabel;

    // type-specific inputs
    private InputBox? _back, _hint, _model, _accept, _keyPoints, _content;
    private Segmented? _truth;
    private readonly List<OptionRow> _options = new();
    private Column? _optionList;

    private ItemEditorDialog(Item item, bool isNew) : base(isNew ? T("New item") : T("Edit item"), 720)
    {
        _item = item;
        _type = new DropDown(260);
        foreach (var t in TypeOrder) _type.Add(ItemTypes.DisplayName(t), t);
        _type.SelectedValue = item.Type;
        _type.SelectionChanged += (_, _) => { CollectSpecific(); _item.Type = (string)_type.SelectedValue!; BuildSpecific(); Relayout(); };

        _promptLabel = new TextLabel("", Fonts.UiSemibold(9.5f), Theme.P.Text2);
        _prompt = new InputBox("", multiline: true, lines: 3) { Text = item.Prompt };
        _explanation = new InputBox(T("Why is this the answer?"), multiline: true, lines: 3) { Text = item.Explanation };
        _page = new InputBox(T("e.g. 52 or 52-53")) { Text = item.Page };
        _tags = new InputBox(T("comma separated")) { Text = string.Join(", ", item.Tags) };
        _difficulty = new Segmented(item.Difficulty, T("Not set"), "1", "2", "3", "4", "5");
        _flagged = new Toggle(T("Flagged for later"), item.Flagged);
        _wrong = new Toggle(T("Wrong in source"), item.WrongInSource, T("The AI got this wrong compared to the textbook. It is left out of study sessions until you fix it and turn this off."));
        _note = new InputBox(T("Personal note (optional)"), multiline: true, lines: 2) { Text = item.PersonalNote };

        var promptField = new Column { Gap = S(6) };
        promptField.Controls.Add(_promptLabel);
        promptField.Controls.Add(_prompt);

        Content.Controls.Add(Ui.Field(T("Type"), _type.Align(HAlign.Left)));
        Content.Controls.Add(promptField);
        Content.Controls.Add(_specific);
        Content.Controls.Add(Ui.Field(T("Explanation"), _explanation));
        var meta = new Row { Gap = S(14), Align = VAlign.Top };
        meta.Controls.Add(Ui.Field(T("Page"), _page).Also(c => _page.Width = S(150)));
        meta.Controls.Add(Ui.Field(T("Tags"), _tags).Flex());
        Content.Controls.Add(meta);
        Content.Controls.Add(Ui.Field(T("Difficulty"), _difficulty.Align(HAlign.Left)));
        Content.Controls.Add(_flagged);
        Content.Controls.Add(_wrong);
        Content.Controls.Add(Ui.Field(T("Personal note"), _note));

        BuildSpecific();
        AddButton(T("Cancel"), Kind.Secondary, DialogResult.Cancel);
        AcceptButton = AddAction(T("Save"), Kind.Primary, Save, Icons.Save);
        Shown += (_, _) => _prompt.Focus();
    }

    private void BuildSpecific()
    {
        _specific.SuspendLayout();
        foreach (Control c in _specific.Controls.Cast<Control>().ToList()) { _specific.Controls.Remove(c); c.Dispose(); }
        _options.Clear();
        _back = _hint = _model = _accept = _keyPoints = _content = null;
        _truth = null;
        var type = _item.Type;

        _promptLabel.Text = type switch
        {
            ItemTypes.Flashcard => T("Front (question or term)"),
            ItemTypes.TrueFalse => T("Statement"),
            ItemTypes.Note => T("Heading"),
            _ => T("Question"),
        };

        switch (type)
        {
            case ItemTypes.Flashcard:
                _back = new InputBox(T("Answer"), multiline: true, lines: 3) { Text = _item.Answer };
                _hint = new InputBox(T("Optional hint")) { Text = _item.Data.Hint ?? "" };
                _specific.Controls.Add(Ui.Field(T("Back (answer)"), _back));
                _specific.Controls.Add(Ui.Field(T("Hint"), _hint));
                break;

            case ItemTypes.Single:
            case ItemTypes.Multiple:
                _optionList = new Column { Gap = S(8) };
                var opts = _item.Data.Options ?? new List<ChoiceOption> { new(""), new(""), new(""), new("") };
                var correct = _item.Data.Correct ?? new List<int>();
                for (int i = 0; i < opts.Count; i++) AddOption(opts[i], correct.Contains(i));
                var add = new ButtonEx(T("Add option"), Icons.Add, Kind.Soft, 34).Align(HAlign.Left);
                add.Click += (_, _) => { if (_options.Count < 8) { AddOption(new ChoiceOption(""), false); Relayout(); } };
                _specific.Controls.Add(Ui.Field(type == ItemTypes.Single ? T("Options (tick the correct one)") : T("Options (tick every correct one)"), _optionList));
                _specific.Controls.Add(add);
                break;

            case ItemTypes.TrueFalse:
                _truth = new Segmented(_item.Data.Truth == false ? 1 : 0, T("True"), T("False"));
                _specific.Controls.Add(Ui.Field(T("Correct answer"), _truth.Align(HAlign.Left)));
                break;

            case ItemTypes.Short:
                _model = new InputBox(T("The answer you would expect"), multiline: true, lines: 3) { Text = _item.Answer };
                _accept = new InputBox(T("One per line"), multiline: true, lines: 2) { Text = string.Join(Environment.NewLine, _item.Data.Accept ?? new()) };
                _keyPoints = new InputBox(T("One per line"), multiline: true, lines: 3) { Text = string.Join(Environment.NewLine, _item.Data.KeyPoints ?? new()) };
                _specific.Controls.Add(Ui.Field(T("Model answer"), _model));
                _specific.Controls.Add(Ui.Field(T("Other accepted answers"), _accept));
                _specific.Controls.Add(Ui.Field(T("Key points a good answer mentions"), _keyPoints));
                break;

            case ItemTypes.Note:
                _content = new InputBox(T("Markdown: **bold**, - bullet lists, # headings"), multiline: true, lines: 12) { Text = _item.Answer };
                _specific.Controls.Add(Ui.Field(T("Content"), _content));
                break;
        }
        _specific.ResumeLayout(true);
    }

    private void AddOption(ChoiceOption option, bool isCorrect)
    {
        var row = new OptionRow(_options.Count, option, isCorrect);
        row.CorrectChanged += (_, _) =>
        {
            if (_item.Type == ItemTypes.Single && row.Correct.Checked)
                foreach (var other in _options.Where(o => o != row)) other.Correct.Checked = false;
        };
        row.Removed += (_, _) =>
        {
            if (_options.Count <= 2) return;
            _options.Remove(row);
            _optionList!.Controls.Remove(row);
            row.Dispose();
            for (int i = 0; i < _options.Count; i++) _options[i].SetIndex(i);
            Relayout();
        };
        _options.Add(row);
        _optionList!.Controls.Add(row);
    }

    /// <summary>Copies type-specific inputs into the item (before switching type or saving).</summary>
    private void CollectSpecific()
    {
        switch (_item.Type)
        {
            case ItemTypes.Flashcard:
                _item.Answer = _back?.Text.Trim() ?? _item.Answer;
                _item.Data.Hint = string.IsNullOrWhiteSpace(_hint?.Text) ? null : _hint!.Text.Trim();
                break;
            case ItemTypes.Single:
            case ItemTypes.Multiple:
                _item.Data.Options = _options.Select(o => new ChoiceOption(o.TextBox.Text.Trim(), string.IsNullOrWhiteSpace(o.Why.Text) ? null : o.Why.Text.Trim())).ToList();
                _item.Data.Correct = _options.Select((o, i) => (o, i)).Where(t => t.o.Correct.Checked).Select(t => t.i).ToList();
                break;
            case ItemTypes.TrueFalse:
                _item.Data.Truth = _truth?.SelectedIndex == 0;
                break;
            case ItemTypes.Short:
                _item.Answer = _model?.Text.Trim() ?? _item.Answer;
                _item.Data.Accept = Lines(_accept?.Text);
                _item.Data.KeyPoints = Lines(_keyPoints?.Text);
                break;
            case ItemTypes.Note:
                _item.Answer = _content?.Text.Trim() ?? _item.Answer;
                break;
        }
    }

    private static List<string>? Lines(string? text)
    {
        var list = (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        return list.Count == 0 ? null : list;
    }

    private void Save()
    {
        CollectSpecific();
        _item.Prompt = TextUtil.Clean(_prompt.Text);
        string? problem = null;
        if (_item.Prompt.Length == 0 && _item.Type != ItemTypes.Note) problem = T("Please fill in the question / front side.");
        else if (_item.Type is ItemTypes.Flashcard or ItemTypes.Short or ItemTypes.Note && string.IsNullOrWhiteSpace(_item.Answer)) problem = T("Please fill in the answer.");
        else if (ItemTypes.IsChoice(_item.Type))
        {
            var options = _item.Data.Options ?? new List<ChoiceOption>();
            var correct = _item.Data.Correct ?? new List<int>();
            var filled = options.Count(o => o.Text.Length > 0);
            if (filled < 2) problem = T("A choice question needs at least 2 options.");
            else if (correct.Count == 0) problem = T("Tick the correct option.");
            else
            {
                // Drop empty options and fix the indices of the correct ones.
                var kept = new List<ChoiceOption>();
                var map = new Dictionary<int, int>();
                for (int i = 0; i < options.Count; i++)
                {
                    if (options[i].Text.Length == 0) continue;
                    map[i] = kept.Count;
                    kept.Add(options[i]);
                }
                _item.Data.Correct = correct.Where(map.ContainsKey).Select(i => map[i]).ToList();
                _item.Data.Options = kept;
                if (_item.Data.Correct.Count == 0) problem = T("Tick the correct option.");
            }
        }
        if (problem != null)
        {
            Ask.Info(T("Almost there"), problem);
            return;
        }

        if (_item.Type != ItemTypes.Single && _item.Type != ItemTypes.Multiple) { _item.Data.Options = null; _item.Data.Correct = null; }
        if (_item.Type != ItemTypes.TrueFalse) _item.Data.Truth = null;
        if (_item.Type != ItemTypes.Short) { _item.Data.Accept = null; _item.Data.KeyPoints = null; }

        _item.Explanation = TextUtil.Clean(_explanation.Text);
        _item.Page = _page.Text.Trim();
        _item.Tags = TextUtil.SplitTags(_tags.Text);
        _item.Difficulty = _difficulty.SelectedIndex;
        _item.Flagged = _flagged.On;
        _item.WrongInSource = _wrong.On;
        _item.PersonalNote = _note.Text.Trim();
        DialogResult = DialogResult.OK;
    }

    /// <summary>Edits an item and saves it. Returns true when saved.</summary>
    public static bool Edit(Item item)
    {
        var copy = Host.Store.GetItem(item.Id) ?? item;
        using var d = new ItemEditorDialog(copy, isNew: false);
        if (d.ShowDialog(Form.ActiveForm ?? Host.Main) != DialogResult.OK) return false;
        Host.Store.UpdateItemContent(copy);
        CopyContent(copy, item);
        return true;
    }

    /// <summary>Creates a new item in a deck. Returns the new item or null.</summary>
    public static Item? Create(Deck deck)
    {
        var type = deck.Kind switch { DeckKind.Test => ItemTypes.Single, DeckKind.Notes => ItemTypes.Note, _ => ItemTypes.Flashcard };
        var item = new Item { DeckId = deck.Id, Type = type, Source = deck.Source, Difficulty = deck.Difficulty };
        using var d = new ItemEditorDialog(item, isNew: true);
        if (d.ShowDialog(Form.ActiveForm ?? Host.Main) != DialogResult.OK) return null;
        item.SortOrder = Host.Store.NextItemSortOrder(deck.Id);
        Host.Store.InsertItem(item);
        return item;
    }

    private static void CopyContent(Item from, Item to)
    {
        to.Type = from.Type; to.Prompt = from.Prompt; to.Answer = from.Answer; to.Data = from.Data; to.Explanation = from.Explanation;
        to.Page = from.Page; to.Tags = from.Tags; to.Difficulty = from.Difficulty; to.Flagged = from.Flagged;
        to.WrongInSource = from.WrongInSource; to.PersonalNote = from.PersonalNote;
    }

    private sealed class OptionRow : Row
    {
        private readonly TextLabel _letter;
        public CheckBoxEx Correct { get; }
        public InputBox TextBox { get; }
        public InputBox Why { get; }
        public event EventHandler? CorrectChanged;
        public event EventHandler? Removed;

        public OptionRow(int index, ChoiceOption option, bool correct)
        {
            Gap = S(8);
            Align = VAlign.Center;
            Correct = new CheckBoxEx("", correct) { Width = S(26) };
            Correct.CheckedChanged += (_, _) => CorrectChanged?.Invoke(this, EventArgs.Empty);
            Tips.Set(Correct, T("Correct answer"));
            _letter = new TextLabel(((char)('A' + index)).ToString(), Fonts.UiSemibold(10f), Theme.P.Text2, wrap: false) { Width = S(18) };
            TextBox = new InputBox(T("Option text")) { Text = option.Text };
            Why = new InputBox(T("Why right/wrong (optional)")) { Text = option.Why ?? "" };
            var remove = ButtonEx.Icon(Icons.Close, T("Remove option"));
            remove.Click += (_, _) => Removed?.Invoke(this, EventArgs.Empty);
            Controls.Add(Correct);
            Controls.Add(_letter);
            Controls.Add(TextBox.Flex(1.2f));
            Controls.Add(Why.Flex(1f));
            Controls.Add(remove);
        }

        public void SetIndex(int index) => _letter.Text = ((char)('A' + index)).ToString();
    }
}

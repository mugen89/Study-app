using StudyApp.Data.Services;
using StudyApp.Domain.Sessions;

namespace StudyApp.App.Dialogs;

/// <summary>Configures a flashcard or test session: which items, filters, order, length, timer, feedback.</summary>
public sealed class SessionSetupDialog : DialogBase
{
    private readonly SessionOptions _o;
    private readonly TextLabel _count;
    private readonly ChoiceList? _which;
    private readonly List<(CheckBoxEx Box, string Type)> _types = new();
    private readonly List<(ToggleChip Chip, int Level)> _levels = new();
    private readonly List<(ToggleChip Chip, string Tag)> _tags = new();
    private readonly Segmented _order;
    private readonly Stepper _maxItems;
    private readonly Stepper _minutes;
    private readonly Toggle? _shuffleChoices;
    private readonly ChoiceList? _feedback;
    private readonly CheckBoxEx? _onlyMissed, _onlyFlagged, _onlyNew;
    private readonly ButtonEx _start;

    private SessionSetupDialog(SessionOptions options, string scopeText)
        : base(options.Mode == SessionMode.Test ? T("Test options") : T("Study options"), 640, scopeText)
    {
        _o = options;
        bool test = options.Mode == SessionMode.Test;

        if (!test)
        {
            int which = options.OnlyDue ? 2 : options.OnlyNew ? 3 : options.OnlyMissed ? 4 : options.OnlyFlagged ? 5 : options.Order == ItemOrder.Default && !options.HasFilters ? 0 : 1;
            _which = new ChoiceList(which,
                (T("Smart review"), T("Items that are due today plus a few new ones (recommended for daily study).")),
                (T("Practise everything"), T("Go through all matching items, even if they are not due.")),
                (T("Only due items"), T("Items scheduled for today.")),
                (T("Only new items"), T("Items you have never studied.")),
                (T("Only missed items"), T("Items you got wrong or only partly right last time.")),
                (T("Only flagged items"), T("Items you flagged for later.")));
            _which.SelectionChanged += (_, _) => UpdateCount();
            Content.Controls.Add(Section(T("Which items")));
            Content.Controls.Add(_which);
        }

        // Item types
        Content.Controls.Add(Section(T("Item types")));
        var typeRow = new Row { Gap = S(14), Wrap = true };
        var typeKeys = test
            ? new[] { ItemTypes.Single, ItemTypes.Multiple, ItemTypes.TrueFalse, ItemTypes.Short, ItemTypes.Flashcard }
            : new[] { ItemTypes.Flashcard, ItemTypes.Single, ItemTypes.Multiple, ItemTypes.TrueFalse, ItemTypes.Short };
        foreach (var t in typeKeys)
        {
            var label = test && t == ItemTypes.Flashcard ? T("Flashcards (type the answer)") : ItemTypes.PluralName(t);
            var box = new CheckBoxEx(label, options.Types.Count == 0 || options.Types.Contains(t));
            box.Width = box.GetPreferredSize(Size.Empty).Width;
            box.CheckedChanged += (_, _) => UpdateCount();
            _types.Add((box, t));
            typeRow.Controls.Add(box);
        }
        Content.Controls.Add(typeRow);

        if (test)
        {
            Content.Controls.Add(Section(T("Only include")));
            var filterRow = new Row { Gap = S(14), Wrap = true };
            _onlyMissed = new CheckBoxEx(T("Missed last time"), options.OnlyMissed);
            _onlyFlagged = new CheckBoxEx(T("Flagged"), options.OnlyFlagged);
            _onlyNew = new CheckBoxEx(T("Never answered"), options.OnlyNew);
            foreach (var b in new[] { _onlyMissed, _onlyFlagged, _onlyNew })
            {
                b.Width = b.GetPreferredSize(Size.Empty).Width;
                b.CheckedChanged += (_, _) => UpdateCount();
                filterRow.Controls.Add(b);
            }
            Content.Controls.Add(filterRow);
        }

        // Difficulty
        Content.Controls.Add(Section(T("Difficulty"), T("None selected = all levels")));
        var levelRow = new Row { Gap = S(8), Wrap = true };
        for (int d = 1; d <= 5; d++)
        {
            var chip = new ToggleChip($"{d} · {Difficulty.Label(d)}", options.Difficulties.Contains(d));
            chip.Toggled += (_, _) => UpdateCount();
            _levels.Add((chip, d));
            levelRow.Controls.Add(chip);
        }
        Content.Controls.Add(levelRow);

        // Tags
        var tags = Host.Store.GetTags(options.SubjectId).Take(40).ToList();
        if (tags.Count > 0)
        {
            Content.Controls.Add(Section(T("Topics (tags)"), T("None selected = all topics")));
            var tagRow = new Row { Gap = S(8), Wrap = true };
            foreach (var tag in tags)
            {
                var chip = new ToggleChip(tag, options.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
                chip.Toggled += (_, _) => UpdateCount();
                _tags.Add((chip, tag));
                tagRow.Controls.Add(chip);
            }
            Content.Controls.Add(tagRow);
        }

        // Order
        Content.Controls.Add(Section(T("Order")));
        _order = new Segmented((int)options.Order, test ? T("As in deck") : T("Due first"), T("Shuffle"), T("By difficulty"), T("By page"));
        _order.SelectionChanged += (_, _) => UpdateCount();
        Content.Controls.Add(_order.Align(HAlign.Left));
        if (test)
        {
            _shuffleChoices = new Toggle(T("Shuffle answer choices"), options.ShuffleChoices, T("Show the options of each question in a random order."));
            Content.Controls.Add(_shuffleChoices);
        }

        // Length
        Content.Controls.Add(Section(T("Length")));
        var lengthRow = new Row { Gap = S(20), Wrap = true, Align = VAlign.Bottom };
        _maxItems = new Stepper(0, 500, options.MaxItems, test ? 5 : 10, 170) { Format = v => v == 0 ? T("All") : N(v, test ? "question" : "item", test ? "questions" : "items") };
        _maxItems.ValueChanged += (_, _) => UpdateCount();
        _minutes = new Stepper(0, 240, options.TimeLimitMinutes, 5, 170) { Format = v => v == 0 ? (test ? T("No time limit") : T("No limit")) : N(v, "minute", "minutes") };
        lengthRow.Controls.Add(Ui.Field(test ? T("Number of questions") : T("Maximum items"), _maxItems));
        lengthRow.Controls.Add(Ui.Field(test ? T("Time limit") : T("Stop after"), _minutes));
        Content.Controls.Add(lengthRow);

        if (test)
        {
            Content.Controls.Add(Section(T("Show answers")));
            _feedback = new ChoiceList((int)options.Feedback,
                (T("After each question"), T("See right away if you were right, with the explanation and page.")),
                (T("At the end (exam mode)"), T("No feedback until you finish — like a real exam.")));
            Content.Controls.Add(_feedback);
        }

        _count = new TextLabel("", Fonts.UiSemibold(10f), Theme.P.AccentText, wrap: false).Flex();
        Buttons.Controls.Clear();
        Buttons.Controls.Add(_count);
        AddButton(T("Cancel"), Kind.Secondary, DialogResult.Cancel);
        _start = AddAction(test ? T("Start test") : T("Start studying"), Kind.Primary, Start, Icons.Play);
        AcceptButton = _start;
        UpdateCount();
    }

    private static Control Section(string title, string? hint = null)
    {
        var row = new Row { Gap = S(8), Margin = new Padding(0, S(6), 0, 0) };
        row.Controls.Add(new TextLabel(title, Fonts.UiSemibold(10.5f), Theme.P.Text, wrap: false));
        if (hint != null) row.Controls.Add(new TextLabel(hint, Fonts.Ui(9f), Theme.P.Text3, wrap: false));
        return row;
    }

    private SessionOptions Collect()
    {
        var o = _o.Clone();
        o.Types = _types.Where(t => t.Box.Checked).Select(t => t.Type).ToList();
        o.Difficulties = _levels.Where(l => l.Chip.On).Select(l => l.Level).ToList();
        o.Tags = _tags.Where(t => t.Chip.On).Select(t => t.Tag).ToList();
        o.Order = (ItemOrder)_order.SelectedIndex;
        o.MaxItems = _maxItems.Value;
        o.TimeLimitMinutes = _minutes.Value;
        if (_which != null)
        {
            o.OnlyDue = _which.SelectedIndex == 2;
            o.OnlyNew = _which.SelectedIndex == 3;
            o.OnlyMissed = _which.SelectedIndex == 4;
            o.OnlyFlagged = _which.SelectedIndex == 5;
            if (_which.SelectedIndex == 1 && o.Order == ItemOrder.Default) o.Order = ItemOrder.Shuffle;
            if (_which.SelectedIndex == 0) o.Order = o.Order == ItemOrder.Default ? ItemOrder.Default : o.Order;
        }
        if (_onlyMissed != null) o.OnlyMissed = _onlyMissed.Checked;
        if (_onlyFlagged != null) o.OnlyFlagged = _onlyFlagged.Checked;
        if (_onlyNew != null) o.OnlyNew = _onlyNew.Checked;
        if (_shuffleChoices != null) o.ShuffleChoices = _shuffleChoices.On;
        if (_feedback != null) o.Feedback = (FeedbackTiming)_feedback.SelectedIndex;
        return o;
    }

    private int _matching;

    private void UpdateCount()
    {
        var o = Collect();
        try
        {
            _matching = SessionBuilder.Build(Host.Store, o, Host.Settings).Count;
        }
        catch
        {
            _matching = 0;
        }
        _count.Text = _matching == 0 ? T("No items match these settings") : F("{0} ready", N(_matching, "item", "items"));
        _count.ForeColor = _matching == 0 ? Theme.P.Warning : Theme.P.AccentText;
        _count.Invalidate();
        if (_start != null) _start.Enabled = _matching > 0;
    }

    private void Start()
    {
        if (_matching == 0) return;
        Result = Collect();
        DialogResult = DialogResult.OK;
    }

    private SessionOptions? Result { get; set; }

    public static SessionOptions? Show(SessionOptions options, string scopeText)
    {
        using var d = new SessionSetupDialog(options, scopeText);
        return d.ShowDialog(Form.ActiveForm ?? Host.Main) == DialogResult.OK ? d.Result : null;
    }

    /// <summary>Pill that toggles on/off (difficulty levels, tags).</summary>
    private sealed class ToggleChip : UiControl
    {
    public override bool SizesToContent => true;

        private bool _on;
        public event EventHandler? Toggled;

        public ToggleChip(string text, bool on)
        {
            Text = text;
            _on = on;
            Height = S(32);
            Font = Fonts.UiSemibold(9f);
            Cursor = Cursors.Hand;
            Width = GetPreferredSize(Size.Empty).Width;
        }

        public bool On => _on;

        public override Size GetPreferredSize(Size proposedSize) => new(Gfx.Measure(Text, Font).Width + S(30) + (_on ? 0 : 0) + S(16), Height);

        protected override void OnClick(EventArgs e)
        {
            _on = !_on;
            Invalidate();
            Toggled?.Invoke(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Smooth(g);
            var p = Theme.P;
            var r = new RectangleF(0, 0, Width, Height);
            var fill = _on ? p.AccentSoft : Gfx.Blend(InputBox.InputBack, p.Text, 0.04f * HoverAnim.Value);
            Gfx.FillRound(g, fill, r, Height / 2f);
            Gfx.DrawRound(g, _on ? p.Accent : p.Border, r, Height / 2f, _on ? Sf(1.4f) : 1f);
            var glyphRect = new Rectangle(S(12), 0, S(14), Height);
            Gfx.Glyph(g, _on ? Icons.Check : Icons.Add, Fonts.Icon(8f), _on ? p.AccentText : p.Text3, glyphRect);
            Gfx.Text(g, Text, Font, _on ? p.AccentText : p.Text2, new Rectangle(S(30), 0, Width - S(38), Height), Gfx.Left);
        }
    }
}

using StudyApp.App.Dialogs;
using StudyApp.App.Shell;
using StudyApp.Domain.Prompts;

namespace StudyApp.App.Pages;

/// <summary>Ready-to-copy prompts that make ChatGPT/Claude answer in the app's JSON format.</summary>
public sealed class PromptsPage : PageBase
{
    private readonly List<PromptTemplate> _templates;
    private readonly PromptTemplate _template;
    private readonly PromptOptions _o;
    private readonly InputBox _preview;
    private readonly TextLabel _chars;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 150 };
    private readonly HashSet<string> _used;

    public override string NavKey => "prompts";

    public PromptsPage(string? templateKey = null, PromptOptions? preset = null)
    {
        var body = UseStandardLayout(1280);
        _templates = Host.Store.GetTemplates(L.Lang);
        if (_templates.Count == 0)
        {
            Host.Store.EnsureBuiltInTemplates();
            _templates = Host.Store.GetTemplates(L.Lang);
        }
        _template = _templates.FirstOrDefault(t => t.Key == (templateKey ?? Host.Settings.LastTemplateKey)) ?? _templates.First();
        _o = preset?.Clone() ?? Host.Settings.PromptOptions.Clone();
        _used = PromptRenderer.UsedPlaceholders(_template.Body);
        if (_used.Contains("weak_items") && string.IsNullOrWhiteSpace(_o.WeakItems)) FillWeakItems();
        if (_used.Contains("error") && string.IsNullOrWhiteSpace(_o.ErrorText)) _o.ErrorText = Host.LastImportError;

        body.Controls.Add(PageHeader(T("AI prompts"),
            T("Copy a prompt into ChatGPT or Claude (in the chat where your textbook is uploaded). The AI answers with JSON that you then import."), false,
            new ButtonEx(T("Go to Import"), Icons.Import, Kind.Secondary).WithTip(T("Import the AI's answer"), "Ctrl+I").OnClick(() => Go(() => new ImportPage()))));

        _preview = new InputBox("", multiline: true, lines: 18, mono: true) { ReadOnly = true };
        _chars = new TextLabel("", Fonts.Ui(9f), Theme.P.Text3, wrap: false);
        _debounce.Tick += (_, _) => { _debounce.Stop(); Render(); };

        var left = BuildTemplateList();
        var right = new Column { Gap = S(18) };
        right.Controls.Add(BuildTemplateHeader());
        var fields = BuildFields();
        if (fields != null) right.Controls.Add(fields);
        right.Controls.Add(BuildPreview());
        right.Controls.Add(new Banner(Tone.Accent, T("How to use it"),
            T("1. Open your AI chat (ChatGPT or Claude) with the textbook uploaded.  2. Paste the prompt and send it.  3. Copy the JSON reply.  4. Import it here — paste it on the Import page (Ctrl+I)."),
            new ButtonEx(T("Go to Import"), Icons.Import, Kind.Soft, 34).OnClick(() => Go(() => new ImportPage()))));

        body.Controls.Add(new TwoColumns(left, right, 0.27f) { StackBelow = S(940) });
        Render();
    }

    public override void OnDeactivated()
    {
        _debounce.Stop();
        _debounce.Dispose();
    }

    public override bool HandleKey(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Shift | Keys.C)) { Copy(); return true; }
        return false;
    }

    private void Changed()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void Render()
    {
        var text = PromptRenderer.Render(_template.Body, _o, _template.Lang);
        _preview.Text = text.Replace("\n", "\r\n");
        _chars.Text = F("{0} characters", text.Length.ToString("N0", L.Culture));
        _chars.Invalidate();
    }

    private void Copy()
    {
        Render();
        try
        {
            Clipboard.SetText(_preview.Text);
            Host.Settings.PromptOptions = _o.Clone();
            Host.Settings.LastTemplateKey = _template.Key;
            Host.SaveSettings();
            Toast.Show(T("Prompt copied. Paste it into your AI chat (Ctrl+V)."), Tone.Success);
        }
        catch (Exception ex)
        {
            Log.Warn("Copy prompt failed", ex);
            Toast.Show(T("Could not copy. Select the text and press Ctrl+C."), Tone.Warning);
        }
    }

    private void FillWeakItems()
    {
        var subject = Host.Store.GetSubjects().FirstOrDefault(s => string.Equals(s.Name, _o.Subject, StringComparison.CurrentCultureIgnoreCase));
        var items = Host.Store.WeakItems(subject?.Id, 15);
        _o.WeakItems = items.Count == 0 ? "" : PromptText.WeakItems(items, _template.Lang == "bg");
    }

    // ── template list ────────────────────────────────────────────────

    private Control BuildTemplateList()
    {
        var card = new Card(14) { Gap = S(6) };
        card.Controls.Add(new TextLabel(T("Templates"), Fonts.UiSemibold(10.5f), Theme.P.Text).Also(t => t.Margin = new Padding(S(6), S(4), 0, S(4))));
        foreach (var t in _templates)
        {
            var item = new TemplateItem(t, t.Id == _template.Id);
            item.Click += (_, _) =>
            {
                Host.Settings.LastTemplateKey = t.Key;
                Host.Main?.Replace(() => new PromptsPage(t.Key, _o));
            };
            card.Controls.Add(item);
        }
        var add = new ButtonEx(T("New template"), Icons.Add, Kind.Ghost, 36).Align(HAlign.Left);
        add.Click += (_, _) =>
        {
            var t = new PromptTemplate { Lang = L.Lang, Name = T("My template"), Body = _template.Body };
            if (TemplateEditorDialog.Show(t, isNew: true))
            {
                Host.Store.SaveTemplate(t);
                Host.Main?.Replace(() => new PromptsPage(t.Key, _o));
            }
        };
        card.Controls.Add(add);
        return card;
    }

    private Control BuildTemplateHeader()
    {
        var card = new Card(20) { Gap = S(8) };
        var row = new Row { Gap = S(8) };
        var col = new Column { Gap = S(4) }.Flex();
        var titleRow = new Row { Gap = S(8) };
        titleRow.Controls.Add(new TextLabel(_template.Name, Fonts.Heading(14f), Theme.P.Text, wrap: false));
        if (_template.IsBuiltIn && _template.IsModified) titleRow.Controls.Add(Chip.Status(T("Edited"), Theme.P.Warning));
        if (!_template.IsBuiltIn) titleRow.Controls.Add(Chip.Accent(T("Your template")));
        col.Controls.Add(titleRow);
        if (_template.Description.Length > 0) col.Controls.Add(new TextLabel(_template.Description, Fonts.Ui(9.5f), Theme.P.Text2));
        row.Controls.Add(col);

        var edit = ButtonEx.Icon(Icons.Edit, T("Edit template"), null, Kind.Secondary, 36);
        edit.Click += (_, _) =>
        {
            var copy = Host.Store.GetTemplate(_template.Id)!;
            if (TemplateEditorDialog.Show(copy, isNew: false))
            {
                Host.Store.SaveTemplate(copy);
                Host.Main?.Replace(() => new PromptsPage(copy.Key, _o));
            }
        };
        row.Controls.Add(edit);
        var more = ButtonEx.Icon(Icons.More, T("More actions"), null, Kind.Secondary, 36);
        more.Click += (_, _) =>
        {
            var items = new List<ToolStripItem>
            {
                Menus.Item(T("Duplicate"), Icons.Copy, () =>
                {
                    var t = new PromptTemplate { Lang = _template.Lang, Name = _template.Name + " (2)", Description = _template.Description, Body = _template.Body };
                    Host.Store.SaveTemplate(t);
                    Host.Main?.Replace(() => new PromptsPage(t.Key, _o));
                }),
            };
            if (_template.IsBuiltIn)
                items.Add(Menus.Item(T("Reset to default"), Icons.Undo, () =>
                {
                    if (!Ask.Confirm(T("Reset template?"), T("Your changes to this template will be replaced by the original text."), T("Reset"))) return;
                    Host.Store.ResetTemplate(_template.Id);
                    Host.Main?.Replace(() => new PromptsPage(_template.Key, _o));
                }));
            else
                items.Add(Menus.Item(T("Delete template"), Icons.Delete, () =>
                {
                    if (!Ask.Confirm(T("Delete template?"), F("\"{0}\" will be deleted.", _template.Name), T("Delete"), danger: true)) return;
                    Host.Store.DeleteTemplate(_template.Id);
                    Host.Main?.Replace(() => new PromptsPage(null, _o));
                }, danger: true));
            Menus.Show(more, items.ToArray());
        };
        row.Controls.Add(more);
        var quickCopy = new ButtonEx(T("Copy prompt"), Icons.Copy, Kind.Primary).WithTip(T("Copy to clipboard"), "Ctrl+Shift+C");
        quickCopy.Click += (_, _) => Copy();
        row.Controls.Add(quickCopy);
        card.Controls.Add(row);
        return card;
    }

    // ── fill-in fields ───────────────────────────────────────────────

    private Control? BuildFields()
    {
        bool hasSchema = _used.Any(u => u.StartsWith("schema", StringComparison.Ordinal));
        bool Uses(params string[] names) => names.Any(_used.Contains) || (hasSchema && names.Any(n => n is "subject" or "source" or "scope" or "chapter" or "pages"));
        var card = new Card(20) { Gap = S(14) };
        card.Controls.Add(new SectionHeader(T("Fill in"), T("Leave anything blank that you don't need.")));
        var subjects = Host.Store.GetSubjects(includeArchived: false);

        if (Uses("subject", "source"))
        {
            var subject = new InputBox(T("e.g. Human Anatomy")) { Text = _o.Subject };
            var source = new InputBox(T("e.g. Gray's Anatomy for Students")) { Text = _o.Source };
            subject.Changed += (_, _) => { _o.Subject = subject.Text; Changed(); };
            source.Changed += (_, _) => { _o.Source = source.Text; Changed(); };
            var row = new Row { Gap = S(12), Align = VAlign.Bottom };
            row.Controls.Add(Ui.Field(T("Subject"), subject).Flex());
            if (subjects.Count > 0)
            {
                var pick = new DropDown(210) { Placeholder = T("From my subjects"), Glyph = Icons.Library };
                foreach (var s in subjects) pick.Add(s.Name, s);
                pick.SelectionChanged += (_, _) =>
                {
                    if (pick.SelectedValue is not Subject s) return;
                    _o.Subject = s.Name;
                    _o.Source = s.Source;
                    _o.ChapterNumber = _o.ChapterTitle = _o.Pages = "";
                    if (_used.Contains("weak_items")) FillWeakItems();
                    Host.Main?.Replace(() => new PromptsPage(_template.Key, _o));
                };
                row.Controls.Add(pick);
            }
            card.Controls.Add(row);
            card.Controls.Add(Ui.Field(T("Textbook / source"), source));
        }

        if (Uses("scope", "chapter", "pages"))
        {
            var number = new InputBox(T("e.g. 3")) { Text = _o.ChapterNumber, Width = S(100) };
            var title = new InputBox(T("e.g. The Skeletal System")) { Text = _o.ChapterTitle };
            var pages = new InputBox(T("e.g. 45-78")) { Text = _o.Pages, Width = S(150) };
            number.Changed += (_, _) => { _o.ChapterNumber = number.Text; Changed(); };
            title.Changed += (_, _) => { _o.ChapterTitle = title.Text; Changed(); };
            pages.Changed += (_, _) => { _o.Pages = pages.Text; Changed(); };
            var row = new Row { Gap = S(12), Align = VAlign.Bottom, Wrap = true };
            row.Controls.Add(Ui.Field(T("Chapter no."), number).Also(f => f.Width = S(100)));
            row.Controls.Add(Ui.Field(T("Chapter title"), title).Flex());
            row.Controls.Add(Ui.Field(T("Pages"), pages).Also(f => f.Width = S(150)));
            card.Controls.Add(row);

            var subject = subjects.FirstOrDefault(s => string.Equals(s.Name, _o.Subject, StringComparison.CurrentCultureIgnoreCase));
            var chapters = subject == null ? new List<Chapter>() : Host.Store.GetChapters(subject.Id);
            if (chapters.Count > 0)
            {
                var pick = new DropDown(300) { Placeholder = T("Pick a chapter of this subject"), Glyph = Icons.Layers };
                foreach (var c in chapters) pick.Add(c.DisplayName, c);
                pick.SelectionChanged += (_, _) =>
                {
                    if (pick.SelectedValue is not Chapter c) return;
                    number.Text = c.Number;
                    title.Text = c.Title;
                    pages.Text = c.Pages;
                };
                card.Controls.Add(pick.Align(HAlign.Left));
            }
            card.Controls.Add(Ui.Faint(T("Without a chapter or pages, the AI uses the whole uploaded material.")));
        }

        var settingsRow = new Row { Gap = S(20), Wrap = true, Align = VAlign.Top };
        if (Uses("count"))
        {
            var count = new Stepper(1, 150, Math.Clamp(_o.Count, 1, 150), 5, 170);
            count.ValueChanged += (_, _) => { _o.Count = count.Value; Changed(); };
            settingsRow.Controls.Add(Ui.Field(_template.Key == PromptDefaults.Notes ? T("Number of sections") : T("Number of items"), count));
        }
        if (Uses("language", "rules"))
        {
            var lang = new DropDown(290) { Glyph = Icons.Language };
            lang.Add(T("Same as the textbook"), "").Add(T("English"), "en").Add(T("Bulgarian"), "bg");
            bool custom = _o.Language.Length > 0 && _o.Language is not ("en" or "bg");
            if (custom) lang.Add(_o.Language, _o.Language);
            lang.Add(T("Other language…"), "?");
            lang.SelectedValue = _o.Language;
            lang.SelectionChanged += (_, _) =>
            {
                if (lang.SelectedValue as string == "?")
                {
                    var other = Ask.Prompt(T("Other language"), T("Language for the AI's answer"), "", T("e.g. German"));
                    if (string.IsNullOrWhiteSpace(other)) { lang.SelectedValue = _o.Language; return; }
                    _o.Language = other;
                    Host.Main?.Replace(() => new PromptsPage(_template.Key, _o));
                    return;
                }
                _o.Language = lang.SelectedValue as string ?? "";
                Changed();
            };
            settingsRow.Controls.Add(Ui.Field(T("Language of the answer"), lang));
        }
        if (settingsRow.Controls.Count > 0) card.Controls.Add(settingsRow);

        if (Uses("difficulty", "difficulty_rules"))
        {
            var seg = new Segmented(_o.Difficulty is >= 1 and <= 5 ? _o.Difficulty : 0, T("Mixed"), "1", "2", "3", "4", "5");
            var desc = new TextLabel(PromptRenderer.DifficultyRules(_o.Difficulty is >= 1 and <= 5 ? _o.Difficulty : 0, L.IsBg), Fonts.Ui(9f), Theme.P.Text2);
            seg.SelectionChanged += (_, _) =>
            {
                _o.Difficulty = seg.SelectedIndex;
                desc.Text = PromptRenderer.DifficultyRules(_o.Difficulty, L.IsBg);
                LayoutProps.Relayout(desc);
                Changed();
            };
            card.Controls.Add(Ui.Field(T("Difficulty"), seg.Align(HAlign.Left)));
            card.Controls.Add(desc);
        }

        if (Uses("question_types", "schema_test") && !_template.Body.Contains("schema_test:"))
        {
            var row = new Row { Gap = S(14), Wrap = true };
            foreach (var t in ItemTypes.Questions)
            {
                var box = new CheckBoxEx(ItemTypes.PluralName(t), _o.QuestionTypes.Contains(t));
                box.Width = box.GetPreferredSize(Size.Empty).Width;
                box.CheckedChanged += (_, _) =>
                {
                    if (box.Checked) { if (!_o.QuestionTypes.Contains(t)) _o.QuestionTypes.Add(t); }
                    else _o.QuestionTypes.Remove(t);
                    if (_o.QuestionTypes.Count == 0) { _o.QuestionTypes.Add(t); box.Checked = true; }
                    Changed();
                };
                row.Controls.Add(box);
            }
            card.Controls.Add(Ui.Field(T("Question types"), row));
        }

        if (Uses("option_why_rule"))
        {
            var why = new Toggle(T("Explain every answer option"), _o.ExplainOptions, T("The AI adds a short \"why\" to each option, so you learn from wrong answers too. Makes the reply longer."));
            why.Toggled += (_, _) => { _o.ExplainOptions = why.On; Changed(); };
            card.Controls.Add(why);
        }

        if (Uses("focus"))
        {
            var focus = new InputBox(T("Optional – e.g. fractures, bone types, the clinical cases")) { Text = _o.Focus };
            focus.Changed += (_, _) => { _o.Focus = focus.Text; Changed(); };
            card.Controls.Add(Ui.Field(T("Focus topics"), focus));
        }

        if (Uses("weak_items"))
        {
            var weak = new InputBox(T("Your most-missed items appear here automatically"), multiline: true, lines: 6) { Text = _o.WeakItems.Replace("\n", "\r\n") };
            weak.Changed += (_, _) => { _o.WeakItems = weak.Text.Replace("\r\n", "\n"); Changed(); };
            var refresh = new ButtonEx(T("Refresh from my results"), Icons.Refresh, Kind.Ghost, 32).Align(HAlign.Left);
            refresh.Click += (_, _) => { FillWeakItems(); weak.Text = _o.WeakItems.Replace("\n", "\r\n"); };
            card.Controls.Add(Ui.Field(T("Your weak spots"), weak, T("Taken from the items you missed most (for the subject above, or all subjects). You can edit the list.")));
            card.Controls.Add(refresh);
        }

        if (Uses("error"))
        {
            var error = new InputBox(T("Paste the error message from the Import page"), multiline: true, lines: 4) { Text = _o.ErrorText.Replace("\n", "\r\n") };
            error.Changed += (_, _) => { _o.ErrorText = error.Text; Changed(); };
            card.Controls.Add(Ui.Field(T("Import error"), error, T("Filled in automatically from your last failed import.")));
        }

        if (Uses("extra"))
        {
            var extra = new InputBox(T("Optional – anything else the AI should know"), multiline: true, lines: 2) { Text = _o.Extra };
            extra.Changed += (_, _) => { _o.Extra = extra.Text; Changed(); };
            card.Controls.Add(Ui.Field(T("Extra instructions"), extra));
        }

        return card.Controls.Count > 1 ? card : null;
    }

    private Control BuildPreview()
    {
        var card = new Card(20) { Gap = S(12) };
        var copy = new ButtonEx(T("Copy prompt"), Icons.Copy, Kind.Primary, 44) { FontPt = 11f }.WithTip(T("Copy to clipboard"), "Ctrl+Shift+C");
        copy.Click += (_, _) => Copy();
        card.Controls.Add(new SectionHeader(T("Your prompt"), null, _chars, copy));
        card.Controls.Add(_preview);
        return card;
    }

    /// <summary>Entry in the template list.</summary>
    private sealed class TemplateItem : UiControl, IHeightForWidth
    {
        private readonly PromptTemplate _t;
        private readonly bool _selected;

        public TemplateItem(PromptTemplate t, bool selected)
        {
            _t = t;
            _selected = selected;
            Cursor = Cursors.Hand;
        }

        public int HeightFor(int width)
        {
            int h = S(12) + Gfx.WrappedHeight(_t.Name, Fonts.UiSemibold(10f), width - S(24));
            if (_t.Description.Length > 0) h += S(2) + Math.Min(Gfx.WrappedHeight(_t.Description, Fonts.Ui(8.5f), width - S(24)), Fonts.Ui(8.5f).Height * 2);
            return h + S(12);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Smooth(g);
            var p = Theme.P;
            var r = new RectangleF(0, 0, Width, Height);
            if (_selected) Gfx.FillRound(g, p.AccentSoft, r, S(10));
            else if (HoverAnim.Value > 0) Gfx.FillRound(g, Gfx.Blend(p.Surface, p.Text, 0.04f * HoverAnim.Value), r, S(10));
            if (_selected) Gfx.FillRound(g, p.Accent, new RectangleF(0, S(10), S(3), Height - S(20)), S(2));
            int nh = Gfx.WrappedHeight(_t.Name, Fonts.UiSemibold(10f), Width - S(24));
            Gfx.Text(g, _t.Name + (_t.IsModified ? " *" : ""), Fonts.UiSemibold(10f), _selected ? p.AccentText : p.Text, new Rectangle(S(12), S(10), Width - S(24), nh), Gfx.Wrap);
            if (_t.Description.Length > 0)
                Gfx.Text(g, _t.Description, Fonts.Ui(8.5f), p.Text2, new Rectangle(S(12), S(12) + nh, Width - S(24), Height - nh - S(14)), Gfx.Wrap | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>Edit the text of a prompt template; placeholders can be inserted with a click.</summary>
public sealed class TemplateEditorDialog : DialogBase
{
    private readonly PromptTemplate _t;
    private readonly InputBox _name;
    private readonly InputBox _description;
    private readonly InputBox _body;

    private TemplateEditorDialog(PromptTemplate t, bool isNew) : base(isNew ? T("New template") : T("Edit template"), 820,
        T("Placeholders in {{double braces}} are filled in from the fields on the prompts page."))
    {
        _t = t;
        _name = new InputBox(T("Template name")) { Text = t.Name };
        _description = new InputBox(T("Short description")) { Text = t.Description };
        _body = new InputBox("", multiline: true, lines: 20, mono: true) { Text = t.Body.Replace("\n", "\r\n") };
        Content.Controls.Add(Ui.Field(T("Name"), _name));
        Content.Controls.Add(Ui.Field(T("Description"), _description));
        Content.Controls.Add(Ui.Field(T("Prompt text"), _body));
        var chips = new Row { Gap = S(6), Wrap = true };
        foreach (var ph in PromptRenderer.Placeholders)
        {
            var chip = Chip.Neutral("{{" + ph + "}}");
            chip.Clickable = true;
            chip.Click += (_, _) =>
            {
                _body.Box.SelectedText = "{{" + ph + "}}";
                _body.Focus();
            };
            chips.Controls.Add(chip);
        }
        Content.Controls.Add(Ui.Field(T("Insert a placeholder"), chips));
        AddButton(T("Cancel"), Kind.Secondary, DialogResult.Cancel);
        AcceptButton = AddAction(T("Save template"), Kind.Primary, Save, Icons.Save);
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_body.Text)) return;
        _t.Name = _name.Text.Trim();
        _t.Description = _description.Text.Trim();
        _t.Body = _body.Text.Replace("\r\n", "\n").Trim();
        DialogResult = DialogResult.OK;
    }

    public static bool Show(PromptTemplate t, bool isNew)
    {
        using var d = new TemplateEditorDialog(t, isNew);
        return d.ShowDialog(Form.ActiveForm ?? Host.Main) == DialogResult.OK;
    }
}

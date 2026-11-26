using System.Text;

namespace StudyApp.App.UI;

public enum BlockKind { Paragraph, Heading1, Heading2, Heading3, Bullet, Numbered, Quote, Code, Rule }

public sealed class Inline
{
    public string Text = "";
    public bool Bold, Italic, Code;
}

public sealed class Block
{
    public BlockKind Kind;
    public List<Inline> Inlines = new();
    public int Indent;
    public string Marker = "";
    public string Raw = "";
}

/// <summary>Small Markdown subset used by LLM output: headings, lists, quotes, code, **bold**, *italic*, `code`.</summary>
public static class MarkdownLite
{
    public static List<Block> Parse(string? text)
    {
        var blocks = new List<Block>();
        if (string.IsNullOrWhiteSpace(text)) return blocks;
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var para = new StringBuilder();
        bool inCode = false;
        var code = new StringBuilder();

        void FlushPara()
        {
            if (para.Length == 0) return;
            blocks.Add(new Block { Kind = BlockKind.Paragraph, Inlines = ParseInlines(para.ToString().TrimEnd('\n')) });
            para.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```"))
            {
                if (inCode)
                {
                    blocks.Add(new Block { Kind = BlockKind.Code, Raw = code.ToString().TrimEnd('\n') });
                    code.Clear();
                    inCode = false;
                }
                else
                {
                    FlushPara();
                    inCode = true;
                }
                continue;
            }
            if (inCode)
            {
                code.Append(rawLine).Append('\n');
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushPara();
                continue;
            }

            int indent = (line.Length - trimmed.Length) / 2;
            if (trimmed.StartsWith("### ")) { FlushPara(); blocks.Add(Heading(BlockKind.Heading3, trimmed[4..])); continue; }
            if (trimmed.StartsWith("## ")) { FlushPara(); blocks.Add(Heading(BlockKind.Heading2, trimmed[3..])); continue; }
            if (trimmed.StartsWith("# ")) { FlushPara(); blocks.Add(Heading(BlockKind.Heading1, trimmed[2..])); continue; }
            if (trimmed is "---" or "***" or "___") { FlushPara(); blocks.Add(new Block { Kind = BlockKind.Rule }); continue; }

            if ((trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("• ") || trimmed.StartsWith("+ ")) && trimmed.Length > 2)
            {
                FlushPara();
                blocks.Add(new Block { Kind = BlockKind.Bullet, Indent = Math.Min(indent, 4), Inlines = ParseInlines(trimmed[2..].TrimStart()) });
                continue;
            }

            int dot = 0;
            while (dot < trimmed.Length && dot < 4 && char.IsDigit(trimmed[dot])) dot++;
            if (dot > 0 && dot + 1 < trimmed.Length && (trimmed[dot] == '.' || trimmed[dot] == ')') && trimmed[dot + 1] == ' ')
            {
                FlushPara();
                blocks.Add(new Block { Kind = BlockKind.Numbered, Indent = Math.Min(indent, 4), Marker = trimmed[..(dot + 1)], Inlines = ParseInlines(trimmed[(dot + 2)..]) });
                continue;
            }

            if (trimmed.StartsWith("> ") || trimmed == ">")
            {
                FlushPara();
                blocks.Add(new Block { Kind = BlockKind.Quote, Inlines = ParseInlines(trimmed.Length > 2 ? trimmed[2..] : "") });
                continue;
            }

            if (para.Length > 0) para.Append('\n');
            para.Append(trimmed);
        }

        if (inCode && code.Length > 0) blocks.Add(new Block { Kind = BlockKind.Code, Raw = code.ToString().TrimEnd('\n') });
        FlushPara();
        return blocks;
    }

    private static Block Heading(BlockKind kind, string text) => new() { Kind = kind, Inlines = ParseInlines(text.Trim().TrimEnd('#').Trim()) };

    public static List<Inline> ParseInlines(string text)
    {
        var result = new List<Inline>();
        var sb = new StringBuilder();
        bool bold = false, italic = false;
        int i = 0;

        void Flush()
        {
            if (sb.Length == 0) return;
            result.Add(new Inline { Text = sb.ToString(), Bold = bold, Italic = italic });
            sb.Clear();
        }

        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && "*_`\\".Contains(text[i + 1]))
            {
                sb.Append(text[i + 1]);
                i += 2;
                continue;
            }
            if (c == '`')
            {
                int end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    result.Add(new Inline { Text = text[(i + 1)..end], Code = true, Bold = bold, Italic = italic });
                    i = end + 1;
                    continue;
                }
            }
            if ((c == '*' || c == '_') && i + 1 < text.Length && text[i + 1] == c)
            {
                bool closing = bold;
                if (closing || text.IndexOf(new string(c, 2), i + 2, StringComparison.Ordinal) > 0)
                {
                    Flush();
                    bold = !bold;
                    i += 2;
                    continue;
                }
            }
            if (c == '*' || (c == '_' && (i == 0 || !char.IsLetterOrDigit(text[i - 1]))))
            {
                bool canOpen = i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]) && text.IndexOf(c, i + 1) > 0;
                bool canClose = italic && i > 0 && !char.IsWhiteSpace(text[i - 1]);
                if (canClose || (!italic && canOpen))
                {
                    Flush();
                    italic = !italic;
                    i++;
                    continue;
                }
            }
            sb.Append(c);
            i++;
        }
        Flush();
        return result;
    }

    public static string ToPlainText(string? text)
    {
        var sb = new StringBuilder();
        foreach (var b in Parse(text))
        {
            if (sb.Length > 0) sb.Append('\n');
            if (b.Kind == BlockKind.Bullet) sb.Append("• ");
            if (b.Kind == BlockKind.Numbered) sb.Append(b.Marker).Append(' ');
            if (b.Kind == BlockKind.Code) sb.Append(b.Raw);
            foreach (var inl in b.Inlines) sb.Append(inl.Text);
        }
        return sb.ToString();
    }
}

/// <summary>Fonts and colours for a block of rich text.</summary>
public sealed class RichStyle
{
    public required Font Regular { get; init; }
    public required Font Bold { get; init; }
    public required Font Italic { get; init; }
    public required Font BoldItalic { get; init; }
    public required Font Code { get; init; }
    public required Font H1 { get; init; }
    public required Font H2 { get; init; }
    public required Font H3 { get; init; }
    public Color Text { get; init; }
    public Color Muted { get; init; }
    public Color Accent { get; init; }
    public Color CodeBack { get; init; }
    public float LineHeight { get; init; } = 1.5f;
    public float ParagraphGap { get; init; } = 0.75f;
    public bool Center { get; init; }

    /// <summary>Study text in the user's reading font and size.</summary>
    public static RichStyle Study(float pt, Color? text = null, bool center = false, float? lineHeight = null) => new()
    {
        Regular = Fonts.StudyText(pt),
        Bold = Fonts.StudyStrong(pt),
        Italic = Fonts.StudyText(pt, FontStyle.Italic),
        BoldItalic = Fonts.StudyText(pt, FontStyle.Bold | FontStyle.Italic),
        Code = Fonts.GetDevice(Fonts.Mono, pt * 0.9f),
        H1 = Fonts.StudyStrong(pt * 1.45f),
        H2 = Fonts.StudyStrong(pt * 1.25f),
        H3 = Fonts.StudyStrong(pt * 1.1f),
        Text = text ?? Theme.P.Text,
        Muted = Theme.P.Text2,
        Accent = Theme.P.Accent,
        CodeBack = Theme.P.Surface3,
        LineHeight = lineHeight ?? Host.Settings.LineHeight,
        Center = center,
    };

    /// <summary>UI-sized rich text (explanations, previews).</summary>
    public static RichStyle Ui(float pt, Color? text = null) => new()
    {
        Regular = Fonts.Ui(pt),
        Bold = Fonts.UiSemibold(pt),
        Italic = Fonts.Get(Fonts.Text, pt, FontStyle.Italic),
        BoldItalic = Fonts.Get(Fonts.TextSemibold, pt, FontStyle.Italic),
        Code = Fonts.Code(pt * 0.92f),
        H1 = Fonts.Heading(pt * 1.35f),
        H2 = Fonts.Heading(pt * 1.2f),
        H3 = Fonts.UiSemibold(pt * 1.05f),
        Text = text ?? Theme.P.Text,
        Muted = Theme.P.Text2,
        Accent = Theme.P.Accent,
        CodeBack = Theme.P.Surface3,
        LineHeight = 1.45f,
        ParagraphGap = 0.6f,
    };
}

/// <summary>
/// Lays out parsed Markdown into positioned text runs with real line-height control
/// (WinForms labels cannot do this) and draws them with crisp GDI text.
/// </summary>
public sealed class RichLayout
{
    private readonly struct Run
    {
        public readonly string Text;
        public readonly Font Font;
        public readonly Color Color;
        public readonly int X, Y, W, H;
        public readonly bool CodeBack;
        public Run(string text, Font font, Color color, int x, int y, int w, int h, bool codeBack)
        {
            Text = text; Font = font; Color = color; X = x; Y = y; W = w; H = h; CodeBack = codeBack;
        }
    }

    private enum DecoKind { Bullet, Number, QuoteBar, CodeBlock, Rule }

    private readonly struct Deco
    {
        public readonly DecoKind Kind;
        public readonly Rectangle Rect;
        public readonly string Text;
        public readonly Font? Font;
        public Deco(DecoKind kind, Rectangle rect, string text = "", Font? font = null)
        {
            Kind = kind; Rect = rect; Text = text; Font = font;
        }
    }

    private readonly List<Run> _runs = new();
    private readonly List<Deco> _decos = new();
    private readonly RichStyle _style;

    public int Width { get; }
    public int Height { get; private set; }

    private static readonly Dictionary<(Font, string), int> WidthCache = new();
    private static readonly Dictionary<Font, (int Space, float Ascent, float Descent)> Metrics = new();

    private RichLayout(RichStyle style, int width)
    {
        _style = style;
        Width = width;
    }

    public static RichLayout Build(IReadOnlyList<Block> blocks, RichStyle style, int width)
    {
        var layout = new RichLayout(style, Math.Max(10, width));
        layout.LayoutBlocks(blocks);
        return layout;
    }

    public static void ClearCaches()
    {
        WidthCache.Clear();
        Metrics.Clear();
    }

    private static int TextWidth(Font font, string text)
    {
        if (text.Length == 0) return 0;
        var key = (font, text);
        if (WidthCache.TryGetValue(key, out var w)) return w;
        w = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), Gfx.Flags | TextFormatFlags.SingleLine).Width;
        if (WidthCache.Count > 20000) WidthCache.Clear();
        WidthCache[key] = w;
        return w;
    }

    private static (int Space, float Ascent, float Descent) FontMetrics(Font font)
    {
        if (Metrics.TryGetValue(font, out var m)) return m;
        var fam = font.FontFamily;
        float em = fam.GetEmHeight(font.Style);
        float ascent = font.Size * fam.GetCellAscent(font.Style) / em;
        float descent = font.Size * fam.GetCellDescent(font.Style) / em;
        int space = Math.Max(1, TextWidth(font, "a a") - TextWidth(font, "aa"));
        m = (space, ascent, descent);
        Metrics[font] = m;
        return m;
    }

    private Font FontFor(Inline inl, Font regular, Font bold, Font italic, Font boldItalic)
    {
        if (inl.Code) return _style.Code;
        if (inl.Bold && inl.Italic) return boldItalic;
        if (inl.Bold) return bold;
        if (inl.Italic) return italic;
        return regular;
    }

    private void LayoutBlocks(IReadOnlyList<Block> blocks)
    {
        int y = 0;
        float em = _style.Regular.Size;
        int paraGap = (int)Math.Round(em * _style.ParagraphGap);

        for (int bi = 0; bi < blocks.Count; bi++)
        {
            var block = blocks[bi];
            if (bi > 0)
            {
                bool listContinues = block.Kind is BlockKind.Bullet or BlockKind.Numbered && blocks[bi - 1].Kind is BlockKind.Bullet or BlockKind.Numbered;
                y += listContinues ? (int)(em * 0.3f) : block.Kind is BlockKind.Heading1 or BlockKind.Heading2 or BlockKind.Heading3 ? (int)(paraGap * 1.6f) : paraGap;
            }

            switch (block.Kind)
            {
                case BlockKind.Rule:
                    _decos.Add(new Deco(DecoKind.Rule, new Rectangle(0, y + (int)(em * 0.5f), Width, Math.Max(1, (int)Dpi.Sf(1)))));
                    y += (int)em;
                    break;

                case BlockKind.Code:
                    {
                        int pad = (int)(em * 0.7f);
                        var font = _style.Code;
                        int top = y;
                        y += pad;
                        foreach (var line in block.Raw.Split('\n'))
                        {
                            var lh = (int)(font.Size * 1.45f);
                            _runs.Add(new Run(line, font, _style.Text, pad, y, TextWidth(font, line), lh, false));
                            y += lh;
                        }
                        y += pad;
                        _decos.Insert(0, new Deco(DecoKind.CodeBlock, new Rectangle(0, top, Width, y - top)));
                        break;
                    }

                default:
                    y = LayoutParagraph(block, y);
                    break;
            }
        }
        Height = Math.Max(y, 1);
    }

    private int LayoutParagraph(Block block, int y)
    {
        Font regular = _style.Regular, bold = _style.Bold, italic = _style.Italic, boldItalic = _style.BoldItalic;
        float lineFactor = _style.LineHeight;
        Color color = _style.Text;
        switch (block.Kind)
        {
            case BlockKind.Heading1: regular = bold = italic = boldItalic = _style.H1; lineFactor = 1.3f; break;
            case BlockKind.Heading2: regular = bold = italic = boldItalic = _style.H2; lineFactor = 1.3f; break;
            case BlockKind.Heading3: regular = bold = italic = boldItalic = _style.H3; lineFactor = 1.35f; break;
        }

        float em = regular.Size;
        int indent = 0;
        if (block.Kind is BlockKind.Bullet or BlockKind.Numbered) indent = (int)(em * (1.5f + block.Indent * 1.3f));
        if (block.Kind == BlockKind.Quote) indent = (int)(em * 1.1f);
        int maxX = Width;

        // Tokenise into words (hard line breaks preserved).
        var tokens = new List<(string Text, Font Font, bool Code, bool Break)>();
        foreach (var inl in block.Inlines)
        {
            var font = FontFor(inl, regular, bold, italic, boldItalic);
            var parts = inl.Text.Split('\n');
            for (int p = 0; p < parts.Length; p++)
            {
                if (p > 0) tokens.Add(("", font, false, true));
                var words = parts[p].Split(' ');
                for (int w = 0; w < words.Length; w++)
                {
                    if (w > 0) tokens.Add((" ", font, inl.Code, false));
                    if (words[w].Length > 0) tokens.Add((words[w], font, inl.Code, false));
                }
            }
        }

        var line = new List<(string Text, Font Font, bool Code, int X, int W)>();
        int x = indent;
        int lineStartY = y;
        bool firstLine = true;
        int markerY = y;

        void FlushLine()
        {
            while (line.Count > 0 && line[^1].Text == " ") line.RemoveAt(line.Count - 1);
            float maxSize = line.Count == 0 ? em : Math.Max(em, line.Max(t => t.Font.Size));
            int lineH = (int)Math.Round(maxSize * lineFactor);
            var metrics = FontMetrics(regular);
            float textH = metrics.Ascent + metrics.Descent;
            float baseline = lineStartY + (lineH - textH) / 2f + metrics.Ascent;
            int shift = 0;
            if (_style.Center && line.Count > 0)
            {
                int used = line[^1].X + line[^1].W;
                shift = Math.Max(0, (Width - used) / 2);
            }
            foreach (var t in line)
            {
                if (t.Text == " ") continue;
                var m = FontMetrics(t.Font);
                int top = (int)Math.Round(baseline - m.Ascent);
                _runs.Add(new Run(t.Text, t.Font, t.Code ? _style.Text : color, t.X + shift, top, t.W, (int)(m.Ascent + m.Descent), t.Code));
            }
            if (firstLine) markerY = (int)Math.Round(baseline - metrics.Ascent);
            firstLine = false;
            line.Clear();
            lineStartY += lineH;
            x = indent;
        }

        foreach (var tok in tokens)
        {
            if (tok.Break)
            {
                FlushLine();
                continue;
            }
            if (tok.Text == " ")
            {
                if (line.Count == 0) continue;
                int sw = FontMetrics(tok.Font).Space;
                line.Add((" ", tok.Font, tok.Code, x, sw));
                x += sw;
                continue;
            }

            int w = TextWidth(tok.Font, tok.Text);
            if (x + w > maxX && line.Count > 0)
            {
                FlushLine();
            }
            if (w > maxX - indent)
            {
                // Very long word: split by characters.
                var chunk = new StringBuilder();
                foreach (var ch in tok.Text)
                {
                    var candidate = chunk.ToString() + ch;
                    if (x + TextWidth(tok.Font, candidate) > maxX && chunk.Length > 0)
                    {
                        line.Add((chunk.ToString(), tok.Font, tok.Code, x, TextWidth(tok.Font, chunk.ToString())));
                        FlushLine();
                        chunk.Clear();
                    }
                    chunk.Append(ch);
                }
                if (chunk.Length > 0)
                {
                    int cw = TextWidth(tok.Font, chunk.ToString());
                    line.Add((chunk.ToString(), tok.Font, tok.Code, x, cw));
                    x += cw;
                }
                continue;
            }
            line.Add((tok.Text, tok.Font, tok.Code, x, w));
            x += w;
        }
        if (line.Count > 0 || firstLine) FlushLine();

        if (block.Kind == BlockKind.Bullet)
        {
            string bulletChar = block.Indent % 2 == 0 ? "•" : "◦";
            int bw = TextWidth(regular, bulletChar);
            _decos.Add(new Deco(DecoKind.Bullet, new Rectangle(indent - (int)(em * 1.0f) - bw / 2, markerY, bw * 2, (int)em * 2), bulletChar, regular));
        }
        else if (block.Kind == BlockKind.Numbered)
        {
            int nw = TextWidth(regular, block.Marker);
            _decos.Add(new Deco(DecoKind.Number, new Rectangle(indent - nw - (int)(em * 0.45f), markerY, nw + 2, (int)em * 2), block.Marker, regular));
        }
        else if (block.Kind == BlockKind.Quote)
        {
            _decos.Add(new Deco(DecoKind.QuoteBar, new Rectangle(0, y, Math.Max(2, (int)Dpi.Sf(3)), lineStartY - y)));
        }
        return lineStartY;
    }

    public void Draw(Graphics g, int ox, int oy, Rectangle clip)
    {
        Gfx.Smooth(g);
        foreach (var d in _decos)
        {
            var r = new Rectangle(d.Rect.X + ox, d.Rect.Y + oy, d.Rect.Width, d.Rect.Height);
            if (r.Bottom < clip.Top || r.Top > clip.Bottom) continue;
            switch (d.Kind)
            {
                case DecoKind.CodeBlock:
                    Gfx.FillRound(g, _style.CodeBack, r, Dpi.Sf(6));
                    break;
                case DecoKind.Rule:
                    using (var b = new SolidBrush(Theme.P.Border)) g.FillRectangle(b, r);
                    break;
                case DecoKind.QuoteBar:
                    Gfx.FillRound(g, Gfx.Blend(_style.Accent, Theme.P.Surface, 0.4f), r, r.Width / 2f);
                    break;
                case DecoKind.Bullet:
                    TextRenderer.DrawText(g, d.Text, d.Font!, new Point(r.X, r.Y), _style.Accent, Gfx.Flags);
                    break;
                case DecoKind.Number:
                    TextRenderer.DrawText(g, d.Text, d.Font!, new Point(r.X, r.Y), _style.Muted, Gfx.Flags);
                    break;
            }
        }

        foreach (var run in _runs)
        {
            int ry = run.Y + oy;
            if (ry + run.H < clip.Top || ry > clip.Bottom) continue;
            if (run.CodeBack)
            {
                var back = new RectangleF(run.X + ox - Dpi.Sf(3), ry - Dpi.Sf(1), run.W + Dpi.Sf(6), run.H + Dpi.Sf(2));
                Gfx.FillRound(g, _style.CodeBack, back, Dpi.Sf(4));
            }
            TextRenderer.DrawText(g, run.Text, run.Font, new Point(run.X + ox, ry), run.Color, Gfx.Flags);
        }
    }
}

/// <summary>A control that displays Markdown-lite text and reports its height for a width.</summary>
public sealed class RichTextView : UiControl, IHeightForWidth
{
    private List<Block> _blocks = new();
    private RichLayout? _layout;
    private RichStyle _style;
    private string _markdown = "";

    public RichTextView(string markdown, RichStyle style)
    {
        _style = style;
        Markdown = markdown;
    }

    public string Markdown
    {
        get => _markdown;
        set
        {
            _markdown = value ?? "";
            _blocks = MarkdownLite.Parse(_markdown);
            _layout = null;
            Invalidate();
        }
    }

    public RichStyle Style
    {
        get => _style;
        set
        {
            _style = value;
            _layout = null;
            Invalidate();
        }
    }

    private RichLayout GetLayout(int width)
    {
        if (_layout == null || _layout.Width != width) _layout = RichLayout.Build(_blocks, _style, width);
        return _layout;
    }

    public int HeightFor(int width) => _blocks.Count == 0 ? 0 : GetLayout(width).Height;

    public override Size GetPreferredSize(Size proposedSize)
    {
        int w = proposedSize.Width > 0 && proposedSize.Width < 100000 ? proposedSize.Width : Dpi.S(600);
        return new Size(w, HeightFor(w));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_blocks.Count == 0) return;
        GetLayout(Width).Draw(e.Graphics, 0, 0, e.ClipRectangle);
    }
}

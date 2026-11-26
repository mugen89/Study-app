namespace StudyApp.App.UI;

/// <summary>Study activity calendar (GitHub-style): one square per day, darker = more answers.</summary>
public sealed class Heatmap : UiControl, IHeightForWidth
{
    private readonly Dictionary<DateOnly, (int Reviews, int Correct)> _data;
    private readonly int _weeks;
    private (DateOnly Day, Rectangle Rect)? _hover;

    public Heatmap(IEnumerable<StudyApp.Data.DayActivity> data, int weeks = 20)
    {
        _data = data.ToDictionary(d => d.Day, d => (d.Reviews, d.Correct));
        _weeks = weeks;
    }

    private int LabelW => S(30);
    private int Cell(int width) => Math.Clamp((width - LabelW) / _weeks - S(3), S(9), S(18));

    public int HeightFor(int width) => (Cell(width) + S(3)) * 7 + S(22) + S(26);

    private DateOnly Start
    {
        get
        {
            var today = StudyDay.Today;
            int dow = ((int)today.DayOfWeek + 6) % 7; // Monday = 0
            return today.AddDays(-dow - 7 * (_weeks - 1));
        }
    }

    private IEnumerable<(DateOnly Day, Rectangle Rect)> Cells()
    {
        int cell = Cell(Width);
        int step = cell + S(3);
        var start = Start;
        for (int w = 0; w < _weeks; w++)
        for (int d = 0; d < 7; d++)
        {
            var day = start.AddDays(w * 7 + d);
            if (day > StudyDay.Today) yield break;
            yield return (day, new Rectangle(LabelW + w * step, S(20) + d * step, cell, cell));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = Cells().Cast<(DateOnly, Rectangle)?>().FirstOrDefault(c => c!.Value.Item2.Contains(e.Location));
        if (hit?.Item1 != _hover?.Day)
        {
            _hover = hit;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = null;
        base.OnMouseLeave(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var p = Theme.P;
        int max = Math.Max(10, _data.Values.Select(v => v.Reviews).DefaultIfEmpty(0).Max());
        var small = Fonts.Ui(8f);

        // Month labels
        int cell = Cell(Width);
        int step = cell + S(3);
        int lastMonth = -1;
        for (int w = 0; w < _weeks; w++)
        {
            var day = Start.AddDays(w * 7);
            if (day.Month != lastMonth)
            {
                lastMonth = day.Month;
                if (w < _weeks - 1)
                    Gfx.Text(g, day.ToDateTime(TimeOnly.MinValue).ToString("MMM", L.Culture), small, p.Text3, new Rectangle(LabelW + w * step, 0, S(48), S(16)), Gfx.Left);
            }
        }
        // Weekday labels (Mon, Wed, Fri)
        var monday = new DateTime(2024, 1, 1);
        foreach (var d in new[] { 0, 2, 4 })
            Gfx.Text(g, monday.AddDays(d).ToString("ddd", L.Culture), small, p.Text3, new Rectangle(0, S(20) + d * step, LabelW, cell), Gfx.Left);

        foreach (var (day, rect) in Cells())
        {
            int n = _data.TryGetValue(day, out var v) ? v.Reviews : 0;
            Color c;
            if (n == 0) c = p.Dark ? p.Surface3 : Gfx.Blend(p.Surface2, p.Surface3, 0.7f);
            else
            {
                float t = 0.3f + 0.7f * Math.Min(1f, (float)Math.Sqrt((double)n / max));
                c = Gfx.Blend(p.Surface, p.Accent, t);
            }
            Gfx.FillRound(g, c, rect, S(3));
            if (day == StudyDay.Today) Gfx.DrawRound(g, p.Accent, RectangleF.Inflate(rect, 1, 1), S(4), 1.2f);
        }

        // Legend + hover text
        int ly = S(20) + 7 * step + S(6);
        var info = _hover is { } h
            ? (h.Day.ToDateTime(TimeOnly.MinValue).ToString("ddd, d MMM", L.Culture) + " — " +
               (_data.TryGetValue(h.Day, out var hv) ? N(hv.Reviews, "answer", "answers") : T("no study")))
            : T("Hover a day to see details");
        int lessW = Gfx.Measure(T("Less"), small).Width + S(4);
        int moreW = Gfx.Measure(T("More"), small).Width + S(4);
        int legendW = lessW + S(4) + 4 * S(14) + S(4) + moreW;
        int lx = Width - legendW;
        Gfx.Text(g, info, Fonts.Ui(8.5f), p.Text2, new Rectangle(LabelW, ly, Math.Max(S(40), lx - LabelW - S(10)), S(18)), Gfx.Left);
        Gfx.Text(g, T("Less"), small, p.Text3, new Rectangle(lx, ly, lessW, S(16)), Gfx.Left);
        for (int i = 0; i < 4; i++)
        {
            var c = i == 0 ? (p.Dark ? p.Surface3 : Gfx.Blend(p.Surface2, p.Surface3, 0.7f)) : Gfx.Blend(p.Surface, p.Accent, 0.3f + 0.23f * i);
            Gfx.FillRound(g, c, new RectangleF(lx + lessW + S(4) + i * S(14), ly + S(2), S(11), S(11)), S(2));
        }
        Gfx.Text(g, T("More"), small, p.Text3, new Rectangle(lx + lessW + S(8) + 4 * S(14), ly, moreW, S(16)), Gfx.Left);
    }
}

/// <summary>Line chart of a percentage over time (accuracy per week).</summary>
public sealed class LineChart : UiControl, IHeightForWidth
{
    private readonly List<(string Label, double Value, int Count)> _points;
    private int _hover = -1;

    public LineChart(IEnumerable<(string Label, double Value, int Count)> points)
    {
        _points = points.ToList();
    }

    public int HeightFor(int width) => S(200);

    private Rectangle Plot => new(S(40), S(12), Width - S(52), Height - S(46));

    private PointF PointAt(int i)
    {
        var r = Plot;
        float x = r.X + (_points.Count <= 1 ? r.Width / 2f : r.Width * i / (float)(_points.Count - 1));
        float y = r.Bottom - r.Height * (float)_points[i].Value;
        return new PointF(x, y);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int best = -1;
        float bestD = S(24);
        for (int i = 0; i < _points.Count; i++)
        {
            float d = Math.Abs(PointAt(i).X - e.X);
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best != _hover) { _hover = best; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        base.OnMouseLeave(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var p = Theme.P;
        var r = Plot;
        var small = Fonts.Ui(8f);
        using (var grid = new Pen(p.Border) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
        {
            foreach (var v in new[] { 0, 0.25, 0.5, 0.75, 1.0 })
            {
                float y = r.Bottom - r.Height * (float)v;
                g.DrawLine(grid, r.X, y, r.Right, y);
                Gfx.Text(g, $"{v * 100:0}%", small, p.Text3, new Rectangle(0, (int)y - S(8), r.X - S(6), S(16)), Gfx.Left | TextFormatFlags.Right);
            }
        }

        var withData = Enumerable.Range(0, _points.Count).Where(i => _points[i].Count > 0).ToList();
        if (withData.Count == 0)
        {
            Gfx.Text(g, T("No answers yet in this period"), Fonts.Ui(9.5f), p.Text3, r, Gfx.Center);
        }
        else
        {
            var pts = withData.Select(PointAt).ToArray();
            if (pts.Length > 1)
            {
                using var area = new System.Drawing.Drawing2D.GraphicsPath();
                area.AddLines(pts);
                area.AddLine(pts[^1], new PointF(pts[^1].X, r.Bottom));
                area.AddLine(new PointF(pts[^1].X, r.Bottom), new PointF(pts[0].X, r.Bottom));
                area.CloseFigure();
                using var fill = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                    Gfx.Alpha(p.Accent, p.Dark ? 70 : 55), Gfx.Alpha(p.Accent, 0), 90f);
                g.FillPath(fill, area);
                using var pen = new Pen(p.Accent, Sf(2.2f)) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
                g.DrawLines(pen, pts);
            }
            foreach (var pt in pts)
            {
                Gfx.Circle(g, p.Surface, new RectangleF(pt.X - S(4), pt.Y - S(4), S(8), S(8)));
                using var pen = new Pen(p.Accent, Sf(2));
                g.DrawEllipse(pen, pt.X - S(4), pt.Y - S(4), S(8), S(8));
            }
        }

        // X labels (first, middle, last)
        foreach (var i in new[] { 0, _points.Count / 2, _points.Count - 1 }.Distinct())
        {
            if (i < 0 || i >= _points.Count) continue;
            var pt = PointAt(i);
            int lx = (int)Math.Clamp(pt.X - S(40), 0, Width - S(80));
            Gfx.Text(g, _points[i].Label, small, p.Text3, new Rectangle(lx, r.Bottom + S(8), S(80), S(16)), Gfx.Center);
        }

        if (_hover >= 0 && _points[_hover].Count > 0)
        {
            var pt = PointAt(_hover);
            var text = $"{_points[_hover].Label}: {_points[_hover].Value * 100:0}% · {N(_points[_hover].Count, "answer", "answers")}";
            var size = Gfx.Measure(text, Fonts.Ui(8.5f));
            var box = new Rectangle((int)Math.Clamp(pt.X - size.Width / 2f - S(8), 0, Width - size.Width - S(16)), (int)Math.Max(0, pt.Y - S(34)), size.Width + S(16), S(24));
            Gfx.FillRound(g, p.Dark ? p.Surface3 : Color.FromArgb(34, 37, 45), box, S(6));
            Gfx.Text(g, text, Fonts.Ui(8.5f), p.Dark ? p.Text : Color.White, box, Gfx.Center);
        }
    }
}

/// <summary>Horizontal bars with labels (weakest chapters/tags, accuracy %).</summary>
public sealed class BarList : UiControl, IHeightForWidth
{
    private readonly List<(string Label, double Value, string Right)> _rows;

    public BarList(IEnumerable<(string Label, double Value, string Right)> rows)
    {
        _rows = rows.ToList();
    }

    private int RowH => S(46);

    public int HeightFor(int width) => Math.Max(S(40), _rows.Count * RowH);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var p = Theme.P;
        if (_rows.Count == 0)
        {
            Gfx.Text(g, T("Not enough answers yet."), Fonts.Ui(9.5f), p.Text3, ClientRectangle, Gfx.Left);
            return;
        }
        for (int i = 0; i < _rows.Count; i++)
        {
            var (label, value, right) = _rows[i];
            int y = i * RowH;
            int rightW = S(110);
            Gfx.Text(g, label, Fonts.Ui(9.5f), p.Text, new Rectangle(0, y, Width - rightW, S(20)), Gfx.Left);
            Gfx.Text(g, right, Fonts.UiSemibold(9f), p.Text2, new Rectangle(Width - rightW, y, rightW, S(20)), Gfx.Left | TextFormatFlags.Right);
            var track = new RectangleF(0, y + S(24), Width, S(8));
            Gfx.FillRound(g, p.Surface3, track, S(4));
            var color = value >= 0.8 ? p.Success : value >= 0.6 ? p.Warning : p.Danger;
            Gfx.FillRound(g, color, new RectangleF(0, track.Y, Math.Max(S(8), (float)(Width * value)), S(8)), S(4));
        }
    }
}
